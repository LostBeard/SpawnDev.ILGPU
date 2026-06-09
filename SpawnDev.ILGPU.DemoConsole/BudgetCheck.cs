using System.Reflection;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using ILGPU.Algorithms;
using ILGPU.Algorithms.RadixSortOperations;
using ILGPU.Algorithms.ScanReduceOperations;

/// <summary>
/// Measures the inliner's per-kernel MaxCumulativeInlinedIL for the heaviest normal
/// algorithm kernels at a candidate budget, to decide whether a GLOBAL low budget
/// (simple) is safe or a PER-BACKEND budget (complex) is needed for the VP9 walker fix.
/// If RadixSort/Scan stay UNDER the candidate budget (skipCount stays 0) the global low
/// budget causes no inlining change for normal kernels. Inlining is backend-agnostic
/// (runs at IR construction), so CPU answers for all backends.
///
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -- budget-check [budget]
/// </summary>
internal static class BudgetCheck
{
    public static int Run(int budget)
    {
        var t = typeof(global::ILGPU.IR.Transformations.Inliner);
        void Set(string f, object v) => t.GetField(f, BindingFlags.Public | BindingFlags.Static)!.SetValue(null, v);
        long GetL(string f) => (long)t.GetField(f, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        int GetI(string f) => (int)t.GetField(f, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

        Console.WriteLine($"=== budget-check @ CumulativeInlinedILBudget={budget} (CPU; inlining is backend-agnostic) ===");
        Set("CumulativeInlinedILBudget", budget);

        using var context = Context.Create(b => b.CPU().EnableAlgorithms());
        using var acc = context.GetCPUDevice(0).CreateCPUAccelerator(context);

        void Measure(string name, Action compile)
        {
            Set("MaxCumulativeInlinedIL", 0);
            Set("CumulativeBudgetSkipCount", 0L);
            try { compile(); }
            catch (Exception ex) { Console.WriteLine($"  {name,-40} COMPILE ERR: {ex.GetType().Name}"); return; }
            Console.WriteLine($"  {name,-40} maxCumIL={GetI("MaxCumulativeInlinedIL"),6}  skipCount={GetL("CumulativeBudgetSkipCount"),3}");
        }

        Measure("RadixSort<int,Asc>", () => acc.CreateRadixSort<int, Stride1D.Dense, AscendingInt32>());
        Measure("RadixSortPairs<float,int,Desc>", () => acc.CreateRadixSortPairs<float, Stride1D.Dense, int, Stride1D.Dense, DescendingFloat>());
        Measure("Scan<int,Inclusive>", () => acc.CreateScan<int, Stride1D.Dense, Stride1D.Dense, AddInt32>(ScanKind.Inclusive));

        Console.WriteLine($"=== skipCount==0 everywhere => budget {budget} does NOT fire for normal kernels (global low budget safe). ===");
        return 0;
    }
}
