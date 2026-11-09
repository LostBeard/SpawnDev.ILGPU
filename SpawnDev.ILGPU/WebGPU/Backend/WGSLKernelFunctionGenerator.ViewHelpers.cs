// ---------------------------------------------------------------------------------------
//                                   SpawnDev.ILGPU
//                    WebGPU view helpers as functions (per binding tuple)
// ---------------------------------------------------------------------------------------

using global::ILGPU;
using global::ILGPU.IR;
using global::ILGPU.IR.Analyses;
using global::ILGPU.IR.Types;
using global::ILGPU.IR.Values;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SpawnDev.ILGPU.WebGPU.Backend
{
    // A helper taking views used to be inlined at EVERY call site (InlineAddressParameterCalls): WGSL has no pointer to a
    // binding that one function definition could take for every buffer, because each binding has its own declared type
    // (array<T>, array<atomic<u32>> for packed sub-word elements, a coalesced shared binding, a body-struct field binding)
    // and its own offset slot. Shader size grew with call sites x helper size (SpawnDev.Codecs' VP9 decoder kernels: 33-87
    // MB of WGSL, Tint lost the device). With keepGlobalViewCalls the IR keeps a call to such a helper when every view
    // argument is a sub-view of a kernel parameter or of the calling helper's own (rooted) view parameter; this emits the
    // helper ONCE PER BINDING TUPLE as a module-scope fn
    // whose view parameters are element offsets (and lengths) into those bindings - each view parameter is a chosen view
    // (see "Views chosen at run time") rooted at the kernel parameter, so every access keeps the root's own handling.
    // Plans/view-helpers-as-functions-2026-11-09.md.
    internal sealed partial class WGSLKernelFunctionGenerator
    {
        /// <summary>Binding-tuple key -> emitted fn name.</summary>
        private readonly Dictionary<string, string> _monomorphizedHelperNames = new(StringComparer.Ordinal);

        /// <summary>The emitted fn definitions, appended after the entry point.</summary>
        private readonly List<string> _monomorphizedHelperFns = new();

        /// <summary>Per helper: its view parameters whose length it reads, with the length's IR type.</summary>
        private readonly Dictionary<Method, Dictionary<Parameter, TypeNode>> _helperViewLengthTypes = new();

        /// <summary>
        /// The view parameters of the helper being emitted whose root is a LOCAL array of a caller (LocalMemory): each is
        /// a <c>ptr&lt;function, array&lt;T, N&gt;&gt;</c> parameter (value: that WGSL array type) plus an element offset.
        /// Unlike a kernel binding, the caller's array is not visible inside the function, so an access does not
        /// redirect to the root: it indexes through the pointer (GenerateCode(LoadElementAddress)).
        /// </summary>
        private readonly Dictionary<Parameter, string> _localViewParams = new();

        /// <summary>True when <paramref name="value"/> is a view parameter of the helper being emitted (bound or local).</summary>
        private bool IsHelperViewParam(Value value) =>
            value is Parameter hp && hp.Method != Method
            && (_chosenViewRoots.ContainsKey(hp) || _localViewParams.ContainsKey(hp));

        /// <summary>The local array behind a view (NewView of a LOCAL Alloca, through casts), or null.</summary>
        private static Alloca? LocalArrayOf(Value value)
        {
            value = StripViewCasts(value);
            if (value is NewView newView)
                value = StripViewCasts(newView.Pointer);
            return value is Alloca { AddressSpace: MemoryAddressSpace.Local } alloca ? alloca : null;
        }

        /// <summary>True when the root of a view argument is a local array (or a local-array view parameter).</summary>
        private bool IsLocalRoot(Value root) =>
            LocalArrayOf(root) is not null || (root is Parameter p && _localViewParams.ContainsKey(p));

        /// <summary>The WGSL array type of a local root: the alloca's declared array, or the forwarded parameter's.</summary>
        private string LocalRootArrayType(Value root)
        {
            if (root is Parameter p && _localViewParams.TryGetValue(p, out var forwarded))
                return forwarded;
            var alloca = LocalArrayOf(root)!;
            string type = Load(alloca).Type;
            if (!type.StartsWith("array<", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"WGSL view helper argument: local allocation {alloca} is declared {type}, not an array, in kernel '{Method.Name}'");
            return type;
        }

        /// <summary>A view through address-space casts (which keep the element type and the length).</summary>
        private static Value StripViewCasts(Value value)
        {
            value = value.Resolve();
            while (value is AddressSpaceCast cast)
                value = cast.Value.Resolve();
            return value;
        }

        /// <summary>The view parameters of <paramref name="helper"/> whose length it reads, with the length type.</summary>
        private Dictionary<Parameter, TypeNode> HelperViewLengthTypes(Method helper)
        {
            if (_helperViewLengthTypes.TryGetValue(helper, out var types))
                return types;
            types = new Dictionary<Parameter, TypeNode>();
            _helperViewLengthTypes[helper] = types;
            foreach (var block in helper.Blocks)
                foreach (var entry in block)
                    if (entry.Value is GetViewLength gvl
                        && StripViewCasts(gvl.View) is Parameter p && p.Method == helper)
                    {
                        if (types.TryGetValue(p, out var known) && known != gvl.Type)
                            throw new InvalidOperationException(
                                $"WGSL view helper '{helper.Name}' reads the length of parameter {p.Index} as both {known} and {gvl.Type}");
                        types[p] = gvl.Type;
                    }
            // a view parameter passed WHOLE to a nested view helper that reads its length: the nested call needs it
            foreach (var block in helper.Blocks)
                foreach (var entry in block)
                    if (entry.Value is MethodCall call && call.Target != helper
                        && WGSLFunctionGenerator.IsMonomorphizedViewHelper(call.Target))
                    {
                        var inner = HelperViewLengthTypes(call.Target);
                        for (int i = 0; i < call.Count && i < call.Target.Parameters.Count; i++)
                            if (inner.TryGetValue(call.Target.Parameters[i], out var innerType)
                                && StripViewCasts(call[i]) is Parameter p && p.Method == helper)
                                types.TryAdd(p, innerType);
                    }
            return types;
        }

        /// <summary>
        /// The view a view argument is cut from: through address-space casts, sub-views and the view parameters of the
        /// helper being emitted (rooted themselves) to a view of a kernel parameter.
        /// </summary>
        private Value ViewArgumentRoot(Value argument, Method helper)
        {
            var value = argument.Resolve();
            while (true)
            {
                if (value is AddressSpaceCast cast) value = cast.Value.Resolve();
                else if (value is SubViewValue sv) value = sv.Source.Resolve();
                else if (value is Parameter hp && hp.Method != Method && _chosenViewRoots.TryGetValue(hp, out var hpRoot))
                    value = hpRoot.Resolve();
                else break;
            }
            // a local array (LocalMemory) of the kernel or a forwarded local-array view parameter
            if (IsLocalRoot(value))
                return value;
            if (ResolveToParameter(value) is Parameter p && p.Method == Method && p.Index >= KernelParamOffset)
                return value;
            throw new InvalidOperationException(
                $"WGSL view helper '{helper.Name}' was kept as a function but is passed a {value.GetType().Name} view " +
                "that is not a view of a kernel parameter (InlineAddressParameterCalls.PassesOnlyGlobalViews must inline it)");
        }

        /// <summary>A stable key for the binding a root view indexes.</summary>
        private static string ViewRootKeyString(Value root) =>
            root switch
            {
                Parameter p => $"P{p.Index}",
                GetField gf when gf.ObjectValue.Resolve() is Parameter fp => $"P{fp.Index}.F{gf.FieldSpan.Index}.{gf.FieldSpan.Span}",
                _ => $"V{root.Id}",
            };

        /// <summary>The element offset of view argument <paramref name="argument"/> relative to its root (<see cref="ViewArgumentRoot"/>).</summary>
        private string ViewArgumentOffsetExpr(Value argument)
        {
            var value = argument.Resolve();
            var parts = new List<string>();
            while (true)
            {
                if (value is AddressSpaceCast cast) value = cast.Value.Resolve();
                else if (value is SubViewValue sv)
                {
                    parts.Add($"({SubViewOffsetExpr(sv)})");
                    value = sv.Source.Resolve();
                }
                else if (IsHelperViewParam(value))
                {
                    parts.Add(ChosenViewVar(value, "vo")); // relative to the root already
                    break;
                }
                else break;
            }
            return parts.Count == 0 ? "0" : string.Join(" + ", parts);
        }

        /// <summary>A length expression of WGSL type <paramref name="from"/> as type <paramref name="to"/> (i32 / emulated i64).</summary>
        private string ConvertLengthExpr(string expr, string from, string to)
        {
            bool from64 = from == "emu_i64" || from == "emu_u64", to64 = to == "emu_i64" || to == "emu_u64";
            if (!Backend.EnableI64Emulation || from64 == to64) return expr;
            return to64 ? $"i64_from_i32({expr})" : $"i64_to_i32({expr})";
        }

        /// <summary>The element count of view argument <paramref name="argument"/> as a WGSL expression of <paramref name="lengthType"/>.</summary>
        private string ViewArgumentLengthExpr(Value argument, string lengthType)
        {
            var value = StripViewCasts(argument);
            bool wants64 = lengthType == "emu_i64" || lengthType == "emu_u64";
            // a whole local array: its view's length operand
            if (value is NewView localView && LocalArrayOf(localView) is not null)
            {
                string localLength = Load(localView.Length).ToString();
                bool localIs64 = localView.Length.Resolve().BasicValueType == BasicValueType.Int64;
                return ConvertLengthExpr(localLength, localIs64 && Backend.EnableI64Emulation ? "emu_i64" : "i32", lengthType);
            }
            // a view parameter of the helper being emitted: its own length parameter
            if (value is Parameter hp && IsHelperViewParam(hp))
            {
                if (!HelperViewLengthTypes(hp.Method).TryGetValue(hp, out var hpType))
                    throw new InvalidOperationException(
                        $"WGSL view helper '{hp.Method.Name}' passes parameter {hp.Index} whole to a helper reading its length, without a length of its own");
                return ConvertLengthExpr(ChosenViewVar(hp, "vl"), TypeGenerator[hpType], lengthType);
            }
            if (value is SubViewValue sv)
            {
                string length = Load(sv.Length).ToString();
                bool lengthIs64 = sv.Length.Resolve().BasicValueType == BasicValueType.Int64;
                if (Backend.EnableI64Emulation && wants64 && !lengthIs64) return $"i64_from_i32({length})";
                if (Backend.EnableI64Emulation && !wants64 && lengthIs64) return $"i64_to_i32({length})";
                return length;
            }
            // the whole parameter view: exactly what GenerateCode(GetViewLength) emits for it in the kernel
            if (ResolveToParameter(value) is Parameter p && p.Index >= KernelParamOffset)
                return ParamViewLengthExpr(p, lengthType);
            throw new InvalidOperationException(
                $"WGSL view helper argument: no length for a {value.GetType().Name} view in kernel '{Method.Name}'");
        }

        /// <summary>
        /// Emits a call to a view-taking helper as a call to its fn for this call's binding tuple, emitting that fn on first
        /// use. False when <paramref name="methodCall"/> does not call a view-taking helper.
        /// </summary>
        private bool TryEmitMonomorphizedHelperCall(MethodCall methodCall)
        {
            var callee = methodCall.Target;
            // Inline-flagged helpers that allocate shared memory stay calls on purpose and are inlined at emission
            // (InlineAddressParameterCalls); a kept call is always to a non-Inline helper
            if (!callee.HasImplementation || !WGSLFunctionGenerator.IsMonomorphizedViewHelper(callee)
                || _generatorArgs.HelperMethods.ContainsKey(callee))
                return false;

            var parameters = callee.Parameters;
            var roots = new Value?[parameters.Count];
            var key = new StringBuilder().Append(callee.Id);
            for (int i = 0; i < parameters.Count; i++)
            {
                // a pointer parameter is ptr<function, T>: any local variable of type T, nothing to specialize
                if (parameters[i].ParameterType is not ViewType) continue;
                roots[i] = ViewArgumentRoot(methodCall[i], callee);
                // a local array: any array of the same WGSL type (one function takes a pointer to it)
                key.Append(',').Append(i).Append('=').Append(
                    IsLocalRoot(roots[i]!) ? "L:" + LocalRootArrayType(roots[i]!) : ViewRootKeyString(roots[i]!));
            }
            var lengthTypes = HelperViewLengthTypes(callee);
            string keyString = key.ToString();
            if (!_monomorphizedHelperNames.TryGetValue(keyString, out var fnName))
            {
                fnName = $"{WGSLFunctionGenerator.GetMethodName(callee)}_m{_monomorphizedHelperNames.Count}";
                _monomorphizedHelperNames[keyString] = fnName;
                EmitMonomorphizedHelper(callee, roots, lengthTypes, fnName);
            }

            var args = new List<string>(parameters.Count + 2);
            for (int i = 0; i < parameters.Count; i++)
            {
                if (roots[i] is null)
                {
                    var argument = Load(methodCall[i]).ToString();
                    // a local variable passed by reference: its address (a cast of it is already `&v`)
                    if (parameters[i].ParameterType is PointerType && _localAllocaVarNames.Contains(argument))
                        argument = "&" + argument;
                    args.Add(argument);
                    continue;
                }
                if (IsLocalRoot(roots[i]!))
                {
                    // the array: `&v` of the kernel's (or this helper's caller's) local, or a forwarded pointer parameter
                    args.Add(roots[i] is Parameter forwarded
                        ? Load(forwarded).Name
                        : "&" + Load(LocalArrayOf(roots[i]!)!).Name);
                }
                args.Add(ViewArgumentOffsetExpr(methodCall[i]));
                if (lengthTypes.TryGetValue(parameters[i], out var lengthType))
                    args.Add(ViewArgumentLengthExpr(methodCall[i], TypeGenerator[lengthType]));
            }
            string argList = string.Join(", ", args);
            if (methodCall.Type.IsVoidType)
            {
                AppendLine($"{fnName}({argList});");
                return true;
            }
            var target = Load(methodCall);
            Declare(target);
            AppendLine($"{target} = {fnName}({argList});");
            return true;
        }

        /// <summary>
        /// Generates <paramref name="callee"/> as <c>fn <paramref name="fnName"/></c>: every view parameter is an element
        /// offset <c>hv{i}_vo</c> (and, when read, a length <c>hv{i}_vl</c>) into its root's binding, every other parameter
        /// is passed by value. The body is generated by this kernel generator through the kernel body pipeline, so a
        /// view access takes the root binding's handling: offset slot, sub-word packing, coalescing, body-struct field.
        /// </summary>
        private void EmitMonomorphizedHelper(
            Method callee,
            Value?[] roots,
            Dictionary<Parameter, TypeNode> lengthTypes,
            string fnName)
        {
            // the kernel walk this one interrupts (the helper is generated at its first call site)
            var savedBuilder = Builder;
            int savedIndent = IndentLevel;
            bool savedStateMachine = IsStateMachineActive;
            bool savedDeferred = _useDeferredDeclarations;
            var savedDeferredDeclarations = _deferredVarDeclarations.ToList();
            var savedDeclared = new HashSet<string>(declaredVariables);
            var savedLets = new HashSet<string>(_emittedLetBindings);
            var savedLoops = _loops;
            var savedReturnMerge = _returnMerge;
            var savedHeaderExit = _currentLoopHeaderExitTarget;
            var savedLoopExits = _loopExitStack.ToArray();
            var savedHoisted = _hoistedPrimitives;
            var savedCrossBlockPointers = _crossBlockPointers;
            var savedCrossBlockPointerExprs = _crossBlockPointerExprs;
            var savedCrossBlockElementPointers = _crossBlockElementPointers;
            bool savedEmittingHelper = _emittingHelperFunction;

            var fn = new StringBuilder();
            Builder = fn;
            IndentLevel = 1;
            IsStateMachineActive = false;
            _useDeferredDeclarations = false;
            _deferredVarDeclarations.Clear();
            // names of the kernel's scope are not visible here; fresh names keep the helper's own declarations
            declaredVariables.Clear();
            _emittedLetBindings.Clear();
            // varCounter keeps counting (never reset): names stay unique across the module, nested helpers included
            _inlineCounter++;
            _loopExitStack.Clear();
            _currentLoopHeaderExitTarget = null;
            _returnMerge = null;
            _hoistedPrimitives = new HashSet<Value>();
            _crossBlockPointers = new HashSet<Value>();
            _crossBlockPointerExprs = new Dictionary<string, string>();
            _crossBlockElementPointers = new HashSet<string>();
            _emittingHelperFunction = true;

            var blockList = callee.Blocks.ToList();
            foreach (var block in blockList)
                foreach (var entry in block)
                    valueVariables.Remove(entry.Value);

            var signature = new List<string>();
            var parameters = callee.Parameters;
            var helperViewParams = new List<Parameter>();
            var localViewParams = new List<(Parameter Param, string ArrayType)>();
            for (int i = 0; i < parameters.Count; i++)
            {
                var param = parameters[i];
                if (roots[i] is Value localRoot && IsLocalRoot(localRoot))
                {
                    string name = $"hv{i}";
                    string arrayType = LocalRootArrayType(localRoot);
                    valueVariables[param] = new Variable(name, $"ptr<function, {arrayType}>");
                    localViewParams.Add((param, arrayType));
                    signature.Add($"{name} : ptr<function, {arrayType}>");
                    signature.Add($"{name}_vo : i32");
                    if (lengthTypes.TryGetValue(param, out var localLengthType))
                        signature.Add($"{name}_vl : {TypeGenerator[localLengthType]}");
                }
                else if (roots[i] is Value root)
                {
                    string name = $"hv{i}";
                    valueVariables[param] = new Variable(name, "i32");
                    _chosenViewRoots[param] = root;
                    helperViewParams.Add(param);
                    signature.Add($"{name}_vo : i32");
                    if (lengthTypes.TryGetValue(param, out var lengthType))
                    {
                        _chosenViewNeedsLength.Add(param);
                        signature.Add($"{name}_vl : {TypeGenerator[lengthType]}");
                    }
                }
                else if (param.ParameterType is PointerType pointerParam)
                {
                    // a reference to a local variable of the caller: WGSL ptr<function, T>, used through `*hp{i}`
                    var variable = new Variable($"hp{i}", $"ptr<function, {TypeGenerator[pointerParam.ElementType]}>");
                    valueVariables[param] = variable;
                    signature.Add($"{variable.Name} : {variable.Type}");
                }
                else
                {
                    var variable = new Variable($"hs{i}", TypeGenerator[param.Type]);
                    valueVariables[param] = variable;
                    signature.Add($"{variable.Name} : {variable.Type}");
                }
            }

            // the outer helper's local-view parameters are not this function's: only its own are visible here
            var savedLocalViewParams = new Dictionary<Parameter, string>(_localViewParams);
            _localViewParams.Clear();
            foreach (var (param, arrayType) in localViewParams)
                _localViewParams[param] = arrayType;

            // the kernel body's own pipeline (GenerateCode()): cross-block values declared at function scope, local
            // allocations, the structured walker with function-scope deferred declarations, the scoping post-process
            _loops = callee.Blocks.CreateCFG().CreateLoops();
            HoistCrossBlockVariables(callee.Blocks);
            if (_generatorArgs.FunctionAllocas.TryGetValue(callee, out var allocas) && allocas.LocalAllocations.Length > 0)
                SetupAllocations(allocas.LocalAllocations, MemoryAddressSpace.Local);
            int codeGenStartPosition = fn.Length;
            if (blockList.Count == 1)
            {
                var block = blockList[0];
                foreach (var entry in block)
                    GenerateCodeFor(entry.Value);
                if (block.Terminator is ReturnTerminator ret)
                    AppendLine(ret.IsVoidReturn ? "return;" : $"return {Load(ret.ReturnValue.Resolve())};");
            }
            else
            {
                _useDeferredDeclarations = true;
                var pd = global::ILGPU.IR.Analyses.Dominators.CreatePostDominators(callee.Blocks);
                GenerateStructuredCode(callee.EntryBlock, null, pd);
                _useDeferredDeclarations = false;
                if (_deferredVarDeclarations.Count > 0)
                {
                    var deferred = string.Join(Environment.NewLine, _deferredVarDeclarations) + Environment.NewLine;
                    fn.Insert(codeGenStartPosition, deferred);
                    codeGenStartPosition += deferred.Length;
                }
            }
            FixStructuredBodyScoping(codeGenStartPosition, blockList.Count > 1);

            // PHASE 4 / 5 of the kernel body: unused hoisted declarations go, single-assignment vars become lets
            var bodyLines = fn.ToString().Split('\n');
            EliminateDeadVarDeclarations(bodyLines);
            ConvertSingleAssignmentVarsToLet(bodyLines);
            string body = string.Join("\n", bodyLines.Where(l => l != null));
            if (!body.EndsWith('\n')) body += "\n";

            string returnType = callee.ReturnType.IsVoidType ? "" : $" -> {TypeGenerator[callee.ReturnType]}";
            // the same literal-slot hoist as the entry point (HoistScalarParamLoads): one load per slot at fn entry
            string scalarLets = HoistScalarParamLoads(ref body);
            _monomorphizedHelperFns.Add(
                $"// view helper {callee.Name} for bindings {string.Join(", ", roots.Where(r => r != null).Select(r => ViewRootKeyString(r!)))}\n" +
                $"fn {fnName}({string.Join(", ", signature)}){returnType} {{\n{scalarLets}{body}}}\n");

            foreach (var param in helperViewParams)
            {
                _chosenViewRoots.Remove(param);
                _chosenViewNeedsLength.Remove(param);
            }
            _localViewParams.Clear();
            foreach (var entry in savedLocalViewParams)
                _localViewParams[entry.Key] = entry.Value;
            Builder = savedBuilder;
            IndentLevel = savedIndent;
            IsStateMachineActive = savedStateMachine;
            _useDeferredDeclarations = savedDeferred;
            _deferredVarDeclarations.Clear();
            _deferredVarDeclarations.AddRange(savedDeferredDeclarations);
            declaredVariables.Clear();
            declaredVariables.UnionWith(savedDeclared);
            _emittedLetBindings.Clear();
            _emittedLetBindings.UnionWith(savedLets);
            _loops = savedLoops;
            _returnMerge = savedReturnMerge;
            _currentLoopHeaderExitTarget = savedHeaderExit;
            _loopExitStack.Clear();
            for (int i = savedLoopExits.Length - 1; i >= 0; i--)
                _loopExitStack.Push(savedLoopExits[i]);
            _hoistedPrimitives = savedHoisted;
            _crossBlockPointers = savedCrossBlockPointers;
            _crossBlockPointerExprs = savedCrossBlockPointerExprs;
            _crossBlockElementPointers = savedCrossBlockElementPointers;
            _emittingHelperFunction = savedEmittingHelper;
        }

        /// <summary>True while <see cref="EmitMonomorphizedHelper"/> generates a helper function (returns carry values).</summary>
        private bool _emittingHelperFunction;

        /// <summary>
        /// A sub-view is an alias whose offset is applied where it is indexed (the base emits <c>let v = source;</c>). Of a
        /// view parameter of a helper being emitted there is nothing to alias: that parameter is an offset (and length),
        /// not a WGSL value, and every use walks the IR back to it.
        /// </summary>
        public override void GenerateCode(SubViewValue value)
        {
            var source = StripViewCasts(value.Source);
            while (source is SubViewValue inner)
                source = StripViewCasts(inner.Source);
            if (IsHelperViewParam(source))
            {
                valueVariables[value] = Load(source);
                return;
            }
            base.GenerateCode(value);
        }

        /// <summary>Appends the per-binding-tuple helper fns after the entry point.</summary>
        private void AppendMonomorphizedHelpers()
        {
            foreach (var fn in _monomorphizedHelperFns)
            {
                Builder.Append('\n');
                Builder.Append(fn);
            }
        }
    }
}
