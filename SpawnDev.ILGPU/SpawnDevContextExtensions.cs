// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU
//                 Unified Context Extensions for Blazor WebAssembly
//
// File: SpawnDevContextExtensions.cs
//
// Provides AllAcceleratorsAsync() and CreatePreferredAcceleratorAsync()
// for easy device discovery and accelerator creation in WASM.
// ---------------------------------------------------------------------------------------

using global::ILGPU;
using global::ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using SpawnDev.BlazorJS.JSObjects;
using SpawnDev.ILGPU.Wasm;
using SpawnDev.ILGPU.Wasm.Algorithms;
using SpawnDev.ILGPU.WebGL;
using SpawnDev.ILGPU.WebGL.Backend;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.ILGPU.WebGPU.Algorithms;
using SpawnDev.ILGPU.WebGPU.Backend;

using System.Runtime.InteropServices;

namespace SpawnDev.ILGPU
{
    /// <summary>
    /// Unified context extensions for Blazor WebAssembly.
    /// Provides async device probing for all WASM-compatible backends
    /// (WebGPU, WebGL, Wasm, CPU).
    /// </summary>
    public static class SpawnDevContextExtensions
    {
        #region Builder Extensions

        /// <summary>
        /// Enables all supported WASM accelerators: CPU, WebGL, Wasm, and WebGPU.
        /// WebGPU requires async GPU probing, so this method is async.
        /// If WebGPU is not available, it is silently skipped.
        /// </summary>
        /// <param name="builder">The context builder instance.</param>
        /// <returns>The builder for chaining.</returns>
        public static async Task<Context.Builder> AllAcceleratorsAsync(
            this Context.Builder builder)
        {
            // Enable algorithms by default — users shouldn't need to call this manually
            builder.EnableAlgorithms();

            // Synchronous backends first (CPU, OpenCL, Cuda — latter two fail silently in WASM)
            builder.AllAccelerators();

            // Browser backends — only available in Blazor WebAssembly
            if (OperatingSystem.IsBrowser())
            {
                // Wasm backend — always available in WASM
                try
                {
                    builder.Wasm();
                    builder.EnableWasmAlgorithms();
                }
                catch
                {
                    // Wasm registration failed
                }

                // WebGPU requires async probing — may not be available
                try
                {
                    await builder.WebGPU();
                    builder.EnableWebGPUAlgorithms();
                }
                catch
                {
                    // WebGPU not available in this environment
                }

                // WebGL2 requires async probing — may not be available
                try
                {
                    await builder.WebGL();
                }
                catch
                {
                    // WebGL2 not available in this environment
                }
            }

            return builder;
        }



        #endregion

        #region Preferred Accelerator

        /// <summary>
        /// Creates the preferred accelerator.
        /// Browser priority: WebGPU > WebGL > Wasm > CPU.
        /// Desktop priority: Cuda > OpenCL > CPU (via GetPreferredDevice).
        /// </summary>
        /// <param name="context">The ILGPU context (must have devices registered).</param>
        /// <returns>The best available accelerator.</returns>
        public static Task<Accelerator> CreatePreferredAcceleratorAsync(
            this Context context) => context.CreatePreferredAcceleratorAsync(AcceleratorRequirements.None);

        /// <summary>
        /// Creates the preferred accelerator that satisfies the given requirements.
        /// Same priority order as the no-arg overload (WebGPU &gt; WebGL &gt; Wasm &gt; CPU on
        /// browser, Cuda &gt; OpenCL &gt; CPU on desktop), restricted to devices that pass
        /// <see cref="AcceleratorRequirements"/> filtering.
        ///
        /// When <see cref="AcceleratorRequirements.RequiresFloat64Strict"/> is set and a
        /// WebGPU or WebGL device is selected, the accelerator is created with
        /// <c>F64Emulation = Ozaki</c> instead of the default Dekker. This is the v1
        /// shape of the strict-f64 path - native-f64 backends are always strict, browser
        /// backends are configured at create time.
        ///
        /// Throws <see cref="NotSupportedException"/> when no available device satisfies
        /// the requirements.
        /// </summary>
        public static async Task<Accelerator> CreatePreferredAcceleratorAsync(
            this Context context, AcceleratorRequirements requirements)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(requirements);

