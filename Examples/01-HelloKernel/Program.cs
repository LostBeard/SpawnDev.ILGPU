// SpawnDev.ILGPU - Example 01: Hello Kernel
//
// The smallest useful GPU program: add two arrays in parallel. One C# method becomes a kernel
// that runs on every element at once. This console app runs on the desktop backends (CUDA if you
// have an NVIDIA GPU, else OpenCL, else CPU) - the SAME code runs on WebGPU/WebGL/Wasm in a Blazor
// WASM host (see example 05).

using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU;

// 1. Create a context with every available accelerator, then pick the best one.
//    On the desktop this is CUDA > OpenCL > CPU; in the browser it would be WebGPU > WebGL > Wasm.
//    The async pattern is the portable one - it works identically on both (and browser backends
//    REQUIRE async; the desktop ones fall back to synchronous calls).
using var context = await Context.CreateAsync(builder => builder.AllAcceleratorsAsync());
using var accelerator = await context.CreatePreferredAcceleratorAsync();

Console.WriteLine($"Running on: {accelerator.Name}  ({accelerator.AcceleratorType})");

// 2. Some input data.
const int n = 16;
var a = Enumerable.Range(0, n).Select(i => (float)i).ToArray();        // 0, 1, 2, ...
var b = Enumerable.Range(0, n).Select(i => (float)i * 10f).ToArray();  // 0, 10, 20, ...

// 3. Allocate device buffers (the Allocate1D overload that takes an array also uploads it).
using var bufA = accelerator.Allocate1D(a);
using var bufB = accelerator.Allocate1D(b);
using var bufResult = accelerator.Allocate1D<float>(n);

// 4. Load the kernel and launch it over n elements. "AutoGrouped" picks the workgroup size for you.
var kernel = accelerator.LoadAutoGroupedStreamKernel<
    Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(AddKernel);
kernel((Index1D)n, bufA.View, bufB.View, bufResult.View);

// 5. Wait for the GPU, then read the result back to the CPU.
await accelerator.SynchronizeAsync();
var result = await bufResult.CopyToHostAsync<float>();

// 6. Show it.
for (int i = 0; i < n; i++)
    Console.WriteLine($"  {a[i],4} + {b[i],4} = {result[i]}");
Console.WriteLine("Done.");

// The kernel: a plain static method. The first parameter is the thread's index; the rest are the
// buffers/scalars you pass at launch. Every index runs this body in parallel.
static void AddKernel(Index1D i, ArrayView<float> a, ArrayView<float> b, ArrayView<float> result)
    => result[i] = a[i] + b[i];
