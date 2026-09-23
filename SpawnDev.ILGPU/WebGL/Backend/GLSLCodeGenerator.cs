// ---------------------------------------------------------------------------------------
//                                 SpawnDev.ILGPU.WebGL
//                        Copyright (c) 2024 SpawnDev Project
//
// File: GLSLCodeGenerator.cs
//
// Base GLSL ES 3.0 code generator implementing IBackendCodeGenerator for WebGL backend.
// Uses Transform Feedback to emulate compute shaders via vertex shader output.
// ---------------------------------------------------------------------------------------

using global::ILGPU;
using global::ILGPU.Backends;
using global::ILGPU.Backends.EntryPoints;
using global::ILGPU.IR;
using global::ILGPU.IR.Analyses;
using global::ILGPU.IR.Analyses.ControlFlowDirection;
using global::ILGPU.IR.Analyses.TraversalOrders;
using global::ILGPU.IR.Types;
using global::ILGPU.IR.Values;
using System.Text;
using System.Linq;

namespace SpawnDev.ILGPU.WebGL.Backend
{
    /// <summary>
    /// Base class for GLSL ES 3.0 code generation. Generates GLSL vertex shader source
    /// from ILGPU IR values by implementing the IBackendCodeGenerator interface.
    /// Transform Feedback captures output varyings to emulate compute shader buffers.
    /// </summary>
    public abstract partial class GLSLCodeGenerator : IBackendCodeGenerator<StringBuilder>
    {
        #region Nested Types

        /// <summary>
        /// Generation arguments for GLSL code generator construction.
        /// </summary>
        public readonly struct GeneratorArgs
        {
            public GeneratorArgs(
                WebGLBackend backend,
                GLSLTypeGenerator typeGenerator,
                EntryPoint entryPoint,
                AllocaKindInformation sharedAllocations,
                AllocaKindInformation dynamicSharedAllocations)
            {
                Backend = backend;
                TypeGenerator = typeGenerator;
                EntryPoint = entryPoint;
                SharedAllocations = sharedAllocations;
                DynamicSharedAllocations = dynamicSharedAllocations;
                OutputVaryings = new List<OutputVaryingInfo>();
                ParameterBindings = new List<KernelParameterBinding>();
                BodyStructTypeIdsToSkip = new HashSet<long>();
                BodyStructManifest = new List<BodyStructBindingEntry>();
                HelperPackedViewParams = new Dictionary<(long, int), PackedViewParamInfo>();
            }

            /// <summary>The parent backend.</summary>
            public WebGLBackend Backend { get; }
            /// <summary>The type generator.</summary>
            public GLSLTypeGenerator TypeGenerator { get; }
            /// <summary>The kernel entry point.</summary>
            public EntryPoint EntryPoint { get; }
            /// <summary>Shared memory allocations.</summary>
            public AllocaKindInformation SharedAllocations { get; }
            /// <summary>Dynamic shared memory allocations.</summary>
            public AllocaKindInformation DynamicSharedAllocations { get; }
            /// <summary>Output varying metadata populated by the kernel code generator for TF.</summary>
            public List<OutputVaryingInfo> OutputVaryings { get; }
            /// <summary>Parameter binding metadata populated by the kernel code generator.</summary>
            public List<KernelParameterBinding> ParameterBindings { get; }
            /// <summary>StructureType.Id values for body-struct kernel parameters whose
            /// GLSL struct definitions should NOT be emitted (their fields are
            /// decomposed into per-field samplers).</summary>
            public HashSet<long> BodyStructTypeIdsToSkip { get; }
            /// <summary>Per-field metadata for body-struct kernel parameters. Consumed
            /// at dispatch time by WebGLAccelerator to decompose body-struct args into
            /// per-field buffer_ref entries.</summary>
            public List<BodyStructBindingEntry> BodyStructManifest { get; }
            /// <summary>Packed-4-bit (FP4/QInt4/QUInt4) view params passed BY VALUE into a
            /// [NoInlining] helper, keyed by (helper Method.Id, helper param Index). GLSL ES 3.0
            /// has no pointer types, so a packed-4-bit ArrayView fn-param is passed as a sampler
            /// TRIPLE (isampler2D + _tileW + _offset). The kernel generator populates this during
            /// its MethodCall scan (it knows the SOURCE kernel param's CLR type, hence QUInt4 vs
            /// QInt4 signedness); the helper generator (GLSLFunctionGenerator) reads it to emit the
            /// sampler-triple signature + the 8-nibbles/word texelFetch load. Without it the view
            /// param collapsed to a scalar `float`/`int` and the load produced an undeclared-var /
            /// garbage decode (the helper-gen packed-4-bit gap).</summary>
            public Dictionary<(long MethodId, int ParamIndex), PackedViewParamInfo> HelperPackedViewParams { get; }
        }

        /// <summary>
        /// Describes a packed-4-bit view param passed into a helper fn (see
        /// <see cref="GeneratorArgs.HelperPackedViewParams"/>).
        /// </summary>
        public readonly struct PackedViewParamInfo
        {
            public PackedViewParamInfo(bool isFloat4, bool isUnsigned)
            {
                IsFloat4 = isFloat4;
                IsUnsigned = isUnsigned;
            }
            /// <summary>FP4 (Float4E2M1): decode each nibble with _e2m1_to_f32. Otherwise QInt4/QUInt4.</summary>
            public bool IsFloat4 { get; }
            /// <summary>QUInt4 (zero-extend 0..15). Ignored when <see cref="IsFloat4"/>.</summary>
            public bool IsUnsigned { get; }
        }

        /// <summary>
        /// Represents a variable in GLSL code.
        /// </summary>
        public class Variable
        {
            public Variable(string name, string type)
            {
                Name = name;
                Type = type;
            }

            public string Name { get; }
            public string Type { get; }
            public override string ToString() => Name;
        }

        #endregion

        #region Instance

        protected int varCounter = 0;
        protected int labelCounter = 0;
        protected readonly Dictionary<Value, Variable> valueVariables = new();
        private readonly Dictionary<BasicBlock, string> blockLabels = new();
        // True while emitting a multi-block body: Declare() writes every declaration into
        // VariableBuilder, which is spliced in at function scope, so a variable defined inside
        // one structured branch or loop body is visible wherever it is used.
        protected bool HoistDeclarations { get; set; } = false;

        protected GLSLCodeGenerator(in GeneratorArgs args, Method method, Allocas allocas)
        {
            Backend = args.Backend;
            TypeGenerator = args.TypeGenerator;
            Method = method;
            Allocas = allocas;
            Builder = new StringBuilder();

            _cfg = method.Blocks.CreateCFG();
            _postDominators = _cfg.Blocks.CreatePostDominators();
            _loops = _cfg.CreateLoops();
        }

        #endregion

        #region Properties

        public WebGLBackend Backend { get; }
        public GLSLTypeGenerator TypeGenerator { get; }
        public Method Method { get; }
        public Allocas Allocas { get; }
        public StringBuilder Builder { get; protected set; }
        public StringBuilder VariableBuilder { get; } = new StringBuilder();
        public global::ILGPU.IR.Intrinsics.IntrinsicImplementationProvider<GLSLIntrinsic.Handler> ImplementationProvider => Backend.IntrinsicProvider;
        protected int IndentLevel { get; set; } = 0;

        #endregion

        #region IBackendCodeGenerator

        public abstract void GenerateHeader(StringBuilder builder);
        public abstract void GenerateCode();
        public void GenerateConstants(StringBuilder builder) { }
        public void Merge(StringBuilder builder) => builder.Append(Builder);

        #endregion

        #region Variable Management

        protected Variable Allocate(Value value)
        {
            var name = $"v_{varCounter++}";
            var type = TypeGenerator[value.Type];
            var variable = new Variable(name, type);
            valueVariables[value] = variable;
            return variable;
        }

        protected Variable AllocateType(TypeNode type)
        {
            var name = $"v_{varCounter++}";
            var glslType = TypeGenerator[type];
            return new Variable(name, glslType);
        }

        public Variable Load(Value value)
        {
            if (!valueVariables.TryGetValue(value, out var variable))
            {
                variable = Allocate(value);
            }
            return variable;
        }

        public Variable LoadIntrinsicValue(Value value) => Load(value);

        protected void Bind(Value value, Variable variable)
        {
            valueVariables[value] = variable;
        }

        protected readonly HashSet<string> declaredVariables = new();
        protected readonly HashSet<string> booleanVariables = new();
        // Maps LAEA pointer variable names to their array[index] expressions
        protected readonly Dictionary<string, string> _leaArrayExprs = new();
        // Maps Alloca value variable names to their GLSL array names
        protected readonly Dictionary<string, string> _allocaArrayNames = new();
        // Tracks Alloca values for array declarations
        protected int _localArrayCounter = 0;

        protected void Declare(Variable variable)
        {
            if (declaredVariables.Contains(variable.Name)) return;
            declaredVariables.Add(variable.Name);

            // Track boolean variables for operator selection (GLSL requires && || for booleans)
            if (variable.Type == "bool")
                booleanVariables.Add(variable.Name);

            if (HoistDeclarations)
            {
                VariableBuilder.Append("    ");
                VariableBuilder.Append(variable.Type);
                VariableBuilder.Append(" ");
                VariableBuilder.Append(variable.Name);
                VariableBuilder.AppendLine(";");
            }
            else
            {
                AppendIndent();
                Builder.Append(variable.Type);
                Builder.Append(" ");
                Builder.Append(variable.Name);
                Builder.AppendLine(";");
            }
        }

        /// <summary>
        /// Emit a typed GLSL variable declaration with initializer when this name is not yet
        /// declared in shader source. Returns true if a new declaration was emitted.
        /// </summary>
        protected bool TryEmitDeclaration(string name, string glslType, string initializer)
        {
            if (!declaredVariables.Add(name))
                return false;

            if (glslType == "bool")
                booleanVariables.Add(name);

            string line = $"{glslType} {name} = {initializer};";
            if (HoistDeclarations)
                VariableBuilder.AppendLine($"    {line}");
            else
                AppendLine(line);
            return true;
        }

        /// <summary>
        /// Declare or assign an int LEA pointer variable used for buffer/alloca indexing.
        /// </summary>
        protected void EmitLeaIntPointer(Variable target, string offsetExpr, string comment = "")
        {
            string init = $"int({offsetExpr})";
            string suffix = string.IsNullOrEmpty(comment) ? "" : $" // {comment}";
            if (!TryEmitDeclaration(target.Name, "int", init))
                AppendLine($"{target.Name} = {init};{suffix}");
        }

        /// <summary>
        /// Declare or assign a variable of the given GLSL type.
        /// </summary>
        protected void EmitTypedAssignment(Variable target, string glslType, string valueExpr, string comment = "")
        {
            string suffix = string.IsNullOrEmpty(comment) ? "" : $" // {comment}";
            if (!TryEmitDeclaration(target.Name, glslType, valueExpr))
                AppendLine($"{target} = {valueExpr};{suffix}");
        }

        #endregion

        #region Type Casting Helpers

        /// <summary>
        /// Wraps a variable reference in a GLSL type cast if the source type
        /// differs from the target type. Struct types are excluded from casting.
        /// </summary>
        protected static string CastIfNeeded(Variable source, string targetType)
        {
            if (source.Type == targetType) return source.ToString();
            // Don't cast struct types
            if (targetType.StartsWith("struct_") || source.Type.StartsWith("struct_")) return source.ToString();
            // Don't cast booleans
            if (targetType == "bool" || source.Type == "bool") return source.ToString();
            return $"{targetType}({source})";
        }

        /// <summary>
        /// Wraps a string expression in a GLSL type cast if the inferred type
        /// differs from the target type.
        /// </summary>
        protected static string CastIfNeeded(string expression, string targetType)
        {
            // For raw string expressions we can't infer source type, so wrap if target is numeric
            if (targetType == "int" || targetType == "uint" || targetType == "float")
                return $"{targetType}({expression})";
            return expression;
        }

        /// <summary>
        /// Returns a GLSL default/zero value expression for the given type.
        /// </summary>
        protected static string GetDefaultValue(string glslType)
        {
            return glslType switch
            {
                "int" => "0",
                "uint" => "0u",
                "float" => "0.0",
                "bool" => "false",
                "vec2" => "vec2(0.0)",
                "vec3" => "vec3(0.0)",
                "vec4" => "vec4(0.0)",
                "ivec2" => "ivec2(0)",
                "ivec3" => "ivec3(0)",
                "ivec4" => "ivec4(0)",
                "uvec2" => "uvec2(0u)",
                "uvec3" => "uvec3(0u)",
                "uvec4" => "uvec4(0u)",
                _ => $"{glslType}(0)"
            };
        }

        /// <summary>
        /// GLSL ES 3.0 struct constructor with one argument per field (nested structs recurse).
        /// Single-arg <c>struct_N(0)</c> is rejected by ANGLE ("constructor parameters does not
        /// match structure fields") - BVHRayTraversalTest / rc.16 fn-def Bug D.
        /// </summary>
        protected string GetStructDefaultInitializer(global::ILGPU.IR.Types.StructureType structType)
        {
            string glslType = TypeGenerator[structType];
            var sb = new StringBuilder();
            sb.Append(glslType);
            sb.Append('(');
            for (int i = 0; i < structType.NumFields; i++)
            {
                if (i > 0) sb.Append(", ");
                var fieldNode = structType.Fields[i];
                string fieldGlslType = TypeGenerator[fieldNode];
                if (fieldNode is global::ILGPU.IR.Types.StructureType nested
                    && fieldGlslType.StartsWith("struct_"))
                    sb.Append(GetStructDefaultInitializer(nested));
                else
                    sb.Append(GetDefaultValue(fieldGlslType));
            }
            sb.Append(')');
            return sb.ToString();
        }

        #endregion

        #region Label Management

        protected string DeclareLabel() => $"L_{Method.Id}_{labelCounter++}";

        protected string GetBlockLabel(BasicBlock block)
        {
            if (!blockLabels.TryGetValue(block, out var label))
            {
                label = DeclareLabel();
                blockLabels[block] = label;
            }
            return label;
        }

        protected void MarkLabel(string label) { }

        #endregion

        #region Code Emission Helpers

        protected void AppendIndent()
        {
            for (int i = 0; i < IndentLevel; i++)
                Builder.Append("    ");
        }

        protected void PushIndent() => IndentLevel++;
        protected void PopIndent() => IndentLevel--;

        protected void AppendLine(string line)
        {
            AppendIndent();
            Builder.AppendLine(line);
        }

        protected void AppendLineRaw(string line) => Builder.AppendLine(line);

        protected void BeginFunctionBody()
        {
            Builder.AppendLine("{");
            PushIndent();
        }

        protected void FinishFunctionBody()
        {
            PopIndent();
            Builder.AppendLine("}");
        }

        #endregion

        #region IR Traversal

        protected void GenerateCodeInternal()
        {
            var blocks = Method.Blocks;
            SetupAllocations(Allocas.LocalAllocations, MemoryAddressSpace.Local);

            if (blocks.Count == 1)
            {
                HoistDeclarations = false;
                var theBlock = blocks.First();
                foreach (var valueEntry in theBlock)
                    GenerateCodeFor(valueEntry.Value);
                // BasicBlock iteration yields only values, not the terminator
                // (BasicBlock.cs:241 iterates basicBlock.values; Terminator is
                // stored separately). For single-block methods we must emit
                // the terminator explicitly here, or non-void GLSL functions
                // fall off the end and return undefined values.
                if (theBlock.Terminator != null)
                    GenerateCodeFor(theBlock.Terminator);
                return;
            }

            // Multiple blocks: the SAME structured walker the kernel body uses (for/if/break/
            // continue - ANGLE's D3D11 backend cannot compile a switch/case state machine inside
            // a loop in a vertex shader). Every variable is declared at function scope, so a
            // value defined inside one branch or loop body is visible wherever it is used.
            // The helper-only emitter this replaced read each block's "terminator" from the
            // value enumeration, which never contains it - so every loop and branch in a
            // [NoInlining] helper was flattened into straight-line code.
            HoistDeclarations = true;
            int deferredInsertPosition = Builder.Length;
            GenerateStructuredBody();
            if (VariableBuilder.Length > 0)
                Builder.Insert(deferredInsertPosition, VariableBuilder.ToString());
        }

        /// <summary>
        /// Emits the method's blocks as structured control flow, starting at the entry block.
        /// </summary>
        protected void GenerateStructuredBody()
        {
            _visitedBlocks.Clear();
            _activeLoopHeaders.Clear();
            GenerateStructuredCode(Method.EntryBlock, null);
        }