            var compatible = context.EnumerateCompatibleDevices(requirements);
            if (compatible.Count == 0)
            {
                throw new NotSupportedException(
                    $"No compatible accelerator found for requirements: {requirements.Describe()}. " +
                    $"Available devices: {string.Join(", ", context.Devices.Select(d => d.AcceleratorType))}.");
            }

            if (OperatingSystem.IsBrowser())
            {
                // Try WebGPU first (true GPU compute)
                var webGpuDevice = compatible.OfType<WebGPUILGPUDevice>().FirstOrDefault();
                if (webGpuDevice != null)
                {
                    var options = requirements.RequiresFloat64Strict
                        ? new WebGPUBackendOptions { F64Emulation = F64EmulationMode.Ozaki }
                        : null;
                    return await webGpuDevice.CreateAcceleratorAsync(context, options);
                }

                // Try WebGL2 (GPU compute via Transform Feedback)
                var webGlDevice = compatible.OfType<WebGLILGPUDevice>().FirstOrDefault();
                if (webGlDevice != null)
                {
                    var options = requirements.RequiresFloat64Strict
                        ? new WebGLBackendOptions { F64Emulation = F64EmulationMode.Ozaki }
                        : null;
                    return webGlDevice.CreateAccelerator(context, options);
                }

                // Try Wasm (near-native WebAssembly compute)
                var wasmDevice = compatible.OfType<WasmILGPUDevice>().FirstOrDefault();
                if (wasmDevice != null)
                {
                    return await WasmAccelerator.Create(context);
                }
            }

            // Desktop: Cuda > OpenCL > CPU  |  Browser fallback: CPU
            // Prefer non-CPU when a GPU backend is compatible.
            var preferred = compatible.FirstOrDefault(d => d.AcceleratorType != AcceleratorType.CPU)
                            ?? compatible[0];
            return preferred.CreateAccelerator(context);
        }

        /// <summary>
        /// Gets information about all registered devices suitable for display.
        /// </summary>
        /// <param name="context">The ILGPU context.</param>
        /// <returns>
        /// A list of tuples containing (Name, AcceleratorType) for each registered device.
        /// </returns>
        public static List<(string Name, AcceleratorType Type)> GetAllDeviceInfo(
            this Context context)
        {
            var result = new List<(string, AcceleratorType)>();
            foreach (var device in context.Devices)
            {
                result.Add((device.Name, device.AcceleratorType));
            }
            return result;
        }

        #endregion

        #region Unified Buffer Readback

        /// <summary>
        /// Copies data from any ILGPU buffer (WebGPU, WebGL, Wasm, or CPU) back to the host.
        /// Automatically detects the underlying buffer type and uses the appropriate method.
        /// Use this instead of backend-specific CopyToHostAsync to avoid ambiguity.
        /// </summary>
        /// <typeparam name="T">The element type of the buffer.</typeparam>
        /// <param name="buffer">The MemoryBuffer1D to read from.</param>
        /// <returns>An array containing the buffer data.</returns>
        public static async Task<T[]> CopyToHostAsync<T>(
            this MemoryBuffer1D<T, Stride1D.Dense> buffer) where T : unmanaged
        {
            return await CopyToHostAsync<T>((MemoryBuffer)buffer);
        }

        /// <summary>
        /// Copies a range of data from any ILGPU buffer back to the host.
        /// Works on all backends (WebGPU, WebGL, Wasm, CUDA, OpenCL, CPU).
        /// For small reads (≤64 elements), the overhead of reading the full buffer is negligible.
        /// </summary>
        /// <typeparam name="T">The element type of the buffer.</typeparam>
        /// <param name="buffer">The MemoryBuffer1D to read from.</param>
        /// <param name="offset">Start offset in elements.</param>
        /// <param name="count">Number of elements to read.</param>
        /// <returns>An array containing the requested range.</returns>
        public static async Task<T[]> CopyToHostAsync<T>(
            this MemoryBuffer1D<T, Stride1D.Dense> buffer, long offset, long count) where T : unmanaged
        {
            var all = await CopyToHostAsync<T>(buffer);
            if (offset == 0 && count == all.Length) return all;
            var result = new T[count];
            System.Array.Copy(all, offset, result, 0, count);
            return result;
        }

