using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using ILGPU.Frontend;
using ILGPU.Frontend.DebugInformation;

namespace SpawnDev.ILGPU;

/// <summary>One assembly whose code went into a shader: its name and its build id (the manifest module's MVID).</summary>
/// <param name="Name">The assembly's simple name.</param>
/// <param name="Mvid">The build id of the assembly the shader was generated from.</param>
public sealed record SourceAssemblyStamp(string Name, Guid Mvid);

/// <summary>
/// Which assemblies a kernel's shader was generated from, so a shader persisted by one session is never used by a later
/// session whose code changed. The cache key (kernel signature + device profile + library version) does not see a kernel's
/// BODY: rebuild a kernel, or a helper it calls, without a SpawnDev.ILGPU change and the old shader would load under the same
/// key and compute the old thing (found by Tuvok, 2026-11-26, iterating on SpawnDev.ILGPU.ML's convolution kernels).
/// </summary>
/// <remarks>
/// The stamp set is the assemblies of the kernel's whole call closure, walked from its IL (the IR is not available on an
/// early hit, and the optimized IR has already inlined the helpers that are most likely to change): every called or
/// referenced method's declaring assembly, the assemblies of all generic arguments (a constrained call such as
/// <c>TReduction.Apply</c> resolves to the interface method, whose implementation lives with the type argument), and the
/// generator's own assemblies (SpawnDev.ILGPU and the ILGPU fork: a local build can reuse a version number). Framework
/// assemblies are stamped but not walked (they change with the .NET runtime, as a whole). Dynamic assemblies are walked but
/// not stamped: their build id is new on every emit, and DelegateSpecialization kernels name theirs by a stable hash already;
/// the code they call is stamped through the walk.
/// </remarks>
internal static class KernelSourceStamps
{
    /// <summary>A kernel whose closure is larger than this is not stamped (and so not exported): a safety bound, not a limit
    /// any real kernel reaches.</summary>
    private const int MaxMethods = 8192;

