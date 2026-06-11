// SpawnDev.ILGPU - Example 02: Backend Selection
//
// One package, six backends. This shows how to (a) see every device, (b) take the default
// preferred pick, and (c) GATE on capabilities so your kernel never lands on a backend that
// can't actually run it (e.g. native f64 - which WebGPU/WebGL only emulate).

using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU;

using var context = await Context.CreateAsync(builder => builder.AllAcceleratorsAsync());

// 1. Every device the context discovered.
//    Desktop: Cuda / OpenCL / CPU.   Browser (Blazor WASM): WebGPU / WebGL / Wasm.
Console.WriteLine("Available devices:");
foreach (var device in context.Devices)
    Console.WriteLine($"  {device.AcceleratorType,-8} {device.Name}");

// 2. The default preferred pick - a real GPU before the CPU (Cuda > OpenCL > CPU, or
//    WebGPU > WebGL > Wasm in the browser).
using (var preferred = await context.CreatePreferredAcceleratorAsync())
    Console.WriteLine($"\nDefault preferred: {preferred.AcceleratorType} ({preferred.Name})");

// 3. Capability gating. Declare what your kernel NEEDS up front and the selection drops any
//    backend that can't satisfy it - instead of silently producing wrong results on the wrong one.
var needsNativeDouble = new AcceleratorRequirements
{
    RequiresFloat64Native = true,   // rules out WebGPU + WebGL (they EMULATE f64, not native)
    RequiresAtomics = true,         // rules out WebGL (no atomics at all)
};

Console.WriteLine("\nDevices that satisfy { native f64 + atomics }:");
foreach (var device in context.EnumerateCompatibleDevices(needsNativeDouble))
    Console.WriteLine($"  {device.AcceleratorType,-8} {device.Name}");

// CreatePreferredAccelerator(requirements) throws NotSupportedException (naming the requirements)
// if nothing on this host qualifies - far better than a wrong answer at runtime.
using (var acc = context.CreatePreferredAccelerator(needsNativeDouble))
    Console.WriteLine($"\nSelected for native-f64 work: {acc.AcceleratorType} ({acc.Name})");

// You can also ask a single device directly.
var cpu = context.Devices.First(d => d.AcceleratorType == AcceleratorType.CPU);
Console.WriteLine($"\nDoes CPU satisfy native f64? {cpu.Satisfies(needsNativeDouble)}");

Console.WriteLine("\nDone.");