        /// <summary>
        /// Copies the data of an <see cref="ArrayView{T}"/> back to the host as a managed array.
        /// Works on all backends (WebGPU, WebGL, Wasm, CUDA, OpenCL, CPU). The view's
        /// <c>Length</c> elements starting at the view's index are returned.
        ///
        /// <para><b>Real per-backend partial readback.</b> Only the bytes inside the requested
        /// view cross the device-to-host boundary - never the whole backing buffer. This is the
        /// load-bearing rule of this API: data outside the view IS NOT READ BACK.</para>
        /// <list type="bullet">
        ///   <item>WebGPU: <c>queue.CopyBufferToBuffer(srcBuf, srcByteOffset, staging, 0, byteCount)</c> -> <c>mapAsync</c> on the staging buffer's exact byte range.</item>
        ///   <item>WebGL: GL worker readback path with <c>(sourceByteOffset, byteCount)</c>.</item>
        ///   <item>Wasm: SharedArrayBuffer direct slice - <c>new Uint8Array(SAB).SubArray(byteOffset, byteOffset+byteCount)</c>.</item>
        ///   <item>CUDA / OpenCL / CPU: ILGPU's native <c>view.CopyToCPU(target)</c> - this is a real <c>cudaMemcpy</c> / <c>clEnqueueReadBuffer</c> / direct memcpy of just the view's range.</item>
        /// </list>
        ///
        /// <para>Pair with <c>view.SubView(offset, count)</c> for tight per-channel / per-plane copies:</para>
        ///
        /// <code>
        /// var y = await dRecon.View.SubView(0,                yLen).CopyToHostAsync();
        /// var u = await dRecon.View.SubView(yLen,             uvLen).CopyToHostAsync();
        /// var v = await dRecon.View.SubView(yLen + uvLen,     uvLen).CopyToHostAsync();
        /// </code>
        /// </summary>
        /// <typeparam name="T">The element type of the view.</typeparam>
        /// <param name="view">The view to read back. Only its byte range crosses to host.</param>
        /// <returns>An array of <c>view.Length</c> elements.</returns>
        public static async Task<T[]> CopyToHostAsync<T>(this ArrayView<T> view) where T : unmanaged
        {
            var iContig = (IContiguousArrayView)view;
            var buffer = iContig.Buffer ?? throw new InvalidOperationException(
                "ArrayView has no backing buffer.");
            long countElems = view.Length;
            int elementSize = ((IArrayView)view).ElementSize;
            long byteOffset = iContig.IndexInBytes;
            long byteCount = countElems * elementSize;
            if (countElems == 0) return System.Array.Empty<T>();
            if (byteOffset + byteCount > buffer.LengthInBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(view),
                    $"View byte range [{byteOffset}, {byteOffset + byteCount}) exceeds buffer length {buffer.LengthInBytes} bytes.");
            }

            // WebGPU: native partial-range CopyBufferToBuffer + mapAsync.
            if (buffer is WebGPUMemoryBuffer webGpuBuffer)
            {
                using var u8 = await webGpuBuffer.NativeBuffer.CopyToHostUint8ArrayAsync(byteOffset, byteCount);
                var bytes = u8.ReadBytes();
                var result = new T[countElems];
                MemoryMarshal.Cast<byte, T>(bytes).CopyTo(new Span<T>(result));
                return result;
            }

            // WebGL: existing partial GL-worker readback (sourceByteOffset, length).
            if (buffer is WebGLMemoryBuffer webGlBuffer)
            {
                var accel = (WebGLAccelerator)buffer.Accelerator;
                using var u8 = await accel.ReadbackAndGetUint8ArrayAsync(webGlBuffer, byteOffset, byteCount);
                var bytes = u8.ReadBytes();
                var result = new T[countElems];
                MemoryMarshal.Cast<byte, T>(bytes).CopyTo(new Span<T>(result));
                return result;
            }