        protected void SetupAllocations(AllocaKindInformation allocas, MemoryAddressSpace addressSpace)
        {
            foreach (var allocaInfo in allocas)
            {
                var variable = Allocate(allocaInfo.Alloca);
                var elementType = TypeGenerator[allocaInfo.ElementType];

                // `AllocaKindInformation.IsArray` returns false at N=1 (defined as
                // `ArraySize > 1`). The IR distinguishes scalar locals from
                // single-element arrays via `Alloca.IsArrayAllocation` - GLSL
                // codegen for `LoadArrayElementAddress` always emits `v[idx]`,
                // which is invalid when v is a scalar. Mirrors the WGSL fix in
                // WGSLCodeGenerator.SetupAllocations for `LocalMemory.Allocate<T>(1)`.
                //
                // Additional case: ILGPU IR can scalarize `LocalMemory.Allocate<T>(1)`
                // so that BOTH IsArray and IsArrayAllocation return false (the
                // ArrayLength gets optimized to non-primitive). The signal that
                // distinguishes "user-array, scalarized" from "compiler-scratch
                // scalar" is whether the alloca has a NewView consumer - the
                // former always does (LocalMemory.Allocate returns ArrayView
                // through NewView), the latter doesn't. Declare the user-array
                // case as a 1-element array so downstream LEA + Store/Load
                // preserve array semantics. Without this, GLSL emits a scalar
                // declaration but downstream NewView falls back to a comment-only
                // alias and v_5-style undeclared identifiers cascade.
                bool hasNewViewConsumer = false;
                foreach (var use in allocaInfo.Alloca.Uses)
                {
                    if (use.Resolve() is global::ILGPU.IR.Values.NewView)
                    {
                        hasNewViewConsumer = true;
                        break;
                    }
                }

                bool emitAsArray =
                    allocaInfo.IsArray
                    || allocaInfo.Alloca.IsArrayAllocation(out _)
                    || hasNewViewConsumer;

                if (emitAsArray)
                    AppendLine($"{elementType} {variable.Name}[{allocaInfo.ArraySize}];");
                else
                    AppendLine($"{elementType} {variable.Name};");
            }
        }

        #endregion

        #region Structured Control Flow

        // CFG analyses for the structured walker, computed once per method.
        protected readonly CFG<ReversePostOrder, Forwards> _cfg;
        protected readonly Dominators<Backwards> _postDominators;
        protected readonly Loops<ReversePostOrder, Forwards> _loops;
        protected readonly HashSet<BasicBlock> _visitedBlocks = new();
        protected readonly Stack<BasicBlock> _activeLoopHeaders = new();
        /// <summary>
        /// The current loop's header exit target. Used by EmitBreakWithIntermediateCode
        /// to distinguish body-break-specific blocks from the shared merge block.
        /// </summary>
        protected BasicBlock? _glslHeaderExitTarget;
        private int _loopCounter = 0;

        protected void GenerateBlockCode(BasicBlock block)
        {
            // Emit all non-terminator values in the block
            foreach (var value in block)
            {
                if (value.Value is TerminatorValue) continue;
                GenerateCodeFor(value.Value);
            }
        }

        /// <summary>
        /// Finds the innermost loop that has the given block as a header.
        /// </summary>
        protected Loops<ReversePostOrder, Forwards>.Node? FindLoopForHeader(BasicBlock block)
        {
            for (int i = 0; i < _loops.Count; i++)
            {
                var loop = _loops[i];
                foreach (var header in loop.Headers)
                {
                    if (header == block)
                        return loop;
                }
            }
            return null;
        }

        /// <summary>
        /// Checks if target is a back-edge to an active loop header (should emit 'continue').
        /// </summary>
        protected bool IsBackEdgeToActiveLoop(BasicBlock target)
        {
            return _activeLoopHeaders.Contains(target);
        }

        /// <summary>
        /// Emit a branch to target, handling loop continue/break/fall-through.
        /// Returns true if the branch was emitted as continue/break (caller should not recurse).
        /// </summary>
        protected bool EmitBranchTarget(BasicBlock target, BasicBlock source, BasicBlock? stop)
        {
            PushPhiValues(target, source);

            // Back-edge to active loop header → continue
            if (IsBackEdgeToActiveLoop(target))
            {
                AppendLine("continue;");
                return true;
            }

            // Target is the stop block → let caller handle it
            if (target == stop)
                return true;

            // Already visited → skip
            if (_visitedBlocks.Contains(target))
                return true;

            return false;
        }

        protected void GenerateStructuredCode(BasicBlock current, BasicBlock? stop)
        {
            if (current == null || current == stop || _visitedBlocks.Contains(current)) return;
            _visitedBlocks.Add(current);

            // Check if this block is a loop header
            var loop = FindLoopForHeader(current);
            if (loop != null)
            {
                // ANGLE D3D11 crashes on while(true) — use bounded for loop instead.
                // The loop body's own break/condition controls actual iteration.
                var loopVarName = $"_loop{_loopCounter++}";
                AppendLine($"for (int {loopVarName} = 0; {loopVarName} < 100000; {loopVarName}++) {{");
                PushIndent();

                _activeLoopHeaders.Push(current);

                // Detect the header's exit target before entering the loop body.
                // This is the first block outside the loop that the header's normal
                // exit leads to — used for post-loop continuation.
                BasicBlock? headerExitTarget = null;
                if (current.Terminator is IfBranch headerBranch)
                {
                    if (!loop.Contains(headerBranch.TrueTarget) || ExitsLoopTransitively(headerBranch.TrueTarget, loop))
                        headerExitTarget = headerBranch.TrueTarget;
                    else if (!loop.Contains(headerBranch.FalseTarget) || ExitsLoopTransitively(headerBranch.FalseTarget, loop))
                        headerExitTarget = headerBranch.FalseTarget;
                }
                _glslHeaderExitTarget = headerExitTarget;

                // Remove current from visited so we can re-enter it for the loop body
                _visitedBlocks.Remove(current);

                // Generate the loop body starting from the header
                GenerateLoopBody(current, loop, stop);

                _activeLoopHeaders.Pop();
                _glslHeaderExitTarget = null;

                PopIndent();
                AppendLine("}");

                // Continue with exit blocks after the loop.
                // Use the header's exit target for continuation instead of iterating
                // all loop.Exits — avoids processing body-break-specific intermediate
                // blocks (whose code was emitted inside the break scope).
                BasicBlock? postLoopBlock = headerExitTarget;
                if (postLoopBlock == null && loop.Exits.Length > 0)
                    postLoopBlock = loop.Exits[0]; // Fallback

                if (postLoopBlock != null)
                {
                    var exitBlock = postLoopBlock;
                    // Skip pass-through blocks
                    int skipLimit = 10;
                    while (exitBlock != null && exitBlock != stop
                        && !_visitedBlocks.Contains(exitBlock)
                        && skipLimit-- > 0)
                    {
                        if (exitBlock.Terminator is UnconditionalBranch exitUBranch
                            && !HasNonPhiInstructions(exitBlock))
                        {
                            _visitedBlocks.Add(exitBlock);
                            exitBlock = exitUBranch.Target;
                        }
                        else
                        {
                            break;
                        }
                    }
                    if (exitBlock != null && exitBlock != stop && !_visitedBlocks.Contains(exitBlock))
                    {
                        GenerateStructuredCode(exitBlock, stop);
                    }
                }
                return;
            }

            // Not a loop header — emit the block's values
            GenerateBlockCode(current);

            // Handle terminator
            var terminator = current.Terminator;

            if (terminator is ReturnTerminator rt)
            {
                GenerateCode(rt);
            }
            else if (terminator is UnconditionalBranch ub)
            {
                if (!EmitBranchTarget(ub.Target, current, stop))
                    GenerateStructuredCode(ub.Target, stop);
            }
            else if (terminator is IfBranch ib)
            {
                EmitIfBranch(ib, current, stop);
            }
            else if (terminator is SwitchBranch sb)
            {
                EmitSwitchBranch(sb, current, stop, null, null);
            }
            else if (terminator != null)
            {
                throw new NotSupportedException($"GLSL structured codegen: unhandled terminator {terminator.GetType().Name} in {Method.Name}.");
            }
        }

        /// <summary>
        /// Emits a <see cref="SwitchBranch"/> (a dense C# switch - case i is selector value i)
        /// as an if / else-if chain on the selector: GLSL ES 3.0 has `switch`, but ANGLE's D3D11
        /// backend cannot compile switch/case inside a loop in a vertex shader (the reason this
        /// walker exists). Each arm is handled like an if-branch target: outside a loop the arms
        /// run up to the switch block's post-dominator, which is then emitted once; inside a
        /// loop an arm is a continue (back edge), a break (loop exit) or loop-body code up to
        /// the in-loop merge. Before this, the walker had no SwitchBranch case at all and the
        /// switch - and everything after it in the loop body - silently vanished.
        /// </summary>
        protected void EmitSwitchBranch(
            SwitchBranch sb,
            BasicBlock source,
            BasicBlock? stop,
            Loops<ReversePostOrder, Forwards>.Node? loop,
            BasicBlock? outerStop)
        {
            var selector = Load(sb.Condition);
            var merge = _postDominators?.GetImmediateDominator(source);
            bool pinnedMerge = false;
            if (loop != null)
            {
                if (merge != null && !loop.Contains(merge))
                    merge = null; // every arm leaves through continue/break; no in-loop merge
                if (merge != null && !_visitedBlocks.Contains(merge))
                {
                    _visitedBlocks.Add(merge);
                    pinnedMerge = true;
                }
            }
            var visitedBeforeArms = new HashSet<BasicBlock>(_visitedBlocks);

            int caseCount = sb.NumCasesWithoutDefault;
            for (int i = 0; i <= caseCount; i++)
            {
                var target = i < caseCount ? sb.GetCaseTarget(i) : sb.DefaultBlock;
                if (caseCount == 0)
                    AppendLine("{");
                else if (i == 0)
                    AppendLine($"if ({selector} == {i}) {{");
                else if (i < caseCount)
                    AppendLine($"}} else if ({selector} == {i}) {{");
                else
                    AppendLine("} else {");
                PushIndent();
                // Each arm may re-reach a block another arm shares (the same target for several
                // cases, or a shared tail) - restore the pre-switch visited set per arm, exactly
                // like EmitIfBranch does for its false arm. A pinned merge stays pinned.
                _visitedBlocks.IntersectWith(visitedBeforeArms);
                PushPhiValues(target, source);
                if (loop == null)
                {
                    GenerateStructuredCode(target, merge);
                }
                else if (IsBackEdgeToActiveLoop(target))
                {
                    AppendLine("continue;");
                }
                else if (ExitsLoopTransitively(target, loop))
                {
                    EmitBreakWithIntermediateCode(target, source, loop);
                }
                else if (target != merge)
                {
                    GenerateLoopBody(target, loop, outerStop);
                }
                PopIndent();
            }
            AppendLine("}");

            if (loop == null)
            {
                if (merge != null && merge != stop)
                    GenerateStructuredCode(merge, stop);
            }
            else if (pinnedMerge)
            {
                _visitedBlocks.Remove(merge!);
                GenerateLoopBody(merge!, loop, outerStop);
            }
        }

        /// <summary>
        /// Generates the body of a loop (all blocks inside the loop).
        /// </summary>
        protected void GenerateLoopBody(BasicBlock current, Loops<ReversePostOrder, Forwards>.Node loop, BasicBlock? outerStop)
        {
            if (current == null || _visitedBlocks.Contains(current)) return;

            // Check if this block is a NESTED loop header (different from current loop)
            var nestedLoop = FindLoopForHeader(current);
            if (nestedLoop != null && nestedLoop != loop)
            {
                // Delegate to GenerateStructuredCode which emits the nested for() construct
                GenerateStructuredCode(current, outerStop);
                return;
            }

            _visitedBlocks.Add(current);

            // Emit the block's values
            GenerateBlockCode(current);

            // Handle terminator with loop-aware logic
            var terminator = current.Terminator;

            if (terminator is ReturnTerminator rt)
            {
                GenerateCode(rt);
            }
            else if (terminator is UnconditionalBranch ub)
            {
                PushPhiValues(ub.Target, current);

                if (IsBackEdgeToActiveLoop(ub.Target))
                {
                    AppendLine("continue;");
                }
                else if (ExitsLoopTransitively(ub.Target, loop))
                {
                    // Exit the loop — trace through intermediate blocks first
                    EmitBreakWithIntermediateCode(ub.Target, current, loop);
                }
                else
                {
                    GenerateLoopBody(ub.Target, loop, outerStop);
                }
            }
            else if (terminator is IfBranch ib)
            {
                EmitLoopIfBranch(ib, current, loop, outerStop);
            }
            else if (terminator is SwitchBranch sb)
            {
                EmitSwitchBranch(sb, current, null, loop, outerStop);
            }
            else if (terminator != null)
            {
                throw new NotSupportedException($"GLSL structured codegen: unhandled terminator {terminator.GetType().Name} in {Method.Name}.");
            }
        }

        /// <summary>
        /// Emits an if/else branch inside a loop body.
        /// </summary>
        protected void EmitLoopIfBranch(IfBranch ib, BasicBlock source, Loops<ReversePostOrder, Forwards>.Node loop, BasicBlock? outerStop)
        {
            var trueTarget = ib.TrueTarget;
            var falseTarget = ib.FalseTarget;
            var cond = Load(ib.Condition);

            bool trueIsBackEdge = IsBackEdgeToActiveLoop(trueTarget);
            bool falseIsBackEdge = IsBackEdgeToActiveLoop(falseTarget);
            // DEBUG IL FIX: Use transitive exit detection.
            // In Debug builds, Roslyn inserts intermediate blocks between
            // branches and the actual loop exit. Follow unconditional branch
            // chains to detect indirect exits.
            bool trueIsExit = ExitsLoopTransitively(trueTarget, loop);
            bool falseIsExit = ExitsLoopTransitively(falseTarget, loop);


            // Case 1: Loop condition check — one side continues, other exits
            if (trueIsBackEdge && falseIsExit)
            {
                // if (cond) continue; else break;
                AppendLine($"if (!{cond}) {{");
                PushIndent();
                PushPhiValues(falseTarget, source);
                EmitBreakWithIntermediateCode(falseTarget, source, loop);
                PopIndent();
                AppendLine("}");
                PushPhiValues(trueTarget, source);
                AppendLine("continue;");
                return;
            }
            if (falseIsBackEdge && trueIsExit)
            {
                AppendLine($"if ({cond}) {{");
                PushIndent();
                PushPhiValues(trueTarget, source);
                EmitBreakWithIntermediateCode(trueTarget, source, loop);
                PopIndent();
                AppendLine("}");
                PushPhiValues(falseTarget, source);
                AppendLine("continue;");
                return;
            }

            // Case 2: One side is a back-edge, other continues in-loop
            if (trueIsBackEdge)
            {
                AppendLine($"if ({cond}) {{");
                PushIndent();
                PushPhiValues(trueTarget, source);
                AppendLine("continue;");
                PopIndent();
                AppendLine("}");
                PushPhiValues(falseTarget, source);
                if (falseIsExit)
                    EmitBreakWithIntermediateCode(falseTarget, source, loop);
                else
                    GenerateLoopBody(falseTarget, loop, outerStop);
                return;
            }
            if (falseIsBackEdge)
            {
                AppendLine($"if (!{cond}) {{");
                PushIndent();
                PushPhiValues(falseTarget, source);
                AppendLine("continue;");
                PopIndent();
                AppendLine("}");
                PushPhiValues(trueTarget, source);
                if (trueIsExit)
                    EmitBreakWithIntermediateCode(trueTarget, source, loop);
                else
                    GenerateLoopBody(trueTarget, loop, outerStop);
                return;
            }

            // Case 3: One side exits the loop, other stays
            if (trueIsExit && !falseIsExit)
            {
                AppendLine($"if ({cond}) {{");
                PushIndent();
                PushPhiValues(trueTarget, source);
                EmitBreakWithIntermediateCode(trueTarget, source, loop);
                PopIndent();
                AppendLine("}");
                PushPhiValues(falseTarget, source);
                GenerateLoopBody(falseTarget, loop, outerStop);
                return;
            }
            if (falseIsExit && !trueIsExit)
            {
                AppendLine($"if (!{cond}) {{");
                PushIndent();
                PushPhiValues(falseTarget, source);
                EmitBreakWithIntermediateCode(falseTarget, source, loop);
                PopIndent();
                AppendLine("}");
                PushPhiValues(trueTarget, source);
                GenerateLoopBody(trueTarget, loop, outerStop);
                return;
            }

            // Case 4: Both sides stay in loop — use post-dominator for merge
            var merge = _postDominators?.GetImmediateDominator(source);
            bool mergeInLoop = merge != null && loop.Contains(merge);

            // FIX: When the post-dominator is outside the loop (due to a break-exit
            // path from one branch), find the in-loop merge by checking which target
            // reaches the header through a UB chain (it's the continuation block).
            if (!mergeInLoop && merge != null)
            {
                BasicBlock? inLoopMerge = null;
                if (loop.Contains(trueTarget) && ReachesHeaderThroughUBChain(trueTarget, loop))
                    inLoopMerge = trueTarget;
                else if (loop.Contains(falseTarget) && ReachesHeaderThroughUBChain(falseTarget, loop))
                    inLoopMerge = falseTarget;
                if (inLoopMerge != null)
                {
                    merge = inLoopMerge;
                    mergeInLoop = true;
                }
            }

            // EMIT EACH BLOCK ONCE (fixes exponential tail-duplication). PIN the in-loop merge
            // (the post-dominator = the block both branches converge on) as visited BEFORE emitting
            // the branches, so neither branch runs INTO the loop-body continuation — each stops at
            // the merge. The merge is then emitted exactly ONCE after the if/else.
            //
            // The OLD approach re-emitted the merge by resetting `_visitedBlocks` to the pre-branch
            // state after the if/else (`IntersectWith(visitedBeforeTrueBranch); GenerateLoopBody(merge)`).
            // Because the merge is the loop-body continuation, and that continuation contains further
            // Case-4 branches that each did the SAME reset+regenerate, the loop body was duplicated on
            // every nested conditional → 2^N. A 4×4 bicubic loop with ~15 bounds/weight conditionals
            // produced a 33 MB / 500K-line GLSL shader (`CubicWeight` inlined 5,561× vs the intended ~20)
            // that exhausted the Blazor WASM managed heap during compile. This mirrors the acyclic
            // `EmitIfBranch` (which passes `merge` as the `stop` and emits it once) and the WGSL backend,
            // which stays ~30 KB on the SAME IR.
            bool pinnedMerge = false;
            if (mergeInLoop && merge != null && !_visitedBlocks.Contains(merge))
            {
                _visitedBlocks.Add(merge);
                pinnedMerge = true;
            }

            // Snapshot AFTER pinning the merge. The || short-circuit restore below still lets the false
            // path re-reach a shared in-branch body block, but the merge stays pinned in the snapshot so
            // both branches keep stopping at it (the restore never un-pins it).
            var visitedBeforeTrueBranch = new HashSet<BasicBlock>(_visitedBlocks);

            AppendLine($"if ({cond}) {{");
            PushIndent();
            PushPhiValues(trueTarget, source);
            if (trueTarget != merge)
                GenerateLoopBody(trueTarget, loop, outerStop);
            PopIndent();
            AppendLine("} else {");
            PushIndent();
            PushPhiValues(falseTarget, source);
            // Restore visited to the pre-true-branch state so the false path can re-reach a shared
            // `||` body block (e.g. the PHI write-back + continue block in BVH traversal). The merge
            // is part of this snapshot, so it stays pinned — this is a bounded re-visit, not the old
            // unbounded merge-subtree regeneration.
            _visitedBlocks.IntersectWith(visitedBeforeTrueBranch);
            if (falseTarget != merge)
                GenerateLoopBody(falseTarget, loop, outerStop);
            PopIndent();
            AppendLine("}");

            // Emit the merge (the loop-body continuation) exactly ONCE, only if we pinned it here
            // (otherwise an enclosing scope owns and will emit it). Un-pin so GenerateLoopBody runs it.
            if (pinnedMerge)
            {
                _visitedBlocks.Remove(merge);
                GenerateLoopBody(merge, loop, outerStop);
            }
        }

