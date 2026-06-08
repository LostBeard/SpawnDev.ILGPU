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
        protected bool IsStateMachineActive { get; set; } = false;

        protected GLSLCodeGenerator(in GeneratorArgs args, Method method, Allocas allocas)
        {
            Backend = args.Backend;
            TypeGenerator = args.TypeGenerator;
            Method = method;
            Allocas = allocas;
            Builder = new StringBuilder();
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

            if (IsStateMachineActive)
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
            if (IsStateMachineActive)
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

            bool hasReturnValue = !Method.ReturnType.IsVoidType;
            string returnType = hasReturnValue ? TypeGenerator[Method.ReturnType] : "void";

            if (blocks.Count == 1)
            {
                IsStateMachineActive = false;
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

            // Multiple blocks: try structured control flow first (ANGLE D3D11 workaround).
            // The D3D11 backend cannot compile vertex shaders containing switch/case state
            // machines inside loops when used with Transform Feedback. Structured flow uses
            // while/if/break/continue which D3D11 can JIT-compile.
            if (TryEmitStructuredFlow(blocks, hasReturnValue, returnType))
                return;

            // Fallback: switch/case state machine (for complex/irreducible CFGs)
            IsStateMachineActive = true;

            if (hasReturnValue)
            {
                string zeroVal = GetDefaultValue(returnType);
                AppendLine($"{returnType} _ilgpu_return_val = {zeroVal};");
            }

            int deferredInsertPosition = Builder.Length;

            AppendLine("int current_block = 0;");
            AppendLine("for (int _sm_iter = 0; _sm_iter < 65535; _sm_iter++) {");
            PushIndent();
            AppendLine("switch (current_block) {");
            PushIndent();

            int blockIndex = 0;
            foreach (var block in blocks)
            {
                AppendLine($"case {blockIndex}: {{");
                PushIndent();
                foreach (var valueEntry in block)
                    GenerateCodeFor(valueEntry.Value);
                AppendLine("break;");
                PopIndent();
                AppendLine("}");
                blockIndex++;
            }

            AppendLine("default: break;");
            PopIndent();
            AppendLine("}"); // end switch

            AppendLine("if (current_block == -1) break;");
            PopIndent();
            AppendLine("}"); // end for

            // Insert deferred variable declarations
            if (VariableBuilder.Length > 0)
                Builder.Insert(deferredInsertPosition, VariableBuilder.ToString());

            if (hasReturnValue)
                AppendLine($"return _ilgpu_return_val;");
            else
                AppendLine("return;");
        }

        /// <summary>
        /// Attempts to emit structured control flow using while/if/break/continue
        /// instead of the switch/case state machine. This is required for ANGLE's D3D11
        /// backend which cannot JIT-compile switch/case inside loops in vertex shaders.
        /// Returns true if structured flow was emitted, false to fall back to state machine.
        /// </summary>
        private bool TryEmitStructuredFlow(
            IEnumerable<BasicBlock> blocks,
            bool hasReturnValue,
            string returnType)
        {
            // Build block list and index mapping
            var blockList = blocks.ToList();
            var blockIndexMap = new Dictionary<BasicBlock, int>();
            for (int i = 0; i < blockList.Count; i++)
                blockIndexMap[blockList[i]] = i;

            // Analyze terminators and detect back edges (loops)
            // A back edge is when a block branches to an earlier block (lower index)
            var loopHeaders = new HashSet<int>();  // blocks that are loop headers
            var backEdgeSources = new Dictionary<int, int>();  // source → header mapping

            for (int i = 0; i < blockList.Count; i++)
            {
                var terminator = GetTerminator(blockList[i]);
                if (terminator == null) continue;

                foreach (var target in GetTerminatorTargets(terminator, blockIndexMap))
                {
                    if (target < i)  // back edge: target has lower index
                    {
                        loopHeaders.Add(target);
                        backEdgeSources[i] = target;
                    }
                }

                // SwitchBranch requires the state machine — bail out
                if (terminator is global::ILGPU.IR.Values.SwitchBranch)
                {
                    if (WebGLBackend.VerboseLogging) WebGLBackend.Log($"[GLSL-SCF] FALLBACK: SwitchBranch found at block {i}");
                    return false;
                }
            }

            // For now, only handle single-loop or no-loop CFGs.
            // Multiple nested loops or complex patterns fall back to state machine.
            if (WebGLBackend.VerboseLogging) WebGLBackend.Log($"[GLSL-SCF] blocks={blockList.Count} loopHeaders={loopHeaders.Count} backEdges={backEdgeSources.Count} headers=[{string.Join(",", loopHeaders)}]");
            if (loopHeaders.Count > 1)
            {
                if (WebGLBackend.VerboseLogging) WebGLBackend.Log("[GLSL-SCF] FALLBACK: multiple loop headers detected, using state machine");
                return false;
            }

            // Use structured flow mode — variables are declared at top like state machine
            IsStateMachineActive = true;

            if (hasReturnValue)
            {
                string zeroVal = GetDefaultValue(returnType);
                AppendLine($"{returnType} _ilgpu_return_val = {zeroVal};");
            }

            int deferredInsertPosition = Builder.Length;

            // Emit blocks in order with structured control flow
            int loopHeader = loopHeaders.Count > 0 ? loopHeaders.First() : -1;
            int loopEnd = -1;  // last block in the loop body

            // Find the loop exit block (first block after the loop that isn't in the loop)
            if (loopHeader >= 0)
            {
                // Find all blocks that are part of the loop (between header and last back-edge source)
                loopEnd = backEdgeSources.Keys.Max();
            }

            bool insideLoop = false;

            for (int i = 0; i < blockList.Count; i++)
            {
                var block = blockList[i];
                var terminator = GetTerminator(block);

                // Open loop construct when we reach the loop header
                if (i == loopHeader)
                {
                    insideLoop = true;
                    AppendLine("while (true) {");
                    PushIndent();
                }

                // Emit all values in this block EXCEPT the terminator
                foreach (var valueEntry in block)
                {
                    if (IsTerminator(valueEntry.Value))
                        continue;  // terminators are handled structurally below
                    GenerateCodeFor(valueEntry.Value);
                }

                // Handle terminator structurally
                if (terminator != null)
                    EmitStructuredTerminator(terminator, i, blockList, blockIndexMap,
                        loopHeader, loopEnd, insideLoop);

                // Close loop construct after the last back-edge source block
                if (insideLoop && i == loopEnd)
                {
                    insideLoop = false;
                    PopIndent();
                    AppendLine("}"); // end while
                }
            }

            // Insert deferred variable declarations
            if (VariableBuilder.Length > 0)
                Builder.Insert(deferredInsertPosition, VariableBuilder.ToString());

            if (hasReturnValue)
                AppendLine($"return _ilgpu_return_val;");
            else
                AppendLine("return;");

            return true;
        }

        /// <summary>
        /// Gets the terminator instruction (last value) of a basic block.
        /// </summary>
        private static Value? GetTerminator(BasicBlock block)
        {
            Value? last = null;
            foreach (var entry in block)
                last = entry.Value;
            return last;
        }

        /// <summary>
        /// Returns true if the value is a terminator instruction.
        /// </summary>
        private static bool IsTerminator(Value value) => value is
            global::ILGPU.IR.Values.UnconditionalBranch or
            global::ILGPU.IR.Values.IfBranch or
            global::ILGPU.IR.Values.SwitchBranch or
            global::ILGPU.IR.Values.ReturnTerminator;

        /// <summary>
        /// Gets the target block indices of a terminator instruction.
        /// </summary>
        private static List<int> GetTerminatorTargets(Value terminator, Dictionary<BasicBlock, int> blockIndexMap)
        {
            var targets = new List<int>();
            switch (terminator)
            {
                case global::ILGPU.IR.Values.UnconditionalBranch br:
                    if (blockIndexMap.TryGetValue(br.Target, out int t1))
                        targets.Add(t1);
                    break;
                case global::ILGPU.IR.Values.IfBranch ifBr:
                    if (blockIndexMap.TryGetValue(ifBr.TrueTarget, out int tt))
                        targets.Add(tt);
                    if (blockIndexMap.TryGetValue(ifBr.FalseTarget, out int ft))
                        targets.Add(ft);
                    break;
                case global::ILGPU.IR.Values.SwitchBranch swBr:
                    if (blockIndexMap.TryGetValue(swBr.DefaultBlock, out int dt))
                        targets.Add(dt);
                    for (int i = 0; i < swBr.NumCasesWithoutDefault; i++)
                    {
                        if (blockIndexMap.TryGetValue(swBr.GetCaseTarget(i), out int ct))
                            targets.Add(ct);
                    }
                    break;
            }
            return targets;
        }

        /// <summary>
        /// Emits a terminator instruction as structured control flow (break/continue/fallthrough).
        /// </summary>
        private void EmitStructuredTerminator(
            Value terminator,
            int currentBlockIdx,
            List<BasicBlock> blockList,
            Dictionary<BasicBlock, int> blockIndexMap,
            int loopHeader,
            int loopEnd,
            bool insideLoop)
        {
            switch (terminator)
            {
                case global::ILGPU.IR.Values.ReturnTerminator ret:
                {
                    if (!ret.IsVoidReturn)
                    {
                        var retVal = Load(ret.ReturnValue);
                        AppendLine($"_ilgpu_return_val = {retVal};");
                    }
                    if (insideLoop)
                        AppendLine("break;"); // exit the while loop, then return at end
                    // If not in a loop, fall through to the return at the end
                    break;
                }

                case global::ILGPU.IR.Values.UnconditionalBranch br:
                {
                    int targetIdx = blockIndexMap[br.Target];
                    EmitPhiAssignments(br.BasicBlock, br.Target);

                    if (targetIdx == loopHeader && insideLoop)
                    {
                        // Back edge: continue the loop
                        AppendLine("continue;");
                    }
                    else if (targetIdx == currentBlockIdx + 1)
                    {
                        // Fall through to next block — no code needed
                    }
                    else if (targetIdx > loopEnd && insideLoop)
                    {
                        // Jump past the loop — break
                        AppendLine("break;");
                    }
                    // else: forward jump to non-adjacent block within the loop body
                    // This case shouldn't happen in simple loops but we handle it
                    // by falling through (the blocks are in order)
                    break;
                }

                case global::ILGPU.IR.Values.IfBranch ifBr:
                {
                    var cond = Load(ifBr.Condition);
                    int trueIdx = blockIndexMap[ifBr.TrueTarget];
                    int falseIdx = blockIndexMap[ifBr.FalseTarget];

                    // Determine which branch is the "continue" (back to loop header)
                    // and which is the "exit" (break out or fall through)
                    if (insideLoop)
                    {
                        bool trueIsBackEdge = trueIdx == loopHeader;
                        bool falseIsBackEdge = falseIdx == loopHeader;
                        bool trueIsExit = trueIdx > loopEnd;
                        bool falseIsExit = falseIdx > loopEnd;

                        if (trueIsBackEdge && falseIsExit)
                        {
                            // if (cond) → continue loop; else → break out
                            AppendLine($"if (!({cond})) {{");
                            PushIndent();
                            EmitPhiAssignments(ifBr.BasicBlock, ifBr.FalseTarget);
                            AppendLine("break;");
                            PopIndent();
                            AppendLine("}");
                            EmitPhiAssignments(ifBr.BasicBlock, ifBr.TrueTarget);
                            AppendLine("continue;");
                        }
                        else if (falseIsBackEdge && trueIsExit)
                        {
                            // if (cond) → break out; else → continue loop
                            AppendLine($"if ({cond}) {{");
                            PushIndent();
                            EmitPhiAssignments(ifBr.BasicBlock, ifBr.TrueTarget);
                            AppendLine("break;");
                            PopIndent();
                            AppendLine("}");
                            EmitPhiAssignments(ifBr.BasicBlock, ifBr.FalseTarget);
                            AppendLine("continue;");
                        }
                        else if (trueIsBackEdge)
                        {
                            // True goes back to header, false falls through to next block
                            AppendLine($"if ({cond}) {{");
                            PushIndent();
                            EmitPhiAssignments(ifBr.BasicBlock, ifBr.TrueTarget);
                            AppendLine("continue;");
                            PopIndent();
                            AppendLine("}");
                            EmitPhiAssignments(ifBr.BasicBlock, ifBr.FalseTarget);
                        }
                        else if (falseIsBackEdge)
                        {
                            // False goes back to header, true falls through
                            AppendLine($"if (!({cond})) {{");
                            PushIndent();
                            EmitPhiAssignments(ifBr.BasicBlock, ifBr.FalseTarget);
                            AppendLine("continue;");
                            PopIndent();
                            AppendLine("}");
                            EmitPhiAssignments(ifBr.BasicBlock, ifBr.TrueTarget);
                        }
                        else if (trueIsExit && !falseIsExit)
                        {
                            // True breaks out of loop, false falls through in loop body
                            AppendLine($"if ({cond}) {{");
                            PushIndent();
                            EmitPhiAssignments(ifBr.BasicBlock, ifBr.TrueTarget);
                            AppendLine("break;");
                            PopIndent();
                            AppendLine("}");
                            EmitPhiAssignments(ifBr.BasicBlock, ifBr.FalseTarget);
                        }
                        else if (falseIsExit && !trueIsExit)
                        {
                            // False breaks out of loop, true falls through in loop body
                            AppendLine($"if (!({cond})) {{");
                            PushIndent();
                            EmitPhiAssignments(ifBr.BasicBlock, ifBr.FalseTarget);
                            AppendLine("break;");
                            PopIndent();
                            AppendLine("}");
                            EmitPhiAssignments(ifBr.BasicBlock, ifBr.TrueTarget);
                        }
                        else
                        {
                            // Both targets are within the loop body — simple if/else
                            // True goes to one body block, false to another
                            EmitIfElseBlocks(ifBr, cond, trueIdx, falseIdx, currentBlockIdx, blockList, blockIndexMap, loopHeader, loopEnd);
                        }
                    }
                    else
                    {
                        // Not in a loop — simple if/else with fall-through
                        // One target should be the next block (fall-through), the other is a forward jump
                        if (trueIdx == currentBlockIdx + 1)
                        {
                            // True is fall-through, false is forward jump
                            AppendLine($"if (!({cond})) {{");
                            PushIndent();
                            EmitPhiAssignments(ifBr.BasicBlock, ifBr.FalseTarget);
                            // Emit the false-target blocks inline? Or just skip ahead via comments?
                            // For now, we need to handle this case — but it's rare outside loops
                            PopIndent();
                            AppendLine("}");
                            EmitPhiAssignments(ifBr.BasicBlock, ifBr.TrueTarget);
                        }
                        else if (falseIdx == currentBlockIdx + 1)
                        {
                            // False is fall-through, true is forward jump
                            AppendLine($"if ({cond}) {{");
                            PushIndent();
                            EmitPhiAssignments(ifBr.BasicBlock, ifBr.TrueTarget);
                            PopIndent();
                            AppendLine("}");
                            EmitPhiAssignments(ifBr.BasicBlock, ifBr.FalseTarget);
                        }
                        else
                        {
                            // Neither is fall-through — shouldn't happen in simple CFGs
                            // Fall back to state machine
                            EmitPhiAssignments(ifBr.BasicBlock, ifBr.TrueTarget);
                            EmitPhiAssignments(ifBr.BasicBlock, ifBr.FalseTarget);
                        }
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Emits an if/else construct for branches where both targets are within the loop body.
        /// </summary>
        private void EmitIfElseBlocks(
            global::ILGPU.IR.Values.IfBranch ifBr,
            Variable cond,
            int trueIdx,
            int falseIdx,
            int currentBlockIdx,
            List<BasicBlock> blockList,
            Dictionary<BasicBlock, int> blockIndexMap,
            int loopHeader,
            int loopEnd)
        {
            // The next sequential block is the fall-through target
            int nextIdx = currentBlockIdx + 1;

            if (trueIdx == nextIdx)
            {
                // True falls through, false needs phi assignments only
                AppendLine($"if (!({cond})) {{");
                PushIndent();
                EmitPhiAssignments(ifBr.BasicBlock, ifBr.FalseTarget);
                PopIndent();
                AppendLine("}");
                EmitPhiAssignments(ifBr.BasicBlock, ifBr.TrueTarget);
            }
            else if (falseIdx == nextIdx)
            {
                // False falls through, true needs phi assignments only  
                AppendLine($"if ({cond}) {{");
                PushIndent();
                EmitPhiAssignments(ifBr.BasicBlock, ifBr.TrueTarget);
                PopIndent();
                AppendLine("}");
                EmitPhiAssignments(ifBr.BasicBlock, ifBr.FalseTarget);
            }
            else
            {
                // Neither is fall-through — emit both phi assignments  
                AppendLine($"if ({cond}) {{");
                PushIndent();
                EmitPhiAssignments(ifBr.BasicBlock, ifBr.TrueTarget);
                PopIndent();
                AppendLine("} else {");
                PushIndent();
                EmitPhiAssignments(ifBr.BasicBlock, ifBr.FalseTarget);
                PopIndent();
                AppendLine("}");
            }
        }

        protected int GetBlockIndex(BasicBlock block)
        {
            int index = 0;
            foreach (var b in Method.Blocks)
            {
                if (b == block) return index;
                index++;
            }
            return -1;
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

            // Float remainder
            if (value.Kind == BinaryArithmeticKind.Rem && TypeGenerator[value.Left.Type].StartsWith("float"))
            {
                AppendLine($"{target} = {left} - {right} * floor({left} / {right});");
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
                // copysign(x, y) = abs(x) * sign(y)
                AppendLine($"{target} = abs({left}) * sign({right});");
                return;
            }

            // Emulated 64-bit shift dispatch — closes Tests23_I64Shift_InHelper_NoCodegenError
            // on WebGL (mirrors WGSL local.8 fix `WGSLCodeGenerator.GenerateBinOp`). Pre-fix
            // GLSL emitted `uvec2 = uvec2 >> int` which performs COMPONENT-WISE shift
            // (`(a.x >> shift, a.y >> shift)`) — losing the carry from hi to lo. For input
            // 0x1234567890ABCDEF >> 8 the GLSL produced 0x0090ABCD (just `0x90ABCDEF >> 8`)
            // instead of 0x7890ABCD. Route emu-i64/u64 shifts through `i64_shl` / `i64_shr`
            // / `u64_shr` emulation helpers from GLSLEmulationLibrary.
            string leftType = TypeGenerator[value.Left.Type];
            bool leftIsEmuI64 = Backend.EnableI64Emulation
                && (leftType == "uvec2" && value.Left.BasicValueType == BasicValueType.Int64);
            if (leftIsEmuI64
                && (value.Kind == BinaryArithmeticKind.Shl || value.Kind == BinaryArithmeticKind.Shr))
            {
                string shiftFn = value.Kind == BinaryArithmeticKind.Shl
                    ? "i64_shl"
                    : (value.IsUnsigned ? "u64_shr" : "i64_shr");
                AppendLine($"{target} = {shiftFn}({left}, uint({right}));");
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

            // Emulated emu_f64 source needs different intrinsic codegen for IsNaN
            // / IsInfinity: GLSL `isnan` / `isinf` operate on `float` only, not on
            // `vec2`. Route to f64_is_nan / f64_is_inf helpers from
            // GLSLEmulationLibrary which check the high f32 lane. Pass the result
            // through CastIfNeeded (same path the f32 IsNaN/IsInf emission uses)
            // so a bool target type is handled correctly - emitting
            // `bool_target = (int)` directly trips "cannot convert from 'int' to
            // 'bool'" GLSL parser errors.
            if (Backend.EnableF64Emulation && operandType == "vec2")
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
                        AppendLine($"{target} = (_f32_is_nan_bits({left}.x) || _f32_is_nan_bits({right}.x)) || {f}({left}, {right});");
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
            var targetType = TypeGenerator[value.Type];
            var sourceType = TypeGenerator[value.Value.Type];
            Declare(target);

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
                bool isTargetUnsigned = (value.Flags & ConvertFlags.TargetUnsigned) == ConvertFlags.TargetUnsigned;
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
                    bool isSourceUnsigned = (value.Flags & ConvertFlags.SourceUnsigned) == ConvertFlags.SourceUnsigned;
                    var srcBasicType = value.Value.BasicValueType;
                    if (srcBasicType == BasicValueType.Int16)
                        castExpr = isSourceUnsigned ? $"({castExpr} & 0xFFFF)" : SignExtend16(castExpr);
                    else if (srcBasicType == BasicValueType.Int8)
                        castExpr = isSourceUnsigned ? $"({castExpr} & 0xFF)" : SignExtend8(castExpr);
                }
            }
            AppendLine($"{target} = {castExpr};");
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
            if (IsStateMachineActive)
            {
                if (!value.IsVoidReturn)
                {
                    var retVal = Load(value.ReturnValue);
                    AppendLine($"_ilgpu_return_val = {retVal};");
                }
                AppendLine("current_block = -1;");
                AppendLine("break;");
            }
            else
            {
                if (value.IsVoidReturn)
                    AppendLine("return;");
                else
                {
                    var retVal = Load(value.ReturnValue);
                    AppendLine($"return {retVal};");
                }
            }
        }

        public virtual void GenerateCode(UnconditionalBranch branch)
        {
            EmitPhiAssignments(branch.BasicBlock, branch.Target);
            int targetIdx = GetBlockIndex(branch.Target);
            AppendLine($"current_block = {targetIdx};");
            AppendLine("continue;");
        }

        public virtual void GenerateCode(IfBranch branch)
        {
            var cond = Load(branch.Condition);
            int trueIdx = GetBlockIndex(branch.TrueTarget);
            int falseIdx = GetBlockIndex(branch.FalseTarget);

            AppendLine($"if ({cond}) {{");
            PushIndent();
            EmitPhiAssignments(branch.BasicBlock, branch.TrueTarget);
            AppendLine($"current_block = {trueIdx};");
            PopIndent();
            AppendLine("} else {");
            PushIndent();
            EmitPhiAssignments(branch.BasicBlock, branch.FalseTarget);
            AppendLine($"current_block = {falseIdx};");
            PopIndent();
            AppendLine("}");
            AppendLine("continue;");
        }

        public virtual void GenerateCode(SwitchBranch branch)
        {
            var selector = Load(branch.Condition);
            AppendLine($"switch ({selector}) {{");
            PushIndent();
            for (int i = 0; i < branch.NumCasesWithoutDefault; i++)
            {
                var target = branch.GetCaseTarget(i);
                int targetIdx = GetBlockIndex(target);
                AppendLine($"case {i}: {{");
                PushIndent();
                EmitPhiAssignments(branch.BasicBlock, target);
                AppendLine($"current_block = {targetIdx};");
                AppendLine("break;");
                PopIndent();
                AppendLine("}");
            }
            int defaultIdx = GetBlockIndex(branch.DefaultBlock);
            AppendLine("default: {");
            PushIndent();
            EmitPhiAssignments(branch.BasicBlock, branch.DefaultBlock);
            AppendLine($"current_block = {defaultIdx};");
            AppendLine("break;");
            PopIndent();
            AppendLine("}");
            PopIndent();
            AppendLine("}");
            AppendLine("continue;");
        }

        protected virtual void EmitPhiAssignments(BasicBlock sourceBlock, BasicBlock targetBlock)
        {
            foreach (var valueEntry in targetBlock)
            {
                if (valueEntry.Value is PhiValue phi)
                {
                    var phiVar = Load(phi);
                    var srcValue = phi.GetValue(sourceBlock);
                    if (srcValue != null)
                    {
                        var srcVar = Load(srcValue);
                        // GLSL ES 3.0: cast to phi variable type if needed
                        string srcExpr = CastIfNeeded(srcVar, phiVar.Type);
                        AppendLine($"{phiVar} = {srcExpr};");
                    }
                }
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

            string name = methodCall.Target.Name;
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
                var n when n.Contains("Round") => "round",
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
            var target = Load(value); var source = Load(value.Source);
            Declare(target);
            AppendLine($"{target} = {source}; // asAligned");
        }

        public virtual void GenerateCode(LanguageEmitValue value) { }

        public virtual void GenerateThrow(Value value)
        {
            AppendLine($"// [GLSL] Throw encountered: {value} (Ignored/Unreachable)");
            if (IsStateMachineActive)
            {
                AppendLine("current_block = -1;");
                AppendLine("break;");
            }
            else
            {
                // For non-void functions, return a typed default value
                // so GLSL can see all code paths return correctly.
                var returnType = TypeGenerator[Method.ReturnType];
                if (returnType == "void")
                    AppendLine("return;");
                else
                    AppendLine($"return {GetDefaultValue(returnType)};");
            }
        }

        #endregion

        #region Math Intrinsics

        public static void GenerateAbs(WebGLBackend backend, GLSLCodeGenerator cg, Value value)
        {
            if (value is MethodCall mc)
            {
                var t = cg.LoadIntrinsicValue(value);
                var o = cg.LoadIntrinsicValue(mc[0].Resolve());
                cg.Declare(t);
                cg.AppendLine($"{t} = abs({o});");
            }
        }

        public static void GenerateSign(WebGLBackend backend, GLSLCodeGenerator cg, Value value)
        {
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
            if (value is MethodCall mc)
            {
                var t = cg.LoadIntrinsicValue(value);
                var o = cg.LoadIntrinsicValue(mc[0].Resolve());
                cg.Declare(t);
                cg.AppendLine($"{t} = round({o});");
            }
        }

        public static void GenerateTruncate(WebGLBackend backend, GLSLCodeGenerator cg, Value value)
        {
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
                cg.AppendLine($"{t} = max({a}, {b});");
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
                cg.AppendLine($"{t} = min({a}, {b});");
            }
        }

        public static void GeneratePow(WebGLBackend backend, GLSLCodeGenerator cg, Value value)
        {
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