    /// <summary>
    /// Assemblies stamped as a whole but not walked: the framework (it changes with the .NET runtime, as a whole) and the
    /// generator's own ILGPU, ILGPU.Algorithms and SpawnDev.ILGPU. Walking those follows intrinsic stubs into ILGPU's CPU
    /// runtime, thousands of methods that never reach a shader, and ends at the size bound (the shared-memory test kernel
    /// was not exported). A user type that reaches them as a generic argument is stamped at the call site. Exact names:
    /// SpawnDev.ILGPU.ML and other libraries built on ILGPU hold kernel code and are walked.
    /// </summary>
    private static bool IsStampedWhole(Assembly assembly)
    {
        var name = assembly.GetName().Name ?? "";
        return name == "System" || name == "mscorlib" || name == "netstandard"
            || name.StartsWith("System.", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.", StringComparison.Ordinal)
            || name == "ILGPU" || name == "ILGPU.Algorithms" || name == "SpawnDev.ILGPU";
    }

    /// <summary>Why the last <see cref="TryCompute"/> that returned null gave up (diagnostics and tests).</summary>
    internal static string? LastFailure { get; private set; }

    /// <summary>
    /// The assemblies <paramref name="kernel"/>'s shader is generated from, or null when its closure cannot be walked
    /// (then the artifact is not exported: an unverifiable shader is never persisted).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Reads the IL of methods ILGPU already compiled into this kernel; their bodies are kept because they were compiled.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "Only names and assemblies of types the kernel already uses are read; nothing is instantiated.")]
    public static IReadOnlyList<SourceAssemblyStamp>? TryCompute(MethodBase kernel)
    {
        var assemblies = new HashSet<Assembly>
        {
            typeof(global::ILGPU.Context).Assembly,     // the ILGPU fork (IR, optimizer)
            typeof(KernelSourceStamps).Assembly,        // SpawnDev.ILGPU (the shader generators)
        };
        var visited = new HashSet<MethodBase>();
        var todo = new Stack<MethodBase>();

        void AddType(Type? type)
        {
            while (type is not null && type.HasElementType) type = type.GetElementType();
            if (type is null || type.IsGenericParameter) return;
            if (!assemblies.Add(type.Assembly) && !type.IsGenericType) return;
            if (type.IsGenericType && !type.IsGenericTypeDefinition)
                foreach (var argument in type.GetGenericArguments()) AddType(argument);
        }
        void AddMethod(MethodBase method)
        {
            AddType(method.DeclaringType);
            if (method is MethodInfo info && info.IsGenericMethod && !info.IsGenericMethodDefinition)
                foreach (var argument in info.GetGenericArguments()) AddType(argument);
            if (visited.Add(method)) todo.Push(method);
        }

        AddMethod(kernel);
        while (todo.Count > 0)
        {
            if (visited.Count > MaxMethods) return Fail($"{kernel.Name}: more than {MaxMethods} methods in the call closure");
            var method = todo.Pop();
            var assembly = method.DeclaringType?.Assembly ?? method.Module.Assembly;
            if (IsStampedWhole(assembly)) continue;         // stamped above, not walked
            if (method.IsAbstract) continue;                // interface / abstract: the implementation's assembly comes with
                                                            // the generic argument or the concrete type, stamped above
            if (method.DeclaringType is null) return Fail($"{kernel.Name}: module-level method {method.Name}");
            MethodBody? body;
            try { body = method.GetMethodBody(); }
            catch (Exception ex) { return Fail($"{kernel.Name}: body of {method.DeclaringType.FullName}.{method.Name}: {ex.GetType().Name} {ex.Message}"); }
            if (body is null) continue;                     // extern / runtime-implemented: no IL to follow
            DisassembledMethod disassembled;
            try { disassembled = new Disassembler(method, SequencePointEnumerator.Empty).Disassemble(); }
            catch (Exception ex) { return Fail($"{kernel.Name}: IL of {method.DeclaringType.FullName}.{method.Name}: {ex.GetType().Name} {ex.Message}"); }
            foreach (var instruction in disassembled.Instructions)
            {
                switch (instruction.Argument)
                {
                    case MethodBase callee: AddMethod(callee); break;
                    case FieldInfo field: AddType(field.DeclaringType); AddType(field.FieldType); break;
                    case Type type: AddType(type); break;
                }
            }
        }

        var stamps = new List<SourceAssemblyStamp>(assemblies.Count);
        foreach (var assembly in assemblies)
        {
            if (assembly.IsDynamic) continue;
            stamps.Add(new SourceAssemblyStamp(assembly.GetName().Name ?? assembly.FullName ?? "", assembly.ManifestModule.ModuleVersionId));
        }
        stamps.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return stamps;

        static IReadOnlyList<SourceAssemblyStamp>? Fail(string why) { LastFailure = why; return null; }
    }

    private static readonly ConcurrentDictionary<string, Guid?> s_currentMvids = new(StringComparer.Ordinal);

    /// <summary>
    /// True when every stamped assembly is, in this process, the build the shader was generated from. An assembly that cannot
    /// be found or loaded does not match.
    /// </summary>
    public static bool Match(IReadOnlyList<SourceAssemblyStamp> stamps)
    {
        foreach (var stamp in stamps)
            if (CurrentMvid(stamp.Name) != stamp.Mvid) return false;
        return true;
    }

    private static Guid? CurrentMvid(string name)
    {
        if (s_currentMvids.TryGetValue(name, out var known)) return known;
        Guid? found = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.IsDynamic && assembly.GetName().Name == name) { found = assembly.ManifestModule.ModuleVersionId; break; }
        }
        if (found is null)
        {
            // not loaded yet (an early hit runs before the kernel's code is touched): load it by name, as compiling the kernel would
            try { found = Assembly.Load(new AssemblyName(name)).ManifestModule.ModuleVersionId; }
            catch (Exception) { return null; }   // not cached: it may become loadable later
        }
        s_currentMvids[name] = found;
        return found;
    }
}