        /// <summary>
        /// Emits an if/else branch outside a loop (acyclic structured flow).
        /// </summary>
        protected void EmitIfBranch(IfBranch ib, BasicBlock source, BasicBlock? stop)
        {
            var trueTarget = ib.TrueTarget;
            var falseTarget = ib.FalseTarget;
            var merge = _postDominators?.GetImmediateDominator(source);

            PushPhiValues(trueTarget, source);

            // SHORT-CIRCUIT FIX: Save visited state before true branch so that
            // blocks visited during the true path can be re-visited by the false
            // path. This is essential for || short-circuit patterns where both
            // branches converge on a shared body block (e.g. if (a || b) { body }).
            var visitedBeforeTrueBranch = new HashSet<BasicBlock>(_visitedBlocks);

            var cond = Load(ib.Condition);
            AppendLine($"if ({cond}) {{");
            PushIndent();
            GenerateStructuredCode(trueTarget, merge);
            PopIndent();
            AppendLine("} else {");
            PushIndent();
            PushPhiValues(falseTarget, source);
            // Restore visited state: remove blocks that were only visited during
            // the true branch, so the false branch can reach shared targets.
            _visitedBlocks.IntersectWith(visitedBeforeTrueBranch);
            GenerateStructuredCode(falseTarget, merge);
            PopIndent();
            AppendLine("}");

            if (merge != null && merge != stop)
                GenerateStructuredCode(merge, stop);
        }

        /// <summary>
        /// Assigns every phi of <paramref name="targetBlock"/> its operand for the edge from
        /// <paramref name="sourceBlock"/> as ONE parallel copy (<see cref="PhiParallelCopy"/>): a
        /// loop that rotates or swaps its loop-carried values must not read a phi this edge
        /// already overwrote. Every phi-copy site (structured walker, break chains) comes here.
        /// </summary>
        protected void PushPhiValues(BasicBlock targetBlock, BasicBlock sourceBlock)
        {
            var copies = new List<(Variable Target, Variable Source)>();
            foreach (var value in targetBlock)
            {
                if (value.Value is PhiValue phi)
                {
                    var targetVar = Load(phi);
                    for (int i = 0; i < phi.Count; i++)
                    {
                        if (phi.Sources[i] == sourceBlock)
                        {
                            if (!declaredVariables.Contains(targetVar.Name))
                                Declare(targetVar);
                            copies.Add((targetVar, Load(phi[i])));
                        }
                    }
                }
            }
            if (copies.Count == 0)
                return;

            var targets = new string[copies.Count];
            var sources = new Variable[copies.Count];
            var sourceNames = new string[copies.Count];
            for (int i = 0; i < copies.Count; i++)
            {
                targets[i] = copies[i].Target.Name;
                sources[i] = copies[i].Source;
                sourceNames[i] = copies[i].Source.Name;
            }
            var stage = PhiParallelCopy.SourcesToStage(targets, sourceNames);
            var read = new string[copies.Count];
            for (int i = 0; i < copies.Count; i++)
            {
                var target = copies[i].Target;
                read[i] = CastIfNeeded(sources[i], target.Type);
                if (stage != null && stage[i])
                {
                    // Staged at the TARGET's type, so the assignment below needs no cast.
                    string temp = $"_phi_copy_{_phiCopyTempCounter++}";
                    AppendLine($"{target.Type} {temp} = {read[i]};");
                    read[i] = temp;
                }
            }
            for (int i = 0; i < copies.Count; i++)
            {
                AppendLine($"{copies[i].Target} = {read[i]};");
                OnPhiCopied(copies[i].Target, sources[i]);
            }
        }

        private int _phiCopyTempCounter;

        /// <summary>
        /// Called after each phi copy so a derived generator can carry per-variable metadata
        /// (e.g. the kernel's buffer-pointer mappings) from the source to the phi.
        /// </summary>
        protected virtual void OnPhiCopied(Variable target, Variable source) { }

        /// <summary>
        /// Emits code for intermediate blocks between a break source and the loop exit,
        /// then emits the break statement. When ILGPU generates IR for `hitT = t; steps = i; break;`,
        /// the assignments end up in intermediate blocks between the break source and the
        /// merge/exit block. These blocks must have their code emitted before the GLSL break.
        /// 
        /// CRITICAL: Does NOT add intermediate blocks to _visitedBlocks — they may need
        /// to be re-entered by the post-loop code generator.
        /// </summary>
        protected void EmitBreakWithIntermediateCode(BasicBlock exitTarget, BasicBlock sourceBlock,
            Loops<ReversePostOrder, Forwards>.Node? currentLoop = null)
        {
            // Trace through intermediate blocks that have unconditional branches.
            // IMPORTANT: Stop when we reach a block outside the current loop — its PHIs
            // belong to an ancestor loop and will be handled by post-loop code emission.
            // Without this check, nested loops push PHI values for ancestor loop counters
            // that reference increment variables not yet computed (triple-nested loop bug).
            var current = exitTarget;
            int maxDepth = 8; // Safety limit
            for (int depth = 0; depth < maxDepth; depth++)
            {
                if (current.Terminator is UnconditionalBranch uBranch)
                {
                    // Stop if next block is outside current loop
                    if (currentLoop != null && !currentLoop.Contains(uBranch.Target))
                        break;
                    // Emit this intermediate block's code (assignments like hitT = t)
                    GenerateBlockCode(current);
                    // Push PHI values to the next block
                    PushPhiValues(uBranch.Target, current);
                    current = uBranch.Target;
                }
                else
                {
                    break; // Not an unconditional branch, stop tracing
                }
            }

            // Follow the exit chain OUTSIDE the loop to find merge-point PHIs.
            // When a loop has multiple exit paths (header normal exit + body break),
            // they converge at a merge block whose PHIs need values from ALL exits.
            // The exit block may be a pass-through (no PHIs) that chains to the merge.
            // This handles the LoopBreakAssignment pattern where break-path assignments
            // need to reach the post-loop merge block.
            if (currentLoop != null)
            {
                // FIX: If the current block is outside the loop and is NOT the header's
                // exit target, it's a body-break-specific intermediate block (e.g.,
                // contains `flagged = true`). Emit its code inside the break scope
                // and mark visited so it's not re-emitted after the loop.
                if (!currentLoop.Contains(current)
                    && _glslHeaderExitTarget != null
                    && current != _glslHeaderExitTarget
                    && HasNonPhiInstructions(current))
                {
                    GenerateBlockCode(current);
                    _visitedBlocks.Add(current);
                }

                // Push PHIs through the exit chain (outside the loop)
                // Stop at parent loop headers to avoid overwriting their PHIs
                var exitChain = current;
                for (int depth = 0; depth < maxDepth; depth++)
                {
                    if (exitChain.Terminator is UnconditionalBranch exitUB)
                    {
                        var next = exitUB.Target;
                        if (IsBlockLoopHeader(next)) break;
                        // Only push if we're outside the loop (don't re-push inside)
                        if (!currentLoop.Contains(exitChain))
                        {
                            PushPhiValues(next, exitChain);
                        }
                        exitChain = next;
                    }
                    else break;
                }
            }

            // Check if the exit path leads directly to a ReturnTerminator
            // (through empty pass-through blocks). If so, this is a genuine kernel
            // return inside the loop, not a break to post-loop code.
            // A break whose exit chain leads straight to the function's return (through empty
            // phi-only blocks) IS that return, emitted in place - GenerateCode(ReturnTerminator)
            // returns the phi-carried value in a helper and emits plain `return;` in a kernel.
            if (TryGetReturnExit(current, out var returnExit))
                GenerateCode(returnExit);
            else
                AppendLine("break;");
        }

        /// <summary>
        /// Checks if a block is a loop header for any loop in the kernel.
        /// </summary>
        protected bool IsBlockLoopHeader(BasicBlock block)
        {
            foreach (var loop in _loops)
                foreach (var header in loop.Headers)
                    if (header == block) return true;
            return false;
        }

        /// <summary>
        /// DEBUG IL FIX: Checks whether a block exits the loop, either directly
        /// or transitively through a chain of unconditional branches.
        /// 
        /// In Debug builds, Roslyn inserts intermediate basic blocks between
        /// control flow decisions and their actual targets. This method follows
        /// such chains to determine if the eventual destination is outside the loop.
        /// </summary>
        protected static bool ExitsLoopTransitively(
            BasicBlock target,
            Loops<ReversePostOrder, Forwards>.Node loop)
        {
            // Fast path: direct exit
            if (!loop.Contains(target))
                return true;

            // Follow unconditional branch chains through intermediate blocks
            var current = target;
            int maxDepth = 10; // Safety limit
            for (int i = 0; i < maxDepth; i++)
            {
                if (current.Terminator is UnconditionalBranch uBranch)
                {
                    if (!loop.Contains(uBranch.Target))
                        return true;
                    current = uBranch.Target;
                }
                else
                {
                    break;
                }
            }
            return false;
        }

        /// <summary>
        /// DEBUG IL FIX: Checks if a block contains any non-PHI, non-terminator instructions.
        /// Pure pass-through blocks can be skipped in post-loop exit chain processing
        /// since their PHI values were already handled by the loop's break paths.
        /// </summary>
        /// <summary>
        /// Check if a block reaches a loop header through an unconditional branch chain.
        /// Used to identify the loop's continuation block (back-edge source).
        /// </summary>
        protected static bool ReachesHeaderThroughUBChain(BasicBlock block, Loops<ReversePostOrder, Forwards>.Node loop)
        {
            var current = block;
            for (int i = 0; i < 10; i++)
            {
                if (current.Terminator is UnconditionalBranch ub)
                {
                    foreach (var header in loop.Headers)
                    {
                        if (ub.Target == header) return true;
                    }
                    if (!loop.Contains(ub.Target)) return false;
                    current = ub.Target;
                }
                else return false;
            }
            return false;
        }

