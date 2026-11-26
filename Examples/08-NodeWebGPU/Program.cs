// SpawnDev.ILGPU - Example 08: a GPU kernel on WebGPU, under Node, with no browser
//
// The kernel and every line around it are example 01's. What differs is the host: this is a .NET WebAssembly console
// app, and Node runs it (main.mjs). Node has no WebGPU of its own; main.mjs hands the .NET runtime a real one from
// Google Dawn (npm `webgpu`, the Dawn project's own Node binding), so SpawnDev.ILGPU's WebGPU backend runs on your actual GPU.

using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU;

// 1. Every accelerator this host offers, then the best one. On browser-wasm that is WebGPU when navigator.gpu exists.
using var context = await Context.CreateAsync(builder => builder.AllAcceleratorsAsync());
using var accelerator = await context.CreatePreferredAcceleratorAsync();

Console.WriteLine($"Running on: {accelerator.Name}  ({accelerator.AcceleratorType})");

// 2. Some input data.
const int n = 16;
var a = Enumerable.Range(0, n).Select(i => (float)i).ToArray();        // 0, 1, 2, ...
var b = Enumerable.Range(0, n).Select(i => (float)i * 10f).ToArray();  // 0, 10, 20, ...

// 3. Device buffers.
using var bufA = accelerator.Allocate1D(a);
using var bufB = accelerator.Allocate1D(b);
using var bufResult = accelerator.Allocate1D<float>(n);

// 4. Load the kernel and launch it over n elements.
var kernel = accelerator.LoadAutoGroupedStreamKernel<
    Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(AddKernel);
kernel((Index1D)n, bufA.View, bufB.View, bufResult.View);

// 5. Wait for the GPU and read the result back. On WebGPU both MUST be async (there is no blocking wait).
await accelerator.SynchronizeAsync();
var result = await bufResult.CopyToHostAsync<float>();

// 6. Show it.
for (int i = 0; i < n; i++)
    Console.WriteLine($"  {a[i],4} + {b[i],4} = {result[i]}");
Console.WriteLine("Done.");

static void AddKernel(Index1D i, ArrayView<float> a, ArrayView<float> b, ArrayView<float> result)
    => result[i] = a[i] + b[i];
