using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using System.Reflection;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Part: generic device structs (views, DataBlock, VariableView) must declare NO static fields.
    //
    // WHY THIS FILE EXISTS: a static constructor on a generic view struct is one ingredient of a Mono WASM AOT fault.
    // Once a bound delegate exists to a runtime-instantiated generic method whose signature carries a generic struct
    // wrapping the view - ILGPU builds exactly that launcher for every grid-stride body (CreateInitializer, CreateScan,
    // reductions) - shared generic AOT code calling a generic method over the view (CopyFromCPU<int> ->
    // HasNoData<ArrayView1D<int, Dense>>, reached by Allocate1D(int[])) jumps to a target with the wrong signature:
    // "RuntimeError: function signature mismatch", or interp.c:2737 asserts. MEASURED 2026-10-07 in SpawnScene's AOT build
    // (an int[] upload after a training trapped the page) and reduced to a repro with no ILGPU code: the trap needs the
    // static fields; as static properties the same repro passes. 5.3.5 moved them (ArrayView<T> -> ArrayViewStatics<T>,
    // the rest -> properties).
    //
    // This suite runs the browser lanes interpreted, so it can never SEE the AOT trap; this guard keeps the cause out.
    public abstract partial class BackendTestBase
    {
        /// <summary>
        /// No generic value type implementing <see cref="IArrayView"/>, and no DataBlock / VariableView, declares a static
        /// field (constants are compile-time literals and allowed).
        /// </summary>
        [TestMethod]
        public async Task GenericDeviceStructsDeclareNoStaticFieldsTest() => await RunTest(async accelerator =>
        {
            await Task.CompletedTask;
            var ilgpu = typeof(ArrayView<>).Assembly;
            Type[] types;
            try { types = ilgpu.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }

            var offenders = new List<string>();
            int checkedTypes = 0;
            foreach (var t in types)
            {
                if (!t.IsValueType || !t.IsGenericTypeDefinition) continue;
                bool device = typeof(IArrayView).IsAssignableFrom(t)
                    || t.Name.StartsWith("DataBlock`", StringComparison.Ordinal)
                    || t.Name.StartsWith("VariableView`", StringComparison.Ordinal);
                if (!device) continue;
                checkedTypes++;
                foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!f.IsLiteral) offenders.Add($"{t.Name}.{f.Name}");
            }

            // Must have looked at the views at all, or the assertion below is vacuous (e.g. trimmed away).
            if (checkedTypes < 4)
                throw new Exception($"only {checkedTypes} generic device structs found in {ilgpu.GetName().Name} - "
                                  + "expected ArrayView`1, ArrayView1D/2D/3D, VariableView`1, DataBlock`N");
            if (offenders.Count > 0)
                throw new Exception($"generic device structs declare static fields (a static constructor - the Mono WASM "
                                  + $"AOT signature-mismatch ingredient; use a static property or a static holder CLASS): "
                                  + string.Join(", ", offenders));
        });
    }
}