        protected static bool HasNonPhiInstructions(BasicBlock block)
        {
            foreach (var value in block)
            {
                if (value.Value is TerminatorValue) continue;
                if (value.Value is PhiValue) continue;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if a block (or chain of unconditional branches from it) leads DIRECTLY
        /// to a ReturnTerminator without passing through any blocks that contain real code.
        /// Only follows empty pass-through blocks (PHI-only + unconditional branch/return).
        /// A block with non-PHI instructions is post-loop code, not a direct return path.
        /// Ported from WGSLKernelFunctionGenerator.IsReturnExit; returns the terminator so the
        /// caller emits the real return (a helper's return carries its value).
        /// </summary>
        protected static bool TryGetReturnExit(BasicBlock block, out ReturnTerminator returnTerminator)
        {
            returnTerminator = null!;
            int limit = 10;
            var current = block;
            while (current != null && limit-- > 0)
            {
                if (HasNonPhiInstructions(current))
                    return false;
                if (current.Terminator is ReturnTerminator rt)
                {
                    returnTerminator = rt;
                    return true;
                }
                if (current.Terminator is UnconditionalBranch ub)
                    current = ub.Target;
                else
                    break;
            }
            return false;
        }

        #endregion

        #region Value Visitors - Dispatch

        protected void GenerateCodeFor(Value value)
        {
            // Same exclusion list as WGSL: void-typed Values without observable
            // side effects are skipped, but void-returning MethodCalls must be
            // visited (helpers may write through ref/out pointer params).
            if (value.Type.IsVoidType &&
                !(value is TerminatorValue) &&
                !(value is Store) &&
                !(value is MemoryBarrier) &&
                !(value is global::ILGPU.IR.Values.Barrier) &&
                !(value is PredicateBarrier) &&
                !(value is MethodCall))
                return;

            if (WebGLBackend.VerboseLogging) WebGLBackend.Log($"[GLSL] Generating code for: {value.GetType().FullName} - {value}");

            if (value.GetType().Name.Contains("Throw"))
            {
                GenerateThrow(value);
                return;
            }

            if (ImplementationProvider.TryGetCodeGenerator(value, out var intrinsicCodeGenerator))
            {
                intrinsicCodeGenerator(Backend, this, value);
                return;
            }

            switch (value)
            {
                case global::ILGPU.IR.Values.Parameter p: GenerateCode(p); break;
                case global::ILGPU.IR.Values.MethodCall v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.BinaryArithmeticValue v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.UnaryArithmeticValue v: GenerateUnOp(v); break;
                case global::ILGPU.IR.Values.TernaryArithmeticValue v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.CompareValue v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.ConvertValue v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.Load v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.Store v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.LoadElementAddress v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.LoadArrayElementAddress v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.NewArray v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.LoadFieldAddress v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.Alloca v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.NewView v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.PrimitiveValue v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.NullValue v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.StringValue v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.PhiValue v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.StructureValue v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.GetField v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.SetField v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.GridIndexValue v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.GroupIndexValue v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.GridDimensionValue v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.GroupDimensionValue v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.WarpSizeValue v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.LaneIdxValue v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.ReturnTerminator v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.UnconditionalBranch v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.IfBranch v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.SwitchBranch v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.IntAsPointerCast v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.PointerAsIntCast v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.PointerCast v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.AddressSpaceCast v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.FloatAsIntCast v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.IntAsFloatCast v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.GenericAtomic v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.AtomicCAS v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.MemoryBarrier v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.Barrier v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.PredicateBarrier v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.Broadcast v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.WarpShuffle v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.SubWarpShuffle v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.DebugAssertOperation v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.WriteToOutput v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.Predicate v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.DynamicMemoryLengthValue v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.GetViewLength v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.AlignTo v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.AsAligned v: GenerateCode(v); break;
                case global::ILGPU.IR.Values.LanguageEmitValue v: GenerateCode(v); break;
                default:
                    AppendLine($"// Unhandled value type: {value.GetType().Name}");
                    break;
            }
        }

        #endregion

        #region Value Visitors - Implementation

        public virtual void GenerateCode(Parameter parameter) { }

        public virtual void GenerateCode(BinaryArithmeticValue value)
        {
            var target = Load(value);
            var left = Load(value.Left);
            var right = Load(value.Right);
            Declare(target);

            // emu_f64 dispatch - the base path serves [NoInlining] helper functions, where
            // `double + double` otherwise came out component-wise (vec2 +, min(vec2, vec2)). Runs
            // FIRST: the Min/Max branch below would otherwise catch emulated operands.
            {
                string lt = TypeGenerator[value.Left.Type], rt = TypeGenerator[value.Right.Type];
                bool emuF64 = Backend.EnableF64Emulation && (lt == "vec2" || rt == "vec2"
                    || (Backend.UseOzakiF64Emulation && (lt == "vec4" || rt == "vec4")));
                string? emulF64Func = !emuF64 ? null : value.Kind switch
                {
                    BinaryArithmeticKind.Add => "f64_add",
                    BinaryArithmeticKind.Sub => "f64_sub",
                    BinaryArithmeticKind.Mul => "f64_mul",
                    BinaryArithmeticKind.Div => "f64_div",
                    BinaryArithmeticKind.Min => "f64_min",
                    BinaryArithmeticKind.Max => "f64_max",
                    BinaryArithmeticKind.Rem => "f64_rem",
                    BinaryArithmeticKind.PowF => "f64_pow",
                    BinaryArithmeticKind.Atan2F => "f64_atan2",
                    BinaryArithmeticKind.CopySignF => "f64_copysign",
                    _ => null
                };
                if (emulF64Func != null)
                {
                    AppendLine($"{target} = {emulF64Func}({left}, {right});");
                    return;
                }
                // Emulated 64-bit integer Min/Max: GLSL min()/max() on uvec2 are per word.
                if (Backend.EnableI64Emulation && (lt == "uvec2" || rt == "uvec2")
                    && (value.Kind == BinaryArithmeticKind.Min || value.Kind == BinaryArithmeticKind.Max))
                {
                    string fn = (value.IsUnsigned ? "u64_" : "i64_") + (value.Kind == BinaryArithmeticKind.Min ? "min" : "max");
                    AppendLine($"{target} = {fn}({left}, {right});");
                    return;
                }
            }
            string leftType = TypeGenerator[value.Left.Type];
            bool leftIsEmuI64 = Backend.EnableI64Emulation
                && (leftType == "uvec2" && value.Left.BasicValueType == BasicValueType.Int64);
            if (leftIsEmuI64
                && (value.Kind == BinaryArithmeticKind.Shl || value.Kind == BinaryArithmeticKind.Shr))
            {
                string shiftFn = value.Kind == BinaryArithmeticKind.Shl
                    ? "i64_shl"
                    : (value.IsUnsigned ? "u64_shr" : "i64_shr");
                AppendLine($"{target} = {ConstantEmulatedShiftOrCall(shiftFn, left.ToString(), value.Right, right.ToString())};");
                return;
            }

            // Float remainder
            if (value.Kind == BinaryArithmeticKind.Rem && TypeGenerator[value.Left.Type].StartsWith("float"))
            {
                // C# float % truncates (fmod): -7.5f % 2f == -1.5f. floor() gave the floored modulo (0.5).
                AppendLine($"{target} = {left} - {right} * trunc({left} / {right});");
                return;
            }

            if (value.Kind == BinaryArithmeticKind.Min || value.Kind == BinaryArithmeticKind.Max)
            {
                string func = value.Kind == BinaryArithmeticKind.Min ? "min" : "max";
                AppendLine($"{target} = {func}({left}, {right});");
                return;
            }

            if (value.Kind == BinaryArithmeticKind.PowF)
            {
                // GLSL `pow(x, y)` is undefined for x < 0; ANGLE emits it as
                // `exp(y * log(x))` and `log(negative_x)` is NaN. For LayerNorm's variance
                // step `(x - mean)^2`, half the inputs are negative — every NaN cascades
                // through ReduceMean -> Sqrt -> Mul -> Add to produce NaN logits.
                //
                // Two-tier fix:
                // (a) Static const detection: if the exponent is a literal PrimitiveValue
                //     non-negative integer (0..8), expand to repeated multiplication.
                //     Cheapest path. (rc.12 fix)
                // (b) Runtime-safe wrapper: when (a) doesn't catch (e.g., exponent loaded
                //     from an ONNX initializer ArrayView — DistilBERT LayerNorm), emit
                //     a runtime branch that handles negative base for integer exponents.
                //     Surfaced 2026-05-04 by Data's WebGL DistilBERT first-divergent at
                //     node 10 Pow: ONNX exponent comes via Load(initializer_buffer) so
                //     value.Right.Resolve() is the Load node, not PrimitiveValue, and (a)
                //     falls through. (rc.21+ fix)
                if (value.Right.Resolve() is PrimitiveValue pv)
                {
                    float pf = pv.BasicValueType == BasicValueType.Float32 || pv.BasicValueType == BasicValueType.Float16
                        ? pv.Float32Value
                        : (pv.BasicValueType == BasicValueType.Float64 ? (float)pv.Float64Value : float.NaN);
                    if (!float.IsNaN(pf) && pf >= 0f && pf <= 8f && pf == (int)pf)
                    {
                        int n = (int)pf;
                        if (n == 0) { AppendLine($"{target} = 1.0;"); return; }
                        if (n == 1) { AppendLine($"{target} = {left};"); return; }
                        var sb = new StringBuilder();
                        sb.Append(left);
                        for (int i = 1; i < n; i++) sb.Append(" * ").Append(left);
                        AppendLine($"{target} = {sb};");
                        return;
                    }
                }
                // Runtime-safe Pow: handles negative base for integer-ish exponents
                // without NaN. For x >= 0, native pow is correct. For x < 0:
                //   - integer exponent: pow(abs(x), y) with sign correction for odd y
                //   - non-integer exponent (mathematically undefined for negative base):
                //     return pow(abs(x), y), at least finite — caller's choice of input
                //     was already invalid.
                AppendLine($"{target} = ({left} >= 0.0 ? pow({left}, {right}) : pow(abs({left}), {right}) * (mod({right}, 2.0) >= 1.0 ? -1.0 : 1.0));");
                return;
            }

            if (value.Kind == BinaryArithmeticKind.Atan2F)
            {
                AppendLine($"{target} = atan({left}, {right});");
                return;
            }

            if (value.Kind == BinaryArithmeticKind.BinaryLogF)
            {
                // log_base(x) = log(x) / log(base)
                AppendLine($"{target} = log({left}) / log({right});");
                return;
            }

            if (value.Kind == BinaryArithmeticKind.CopySignF)
            {
                // Sign BIT of y: abs(x) * sign(y) zeroed the result for y == 0 and dropped -0.0.
                AppendLine($"{target} = {CopySignExpression(left.ToString(), right.ToString())};");
                return;
            }

            // Emulated 64-bit shift/add/sub/mul dispatch — closes
            // Tests23_I64Shift_InHelper_NoCodegenError on WebGL (mirrors WGSL local.8 fix
            // `WGSLCodeGenerator.GenerateBinOp`). Pre-fix GLSL emitted `uvec2 = uvec2 >> int`
            // which performs COMPONENT-WISE shift (`(a.x >> shift, a.y >> shift)`) — losing
            // the carry from hi to lo. For input 0x1234567890ABCDEF >> 8 the GLSL produced
            // 0x0090ABCD (just `0x90ABCDEF >> 8`) instead of 0x7890ABCD.
            //
            // Bug #5 (2026-09-22): this method (the fallback path GLSLFunctionGenerator uses
            // for standalone NoInlining helper functions — GLSLKernelFunctionGenerator has its
            // own override with a correct full i64 dispatch table) only special-cased Shl/Shr
            // here; Add and Sub fell all the way through to the native `+`/`-` operators below.
            // `uvec2 + uvec2` is GLSL's built-in COMPONENT-WISE addition (`a.x+b.x, a.y+b.y`
            // independently) with NO carry from the lo word's overflow into the hi word - wrong
            // for our lo/hi emulated-64-bit representation whenever the lo addition overflows.
            // A Blake2b-G-shaped NoInlining helper (SpawnDev.ILGPU.Crypto.Blake2b.G) called
            // repeatedly produced a result wrong by exactly one bit at the lo/hi boundary,
            // reproducing identically in a hand-written raw-GLSL harness completely outside
            // ILGPU (root-caused via NoInliningBlakeShapedTripleCallTest): correct for small
            // values (the lo addition never overflows on the very first call), silently wrong
            // once accumulated mixing grows a word past 2^32 in the lo word specifically. Mul is
            // the same class of bug (`uvec2 * uvec2` is component-wise, not a real 64-bit
            // multiply) and is fixed alongside Add/Sub even though no failing test hit it yet -
            // it is exactly as wrong for the same reason. And/Or/Xor are NOT touched: those are
            // genuinely bitwise-independent per word, so the native component-wise operators
            // already give the correct 64-bit result.
            if (leftIsEmuI64 && value.Kind is BinaryArithmeticKind.Add or BinaryArithmeticKind.Sub or BinaryArithmeticKind.Mul)
            {
                string emulFn = value.Kind switch
                {
                    BinaryArithmeticKind.Add => "i64_add",
                    BinaryArithmeticKind.Sub => "i64_sub",
                    _ => value.IsUnsigned ? "u64_mul" : "i64_mul",
                };
                AppendLine($"{target} = {emulFn}({left}, {right});");
                return;
            }

            // Check if this is a boolean operation. GLSL requires logical operators
            // (&&, ||) for booleans, not bitwise (&, |).
            // The target variable's Type is set from TypeGenerator[value.Type] in Allocate().
            bool isBoolOp = target.Type == "bool";

            string op = value.Kind switch
            {
                BinaryArithmeticKind.Add => "+",
                BinaryArithmeticKind.Sub => "-",
                BinaryArithmeticKind.Mul => "*",
                BinaryArithmeticKind.Div => "/",
                BinaryArithmeticKind.And => isBoolOp ? "&&" : "&",
                BinaryArithmeticKind.Or => isBoolOp ? "||" : "|",
                BinaryArithmeticKind.Xor => "^",
                BinaryArithmeticKind.Shl => "<<",
                BinaryArithmeticKind.Shr => ">>",
                BinaryArithmeticKind.Rem => "%",
                _ => "+"
            };

            // GLSL ES 3.0 requires explicit casts — no implicit int<->float conversion.
            // Cast operands to match the target type when they differ.
            string leftExpr = CastIfNeeded(left, target.Type);
            string rightExpr = CastIfNeeded(right, target.Type);

            // For unsigned int Shl/Shr/Div/Rem on Int32, GLSL ES 3.0's signed-int
            // operators give wrong results when the high bit is set. ILGPU stores
            // uint as Int32 with `IsUnsigned` flag on the BinaryArithmeticValue.
            // Cast through uint and back to int/uint (bit pattern preserved).
            //
            // - Shr: i32 >> u32 is arithmetic shift — sign-extends a high-bit-set
            //   uint operand. Tuvok's libopus `0x80000000u >> 15` gave 0xFFFF0000
            //   instead of 0x00010000 (parallel WGSL fix landed in rc.12).
            // - Shl into the sign bit can trigger ANGLE inconsistency; uint shift
            //   has well-defined wraparound semantics.
            // - Div / Rem on i32 with high-bit-set operand uses signed div/rem; for
            //   `0x80000000u / 6u` the signed result is 0xEAAAAAAB (wrong) instead
            //   of 0x15555555 (unsigned). Tuvok's `OpusRangeDecoderGpu_DecodeUint_*`
            //   surfaced this on WebGL 2026-05-04.
            bool isInt32 = value.Left.BasicValueType == BasicValueType.Int32;
            bool isIntTargetType = target.Type == "int" || target.Type == "uint";
            bool isShlOrShr = value.Kind == BinaryArithmeticKind.Shl
                || value.Kind == BinaryArithmeticKind.Shr;
            bool isDivOrRem = value.Kind == BinaryArithmeticKind.Div
                || value.Kind == BinaryArithmeticKind.Rem;
            if (isInt32 && isIntTargetType && (
                    (value.IsUnsigned && (isShlOrShr || isDivOrRem))
                    || value.Kind == BinaryArithmeticKind.Shl))
            {
                // Wrap to target type; for `int` cast back, for `uint` no cast.
                if (target.Type == "int")
                    AppendLine($"{target} = int(uint({leftExpr}) {op} uint({rightExpr}));");
                else
                    AppendLine($"{target} = uint({leftExpr}) {op} uint({rightExpr});");
            }
            else
            {
                AppendLine($"{target} = {leftExpr} {op} {rightExpr};");
            }
        }

        public virtual void GenerateCode(UnaryArithmeticValue value) => GenerateUnOp(value);

        /// <summary>
        /// Transcendental on an emulated f64, computed in f32 (f32 accuracy - the emulation
        /// libraries have no double-precision transcendentals). Applying the builtin to the
        /// emulated vector itself works per COMPONENT: cos(0.5) came out 1.8776 (cos(hi) + cos(lo)).
        /// </summary>
        private static string? EmulatedF64ViaF32(UnaryArithmeticKind kind, string operand) => kind switch
        {
            UnaryArithmeticKind.SinF => $"f64_from_f32(sin(f64_to_f32({operand})))",
            UnaryArithmeticKind.CosF => $"f64_from_f32(cos(f64_to_f32({operand})))",
            UnaryArithmeticKind.TanF => $"f64_from_f32(tan(f64_to_f32({operand})))",
            UnaryArithmeticKind.AsinF => $"f64_from_f32(asin(f64_to_f32({operand})))",
            UnaryArithmeticKind.AcosF => $"f64_from_f32(acos(f64_to_f32({operand})))",
            UnaryArithmeticKind.AtanF => $"f64_from_f32(atan(f64_to_f32({operand})))",
            UnaryArithmeticKind.SinhF => $"f64_from_f32(sinh(f64_to_f32({operand})))",
            UnaryArithmeticKind.CoshF => $"f64_from_f32(cosh(f64_to_f32({operand})))",
            UnaryArithmeticKind.TanhF => $"f64_from_f32(tanh(f64_to_f32({operand})))",
            UnaryArithmeticKind.ExpF => $"f64_from_f32(exp(f64_to_f32({operand})))",
            UnaryArithmeticKind.Exp2F => $"f64_from_f32(exp2(f64_to_f32({operand})))",
            UnaryArithmeticKind.LogF => $"f64_from_f32(log(f64_to_f32({operand})))",
            UnaryArithmeticKind.Log2F => $"f64_from_f32(log2(f64_to_f32({operand})))",
            UnaryArithmeticKind.Log10F => $"f64_from_f32(log(f64_to_f32({operand})) / 2.302585093)",
            _ => null
        };

        private void GenerateUnOp(UnaryArithmeticValue value)
        {
            var target = Load(value);
            var operand = Load(value.Value);
            Declare(target);

            var operandType = TypeGenerator[value.Value.Type];
            if (Backend.EnableI64Emulation && (operandType == "uvec2") && value.Kind == UnaryArithmeticKind.Neg)
            {
                AppendLine($"{target} = i64_neg({operand});");
                return;
            }

            bool isEmuF64 = operandType == "vec2" || (Backend.UseOzakiF64Emulation && operandType == "vec4");
            // Emulated f64: the builtins below work per COMPONENT of the (hi, lo[, ...]) vector,
            // which is only right for Neg. Abs of hi = 12345679, lo = -0.25 gave 12345679.25.
            if (Backend.EnableF64Emulation && isEmuF64)
            {
                string? f64Expr = value.Kind switch
                {
                    UnaryArithmeticKind.Neg => $"f64_neg({operand})",
                    UnaryArithmeticKind.Abs => $"f64_abs({operand})",
                    UnaryArithmeticKind.FloorF => $"f64_floor({operand})",
                    UnaryArithmeticKind.CeilingF => $"f64_ceil({operand})",
                    UnaryArithmeticKind.SqrtF => $"f64_sqrt({operand})",
                    UnaryArithmeticKind.RsqrtF => $"f64_div(f64_from_f32(1.0), f64_sqrt({operand}))",
                    UnaryArithmeticKind.RcpF => $"f64_div(f64_from_f32(1.0), {operand})",
                    _ => EmulatedF64ViaF32(value.Kind, operand.ToString())
                };
                if (f64Expr != null)
                {
                    AppendLine($"{target} = {f64Expr};");
                    return;
                }
            }
            // Emulated 64-bit integer Abs: abs() of the unsigned words was an identity (WGSL) or
            // no overload at all (GLSL).
            if (Backend.EnableI64Emulation && operandType == "uvec2" && value.Kind == UnaryArithmeticKind.Abs)
            {
                AppendLine($"{target} = i64_abs({operand});");
                return;
            }

            // Emulated emu_f64 source needs different intrinsic codegen for IsNaN
            // / IsInfinity: GLSL `isnan` / `isinf` operate on `float` only, not on
            // `vec2`. Route to f64_is_nan / f64_is_inf helpers from
            // GLSLEmulationLibrary which check the high f32 lane. Pass the result
            // through CastIfNeeded (same path the f32 IsNaN/IsInf emission uses)
            // so a bool target type is handled correctly - emitting
            // `bool_target = (int)` directly trips "cannot convert from 'int' to
            // 'bool'" GLSL parser errors.
            if (Backend.EnableF64Emulation && isEmuF64)
            {
                // f64 IsNaN/IsInf return bool. Emit a bool expression and only
                // wrap with the int-ternary when the IR target type is numeric -
                // GLSL ES 3.0 forbids implicit bool↔int conversion.
                string? f64Bool = value.Kind switch
                {
                    UnaryArithmeticKind.IsNaNF => $"f64_is_nan({operand})",
                    UnaryArithmeticKind.IsInfF => $"f64_is_inf({operand})",
                    UnaryArithmeticKind.IsFinF => $"(!f64_is_nan({operand}) && !f64_is_inf({operand}))",
                    _ => null
                };
                if (f64Bool != null)
                {
                    if (target.Type == "bool")
                        AppendLine($"{target} = {f64Bool};");
                    else
                        AppendLine($"{target} = ({f64Bool}) ? {target.Type}(1) : {target.Type}(0);");
                    return;
                }
            }

            string result = value.Kind switch
            {
                UnaryArithmeticKind.Neg => $"-{operand}",
                UnaryArithmeticKind.Not => TypeGenerator[value.Value.Type] == "bool" ? $"!{operand}" : $"~{operand}",
                UnaryArithmeticKind.Abs => $"abs({operand})",
                UnaryArithmeticKind.SinF => $"sin({operand})",
                UnaryArithmeticKind.CosF => $"cos({operand})",
                UnaryArithmeticKind.TanF => $"tan({operand})",
                UnaryArithmeticKind.AsinF => $"asin({operand})",
                UnaryArithmeticKind.AcosF => $"acos({operand})",
                UnaryArithmeticKind.AtanF => $"atan({operand})",
                UnaryArithmeticKind.SinhF => $"sinh({operand})",
                UnaryArithmeticKind.CoshF => $"cosh({operand})",
                UnaryArithmeticKind.TanhF => $"tanh({operand})",
                UnaryArithmeticKind.ExpF => $"exp({operand})",
                UnaryArithmeticKind.Exp2F => $"exp2({operand})",
                UnaryArithmeticKind.LogF => $"log({operand})",
                UnaryArithmeticKind.Log2F => $"log2({operand})",
                UnaryArithmeticKind.SqrtF => $"sqrt({operand})",
                UnaryArithmeticKind.RsqrtF => $"inversesqrt({operand})",
                UnaryArithmeticKind.RcpF => $"1.0 / {operand}",
                UnaryArithmeticKind.FloorF => $"floor({operand})",
                UnaryArithmeticKind.CeilingF => $"ceil({operand})",
                UnaryArithmeticKind.IsNaNF => $"(isnan({operand}) ? 1 : 0)",
                UnaryArithmeticKind.IsInfF => $"(isinf({operand}) ? 1 : 0)",
                UnaryArithmeticKind.IsFinF => $"((!isnan({operand}) && !isinf({operand})) ? 1 : 0)",
                UnaryArithmeticKind.Log10F => $"(log({operand}) / log(10.0))",
                // PopC: Hamming weight via Kernighan's algorithm — not available in all WebGL2 vertex shader implementations
                UnaryArithmeticKind.PopC => EmitPopC(target, operand),
                // CLZ: count leading zeros via binary search
                UnaryArithmeticKind.CLZ => EmitCLZ(target, operand),
                // CTZ: count trailing zeros via binary search 
                UnaryArithmeticKind.CTZ => EmitCTZ(target, operand),
                _ => "DEBUG_MISSING"
            };

            if (result == null)
            {
                // Multi-line emission (PopC/CLZ/CTZ) already assigned target directly
                return;
            }

            if (result == "DEBUG_MISSING")
            {
                AppendLine($"// [GLSL] Unhandled UnaryArithmeticKind: {value.Kind}");
                result = $"{operand}";
            }

            // GLSL ES 3.0: cast result to target type if needed
            string castResult = CastIfNeeded(result, target.Type);
            AppendLine($"{target} = {castResult};");
        }

        /// <summary>
        /// Emits PopCount (Hamming weight) using the parallel bit-count algorithm.
        /// bitCount/popcount is not reliably available in WebGL2 ANGLE vertex shaders.
        /// </summary>
        private string EmitPopC(Variable target, Variable operand)
        {
            // Parallel bit-count (Hamming weight) — works entirely with int math
            var tmp = $"_popc_{operand}";
            AppendLine($"int {tmp} = int({operand});");
            AppendLine($"{tmp} = {tmp} - (({tmp} >> 1) & 0x55555555);");
            AppendLine($"{tmp} = ({tmp} & 0x33333333) + (({tmp} >> 2) & 0x33333333);");
            AppendLine($"{tmp} = ({tmp} + ({tmp} >> 4)) & 0x0F0F0F0F;");
            AppendLine($"{tmp} = {tmp} + ({tmp} >> 8);");
            AppendLine($"{tmp} = {tmp} + ({tmp} >> 16);");
            AppendLine($"{target} = {tmp} & 0x3F;");
            return null; // Signal that we already assigned target
        }

        /// <summary>
        /// Emits count-leading-zeros using a binary search approach.
        /// findMSB is not reliably available in WebGL2 ANGLE vertex shaders.
        /// </summary>
        private string EmitCLZ(Variable target, Variable operand)
        {
            var tmp = $"_clz_{operand}";
            var n = $"_clzn_{operand}";
            AppendLine($"int {tmp} = int({operand});");
            AppendLine($"int {n} = 32;");
            AppendLine($"if ({tmp} != 0) {{");
            AppendLine($"  {n} = 0;");
            AppendLine($"  if (({tmp} & 0xFFFF0000) == 0) {{ {n} += 16; {tmp} <<= 16; }}");
            AppendLine($"  if (({tmp} & 0xFF000000) == 0) {{ {n} += 8; {tmp} <<= 8; }}");
            AppendLine($"  if (({tmp} & 0xF0000000) == 0) {{ {n} += 4; {tmp} <<= 4; }}");
            AppendLine($"  if (({tmp} & 0xC0000000) == 0) {{ {n} += 2; {tmp} <<= 2; }}");
            AppendLine($"  if (({tmp} & 0x80000000) == 0) {{ {n} += 1; }}");
            AppendLine($"}}");
            AppendLine($"{target} = {n};");
            return null; // Signal that we already assigned target
        }

        /// <summary>
        /// Emits count-trailing-zeros using a binary search approach.
        /// findLSB is not reliably available in WebGL2 ANGLE vertex shaders.
        /// </summary>
        private string EmitCTZ(Variable target, Variable operand)
        {
            var tmp = $"_ctz_{operand}";
            var n = $"_ctzn_{operand}";
            AppendLine($"int {tmp} = int({operand});");
            AppendLine($"int {n} = 32;");
            AppendLine($"if ({tmp} != 0) {{");
            AppendLine($"  {n} = 0;");
            AppendLine($"  if (({tmp} & 0x0000FFFF) == 0) {{ {n} += 16; {tmp} >>= 16; }}");
            AppendLine($"  if (({tmp} & 0x000000FF) == 0) {{ {n} += 8; {tmp} >>= 8; }}");
            AppendLine($"  if (({tmp} & 0x0000000F) == 0) {{ {n} += 4; {tmp} >>= 4; }}");
            AppendLine($"  if (({tmp} & 0x00000003) == 0) {{ {n} += 2; {tmp} >>= 2; }}");
            AppendLine($"  if (({tmp} & 0x00000001) == 0) {{ {n} += 1; }}");
            AppendLine($"}}");
            AppendLine($"{target} = {n};");
            return null; // Signal that we already assigned target
        }

        public virtual void GenerateCode(TernaryArithmeticValue value)
        {
            var target = Load(value);
            var first = Load(value.First);
            var second = Load(value.Second);
            var third = Load(value.Third);
            Declare(target);
            // GLSL ES 3.0 has no fma() — emulate with a*b+c
            AppendLine($"{target} = ({first} * {second} + {third});");
        }

        public virtual void GenerateCode(CompareValue value)
        {
            var target = Load(value);
            var left = Load(value.Left);
            var right = Load(value.Right);
            Declare(target);

            string op = value.Kind switch
            {
                CompareKind.Equal => "==",
                CompareKind.NotEqual => "!=",
                CompareKind.LessThan => "<",
                CompareKind.LessEqual => "<=",
                CompareKind.GreaterThan => ">",
                CompareKind.GreaterEqual => ">=",
                _ => "=="
            };

            // f32 NaN safety - mirror of GLSLKernelFunctionGenerator override.
            // Helper functions go through this base path so the same Nan-OR
            // / Equal-NaN-guard treatment is required here.
            string leftType = TypeGenerator[value.Left.Type];
            string rightType = TypeGenerator[value.Right.Type];

            // emu_f64 in GLSL: vec2 (Dekker) or vec4 (Ozaki). Raw == returns
            // vec2/vec4 of bool which can't be assigned to bool. Route through
            // f64_xx helpers same as the kernel function generator.
            bool isEmulatedF64 = (leftType == "vec2" || leftType == "vec4" || rightType == "vec2" || rightType == "vec4")
                && value.Left.BasicValueType == BasicValueType.Float64;
            if (isEmulatedF64)
            {
                string? f = value.Kind switch
                {
                    CompareKind.LessThan => "f64_lt", CompareKind.LessEqual => "f64_le",
                    CompareKind.GreaterThan => "f64_gt", CompareKind.GreaterEqual => "f64_ge",
                    CompareKind.Equal => "f64_eq", CompareKind.NotEqual => "f64_ne", _ => null
                };
                if (f != null)
                {
                    if (value.IsUnsignedOrUnordered && value.Kind != CompareKind.NotEqual)
                        AppendLine($"{target} = (f64_is_nan({left}) || f64_is_nan({right})) || {f}({left}, {right});");
                    else
                        AppendLine($"{target} = {f}({left}, {right});");
                    return;
                }
            }

            bool isFloatScalar = (value.Left.BasicValueType == BasicValueType.Float32
                    || value.Left.BasicValueType == BasicValueType.Float16)
                && !leftType.StartsWith("vec") && !rightType.StartsWith("vec");
            bool isNativeFloatUnordered = isFloatScalar && value.IsUnsignedOrUnordered
                && value.Kind != CompareKind.NotEqual
                && value.Kind != CompareKind.Equal;
            bool isNativeFloatEqualLike = isFloatScalar
                && (value.Kind == CompareKind.Equal || value.Kind == CompareKind.NotEqual);

            if (isNativeFloatUnordered)
            {
                string LIsNaN = $"((floatBitsToUint({left}) & 0x7F800000u) == 0x7F800000u && (floatBitsToUint({left}) & 0x007FFFFFu) != 0u)";
                string RIsNaN = $"((floatBitsToUint({right}) & 0x7F800000u) == 0x7F800000u && (floatBitsToUint({right}) & 0x007FFFFFu) != 0u)";
                AppendLine($"{target} = ({LIsNaN} || {RIsNaN} || ({left} {op} {right}));");
            }
            else if (isNativeFloatEqualLike)
            {
                string LIsNaN = $"((floatBitsToUint({left}) & 0x7F800000u) == 0x7F800000u && (floatBitsToUint({left}) & 0x007FFFFFu) != 0u)";
                string RIsNaN = $"((floatBitsToUint({right}) & 0x7F800000u) == 0x7F800000u && (floatBitsToUint({right}) & 0x007FFFFFu) != 0u)";
                if (value.Kind == CompareKind.Equal)
                    AppendLine($"{target} = (!({LIsNaN}) && !({RIsNaN}) && ({left} == {right}));");
                else
                    AppendLine($"{target} = ({LIsNaN} || {RIsNaN} || ({left} != {right}));");
            }
            else
            {
                // For unsigned integer comparisons (`uint <= uintConst` etc.),
                // GLSL ES 3.0's signed-int operators produce the wrong result —
                // values with the high bit set compare as negative. Cast to
                // uint so the comparison uses unsigned semantics.
                //
                // ILGPU's IR represents both signed and unsigned ints as
                // BasicValueType.Int32 with a `IsUnsignedOrUnordered` flag on
                // the CompareValue node. The GLSL TypeGenerator maps
                // BasicValueType.Int32 → "int" by default; without this fix,
                // `state.Rng <= 0x800000u` evaluated as signed and Tuvok's
                // libopus Normalize loop ran indefinitely on WebGL until the
                // safety cap fired (rng=0x80000000 signed = -2147483648 < 0x800000).
                bool isIntegerType = (leftType == "int" || leftType == "uint")
                    && (rightType == "int" || rightType == "uint");
                if (value.IsUnsignedOrUnordered && isIntegerType
                    && value.Kind != CompareKind.Equal && value.Kind != CompareKind.NotEqual)
                {
                    AppendLine($"{target} = uint({left}) {op} uint({right});");
                }
                else
                {
                    AppendLine($"{target} = {left} {op} {right};");
                }
            }
        }

        // Sign-extend the low 16/8 bits of a 32-bit GLSL int WITHOUT shifting a bit into the sign
        // position. The obvious `(x << 16) >> 16` is UNDEFINED BEHAVIOR in GLSL ES 3.0 when bit 15
        // is set: `0x8000 << 16 == 0x80000000` overflows a signed int, and ANGLE/drivers may return
        // 0 instead of the expected value (this is why `(short)0x8000 >> 15` returned 0x1 on WebGL).
        // The `(v ^ signbit) - signbit` idiom is fully defined: mask to width, flip the sign bit,
        // subtract it back. e.g. 0x8000 -> (0 - 0x8000) = 0xFFFF8000; 0x7FFF -> (0xFFFF - 0x8000) = 0x7FFF.
        protected static string SignExtend16(string expr) =>
            $"(((({expr}) & 0xFFFF) ^ 0x8000) - 0x8000)";
        protected static string SignExtend8(string expr) =>
            $"(((({expr}) & 0xFF) ^ 0x80) - 0x80)";

        public virtual void GenerateCode(ConvertValue value)
        {
            var target = Load(value);
            var source = Load(value.Value);
            Declare(target);
            AppendLine($"{target} = {BuildConvertExpression(value, source)};");
        }

        /// <summary>
        /// The expression for a Math/XMath call on EMULATED f64 operands (vec2 Dekker / vec4 Ozaki is
        /// a vector of f32 components), or null if <paramref name="name"/> is not a math function.
        /// The built-ins would run per COMPONENT - round(hi) + round(lo) is not round(hi + lo)
        /// (2.5000000001 = 2.5 + 1e-10 came out 2), and floor/abs/min/max/sign likewise - so the
        /// exact operations go to the emulation library and the transcendentals through f32
        /// (f32 accuracy, the same contract as the unary emulated-f64 path).
        /// </summary>
        internal static string? EmulatedF64MathExpression(string name, IReadOnlyList<string> a)
        {
            int n = a.Count;
            if (n == 1)
            {
                string? exact = name switch
                {
                    _ when name.Contains("RoundAwayFromZero") => $"f64_round_away({a[0]})",
                    _ when name.Contains("Round") => $"f64_round_even({a[0]})",
                    _ when name.Contains("Truncate") => $"f64_trunc({a[0]})",
                    _ when name.Contains("Floor") => $"f64_floor({a[0]})",
                    _ when name.Contains("Ceiling") => $"f64_ceil({a[0]})",
                    _ when name.Contains("Rsqrt") || name.Contains("ReciprocalSqrt") => $"f64_div(f64_from_f32(1.0), f64_sqrt({a[0]}))",
                    _ when name.Contains("Rcp") => $"f64_div(f64_from_f32(1.0), {a[0]})",
                    _ when name.Contains("Sqrt") => $"f64_sqrt({a[0]})",
                    _ when name.Contains("Abs") => $"f64_abs({a[0]})",
                    _ when name.Contains("Sign") => $"(f64_lt({a[0]}, f64_from_f32(0.0)) ? -1 : (f64_gt({a[0]}, f64_from_f32(0.0)) ? 1 : 0))",
                    _ => null
                };
                if (exact != null) return exact;
                string? f32Func = name switch
                {
                    _ when name.Contains("Asin") => "asin",
                    _ when name.Contains("Acos") => "acos",
                    _ when name.Contains("Atan") => "atan",
                    _ when name.Contains("Sinh") => "sinh",
                    _ when name.Contains("Cosh") => "cosh",
                    _ when name.Contains("Tanh") => "tanh",
                    _ when name.Contains("Sin") => "sin",
                    _ when name.Contains("Cos") => "cos",
                    _ when name.Contains("Tan") => "tan",
                    _ when name.Contains("Exp2") => "exp2",
                    _ when name.Contains("Exp") => "exp",
                    _ when name.Contains("Log10") => "log10",
                    _ when name.Contains("Log2") => "log2",
                    _ when name.Contains("Log") => "log",
                    _ => null
                };
                if (f32Func == null) return null;
                string x = $"f64_to_f32({a[0]})";
                string call = f32Func == "log10" ? $"(log({x}) * 0.4342944819)" : $"{f32Func}({x})";
                return $"f64_from_f32({call})";
            }
            if (n == 2)
            {
                return name switch
                {
                    _ when name.Contains("IEEERemainder") => $"f64_ieee_rem({a[0]}, {a[1]})",
                    _ when name.Contains("Atan2") => $"f64_atan2({a[0]}, {a[1]})",
                    _ when name.Contains("Pow") => $"f64_pow({a[0]}, {a[1]})",
                    _ when name.Contains("CopySign") => $"f64_copysign({a[0]}, {a[1]})",
                    _ when name.Contains("Min") => $"f64_min({a[0]}, {a[1]})",
                    _ when name.Contains("Max") => $"f64_max({a[0]}, {a[1]})",
                    _ => null
                };
            }
            if (n == 3)
            {
                return name switch
                {
                    _ when name.Contains("Clamp") => $"f64_min(f64_max({a[0]}, {a[1]}), {a[2]})",
                    _ when name.Contains("FusedMultiplyAdd") => $"f64_add(f64_mul({a[0]}, {a[1]}), {a[2]})",
                    _ => null
                };
            }
            return null;
        }

        /// <summary>
        /// Registered Math intrinsic handlers emit the GLSL built-in, which on an emulated-f64
        /// vec2/vec4 runs per component. Emits the exact form instead and returns true, or returns
        /// false for a non-emulated call.
        /// </summary>
        private static bool TryEmitEmulatedF64Intrinsic(WebGLBackend backend, GLSLCodeGenerator cg, Value value, string name)
        {
            if (value is not MethodCall mc || !backend.EnableF64Emulation || mc.Count == 0
                || mc[0].Resolve().BasicValueType != BasicValueType.Float64)
                return false;
            var target = cg.LoadIntrinsicValue(value);
            var args = new List<string>(mc.Count);
            for (int i = 0; i < mc.Count; i++)
                args.Add(cg.LoadIntrinsicValue(mc[i].Resolve()).ToString());
            var expr = EmulatedF64MathExpression(name, args);
            if (expr == null) return false;
            cg.Declare(target);
            cg.AppendLine($"{target} = {expr};");
            return true;
        }

        /// <summary>
        /// Math.CopySign for a scalar float: |x| carrying the SIGN BIT of y (a `y < 0.0` test
        /// misses -0.0).
        /// </summary>
        protected static string CopySignExpression(string x, string y) =>
            $"(((floatBitsToUint({y}) >> 31u) != 0u) ? -abs({x}) : abs({x}))";

        /// <summary>
        /// The GLSL expression for a <see cref="ConvertValue"/> - shared by kernel bodies and
        /// [NoInlining] helper functions so a conversion means the same thing in both. (Helpers
        /// used to emit a plain constructor cast, so `(ulong)someUint` in a helper became
        /// `uvec2(x)`, which REPLICATES the scalar into both words: the high word held x instead
        /// of 0 - exact only for 0.)
        /// </summary>
        protected string BuildConvertExpression(ConvertValue value, Variable source)
        {
            var targetType = TypeGenerator[value.Type];
            var sourceType = TypeGenerator[value.Value.Type];

            bool isEmulatedF64Target = Backend.EnableF64Emulation && (targetType == "vec2" || (Backend.UseOzakiF64Emulation && targetType == "vec4"));
            bool isEmulatedI64Target = Backend.EnableI64Emulation && targetType == "uvec2";
            bool isEmulatedF64Source = Backend.EnableF64Emulation && (sourceType == "vec2" || (Backend.UseOzakiF64Emulation && sourceType == "vec4"));
            bool isEmulatedI64Source = Backend.EnableI64Emulation && sourceType == "uvec2";

            // Detect unsigned source conversion (e.g. uint → float). Unsigned 32-bit values are
            // typed "int" (IR Int32 carries no signedness; the convert's flag does).
            bool isSourceUnsigned = (value.Flags & ConvertFlags.SourceUnsigned) == ConvertFlags.SourceUnsigned;
            bool isUnsigned32Source = sourceType == "uint" || (isSourceUnsigned && sourceType == "int");
            bool isTargetUnsigned = (value.Flags & ConvertFlags.TargetUnsigned) == ConvertFlags.TargetUnsigned;

            // Every integer <-> emulated-f64 and 64-bit-integer <-> float conversion below runs in the
            // integer domain (GLSLEmulationLibrary's exact conversion functions): the old versions
            // went through float or a 32-bit int, so (double)int.MaxValue came out 2147483648,
            // (int)12345678.75 came out 12345679 and (float)(2^40 + 1) came out 1. Float -> integer
            // truncates and SATURATES like .NET 9+ (NaN -> 0).
            if (isEmulatedF64Target)
            {
                if (isEmulatedF64Source) return source.ToString();
                if (isEmulatedI64Source) return isSourceUnsigned ? $"f64_from_u64({source})" : $"f64_from_i64({source})";
                if (sourceType == "float") return $"f64_from_f32({source})";
                if (isUnsigned32Source) return $"f64_from_u32(uint({source}))";
                if (sourceType == "int") return $"f64_from_i32({source})";
                return $"f64_from_f32(float({source}))";
            }
            if (isEmulatedI64Target)
            {
                if (isEmulatedI64Source) return source.ToString();
                if (isEmulatedF64Source) return isTargetUnsigned ? $"f64_to_u64({source})" : $"f64_to_i64({source})";
                if (sourceType == "float") return isTargetUnsigned ? $"f32_to_u64({source})" : $"f32_to_i64({source})";
                // Zero-extend an unsigned 32-bit source (C# uint -> ulong/long); only a signed
                // source sign-extends. i64_from_i32 on a uint >= 2^31 set the high word to all ones.
                if (isUnsigned32Source) return $"u64_from_u32(uint({source}))";
                if (sourceType == "int") return $"i64_from_i32({source})";
                return $"i64_from_i32(int({source}))";
            }
            if (isEmulatedF64Source)
            {
                if (targetType == "float") return $"f64_to_f32({source})";
                if (targetType == "int")
                {
                    string r = isTargetUnsigned ? $"int(f64_to_u32({source}))" : $"f64_to_i32({source})";
                    var dst = value.Type.BasicValueType;
                    if (dst == BasicValueType.Int16) return isTargetUnsigned ? $"({r} & 0xFFFF)" : SignExtend16(r);
                    if (dst == BasicValueType.Int8) return isTargetUnsigned ? $"({r} & 0xFF)" : SignExtend8(r);
                    return r;
                }
                if (targetType == "uint") return $"f64_to_u32({source})";
                return $"{targetType}(f64_to_f32({source}))";
            }
            if (isEmulatedI64Source)
            {
                if (targetType == "int") return $"i64_to_i32({source})";
                if (targetType == "uint") return $"u64_to_u32({source})";
                if (targetType == "float") return isSourceUnsigned ? $"u64_to_f32({source})" : $"i64_to_f32({source})";
                return $"{targetType}(i64_to_i32({source}))";
            }

            // Unsigned int → float: must cast through uint to preserve unsigned value
            if (isSourceUnsigned && sourceType == "int" && targetType == "float")
                return $"float(uint({source}))";

            // Build the cast expression. Skip redundant cast when types match.
            string castExpr = (targetType == sourceType)
                ? source.ToString()
                : $"{targetType}({source})";

            // Sub-word narrowing for Int16 / Int8 targets - same pattern as
            // WGSL + Wasm fixes. GLSL has no native int16/int8 so `int(int_val)`
            // is identity. `(short)((x + (1 << 13)) >> 14)` in butterfly
            // arithmetic needs explicit narrowing or high bits leak into
            // downstream stages (Tuvok's Vp9Idct16x16Kernel residual).
            if (targetType == "int")
            {
                var dstBasicType = value.Type.BasicValueType;
                if (dstBasicType == BasicValueType.Int16)
                    castExpr = isTargetUnsigned ? $"({castExpr} & 0xFFFF)" : SignExtend16(castExpr);
                else if (dstBasicType == BasicValueType.Int8)
                    castExpr = isTargetUnsigned ? $"({castExpr} & 0xFF)" : SignExtend8(castExpr);
                else
                {
                    // Widening from a SUB-WORD source (Int16/Int8) to a wider int (Int32). The
                    // source lives in a 32-bit register but may be zero-extended - it came from an
                    // unsigned sub-word load, or from a `(short)`/`(sbyte)` signedness reinterpret
                    // that the core IR ELIDES (short and ushort share BasicValueType.Int16, so
                    // node.Type == targetType and the convert is dropped). C# sign-extends
                    // short->int and zero-extends ushort->int; the convert's SourceUnsigned flag
                    // carries which. Re-extend the low bits so the high bits are correct.
                    //
                    // Concretely: `(short)Interop.FloatAsInt(half) >> 15` (AscendingHalf's
                    // ones-complement mask) - PopArithmeticArgs promotes the `(short)` operand to
                    // Int32 via THIS convert before the shift; without the re-extension the value
                    // stayed zero-extended and `>> 15` returned 0 instead of 0xFFFF for negative
                    // Halves on all browser backends. Idempotent for already-correctly-extended
                    // values; desktop backends use native sub-word registers and never reach here.
                    var srcBasicType = value.Value.BasicValueType;
                    if (srcBasicType == BasicValueType.Int16)
                        castExpr = isSourceUnsigned ? $"({castExpr} & 0xFFFF)" : SignExtend16(castExpr);
                    else if (srcBasicType == BasicValueType.Int8)
                        castExpr = isSourceUnsigned ? $"({castExpr} & 0xFF)" : SignExtend8(castExpr);
                }
            }
            return castExpr;
        }

        // Memory Operations — GLSL has no pointers; arrays accessed directly
        public virtual void GenerateCode(global::ILGPU.IR.Values.Load loadVal)
        {
            var target = Load(loadVal);
            var source = Load(loadVal.Source);
            Declare(target);
            // If the source is a LAEA pointer, use the array[index] expression
            if (_leaArrayExprs.TryGetValue(source.Name, out var arrayExpr))
                AppendLine($"{target} = {arrayExpr};");
            else
                AppendLine($"{target} = {source};");
        }

        public virtual void GenerateCode(global::ILGPU.IR.Values.Store storeVal)
        {
            var address = Load(storeVal.Target);
            var val = Load(storeVal.Value);
            // If the address is a LAEA pointer, use the array[index] expression
            if (_leaArrayExprs.TryGetValue(address.Name, out var arrayExpr))
            {
                AppendLine($"{arrayExpr} = {val};");
            }
            else
            {
                // GLSL ES 3.0: cast value to match address type if needed
                string valExpr = CastIfNeeded(val, address.Type);
                AppendLine($"{address} = {valExpr};");
            }
        }

        protected virtual bool IsAtomicPointer(Value ptr) => false;

        public virtual void GenerateCode(LoadElementAddress value)
        {
            var target = Load(value);
            var source = Load(value.Source);
            var offset = Load(value.Offset);
            // In GLSL, array element access is source[offset]
            // We store the expression as a reference for later Load/Store
            Declare(target);
            AppendLine($"// LEA: {target} = {source}[{offset}]");
        }

        public virtual void GenerateCode(global::ILGPU.IR.Values.NewView value)
        {
            var target = Load(value);
            var source = Load(value.Pointer);
            Declare(target);
            AppendLine($"{target} = {source}; // newView");
        }

        /// <summary>
        /// Default GetViewLength handler: emits 0. Overridden in GLSLKernelFunctionGenerator
        /// to emit the u_param{N}_length uniform that was set at dispatch time.
        /// </summary>
        public virtual void GenerateCode(global::ILGPU.IR.Values.GetViewLength value)
        {
            var target = Load(value);
            Declare(target);
            AppendLine($"{target} = 0; // GetViewLength (base: no param info)");
        }

        public virtual void GenerateCode(LoadFieldAddress value)
        {
            var target = Load(value);
            var source = Load(value.Source);
            Declare(target);
            string fieldName = $"field_{value.FieldSpan.Index}";
            if (IsIndexType(value.Source.Type))
            {
                fieldName = value.FieldSpan.Index switch
                {
                    0 => "x",  1 => "y",  2 => "z",  _ => fieldName
                };
            }
            AppendLine($"// LFA: {target} = {source}.{fieldName}");
        }

        public virtual void GenerateCode(Alloca value)
        {
            // Handle array allocations: declare GLSL local arrays
            if (value.IsArrayAllocation(out var lengthVal))
            {
                int arraySize = lengthVal.Int32Value;
                string elementType = TypeGenerator[value.AllocaType];
                string arrayName = $"local_arr_{_localArrayCounter++}";
                var target = Load(value);
                _allocaArrayNames[target.Name] = arrayName;
                bool firstPtrDecl = declaredVariables.Add(target.Name);
                AppendLine($"{elementType} {arrayName}[{arraySize}];");
                // Declare the alloca's pointer variable as an integer offset=0 so that
                // downstream "generic LEA" codegen emitting `v_target = v_alloca + offset`
                // compiles - the result is an absolute index into local_arr_N. Without
                // this, WebGL shader compile fails with "undeclared identifier" on
                // v_alloca whenever loop-unrolled LEAs survive SSAStructureConstruction
                // (Tuvok 2026-04-24 VP9 iDCT 8x8 path, LocalMemory<int>(64)).
                if (firstPtrDecl)
                    AppendLine($"int {target.Name} = 0;");
            }
        }

        public virtual void GenerateCode(global::ILGPU.IR.Values.NewArray value)
        {
            // NewArray creates a local array — declare it as a GLSL array
            var arrayType = value.Type;
            string elementType = TypeGenerator[arrayType.ElementType];
            // Get array size from the dimension node
            int arraySize = 1;
            foreach (var dim in value.Nodes)
            {
                if (dim.Resolve() is PrimitiveValue pv)
                    arraySize *= pv.Int32Value;
            }
            string arrayName = $"local_arr_{_localArrayCounter++}";
            var target = Load(value);
            _allocaArrayNames[target.Name] = arrayName;
            declaredVariables.Add(target.Name);
            AppendLine($"{elementType} {arrayName}[{arraySize}];");
        }

        public virtual void GenerateCode(global::ILGPU.IR.Values.LoadArrayElementAddress value)
        {
            var target = Load(value);
            var arraySource = Load(value.ArrayValue);
            // value.Dimensions[0] is the index expression
            var indexVar = Load(value.Dimensions[0]);
            // Map from the Alloca variable to the actual array name
            string arrayName = _allocaArrayNames.TryGetValue(arraySource.Name, out var name)
                ? name : arraySource.Name;
            _leaArrayExprs[target.Name] = $"{arrayName}[{indexVar}]";
            AppendLine($"// LAEA: {target} -> {arrayName}[{indexVar}]");
        }

        // Constants
        public virtual void GenerateCode(PrimitiveValue value)
        {
            var target = Load(value);
            var type = TypeGenerator[value.Type];
            Declare(target);

            bool isEmulatedF64 = Backend.EnableF64Emulation && value.BasicValueType == BasicValueType.Float64;
            bool isEmulatedI64 = Backend.EnableI64Emulation && value.BasicValueType == BasicValueType.Int64;

            if (isEmulatedF64)
            {
                double doubleVal = value.Float64Value;
                ulong bits = BitConverter.DoubleToUInt64Bits(doubleVal);
                uint lo = (uint)(bits & 0xFFFFFFFF);
                uint hi = (uint)(bits >> 32);
                AppendLine($"{target} = f64_from_ieee754_bits({lo}u, {hi}u);");
                return;
            }

            if (isEmulatedI64)
            {
                long longVal = value.Int64Value;
                uint lo = (uint)(longVal & 0xFFFFFFFF);
                uint hi = (uint)((ulong)longVal >> 32);
                AppendLine($"{target} = uvec2({lo}u, {hi}u);");
                return;
            }

            string valStr = value.BasicValueType switch
            {
                BasicValueType.Int1 => value.Int1Value ? "true" : "false",
                BasicValueType.Int8 => value.Int8Value.ToString(),
                BasicValueType.Int16 => value.Int16Value.ToString(),
                BasicValueType.Int32 => value.Int32Value == int.MinValue
                    // ANGLE/ESSL3 reject the literal `-2147483648` ("integer overflow"
                    // because it parses as `-(2147483648)` and 2147483648 is not a
                    // valid signed-int literal). The previous workaround substituted
                    // `-2147483647` (bit pattern 0x80000001), which silently corrupted
                    // every constant that needed the exact bit pattern 0x80000000 —
                    // libopus Normalize loops, range-coder constants, IEEE -0.0
                    // bitcasts, etc. (Surfaced 2026-05-04 by Tests23_BareUintShift.)
                    // Real fix: emit as uint→int bitcast which produces the exact
                    // bit pattern with no parser issue and no UB.
                    ? "int(2147483648u)"
                    : value.Int32Value.ToString(),
                BasicValueType.Int64 => value.Int64Value.ToString(),
                BasicValueType.Float16 => FormatFloat((float)value.Float16Value),
                BasicValueType.BFloat16 => FormatFloat((float)value.BFloat16Value),
                BasicValueType.Float8E4M3 => FormatFloat((float)value.Float8E4M3Value),
                BasicValueType.Float8E5M2 => FormatFloat((float)value.Float8E5M2Value),
                BasicValueType.Float4E2M1 => FormatFloat((float)value.Float4E2M1Value),
                BasicValueType.QInt4 => ((int)value.QInt4Value).ToString(), // packed 4-bit -> sign-extended int
                BasicValueType.Float32 => FormatFloat(value.Float32Value),
                BasicValueType.Float64 => FormatFloat((float)value.Float64Value),
                _ => "0"
            };

            if (value.BasicValueType != BasicValueType.Int1)
                AppendLine($"{target} = {type}({valStr});");
            else
                AppendLine($"{target} = {valStr};");
        }

        private string FormatFloat(float value)
        {
            // Inf / NaN have no GLSL ES 3.0 literal form; emit via
            // uintBitsToFloat(u32) of the IEEE 754 bit pattern. Pre-fix this
            // branch substituted +Inf with 3.402823e+38 (= float.MaxValue),
            // which silently broke any kernel that compared against +Inf -
            // notably IsInf, where (x == +Inf || x == -Inf) became
            // (x == MaxValue || x == -MaxValue), returning 0 for actually-
            // infinite x. See _DevComms/SpawnDev.ILGPU/data-to-geordi-isinf-
            // wgsl-glsl-codegen-bug-2026-04-28.md.
            if (float.IsPositiveInfinity(value)) return "uintBitsToFloat(0x7F800000u)";
            if (float.IsNegativeInfinity(value)) return "uintBitsToFloat(0xFF800000u)";
            if (float.IsNaN(value)) return "uintBitsToFloat(0x7FC00000u)";
            var str = value.ToString("G9");
            if (!str.Contains('.') && !str.Contains('e') && !str.Contains('E'))
                str += ".0";
            return str;
        }

        public virtual void GenerateCode(NullValue value)
        {
            var target = Load(value);
            Declare(target);
            // GLSL struct constructors require all fields — cannot use structType(0)
            if (value.Type is StructureType structType)
            {
                AppendLine($"{target} = {GetStructDefaultInitializer(structType)}; // null");
            }
            else
            {
                AppendLine($"{target} = {GetDefaultValue(target.Type)}; // null");
            }
        }

        public virtual void GenerateCode(global::ILGPU.IR.Values.Barrier value)
        {
            // WebGL2 vertex shaders do not support barriers
            AppendLine("// barrier not supported in WebGL2 vertex shaders");
        }

        public virtual void GenerateCode(StringValue value)
        {
            AppendLine($"// String: {value.String}");
        }

        public virtual void GenerateCode(PhiValue value)
        {
            var target = Load(value);
            Declare(target);
        }

        // Structures
        public virtual void GenerateCode(StructureValue value)
        {
            var target = Load(value);
            Declare(target);
            var sb = new StringBuilder();
            sb.Append($"{target} = {target.Type}(");
            for (int i = 0; i < value.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Load(value[i]));
            }
            sb.Append(");");
            AppendLine(sb.ToString());
        }

        public virtual void GenerateCode(GetField value)
        {
            var target = Load(value);
            var source = Load(value.ObjectValue);
            Declare(target);
            string fieldName = $"field_{value.FieldSpan.Index}";
            if (IsIndexType(value.ObjectValue.Type))
            {
                fieldName = value.FieldSpan.Index switch
                {
                    0 => "x",  1 => "y",  2 => "z",  _ => fieldName
                };
            }
            AppendLine($"{target} = {source}.{fieldName};");
        }

        public virtual void GenerateCode(SetField value)
        {
            var target = Load(value);
            var source = Load(value.ObjectValue);
            var fieldValue = Load(value.Value);
            Declare(target);
            AppendLine($"{target} = {source};");
            string fieldName = $"field_{value.FieldSpan.Index}";
            if (IsIndexType(value.ObjectValue.Type))
            {
                fieldName = value.FieldSpan.Index switch
                {
                    0 => "x",  1 => "y",  2 => "z",  _ => fieldName
                };
            }
            AppendLine($"{target}.{fieldName} = {fieldValue};");
        }

        protected bool IsIndexType(TypeNode type)
        {
            var typeName = type.ToString();
            return typeName.Contains("Index") &&
                   (typeName.Contains("1D") || typeName.Contains("2D") || typeName.Contains("3D"));
        }

        // Device Constants — mapped from gl_VertexID in kernel generator
        public virtual void GenerateCode(GridIndexValue value)
        {
            var target = Load(value);
            Declare(target);
            AppendLine($"{target} = 0; // GridIndex not supported in WebGL2 TF");
        }

        public virtual void GenerateCode(GroupIndexValue value)
        {
            var target = Load(value);
            Declare(target);
            AppendLine($"{target} = 0; // GroupIndex not supported in WebGL2 TF");
        }

        public virtual void GenerateCode(GridDimensionValue value)
        {
            var target = Load(value);
            Declare(target);
            AppendLine($"{target} = 0; // GridDimension not supported in WebGL2 TF");
        }

        public virtual void GenerateCode(GroupDimensionValue value)
        {
            var target = Load(value);
            Declare(target);
            AppendLine($"{target} = 0; // GroupDimension not supported in WebGL2 TF");
        }

        public virtual void GenerateCode(WarpSizeValue value)
        {
            var target = Load(value);
            Declare(target);
            AppendLine($"{target} = 1; // No warps in WebGL2");
        }

        public virtual void GenerateCode(LaneIdxValue value)
        {
            var target = Load(value);
            Declare(target);
            AppendLine($"{target} = 0; // No lanes in WebGL2");
        }

        // Control Flow
        public virtual void GenerateCode(ReturnTerminator value)
        {
            // GLSL allows `return` anywhere, including inside the structured walker's loops.
            if (value.IsVoidReturn)
                AppendLine("return;");
            else
            {
                var retVal = Load(value.ReturnValue);
                AppendLine($"return {retVal};");
            }
        }

        // Branches are never emitted as values: the structured walker (GenerateStructuredCode)
        // turns every branch into if/else/for/break/continue and emits its phi copies itself.
        // Reaching one of these means a block was emitted outside the walker - fail loudly
        // rather than silently dropping control flow (which is how [NoInlining] helpers lost
        // every loop and branch before).
        public virtual void GenerateCode(UnconditionalBranch branch) =>
            throw new InvalidOperationException($"GLSL: branch {branch} reached value codegen outside the structured walker ({Method.Name}).");

        public virtual void GenerateCode(IfBranch branch) =>
            throw new InvalidOperationException($"GLSL: branch {branch} reached value codegen outside the structured walker ({Method.Name}).");

        public virtual void GenerateCode(SwitchBranch branch) =>
            throw new InvalidOperationException($"GLSL: branch {branch} reached value codegen outside the structured walker ({Method.Name}).");

        /// <summary>
        /// An emulated 64-bit shift (<paramref name="shiftFn"/> = i64_shl / u64_shr / i64_shr on a
        /// uvec2 (lo, hi)). When the shift amount is a compile-time constant - every 64-bit rotate
        /// (`(x >> n) | (x << (64 - n))`) and most shifts in hashing/crypto code - the result is
        /// emitted as a branch-free expression with exactly the library function's result for that
        /// amount, instead of a call whose three runtime branches (== 0, >= 64, >= 32) the driver
        /// compiler must process at EVERY inlined call site. Blake2b.Compress (96 G mixes x 8 shifts)
        /// took ANGLE/FXC ~10 s per inlined copy with the calls. Otherwise returns the plain call.
        /// </summary>
        protected static string ConstantEmulatedShiftOrCall(string shiftFn, string a, Value amount, string amountExpr)
        {
            if (amount.Resolve() is not PrimitiveValue pv)
                return $"{shiftFn}({a}, uint({amountExpr}))";
            // Same value the library sees through uint(...): a negative constant is >= 64.
            ulong c = pv.BasicValueType == BasicValueType.Int64
                ? unchecked((ulong)pv.Int64Value)
                : unchecked((uint)pv.Int32Value);
            if (c == 0)
                return a;
            switch (shiftFn)
            {
                case "i64_shl":
                    if (c >= 64) return "uvec2(0u, 0u)";
                    if (c >= 32) return $"uvec2(0u, {a}.x << {c - 32}u)";
                    return $"uvec2({a}.x << {c}u, ({a}.y << {c}u) | ({a}.x >> {32 - c}u))";
                case "u64_shr":
                    if (c >= 64) return "uvec2(0u, 0u)";
                    if (c >= 32) return $"uvec2({a}.y >> {c - 32}u, 0u)";
                    return $"uvec2(({a}.x >> {c}u) | ({a}.y << {32 - c}u), {a}.y >> {c}u)";
                case "i64_shr":
                    if (c >= 64) return $"uvec2(uint(int({a}.y) >> 31), uint(int({a}.y) >> 31))";
                    if (c >= 32) return $"uvec2(uint(int({a}.y) >> {c - 32}), uint(int({a}.y) >> 31))";
                    return $"uvec2(({a}.x >> {c}u) | ({a}.y << {32 - c}u), uint(int({a}.y) >> {c}))";
                default:
                    return $"{shiftFn}({a}, uint({amountExpr}))";
            }
        }

        // Method Calls
        public virtual void GenerateCode(MethodCall methodCall)
        {
            // Void-returning calls have no result variable - skip Load + Declare
            // for them (Declare("void") would emit `void v_X;` which GLSL
            // rejects with "illegal use of type 'void'"). The fn-call branch
            // below handles void / non-void emission separately.
            Variable? target = null;
            if (!methodCall.Type.IsVoidType)
            {
                target = Load(methodCall);
                Declare(target);
            }

            // Method has an implementation but isn't a recognized intrinsic - emit a
            // real function call. GLSLFunctionGenerator emits a corresponding fn
            // definition at module scope. WebGL's CreateFunctionCodeGenerator does
            // not register methods in a kernel-side HelperMethods inline map (unlike
            // WebGPU rc.13 / rc.15 which inline at codegen time), so without this
            // branch every non-intrinsic call silent-zeros via the unmapped
            // fallback below. The branch handles simple int helpers correctly;
            // complex bodies (multi-arg ref outputs, type mismatches at
            // intermediate values) remain a known-incomplete feature flagged for a
            // follow-up fn-def codegen pass.
            var glslMethod = methodCall.Target;
            if (glslMethod.HasImplementation
                && !glslMethod.HasFlags(MethodFlags.External)
                && !glslMethod.HasFlags(MethodFlags.Intrinsic))
            {
                var args2 = new StringBuilder();
                for (int i = 0; i < methodCall.Count; i++)
                {
                    if (i > 0) args2.Append(", ");
                    // A `ref`/`out` (inout) argument must be the CALLER's persistent alloca
                    // variable itself, not a fresh AddressSpaceCast snapshot copy of it.
                    // GenerateCode(AddressSpaceCast) emits a one-time "v_N = v_alloca;" copy
                    // and this call's `inout` then updates v_N via GLSL's copy-restore
                    // semantics - but v_N is a dead end: nothing ever copies its post-call
                    // value back into v_alloca, so any LATER read of the alloca (another call
                    // reusing it, or the caller's own code after this call returns) silently
                    // sees the pre-call value. Root-caused 2026-09-22 chasing Blake2b.Compress
                    // appearing to drop its ref h0..h7 write-back entirely - reproduced with a
                    // minimal non-Blake2b repro (a NoInlining helper mirroring G's 4-ref-param
                    // rotating-argument call pattern), confirmed against BOTH the RFC7693 known-
                    // answer test vectors and a from-scratch CPU oracle, so this is a general
                    // call-site bug, not specific to Compress/G. Walk through any
                    // AddressSpaceCast chain to the real Alloca and pass IT directly as the
                    // argument - no snapshot, nothing to lose the write-back through. (An
                    // alternate shape - keep the snapshot as the argument, add an explicit
                    // write-back statement after the call - produces identical output and does
                    // NOT avoid the separate ANGLE loop-hang issue below either; this one is
                    // simpler and was kept.)
                    bool isRefParam = i < glslMethod.Parameters.Count
                        && (glslMethod.Parameters[i].ParameterType is PointerType
                            || glslMethod.Parameters[i].ParameterType is AddressSpaceType);
                    if (isRefParam)
                    {
                        var underlying = methodCall[i].Resolve();
                        while (underlying is AddressSpaceCast asc)
                            underlying = asc.Value.Resolve();
                        if (underlying is Alloca allocaArg)
                        {
                            args2.Append(Load(allocaArg));
                            continue;
                        }
                    }
                    args2.Append(Load(methodCall[i]));
                }
                if (methodCall.Type.IsVoidType)
                {
                    AppendLine($"{GLSLFunctionGenerator.GetMethodName(glslMethod)}({args2});");
                }
                else
                {
                    AppendLine($"{target} = {GLSLFunctionGenerator.GetMethodName(glslMethod)}({args2});");
                }
                return;
            }

            // Built-in math mapped BY NAME - only for calls that are NOT a user method with a
            // body. This used to run first, so a [NoInlining] helper whose name merely CONTAINED
            // Sin/Exp/Log/Abs/Min/Max/Sign/Round/Pow/Tan/Mix (SinglePass, Absorb, LogEntry,
            // Signature, a Blake-style "Mix") was replaced by the GLSL builtin - for a void
            // helper that emitted ` = mix(a, b, c);`, a syntax error; for a non-void one, a
            // silently wrong value. Same order as the WGSL helper and kernel generators.
            string name = methodCall.Target.Name;

            if (Backend.EnableF64Emulation && methodCall.Count > 0 && methodCall[0].BasicValueType == BasicValueType.Float64)
            {
                var emuArgs = new List<string>(methodCall.Count);
                for (int i = 0; i < methodCall.Count; i++)
                    emuArgs.Add(Load(methodCall[i]).ToString());
                var emuExpr = EmulatedF64MathExpression(name, emuArgs);
                if (emuExpr != null)
                {
                    AppendLine($"{target} = {emuExpr};");
                    return;
                }
            }
            // XMath.RoundAwayFromZero (x - trunc(x) is exact, so a .5 tie is detected exactly).
            if (methodCall.Count == 1 && name.Contains("RoundAwayFromZero"))
            {
                var x = Load(methodCall[0]);
                AppendLine($"{target} = (abs({x} - trunc({x})) >= 0.5) ? trunc({x}) + sign({x}) : trunc({x});");
                return;
            }
            // XMath.IEEERemainder: x - y * RoundToEven(x / y).
            if (methodCall.Count == 2 && name.Contains("IEEERemainder"))
            {
                var x = Load(methodCall[0]);
                var y = Load(methodCall[1]);
                AppendLine($"{target} = {x} - {y} * roundEven({x} / {y});");
                return;
            }

            string? glslFunc = name switch
            {
                var n when n.Contains("Rsqrt") => "inversesqrt",
                var n when n.Contains("Rcp") => "rcp_custom",
                var n when n.Contains("Asin") => "asin",
                var n when n.Contains("Acos") => "acos",
                var n when n.Contains("Atan2") => "atan",
                var n when n.Contains("Atan") => "atan",
                var n when n.Contains("Sinh") => "sinh",
                var n when n.Contains("Cosh") => "cosh",
                var n when n.Contains("Tanh") => "tanh",
                var n when n.Contains("FusedMultiplyAdd") => "fma_custom",
                var n when n.Contains("Sin") => "sin",
                var n when n.Contains("Cos") => "cos",
                var n when n.Contains("Tan") => "tan",
                var n when n.Contains("Sqrt") => "sqrt",
                var n when n.Contains("Abs") => "abs",
                var n when n.Contains("Pow") => "pow",
                var n when n.Contains("Exp") => "exp",
                var n when n.Contains("Log") => "log",
                var n when n.Contains("Floor") => "floor",
                var n when n.Contains("Ceiling") => "ceil",
                var n when n.Contains("Min") => "min",
                var n when n.Contains("Max") => "max",
                var n when n.Contains("Clamp") => "clamp",
                var n when n.Contains("Sign") => "sign",
                var n when n.Contains("Round") => "roundEven", // GLSL round() leaves a .5 tie to the implementation
                var n when n.Contains("Truncate") => "trunc",
                var n when n.Contains("Lerp") || n.Contains("Mix") => "mix",
                _ => null
            };

            if (glslFunc != null)
            {
                if (glslFunc == "rcp_custom" && methodCall.Count == 1)
                {
                    AppendLine($"{target} = 1.0 / {Load(methodCall[0])};");
                    return;
                }
                if (glslFunc == "fma_custom" && methodCall.Count == 3)
                {
                    var a = Load(methodCall[0]); var b = Load(methodCall[1]); var c = Load(methodCall[2]);
                    AppendLine($"{target} = {a} * {b} + {c};");
                    return;
                }

                var args = new StringBuilder();
                for (int i = 0; i < methodCall.Count; i++)
                {
                    if (i > 0) args.Append(", ");
                    args.Append(Load(methodCall[i]));
                }
                AppendLine($"{target} = {glslFunc}({args});");
                return;
            }


            // In-kernel GROUP/WARP scan & reduce intrinsics (Group.ExclusiveScan / InclusiveScan /
            // AllReduce / Reduce and the warp variants) require the group's threads to communicate
            // within a single dispatch - i.e. shared memory + barriers. WebGL's Transform-Feedback
            // vertex model has neither (each invocation is independent, no shared workgroup storage),
            // so these are STRUCTURALLY impossible in-kernel. Falling through to the silent-zero stub
            // below would make `GroupExtensions.ExclusiveScan(...)` return 0 for every thread -
            // exactly the "silent zeros that users trust as correct" failure the atomic/barrier paths
            // above refuse. Throw a typed, actionable error instead. (Host-level CreateScan /
            // CreateReduce DO work on WebGL - they orchestrate multiple dispatches with the draw-call
            // boundary as the barrier and global ping-pong buffers - so consumers should use those.)
            var unmappedName = methodCall.Target.Name ?? string.Empty;
            if (unmappedName.Contains("Scan") || unmappedName.Contains("Reduce"))
            {
                throw new SpawnDev.ILGPU.UnsupportedKernelFeatureException(
                    feature: $"in-kernel group/warp op '{unmappedName}'",
                    backend: global::ILGPU.Runtime.AcceleratorType.WebGL,
                    remediation: "WebGL2 Transform-Feedback vertex shaders have no shared workgroup " +
                        "memory or barriers, so in-kernel group/warp scan & reduce cannot run. Use the " +
                        "host-level accelerator.CreateScan(...) / CreateReduce(...) (multi-dispatch, " +
                        "WebGL-supported), or declare RequiresSharedMemory = true on " +
                        "AcceleratorRequirements to filter WebGL at selection time.");
            }

            AppendLine($"// Call: {methodCall.Target.Name} (Unmapped)");
            AppendLine($"{target} = {target.Type}(0);");
        }

        // Casts
        public virtual void GenerateCode(IntAsPointerCast value)
        {
            var target = Load(value); var source = Load(value.Value);
            Declare(target);
            AppendLine($"{target} = {source}; // intAsPtr");
        }

        public virtual void GenerateCode(PointerAsIntCast value)
        {
            var target = Load(value); var source = Load(value.Value);
            Declare(target);
            AppendLine($"{target} = {target.Type}({source}); // ptrAsInt");
        }

        public virtual void GenerateCode(PointerCast value)
        {
            var target = Load(value); var source = Load(value.Value);
            Declare(target);
            AppendLine($"{target} = {source}; // ptrCast");
        }

        public virtual void GenerateCode(AddressSpaceCast value)
        {
            var target = Load(value); var source = Load(value.Value);
            Declare(target);
            AppendLine($"{target} = {source}; // addrSpaceCast");
        }

        public virtual void GenerateCode(FloatAsIntCast value)
        {
            var target = Load(value); var source = Load(value.Value);
            Declare(target);
            // Emulated f64 is a Dekker vec2 (or Ozaki vec4) — NOT the IEEE 754 bit pattern. Using
            // floatBitsToInt on it yields an ivec2 of the WRONG (Dekker) bits and mismatches the
            // uvec2 ulong target (e.g. ExtractRadixBits<double>'s FloatAsInt). Convert via the
            // emulation helper f64_to_ieee754_bits -> uvec2(lo, hi) instead.
            if (Backend.EnableF64Emulation && value.Value.BasicValueType == BasicValueType.Float64)
                AppendLine($"{target} = f64_to_ieee754_bits({source});");
            else if (value.Value.BasicValueType == BasicValueType.Float16)
                // Emulated Half is a GLSL `float` (f32-widened), NOT the IEEE f16 bit pattern. Using
                // floatBitsToInt on it yields the 32-bit f32 bits, mismatching the 16-bit ushort target
                // that FloatAsInt(Half) (= Half.RawValue) promises (e.g. ExtractRadixBits<Half>'s
                // FloatAsInt). Compress back to the 16-bit f16 pattern via the emulation helper. Parallel
                // to the Float64 case above.
                AppendLine($"{target} = int(_f32_to_f16({source}));");
            else if (value.Value.BasicValueType == BasicValueType.BFloat16)
                // Emulated bf16 is a GLSL `float`; FloatAsInt(bf16) must yield the 16-bit bf16
                // pattern (AscendingBFloat16 radix sort, NumBits=16), not floatBitsToInt of the
                // widened f32. Compress via _f32_to_bf16, parallel to the Half case above.
                AppendLine($"{target} = int(_f32_to_bf16({source}));");
            else if (value.Value.BasicValueType == BasicValueType.Float8E4M3)
                // Emulated FP8 is a GLSL `float`; FloatAsInt(fp8) must yield the 8-bit FP8 pattern
                // (AscendingFloat8E4M3 radix sort, NumBits=8), not floatBitsToInt of the widened
                // f32. Compress via _f32_to_e4m3, parallel to the bf16 case above.
                AppendLine($"{target} = int(_f32_to_e4m3({source}));");
            else if (value.Value.BasicValueType == BasicValueType.Float8E5M2)
                AppendLine($"{target} = int(_f32_to_e5m2({source}));");
            else if (value.Value.BasicValueType == BasicValueType.Float4E2M1)
                // Emulated FP4 is a GLSL `float`; FloatAsInt(fp4) must yield the 4-bit FP4 pattern
                // (low nibble; AscendingFloat4E2M1 radix sort, NumBits=4), not floatBitsToInt of the
                // widened f32. Compress via _f32_to_e2m1, parallel to the FP8 case above.
                AppendLine($"{target} = int(_f32_to_e2m1({source}));");
            else
                AppendLine($"{target} = floatBitsToInt({source});");
        }

        public virtual void GenerateCode(IntAsFloatCast value)
        {
            var target = Load(value); var source = Load(value.Value);
            Declare(target);
            // Reverse: reconstruct the emulated f64 (Dekker vec2 / Ozaki vec4) from the IEEE uvec2 bits.
            if (Backend.EnableF64Emulation && value.BasicValueType == BasicValueType.Float64)
                AppendLine($"{target} = f64_from_ieee754_bits({source}.x, {source}.y);");
            else if (value.BasicValueType == BasicValueType.Float16)
                // Reverse: expand a 16-bit f16 bit pattern (held in the low 16 bits of the int source)
                // into the emulated Half = GLSL `float`. intBitsToFloat would interpret the pattern as
                // 32-bit f32 bits, which is wrong. Parallel to the Float64 case above.
                AppendLine($"{target} = _f16_to_f32(uint({source}));");
            else if (value.BasicValueType == BasicValueType.BFloat16)
                // Reverse for bf16: expand the low-16 bf16 pattern to the emulated GLSL `float`.
                // Defensive - no IntAsFloat->BFloat16 frontend overload today.
                AppendLine($"{target} = _bf16_to_f32(uint({source}));");
            else if (value.BasicValueType == BasicValueType.Float8E4M3)
                // Reverse for FP8: expand the low-8 FP8 pattern to the emulated GLSL `float`.
                // Defensive - no IntAsFloat->Float8 frontend overload today.
                AppendLine($"{target} = _e4m3_to_f32(uint({source}));");
            else if (value.BasicValueType == BasicValueType.Float8E5M2)
                AppendLine($"{target} = _e5m2_to_f32(uint({source}));");
            else if (value.BasicValueType == BasicValueType.Float4E2M1)
                // Reverse for FP4: expand the low-nibble FP4 pattern to the emulated GLSL `float`.
                // Defensive - no IntAsFloat->Float4 frontend overload today.
                AppendLine($"{target} = _e2m1_to_f32(uint({source}));");
            else
                AppendLine($"{target} = intBitsToFloat({source});");
        }

        // Atomics & Barriers — not supported in WebGL2 vertex shaders.
        // MUST throw at compile time - silent zeros produce wrong results
        // that users trust as correct. No silent garbage.
        public virtual void GenerateCode(GenericAtomic value)
        {
            throw new SpawnDev.ILGPU.UnsupportedKernelFeatureException(
                feature: $"Atomic.{value.Kind}",
                backend: global::ILGPU.Runtime.AcceleratorType.WebGL,
                remediation: "WebGL2 vertex shaders have no atomic operations. Use WebGPU, Wasm, or a desktop backend. " +
                    "Consumers can declare RequiresAtomics = true on AcceleratorRequirements to filter WebGL at selection time.");
        }

        public virtual void GenerateCode(AtomicCAS value)
        {
            throw new SpawnDev.ILGPU.UnsupportedKernelFeatureException(
                feature: "Atomic.CompareExchange",
                backend: global::ILGPU.Runtime.AcceleratorType.WebGL,
                remediation: "WebGL2 vertex shaders have no atomic operations. Use WebGPU, Wasm, or a desktop backend. " +
                    "Consumers can declare RequiresAtomics = true on AcceleratorRequirements to filter WebGL at selection time.");
        }

        public virtual void GenerateCode(MemoryBarrier value)
        {
            AppendLine("// memoryBarrier not supported in WebGL2 TF shaders");
        }

        public virtual void GenerateCode(PredicateBarrier value)
        {
            AppendLine("// predicateBarrier not supported in WebGL2 TF shaders");
        }

        // Warp Operations — not supported
        public virtual void GenerateCode(Broadcast value)
        {
            var target = Load(value);
            var source = Load(value.Variable);
            Declare(target);
            AppendLine($"{target} = {source}; // broadcast fallback");
        }

        public virtual void GenerateCode(WarpShuffle value)
        {
            var target = Load(value);
            var source = Load(value.Variable);
            Declare(target);
            AppendLine($"{target} = {source}; // shuffle fallback");
        }

        public virtual void GenerateCode(SubWarpShuffle value)
        {
            var target = Load(value);
            var source = Load(value.Variable);
            Declare(target);
            AppendLine($"{target} = {source}; // subShuffle fallback");
        }

        // Debug/IO
        public virtual void GenerateCode(DebugAssertOperation value) { }
        public virtual void GenerateCode(WriteToOutput value) { }

        // Other
        public virtual void GenerateCode(Predicate value)
        {
            var target = Load(value);
            var cond = Load(value.Condition);
            var trueVal = Load(value.TrueValue);
            var falseVal = Load(value.FalseValue);
            Declare(target);
            AppendLine($"{target} = {cond} ? {trueVal} : {falseVal};");
        }

        public virtual void GenerateCode(DynamicMemoryLengthValue value)
        {
            var target = Load(value);
            Declare(target);
            AppendLine($"{target} = 0; // dynamic memory length (placeholder)");
        }

        public virtual void GenerateCode(AlignTo value)
        {
            var target = Load(value); var source = Load(value.Source);
            Declare(target);
            AppendLine($"{target} = {source}; // alignTo");
        }

        public virtual void GenerateCode(AsAligned value)
        {
            // AsAligned is a compile-time alignment hint - the runtime view is unchanged, so alias
            // the result to the source view (no new declaration). The old Declare(target) + assign
            // mis-handled the VIEW result and left an undeclared identifier in the GLSL (WebGL
            // "'v_N' : undeclared identifier" compile error). Mirrors the WGSL AsAligned fix.
            var source = Load(value.Source);
            Bind(value, source);
        }

        public virtual void GenerateCode(LanguageEmitValue value) { }

        public virtual void GenerateThrow(Value value)
        {
            AppendLine($"// [GLSL] Throw encountered: {value} (Ignored/Unreachable)");
            // For non-void functions, return a typed default value
            // so GLSL can see all code paths return correctly.
            var returnType = TypeGenerator[Method.ReturnType];
            if (returnType == "void")
                AppendLine("return;");
            else
                AppendLine($"return {GetDefaultValue(returnType)};");
        }

        #endregion

        #region Math Intrinsics

        /// <summary>
        /// The emulation-library prefix for a Math.Abs/Min/Max intrinsic whose operands are an
        /// EMULATED 64-bit type ("f64" for double, "i64"/"u64" for long/ulong), or null for native
        /// types. The GLSL builtins work per component of the vec2/vec4/uvec2 representation, so
        /// Math.Abs(double) of hi = 12345679, lo = -0.25 gave 12345679.25 and Math.Min(long, long)
        /// compared the two 32-bit words independently.
        /// </summary>
        private static string? Emulated64Prefix(WebGLBackend backend, GLSLCodeGenerator cg, MethodCall mc)
        {
            string type = cg.TypeGenerator[mc.Type];
            if (backend.EnableF64Emulation && (type == "vec2" || (backend.UseOzakiF64Emulation && type == "vec4")))
                return "f64";
            if (backend.EnableI64Emulation && type == "uvec2")
                return (mc.Target.Source as System.Reflection.MethodInfo)?.ReturnType == typeof(ulong) ? "u64" : "i64";
            return null;
        }

        public static void GenerateAbs(WebGLBackend backend, GLSLCodeGenerator cg, Value value)
        {
            if (value is MethodCall mc)
            {
                var t = cg.LoadIntrinsicValue(value);
                var o = cg.LoadIntrinsicValue(mc[0].Resolve());
                cg.Declare(t);
                string? emu = Emulated64Prefix(backend, cg, mc);
                if (emu == "u64") cg.AppendLine($"{t} = {o};"); // |ulong| is itself
                else if (emu != null) cg.AppendLine($"{t} = {emu}_abs({o});");
                else cg.AppendLine($"{t} = abs({o});");
            }
        }

        public static void GenerateSign(WebGLBackend backend, GLSLCodeGenerator cg, Value value)
        {
            if (TryEmitEmulatedF64Intrinsic(backend, cg, value, "Sign")) return;
            if (value is MethodCall mc)
            {
                var t = cg.LoadIntrinsicValue(value);
                var o = cg.LoadIntrinsicValue(mc[0].Resolve());
                cg.Declare(t);
                cg.AppendLine($"{t} = sign({o});");
            }
        }

        public static void GenerateRound(WebGLBackend backend, GLSLCodeGenerator cg, Value value)
        {
            if (TryEmitEmulatedF64Intrinsic(backend, cg, value, "Round")) return;
            if (value is MethodCall mc)
            {
                var t = cg.LoadIntrinsicValue(value);
                var o = cg.LoadIntrinsicValue(mc[0].Resolve());
                cg.Declare(t);
                // Math.Round / MathF.Round round halves to EVEN; GLSL ES round() leaves the
                // direction of a .5 to the implementation, roundEven() does not.
                cg.AppendLine($"{t} = roundEven({o});");
            }
        }

        public static void GenerateTruncate(WebGLBackend backend, GLSLCodeGenerator cg, Value value)
        {
            if (TryEmitEmulatedF64Intrinsic(backend, cg, value, "Truncate")) return;
            if (value is MethodCall mc)
            {
                var t = cg.LoadIntrinsicValue(value);
                var o = cg.LoadIntrinsicValue(mc[0].Resolve());
                cg.Declare(t);
                cg.AppendLine($"{t} = trunc({o});");
            }
        }

        public static void GenerateAtan2(WebGLBackend backend, GLSLCodeGenerator cg, Value value)
        {
            if (TryEmitEmulatedF64Intrinsic(backend, cg, value, "Atan2")) return;
            if (value is MethodCall mc)
            {
                var t = cg.LoadIntrinsicValue(value);
                var y = cg.LoadIntrinsicValue(mc[0].Resolve());
                var x = cg.LoadIntrinsicValue(mc[1].Resolve());
                cg.Declare(t);
                cg.AppendLine($"{t} = atan({y}, {x});");
            }
        }

        public static void GenerateMax(WebGLBackend backend, GLSLCodeGenerator cg, Value value)
        {
            if (value is MethodCall mc)
            {
                var t = cg.LoadIntrinsicValue(value);
                var a = cg.LoadIntrinsicValue(mc[0].Resolve());
                var b = cg.LoadIntrinsicValue(mc[1].Resolve());
                cg.Declare(t);
                string? emu = Emulated64Prefix(backend, cg, mc);
                cg.AppendLine(emu != null ? $"{t} = {emu}_max({a}, {b});" : $"{t} = max({a}, {b});");
            }
        }

        public static void GenerateMin(WebGLBackend backend, GLSLCodeGenerator cg, Value value)
        {
            if (value is MethodCall mc)
            {
                var t = cg.LoadIntrinsicValue(value);
                var a = cg.LoadIntrinsicValue(mc[0].Resolve());
                var b = cg.LoadIntrinsicValue(mc[1].Resolve());
                cg.Declare(t);
                string? emu = Emulated64Prefix(backend, cg, mc);
                cg.AppendLine(emu != null ? $"{t} = {emu}_min({a}, {b});" : $"{t} = min({a}, {b});");
            }
        }

        public static void GeneratePow(WebGLBackend backend, GLSLCodeGenerator cg, Value value)
        {
            if (TryEmitEmulatedF64Intrinsic(backend, cg, value, "Pow")) return;
            if (value is MethodCall mc)
            {
                var t = cg.LoadIntrinsicValue(value);
                var b = cg.LoadIntrinsicValue(mc[0].Resolve());
                var e = cg.LoadIntrinsicValue(mc[1].Resolve());
                cg.Declare(t);
                cg.AppendLine($"{t} = pow({b}, {e});");
            }
        }

        public static void GenerateClamp(WebGLBackend backend, GLSLCodeGenerator cg, Value value)
        {
            if (TryEmitEmulatedF64Intrinsic(backend, cg, value, "Clamp")) return;
            if (value is MethodCall mc)
            {
                var t = cg.LoadIntrinsicValue(value);
                var v = cg.LoadIntrinsicValue(mc[0].Resolve());
                var mn = cg.LoadIntrinsicValue(mc[1].Resolve());
                var mx = cg.LoadIntrinsicValue(mc[2].Resolve());
                cg.Declare(t);
                cg.AppendLine($"{t} = clamp({v}, {mn}, {mx});");
            }
        }

        public static void GenerateFusedMultiplyAdd(WebGLBackend backend, GLSLCodeGenerator cg, Value value)
        {
            if (TryEmitEmulatedF64Intrinsic(backend, cg, value, "FusedMultiplyAdd")) return;
            if (value is MethodCall mc)
            {
                var t = cg.LoadIntrinsicValue(value);
                var x = cg.LoadIntrinsicValue(mc[0].Resolve());
                var y = cg.LoadIntrinsicValue(mc[1].Resolve());
                var z = cg.LoadIntrinsicValue(mc[2].Resolve());
                cg.Declare(t);
                cg.AppendLine($"{t} = {x} * {y} + {z};");
            }
        }

        public static void GenerateRsqrt(WebGLBackend backend, GLSLCodeGenerator cg, Value value)
        {
            if (value is MethodCall mc)
            {
                var t = cg.LoadIntrinsicValue(value);
                var o = cg.LoadIntrinsicValue(mc[0].Resolve());
                cg.Declare(t);
                cg.AppendLine($"{t} = inversesqrt({o});");
            }
        }

        public static void GenerateRcp(WebGLBackend backend, GLSLCodeGenerator cg, Value value)
        {
            if (value is MethodCall mc)
            {
                var t = cg.LoadIntrinsicValue(value);
                var o = cg.LoadIntrinsicValue(mc[0].Resolve());
                cg.Declare(t);
                cg.AppendLine($"{t} = 1.0 / {o};");
            }
        }

        #endregion
    }
}
