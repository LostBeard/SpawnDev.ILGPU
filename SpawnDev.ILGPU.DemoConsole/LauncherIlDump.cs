using ILGPU;
using ILGPU.Backends.EntryPoints;
using ILGPU.Backends.IL;
using ILGPU.Backends.PTX;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;

// `launcher-il-dump [out.txt]`: the argument-mapping IL the PTX launcher emits for kernels whose parameters nest structs
// 1 to 6 deep (ArrayView fields inside the structs too), as text from DebugILEmitter. Offline: PTXBackend, no CUDA
// device. Made to prove that ArgumentMapper's FieldPathSource / FieldPathTarget walk (2026-11-18, replacing the
// StructureSource<StructureSource<...>> nesting that blew up browser AOT) emits byte-identical IL: dump before and
// after the change and diff.
public static class LauncherIlDump
{
    // DebugILEmitter prints a field by name only, and the mapped argument types name theirs Field0 / Field1 at every
    // level, so a field of the wrong struct would print the same. This prints DeclaringType::Name for field opcodes.
    public readonly struct FullFieldEmitter : IILEmitter
    {
        private readonly DebugILEmitter inner;
        private readonly TextWriter writer;
        public FullFieldEmitter(DebugILEmitter inner, TextWriter writer) { this.inner = inner; this.writer = writer; }
        public ILLocal DeclareLocal(Type type) => inner.DeclareLocal(type);
        public ILLocal DeclarePinnedLocal(Type type) => inner.DeclarePinnedLocal(type);
        public ILLabel DeclareLabel() => inner.DeclareLabel();
        public void MarkLabel(ILLabel label) => inner.MarkLabel(label);
        public void Emit(LocalOperation operation, ILLocal local) => inner.Emit(operation, local);
        public void Emit(ArgumentOperation operation, int argumentIndex) => inner.Emit(operation, argumentIndex);
        public void EmitCall(System.Reflection.MethodInfo target) => inner.EmitCall(target);
        public void EmitNewObject(System.Reflection.ConstructorInfo info) => inner.EmitNewObject(info);
        public void EmitAlloca(int size) => inner.EmitAlloca(size);
        public void EmitConstant(string constant) => inner.EmitConstant(constant);
        public void EmitConstant(int constant) => inner.EmitConstant(constant);
        public void EmitConstant(long constant) => inner.EmitConstant(constant);
        public void EmitConstant(float constant) => inner.EmitConstant(constant);
        public void EmitConstant(double constant) => inner.EmitConstant(constant);
        public void Emit(System.Reflection.Emit.OpCode opCode) => inner.Emit(opCode);
        public void Emit(System.Reflection.Emit.OpCode opCode, ILLabel label) => inner.Emit(opCode, label);
        public void Emit(System.Reflection.Emit.OpCode opCode, Type type) => inner.Emit(opCode, type);
        public void Emit(System.Reflection.Emit.OpCode opCode, System.Reflection.FieldInfo field) =>
            writer.WriteLine($"	{opCode.Name} {field.DeclaringType?.FullName}::{field.Name} : {field.FieldType.FullName}");
        public void EmitSwitch(ILLabel[] labels) => inner.EmitSwitch(labels);
        public void EmitWriteLine(string message) => inner.EmitWriteLine(message);
        public void Finish() => inner.Finish();
    }

    public struct L1 { public int A; public float B; }
    public struct L2 { public L1 X; public long C; public ArrayView<int> V; }
    public struct L3 { public L2 Y; public L1 Z; }
    public struct L4 { public L3 W; public ArrayView<float> F; }
    public struct L5 { public L4 Q; public int R; }
    public struct L6 { public L5 P; public L1 S; public ArrayView<int> T; }

    static void K1(Index1D i, L1 s, ArrayView<int> o) => o[i] = s.A + (int)s.B;
    static void K2(Index1D i, L2 s, ArrayView<int> o) => o[i] = s.X.A + (int)s.C + s.V[0];
    static void K4(Index1D i, L4 s, ArrayView<int> o) => o[i] = s.W.Y.X.A + s.W.Z.A + s.W.Y.V[0] + (int)s.F[0];
    static void K6(Index1D i, L6 s, L2 t, ArrayView<int> o) =>
        o[i] = s.P.Q.W.Y.X.A + s.P.R + s.P.Q.W.Y.V[0] + (int)s.P.Q.F[0] + s.S.A + s.T[0] + t.X.A + t.V[0];

    public static Task<int> Run(string[] args)
    {
        using var context = Context.Create(b => b.Optimize(OptimizationLevel.O1));
        using var backend = new PTXBackend(context, new CudaCapabilityContext(CudaArchitecture.SM_89),
            CudaArchitecture.SM_89, CudaInstructionSet.ISA_85, null);
        var mapper = new PTXArgumentMapper(context);
        var writer = new StringWriter();
        foreach (var (name, method) in new (string, System.Reflection.MethodInfo)[]
        {
            ("K1 (struct depth 1)", ((Action<Index1D, L1, ArrayView<int>>)K1).Method),
            ("K2 (depth 2 + view field)", ((Action<Index1D, L2, ArrayView<int>>)K2).Method),
            ("K4 (depth 4 + view fields)", ((Action<Index1D, L4, ArrayView<int>>)K4).Method),
            ("K6 (depth 6 + two struct params)", ((Action<Index1D, L6, L2, ArrayView<int>>)K6).Method),
        })
        {
            var entry = EntryPointDescription.FromImplicitlyGroupedKernel(method);
            var compiled = backend.Compile(entry, default(KernelSpecialization));
            writer.WriteLine($"==== {name}");
            var emitter = new FullFieldEmitter(new DebugILEmitter(writer), writer);
            var (_, size) = mapper.Map(emitter, compiled.EntryPoint);
            writer.WriteLine($"---- argument buffer size {size}");
        }
        var text = writer.ToString();
        if (args.Length > 1) File.WriteAllText(args[1], text);
        else Console.WriteLine(text);
        Console.WriteLine($"launcher-il-dump: {text.Split('\n').Length} lines");
        return Task.FromResult(0);
    }
}
