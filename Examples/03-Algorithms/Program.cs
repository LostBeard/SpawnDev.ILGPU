// SpawnDev.ILGPU - Example 03: Algorithms (GPU RadixSort)
//
// You don't have to write a sort kernel - the ILGPU Algorithms layer ships RadixSort, Scan, and
// Reduce, and the SAME call runs on every backend (CUDA, OpenCL, CPU, WebGPU, WebGL, Wasm).
// AllAcceleratorsAsync() enables the algorithms layer for you.

using ILGPU;
using ILGPU.Runtime;
using ILGPU.Algorithms;
using ILGPU.Algorithms.RadixSortOperations;
using SpawnDev.ILGPU;

using var context = await Context.CreateAsync(builder => builder.AllAcceleratorsAsync());
using var accelerator = await context.CreatePreferredAcceleratorAsync();
Console.WriteLine($"Sorting on: {accelerator.Name}  ({accelerator.AcceleratorType})");

// Some unsorted data.
const int n = 16;
var rng = new Random(42);
var data = Enumerable.Range(0, n).Select(_ => rng.Next(0, 100)).ToArray();
Console.WriteLine("Before: " + string.Join(", ", data));

using var dataBuf = accelerator.Allocate1D(data);

// RadixSort needs scratch space; ask for the right size, then create + run the sorter.
// AscendingInt32 is the sort operation - swap for DescendingInt32, AscendingFloat, etc.
var tempSize = accelerator.ComputeRadixSortTempStorageSize<int, AscendingInt32>(n);
using var tempBuf = accelerator.Allocate1D<int>(tempSize);

var sort = accelerator.CreateRadixSort<int, Stride1D.Dense, AscendingInt32>();
sort(accelerator.DefaultStream, dataBuf.View, tempBuf.View.AsContiguous());  // sorts dataBuf in place
await accelerator.SynchronizeAsync();

var sorted = await dataBuf.CopyToHostAsync<int>();
Console.WriteLine("After:  " + string.Join(", ", sorted));

// Scan and Reduce work the same way: accelerator.CreateScan<...>(ScanKind.Inclusive) and
// accelerator.CreateReduce<...>() - one call, every backend.
Console.WriteLine("\nThe same CreateRadixSort call runs on CUDA, OpenCL, CPU, WebGPU, WebGL, and Wasm.");
Console.WriteLine("Done.");
