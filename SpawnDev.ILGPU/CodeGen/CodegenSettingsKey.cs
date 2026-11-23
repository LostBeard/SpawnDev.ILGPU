using System.Collections.Generic;
using global::ILGPU;

namespace SpawnDev.ILGPU;

/// <summary>
/// The generated-code inputs a <see cref="CapabilityProfile"/> does not carry as fields, folded into its
/// <see cref="CapabilityProfile.EnabledFeatures"/> so the shader-artifact cache key (which includes that set) changes
/// whenever they do. The artifact cache is PROCESS-wide: two contexts, or one context before and after a backend switch
/// changes, must never be served each other's code (global Rule 1: the key must fully determine the generated code).
/// </summary>
/// <remarks>
/// Only NON-default values are added, so a default context produces exactly the profile it produced before these entries
/// existed: build-time precompiled manifests (keyed by the static <see cref="CapabilityProfiles"/>, which carry none) keep
/// matching default contexts, and a non-default context misses them, as it must.
/// </remarks>
internal static class CodegenSettingsKey
{
    /// <summary>
    /// Adds the context properties that change the IR, and so the generated code, of every kernel: optimization level,
    /// inlining, math mode, floating-point contraction, static field and array modes, debug symbols, assertions and IO.
    /// Defaults are the values a context has after <c>ContextProperties.InstantiateProperties</c> (debug symbols Auto
    /// resolves to Disabled without a debugger).
    /// </summary>
    public static void AddContextSettings(Context context, ISet<string> features)
    {
        var p = context.Properties;
        if (p.OptimizationLevel != OptimizationLevel.O2) features.Add("ctx:opt=" + p.OptimizationLevel);
        if (p.InliningMode != InliningMode.Default) features.Add("ctx:inline=" + p.InliningMode);
        if (p.MathMode != MathMode.Default) features.Add("ctx:math=" + p.MathMode);
        if (p.FloatingPointContraction) features.Add("ctx:fpcontract");
        if (p.StaticFieldMode != StaticFieldMode.Default) features.Add("ctx:static=" + p.StaticFieldMode);
        if (p.ArrayMode != ArrayMode.Default) features.Add("ctx:array=" + p.ArrayMode);
        if (p.DebugSymbolsMode != DebugSymbolsMode.Disabled) features.Add("ctx:debugsym=" + p.DebugSymbolsMode);
        if (p.EnableAssertions) features.Add("ctx:assert");
        if (p.EnableIOOperations) features.Add("ctx:io");
        if (p.ForceDebuggingOfOptimizedKernels) features.Add("ctx:forcedebug");
    }
}
