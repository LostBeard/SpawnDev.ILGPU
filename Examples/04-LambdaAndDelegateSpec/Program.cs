// SpawnDev.ILGPU - Example 04: Lambda Kernels + DelegateSpecialization
//
// Two ways to parameterize a kernel without writing N copies of it:
//   - Lambda kernels: a captured scalar is shipped to the GPU automatically.
//   - DelegateSpecialization: ONE kernel applies whatever function you hand it; each distinct
//     target compiles its own specialized kernel (the op is inlined at compile time).

using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU;

using var context = await Context.CreateAsync(builder => builder.AllAcceleratorsAsync());
using var accelerator = await context.CreatePreferredAcceleratorAsync();
Console.WriteLine($"Running on: {accelerator.Name}  ({accelerator.AcceleratorType})\n");

const int n = 8;

// --- 1. Lambda kernel. `multiplier` is captured and passed to the GPU for you (ArrayViews can't
//        be captured - they're explicit parameters; scalars can). ---
int multiplier = 5;
var lambda = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(
    (i, buf) => buf[i] = i * multiplier);
using var lambdaBuf = accelerator.Allocate1D<int>(n);
lambda((Index1D)n, lambdaBuf.View);
await accelerator.SynchronizeAsync();
Console.WriteLine($"Lambda  (i * {multiplier}):           " + string.Join(", ", await lambdaBuf.CopyToHostAsync<int>()));

// --- 2. DelegateSpecialization. MapKernel applies any Func<int,int>; Negate and DoubleIt each get
//        their own compiled kernel with the body inlined. ---
using var mapBuf = accelerator.Allocate1D(Enumerable.Range(1, n).ToArray());  // 1..8
var map = accelerator.LoadAutoGroupedStreamKernel<
    Index1D, ArrayView<int>, DelegateSpecialization<Func<int, int>>>(MapKernel);

map((Index1D)n, mapBuf.View, new DelegateSpecialization<Func<int, int>>(Negate));
await accelerator.SynchronizeAsync();
Console.WriteLine("MapKernel(Negate)   on 1..8:  " + string.Join(", ", await mapBuf.CopyToHostAsync<int>()));

map((Index1D)n, mapBuf.View, new DelegateSpecialization<Func<int, int>>(DoubleIt));
await accelerator.SynchronizeAsync();
Console.WriteLine("MapKernel(DoubleIt)  (then):  " + string.Join(", ", await mapBuf.CopyToHostAsync<int>()));

Console.WriteLine("\nDone.");

static int Negate(int x) => -x;
static int DoubleIt(int x) => x * 2;

// One kernel, many ops. `transform.Value` is the specialized function, inlined per target.
static void MapKernel(Index1D i, ArrayView<int> buf, DelegateSpecialization<Func<int, int>> transform)
    => buf[i] = transform.Value(buf[i]);