            // Wasm: SharedArrayBuffer direct slot slice. Only the slot's bytes are
            // copied off the SAB - the rest of the wasm linear memory is not touched.
            // CopyToHostUint8ArrayAsync syncs and returns a Uint8Array windowed onto
            // exactly [byteOffset, byteOffset + byteCount).
            if (buffer is WasmMemoryBuffer wasmBuffer)
            {
                using var u8 = await wasmBuffer.CopyToHostUint8ArrayAsync(byteOffset, byteCount);
                var bytes = u8.ReadBytes();
                var result = new T[countElems];
                MemoryMarshal.Cast<byte, T>(bytes).CopyTo(new Span<T>(result));
                return result;
            }

            // Desktop (CUDA / OpenCL / CPU): ILGPU's ArrayView<T>.CopyToCPU is a
            // per-backend partial copy through cudaMemcpy / clEnqueueReadBuffer /
            // direct memcpy. The view's start offset and length encode the partial
            // range; the call only moves the view's bytes off the device.
            var cpuResult = new T[countElems];
            view.CopyToCPU(cpuResult);
            return cpuResult;
        }

        /// <summary>
        /// <see cref="ArrayView1D{T, TStride}"/> overload of
        /// <see cref="CopyToHostAsync{T}(ArrayView{T})"/>.
        /// Forwards to the <see cref="ArrayView{T}"/> implementation via
        /// <see cref="ArrayView1D{T, TStride}.BaseView"/> so that
        /// <c>buf.View.SubView(offset, count).CopyToHostAsync()</c> resolves naturally
        /// without an explicit cast or <c>.BaseView</c> dereference.
        /// </summary>
        /// <typeparam name="T">The element type of the view.</typeparam>
        /// <typeparam name="TStride">The 1D stride type.</typeparam>
        /// <param name="view">The view to read back. Only its byte range crosses to host.</param>
        /// <returns>An array of <c>view.Length</c> elements.</returns>
        public static Task<T[]> CopyToHostAsync<T, TStride>(this ArrayView1D<T, TStride> view)
            where T : unmanaged
            where TStride : struct, IStride1D
            => CopyToHostAsync<T>(view.BaseView);

        /// <summary>
        /// Copies data from any ILGPU buffer (WebGPU, WebGL, Workers, or CPU) back to the host.
        /// Automatically detects the underlying buffer type and uses the appropriate method.
        /// Use this instead of backend-specific CopyToHostAsync to avoid ambiguity.
        /// </summary>
        /// <typeparam name="T">The element type of the buffer.</typeparam>
        /// <param name="buffer">The MemoryBuffer to read from.</param>
        /// <returns>An array containing the buffer data.</returns>
        public static async Task<T[]> CopyToHostAsync<T>(
            this MemoryBuffer buffer) where T : unmanaged
        {
            var iView = (IArrayView)buffer;

            // Check for WebGPU buffer
            if (iView.Buffer is WebGPUMemoryBuffer webGpuBuffer)
            {
                var byteData = await webGpuBuffer.NativeBuffer.CopyToHostAsync();
                var result = new T[buffer.Length];
                MemoryMarshal.Cast<byte, T>(byteData).CopyTo(new Span<T>(result));
                return result;
            }

            // Check for WebGL2 buffer — must request readback from GL worker first
            if (iView.Buffer is WebGLMemoryBuffer webGlBuffer)
            {
                var accel = (WebGLAccelerator)buffer.Accelerator;
                using var readback = await accel.ReadbackAndGetUint8ArrayAsync(webGlBuffer);
                var byteData = readback.ReadBytes();
                var result = new T[buffer.Length];
                MemoryMarshal.Cast<byte, T>(byteData).CopyTo(new Span<T>(result));
                return result;
            }



            // Check for Wasm buffer
            if (iView.Buffer is WasmMemoryBuffer wasmBuffer)
            {
                // Implicit sync before readback - match desktop behavior where
                // CopyToCPU calls stream.Synchronize() before reading
                if (buffer.Accelerator is WasmAccelerator wasmAccel)
                    await wasmAccel.SynchronizeAsync();
                var byteData = wasmBuffer.TypedArrayView.ReadBytes();
                var result = new T[buffer.Length];
                MemoryMarshal.Cast<byte, T>(byteData).CopyTo(new Span<T>(result));
                return result;
            }

            // CPU buffer — use standard ILGPU synchronous copy
            var cpuResult = new T[buffer.Length];
            buffer.AsArrayView<T>(0, buffer.Length).CopyToCPU(cpuResult);
            return cpuResult;
        }

        /// <summary>
        /// Copies data from any ILGPU buffer (WebGPU, WebGL, Wasm, or CPU) back to the host as a Uint8Array.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="sourceByteOffset"></param>
        /// <param name="copyBytes"></param>
        /// <returns></returns>
        public static async Task<Uint8Array> CopyToHostUint8ArrayAsync(this MemoryBuffer buffer,long sourceByteOffset = 0, long? copyBytes = null)
        {
            var iView = (IArrayView)buffer;

            // Check for WebGPU buffer
            if (iView.Buffer is WebGPUMemoryBuffer webGpuBuffer)
            {
                var result = await webGpuBuffer.NativeBuffer.CopyToHostUint8ArrayAsync(sourceByteOffset, copyBytes);
                return result;
            }

            // Check for WebGL2 buffer — request readback from GL worker
            if (iView.Buffer is WebGLMemoryBuffer webGlBuffer)
            {
                var accel = (WebGLAccelerator)buffer.Accelerator;
                return await accel.ReadbackAndGetUint8ArrayAsync(webGlBuffer, sourceByteOffset, copyBytes);
            }



            // Check for Wasm buffer
            if (iView.Buffer is WasmMemoryBuffer wasmBuffer)
            {
                // Implicit sync before readback - match desktop behavior
                if (buffer.Accelerator is WasmAccelerator wasmAccel)
                    await wasmAccel.SynchronizeAsync();
                using var uint8Array = new Uint8Array(wasmBuffer.SharedBuffer);
                return copyBytes == null ? uint8Array.SubArray(sourceByteOffset) : uint8Array.SubArray(sourceByteOffset, copyBytes.Value + sourceByteOffset);
            }

            // Check for CPU buffer
            if (iView.Buffer is CPUMemoryBuffer)
            {
                // CPU buffer — use standard ILGPU synchronous copy
                var cpuResult = await CopyToHostAsync<byte>(buffer);
                using var uint8Array = new Uint8Array(cpuResult);
                return copyBytes == null ? uint8Array.SubArray(sourceByteOffset) : uint8Array.SubArray(sourceByteOffset, copyBytes.Value + sourceByteOffset);
            }

            throw new NotSupportedException();
        }

        /// <summary>
        /// Copies data from the buffer back to the host as a TypedArray asynchronously.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="buffer"></param>
        /// <returns></returns>
        public static async Task<T> CopyToHostTypeArrayAsync<T>(this MemoryBuffer buffer) where T : TypedArray
        {
            var iView = (IArrayView)buffer;

            // Check for WebGPU buffer
            if (iView.Buffer is WebGPUMemoryBuffer webGpuBuffer)
            {
                var result = await webGpuBuffer.NativeBuffer.CopyToHostUint8ArrayAsync();
                return result.ReCast<T>();
            }

            // Check for WebGL2 buffer — request readback from GL worker
            if (iView.Buffer is WebGLMemoryBuffer webGlBuffer)
            {
                var accel = (WebGLAccelerator)buffer.Accelerator;
                using var readback = await accel.ReadbackAndGetUint8ArrayAsync(webGlBuffer);
                return readback.ReCast<T>();
            }



            // Check for Wasm buffer
            if (iView.Buffer is WasmMemoryBuffer wasmBuffer)
            {
                // Implicit sync before readback - match desktop behavior
                if (buffer.Accelerator is WasmAccelerator wasmAccel)
                    await wasmAccel.SynchronizeAsync();
                return new Uint8Array(wasmBuffer.SharedBuffer).ReCast<T>();
            }

            // CPU buffer — use standard ILGPU synchronous copy
            var cpuResult = await CopyToHostAsync<byte>(buffer);
            return new Uint8Array(cpuResult).ReCast<T>();
        }

        #endregion

        #region Unified GPU-to-GPU Copy (async)

        /// <summary>
        /// Asynchronously copies the contents of the <paramref name="source"/> view into
        /// the <paramref name="target"/> view. Backend-agnostic async mirror of the sync
        /// <c>ArrayView.CopyFrom</c> extension that ALSO waits for any in-flight kernel
        /// work on Wasm before the copy is issued. Use this for GPU->GPU copies that
        /// follow an unawaited kernel dispatch in async code.
        ///
        /// <para><b>Why this exists.</b> Blazor WASM is single-threaded — the main thread
        /// cannot block-wait. <see cref="WasmAccelerator"/> dispatches kernels to worker
        /// threads and returns immediately; <c>WasmAccelerator.Synchronize()</c> is a
        /// no-op (it can't block). The sync <c>CopyFrom</c> code path reads the source
        /// <c>SharedArrayBuffer</c> on the main thread synchronously — if pending worker
        /// kernels are still writing the source buffer, the copy races and reads
        /// stale/partial bytes. This async variant awaits
        /// <see cref="SynchronizeAsync(global::ILGPU.Runtime.Accelerator)"/> on the
        /// source's (and destination's, if different) Wasm accelerator first, draining
        /// pending dispatches before the copy executes.</para>
        ///
        /// <para>On other backends the implicit wait is unnecessary and is skipped:
        /// WebGPU enqueues the copy onto the same command encoder as the kernel;
        /// WebGL routes the copy through the GL worker which processes messages in
        /// order; CUDA/OpenCL serialize via the accelerator stream; CPU is sync.</para>
        ///
        /// <para>Mirrors <c>CopyToHostAsync</c>'s implicit-sync contract so that async
        /// consumer code is backend-agnostic.</para>
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="target">The destination view that receives the bytes.</param>
        /// <param name="source">The source view providing the bytes.</param>
        public static async Task CopyFromAsync<T>(this ArrayView<T> target, ArrayView<T> source)
            where T : unmanaged
        {
            var srcContig = (IContiguousArrayView)source;
            var dstContig = (IContiguousArrayView)target;
            var srcAcc = srcContig.Buffer?.Accelerator;
            var dstAcc = dstContig.Buffer?.Accelerator;

            // Only Wasm needs the explicit drain - see XML doc above for why.
            if (srcAcc is WasmAccelerator srcWasm)
                await srcWasm.SynchronizeAsync();
            if (dstAcc is WasmAccelerator dstWasm
                && !ReferenceEquals(dstAcc, srcAcc))
            {
                await dstWasm.SynchronizeAsync();
            }

            target.CopyFrom(source);
        }

        /// <summary>
        /// <see cref="ArrayView1D{T,TStride}"/> overload of
        /// <see cref="CopyFromAsync{T}(ArrayView{T}, ArrayView{T})"/>.
        /// Forwards through <see cref="ArrayView1D{T,TStride}.BaseView"/> so callers can
        /// write <c>buf.View.CopyFromAsync(otherView)</c> without manual <c>.BaseView</c>
        /// dereferences (mirrors the sync <c>CopyFrom</c> upstream extension).
        /// </summary>
        public static Task CopyFromAsync<T, TStride>(
            this ArrayView1D<T, TStride> target,
            ArrayView<T> source)
            where T : unmanaged
            where TStride : struct, IStride1D
            => target.BaseView.CopyFromAsync(source);

        /// <summary>
        /// <see cref="ArrayView1D{T,TStride}"/>-to-<see cref="ArrayView1D{T,TStride}"/>
        /// overload of <see cref="CopyFromAsync{T}(ArrayView{T}, ArrayView{T})"/>.
        /// </summary>
        public static Task CopyFromAsync<T, TStride>(
            this ArrayView1D<T, TStride> target,
            ArrayView1D<T, TStride> source)
            where T : unmanaged
            where TStride : struct, IStride1D
            => target.BaseView.CopyFromAsync(source.BaseView);

        /// <summary>
        /// Convenience overload that copies the entire source
        /// <see cref="MemoryBuffer1D{T,TStride}"/>'s view into the target buffer's view.
        /// </summary>
        public static Task CopyFromAsync<T, TStride>(
            this MemoryBuffer1D<T, TStride> target,
            MemoryBuffer1D<T, TStride> source)
            where T : unmanaged
            where TStride : struct, IStride1D
            => target.View.CopyFromAsync(source.View);

        #endregion

        #region Unified MemSet (async)

        /// <summary>
        /// Asynchronously zero-fills the <paramref name="view"/>, ordered AFTER any
        /// in-flight kernel work on Wasm. Backend-agnostic async sibling of the sync
        /// <c>ArrayView.MemSetToZero</c>.
        ///
        /// <para><b>Why this exists.</b> On Wasm the sync <c>MemSetToZero</c> writes the
        /// <c>SharedArrayBuffer</c> on the main thread immediately and bypasses the
        /// dispatch queue (<c>WasmAccelerator._pendingWork</c>); if worker kernels are
        /// still reading/writing the buffer the zero-fill races them and the CUDA-style
        /// stream-ordering contract ("memset happens after prior kernels") is broken.
        /// This variant awaits <see cref="SynchronizeAsync"/> on the Wasm accelerator
        /// first, so the fill is correctly ordered after pending dispatches. On other
        /// backends the implicit wait is unnecessary and skipped: WebGPU records the
        /// fill into the same command encoder as the kernels; WebGL routes it through
        /// the in-order GL worker; CUDA/OpenCL serialize via the stream; CPU is sync.
        /// Mirrors <see cref="CopyFromAsync{T}(ArrayView{T}, ArrayView{T})"/>.</para>
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="view">The view to zero.</param>
        /// <param name="stream">The accelerator stream.</param>
        public static async Task MemSetToZeroAsync<T>(this ArrayView<T> view, AcceleratorStream stream)
            where T : unmanaged
        {
            var acc = ((IContiguousArrayView)view).Buffer?.Accelerator;
            // Only Wasm needs the explicit drain — its MemSet is an immediate SAB write
            // that bypasses the dispatch queue. See XML doc above.
            if (acc is WasmAccelerator wasm)
                await wasm.SynchronizeAsync();
            view.MemSetToZero(stream);
        }

        /// <summary>
        /// <see cref="ArrayView1D{T,TStride}"/> overload of
        /// <see cref="MemSetToZeroAsync{T}(ArrayView{T}, AcceleratorStream)"/>.
        /// </summary>
        public static Task MemSetToZeroAsync<T, TStride>(
            this ArrayView1D<T, TStride> view, AcceleratorStream stream)
            where T : unmanaged
            where TStride : struct, IStride1D
            => view.BaseView.MemSetToZeroAsync(stream);

        #endregion

        #region Unified Synchronization

        /// <summary>
        /// Asynchronously waits for all submitted work to complete.
        /// Works with any ILGPU Accelerator — dispatches to the correct
        /// backend-specific implementation (WebGPU, Wasm, or CPU).
        /// </summary>
        /// <param name="accelerator">The ILGPU accelerator.</param>
        /// <returns>A task that completes when all work is done.</returns>
        public static async Task SynchronizeAsync(this global::ILGPU.Runtime.Accelerator accelerator)
        {
            if (accelerator is WebGPUAccelerator webGpuAccelerator)
            {
                await WebGPUAcceleratorExtensions.SynchronizeAsync(webGpuAccelerator);
            }
            else if (accelerator is WebGLAccelerator webGlAccelerator)
            {
                // WebGL2 now uses async worker dispatch — must await pending tasks
                await WebGLAcceleratorExtensions.SynchronizeAsync(webGlAccelerator);
            }

            else if (accelerator is WasmAccelerator wasmAccelerator)
            {
                await wasmAccelerator.SynchronizeAsync();
            }
            else
            {
                // For CPU or other accelerators, use synchronous method
                accelerator.Synchronize();
            }
        }

        #endregion
    }
}
