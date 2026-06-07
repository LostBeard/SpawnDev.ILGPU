// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.WebGL
//                 WebGL2 Compute Library for Blazor WebAssembly
//
// File: WebGLAccelerator.cs
// ---------------------------------------------------------------------------------------

using global::ILGPU;
using global::ILGPU.Backends;
using global::ILGPU.Runtime;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using GL = SpawnDev.BlazorJS.JSObjects.GL;
using SpawnDev.ILGPU.WebGL.Backend;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using Array = System.Array;

namespace SpawnDev.ILGPU.WebGL
{
    /// <summary>
    /// WebGL2 accelerator implementation for ILGPU.
    /// All GL calls are offloaded to a dedicated Web Worker for main-thread responsiveness.
    /// </summary>
    public class WebGLAccelerator : KernelAccelerator<WebGLCompiledKernel, WebGLKernel>
    {
        /// <summary>Last GLSL source dispatched to the worker. Captured for diagnostics.</summary>
        public static string? LastGeneratedGLSL { get; private set; }
        /// <summary>Callback invoked whenever a GLSL shader is compiled.</summary>
        public static Action<string, string>? OnShaderCompiled { get; set; }

        /// <summary>
        /// True if the WebGL context has been lost (driver crash, GPU reset, etc.).
        /// </summary>
        public bool IsContextLost { get; private set; }

        /// <summary>
        /// Fired when the WebGL context is lost. Parameter is a description message.
        /// </summary>
        public event Action<string>? ContextLost;

        /// <summary>
        /// Gets the WebGL backend used for kernel compilation.
        /// </summary>
        public WebGLBackend Backend { get; private set; } = null!;

        /// <summary>
        /// Effective F64 emulation mode for kernels compiled on this accelerator. Reads
        /// from / writes to the underlying <see cref="WebGLBackend.F64Mode"/>. Use this
        /// to flip between Dekker (fast, ~48-53 bit), Ozaki (strict IEEE 754), and
        /// Disabled (f32 promotion) without recreating the accelerator. Kernels compiled
        /// before the flip retain their original mode in cache; new compilations pick up
        /// the change. See <see cref="AcceleratorRequirements.RequiresFloat64Strict"/>
        /// for the consumer-facing path that auto-promotes to Ozaki.
        /// </summary>
        public F64EmulationMode F64Mode
        {
            get => Backend.F64Mode;
            set => Backend.F64Mode = value;
        }

        /// <summary>
        /// Gets the OffscreenCanvas used for the WebGL2 context (transferred to worker).
        /// </summary>
        public OffscreenCanvas Canvas { get; private set; } = null!;

        /// <summary>
        /// Method info for the static RunKernel method used by kernel launchers.
        /// </summary>
        public static readonly MethodInfo RunKernelMethod = typeof(WebGLAccelerator).GetMethod(
            nameof(RunKernel),
            BindingFlags.Public | BindingFlags.Static)!;

        /// <summary>
        /// Controls verbose logging output.
        /// </summary>
        public static bool VerboseLogging { get; set; } = false;

        /// <summary>
        /// Writes a message to the console.
        /// Caller MUST check <see cref="VerboseLogging"/> BEFORE constructing the message string.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static void Log(string message) => Console.WriteLine(message);

        // ---- Dedicated GL Worker ----
        private Worker? _glWorker;
        private bool _workerInitialized;
        private bool _workerMessageHandlerAttached;

        /// <summary>
        /// Monotonically increasing dispatch ID for correlating worker responses.
        /// </summary>
        private int _nextDispatchId;

        /// <summary>
        /// Monotonically increasing buffer ID for GPU-resident buffer tracking in the worker.
        /// </summary>
        private int _nextWorkerBufferId;

        /// <summary>
        /// Monotonically increasing readback request ID.
        /// </summary>
        private int _nextReadbackRequestId;

        /// <summary>
        /// Monotonically increasing blit request ID.
        /// </summary>
        private int _nextBlitRequestId;

        /// <summary>
        /// Pending dispatches awaiting worker responses, keyed by dispatch ID.
        /// </summary>
        private readonly ConcurrentDictionary<int, PendingDispatch> _pendingDispatches = new();

        /// <summary>
        /// Pending readback requests awaiting worker responses, keyed by request ID.
        /// </summary>
        private readonly ConcurrentDictionary<int, PendingReadback> _pendingReadbacks = new();

        /// <summary>
        /// Pending blit requests awaiting worker ImageBitmap responses, keyed by request ID.
        /// The draw action is invoked synchronously inside the message handler so no JS event
        /// loop turn can clear the canvas between the blit and the DrawImage call.
        /// </summary>
        private record PendingBlit(TaskCompletionSource Tcs, Action<ImageBitmap>? Draw);
        private readonly ConcurrentDictionary<int, PendingBlit> _pendingBlits = new();

        /// <summary>
        /// Pending work tasks for fire-and-forget dispatch. Awaited by SynchronizeAsync.
        /// </summary>
        internal List<Task> PendingWorkTasks { get; } = new List<Task>();

        // ---- Reflection cache: avoids per-dispatch GetProperty calls ----
        private static readonly ConcurrentDictionary<Type, ReflectionMetadataCache> _reflectionCache = new();

        private class ReflectionMetadataCache
        {
            public PropertyInfo? BaseViewProperty { get; set; }
        }

        private static ReflectionMetadataCache GetOrCreateReflectionCache(Type type)
        {
            return _reflectionCache.GetOrAdd(type, t =>
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                return new ReflectionMetadataCache
                {
                    BaseViewProperty = t.GetProperty("BaseView", flags),
                };
            });
        }

        /// <summary>
        /// Resolves any ILGPU buffer or view object to its underlying <see cref="WebGLMemoryBuffer"/>.
        /// Handles MemoryBuffer, MemoryBuffer2D, ArrayView, ArrayView2D, etc. via the same
        /// reflection-based BaseView fallback used by kernel argument marshalling.
        /// </summary>
        public WebGLMemoryBuffer? GetWebGLMemoryBuffer(object bufferOrView)
        {
            IArrayView? arrayView = bufferOrView as IArrayView;
            if (arrayView == null && bufferOrView != null)
            {
                var refCache = GetOrCreateReflectionCache(bufferOrView.GetType());
                if (refCache.BaseViewProperty != null)
                    arrayView = refCache.BaseViewProperty.GetValue(bufferOrView) as IArrayView;
            }
            if (arrayView == null) return null;

            var contiguous = arrayView as IContiguousArrayView;
            if (contiguous == null)
            {
                var viewRefCache = GetOrCreateReflectionCache(arrayView.GetType());
                contiguous = (viewRefCache.BaseViewProperty != null
                    ? viewRefCache.BaseViewProperty.GetValue(arrayView)
                    : arrayView) as IContiguousArrayView;
            }
            return contiguous?.Buffer as WebGLMemoryBuffer;
        }

        #region Construction

        private WebGLAccelerator(Context context, Device device) : base(context, device) { }

        /// <summary>
        /// Creates a new WebGL2 accelerator with a dedicated GL worker.
        /// </summary>
        public static WebGLAccelerator Create(Context context, WebGLILGPUDevice device, WebGLBackendOptions? options)
        {
            var accelerator = new WebGLAccelerator(context, device);
            var canvas = device.NativeDevice.CreateOffscreenCanvas();
            accelerator.Canvas = canvas;
            accelerator.Backend = new WebGLBackend(context, options ?? WebGLBackendOptions.Default);
            accelerator.Init(accelerator.Backend);
            accelerator.DefaultStream = accelerator.CreateStreamInternal();

            // Initialize the dedicated GL worker
            accelerator.InitializeGLWorker();

            if (VerboseLogging) Log($"[WebGL] Accelerator created with dedicated GL worker: {device.NativeDevice.Name}");
            if (VerboseLogging) Log($"[WebGL] Max TF Components: {device.NativeDevice.MaxTransformFeedbackInterleavedComponents}");

            return accelerator;
        }

        #endregion

        #region GL Worker Initialization

        /// <summary>
        /// Creates the dedicated GL worker by loading glWorker.js from the library's static assets.
        /// Transfers the OffscreenCanvas to the worker.
        /// </summary>
        private void InitializeGLWorker()
        {
            // Load worker from the library's static web assets (cache-bust to pick up changes)
            var workerUrl = $"_content/SpawnDev.ILGPU/glWorker.js?v={DateTime.UtcNow.Ticks}";
            _glWorker = new Worker(workerUrl);

            // Transfer OffscreenCanvas to the worker
            // Canvas must be both a property of the message AND in the transfer list
            var initMsg = new { type = "init", canvas = Canvas.JSRef! };
            _glWorker.PostMessage(initMsg, new object[] { Canvas.JSRef! });

            _workerInitialized = true;

            // Attach message handler now (not on first dispatch) so readback
            // responses are received even before any kernel has been dispatched.
            _workerMessageHandlerAttached = true;
            _glWorker.OnMessage += (msg) => HandleWorkerResponse(msg);
            _glWorker.OnError += (_) =>
            {
                foreach (var kvp in _pendingDispatches)
                {
                    if (_pendingDispatches.TryRemove(kvp.Key, out var p))
                        p.Tcs.TrySetException(new Exception("[WebGL] GL Worker error during kernel execution"));
                }
            };

            if (VerboseLogging) Log("[WebGL] GL Worker initialized and OffscreenCanvas transferred");
        }


        #endregion

        #region Kernel Management

        /// <inheritdoc/>
        protected override WebGLKernel CreateKernel(WebGLCompiledKernel compiledKernel)
        {
            LastGeneratedGLSL = compiledKernel.GLSLSource;
            var kernelName = compiledKernel.EntryPoint.MethodInfo?.Name ?? "glsl_kernel";
            try { OnShaderCompiled?.Invoke(kernelName, compiledKernel.GLSLSource); } catch { }
            return new WebGLKernel(this, compiledKernel, null);
        }

        /// <inheritdoc/>
        protected override WebGLKernel CreateKernel(WebGLCompiledKernel compiledKernel, MethodInfo launcher)
        {
            LastGeneratedGLSL = compiledKernel.GLSLSource;
            var kernelName = compiledKernel.EntryPoint.MethodInfo?.Name ?? "glsl_kernel";
            try { OnShaderCompiled?.Invoke(kernelName, compiledKernel.GLSLSource); } catch { }
            return new WebGLKernel(this, compiledKernel, launcher);
        }

        /// <inheritdoc/>
        protected override MethodInfo GenerateKernelLauncherMethod(WebGLCompiledKernel kernel, int customGroupSize)
        {
            var parameters = kernel.EntryPoint.Parameters;
            var indexType = kernel.EntryPoint.KernelIndexType;
            var argTypes = new List<Type> { typeof(Kernel), typeof(AcceleratorStream), indexType };
            for (int i = 0; i < parameters.Count; i++) argTypes.Add(parameters[i]);

            var dynamicMethod = new DynamicMethod("WebGLLauncher", typeof(void), argTypes.ToArray(), typeof(WebGLAccelerator).Module);
            var ilGenerator = dynamicMethod.GetILGenerator();
            var argsLocal = ilGenerator.DeclareLocal(typeof(object[]));

            ilGenerator.Emit(OpCodes.Ldc_I4, parameters.Count);
            ilGenerator.Emit(OpCodes.Newarr, typeof(object));
            ilGenerator.Emit(OpCodes.Stloc, argsLocal);

            for (int i = 0; i < parameters.Count; i++)
            {
                ilGenerator.Emit(OpCodes.Ldloc, argsLocal);
                ilGenerator.Emit(OpCodes.Ldc_I4, i);
                ilGenerator.Emit(OpCodes.Ldarg, i + 3);
                var paramType = parameters[i];
                if (paramType.IsValueType) ilGenerator.Emit(OpCodes.Box, paramType);
                ilGenerator.Emit(OpCodes.Stelem_Ref);
            }

            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldarg_1);
            ilGenerator.Emit(OpCodes.Ldarg_2);
            if (indexType.IsValueType) ilGenerator.Emit(OpCodes.Box, indexType);

            ilGenerator.Emit(OpCodes.Ldloc, argsLocal);
            ilGenerator.EmitCall(OpCodes.Call, RunKernelMethod, null);
            ilGenerator.Emit(OpCodes.Ret);

            return dynamicMethod;
        }

        #endregion

        #region Worker Buffer Management

        /// <summary>
        /// Allocates a unique buffer ID for the GL worker.
        /// </summary>
        internal int AllocateWorkerBufferId() => Interlocked.Increment(ref _nextWorkerBufferId);

        /// <summary>
        /// Ensures a WebGLMemoryBuffer is allocated and uploaded in the GL worker.
        /// Called before dispatch for any buffer that needs to exist in the worker.
        /// </summary>
        internal void EnsureBufferInWorker(WebGLMemoryBuffer memBuffer, string glslType)
        {
            if (!memBuffer.IsAllocatedInWorker)
            {
                memBuffer.GlslType = glslType;
                // Allocate in worker
                _glWorker!.PostMessage(new
                {
                    type = "allocBuffer",
                    bufferId = memBuffer.WorkerBufferId,
                    byteSize = (int)memBuffer.LengthInBytes,
                    glslType
                });
                memBuffer.IsAllocatedInWorker = true;
                memBuffer.NeedsUpload = true; // Fresh alloc always needs initial data
            }

            if (memBuffer.NeedsUpload)
            {
                UploadBufferToWorker(memBuffer);
            }
        }

        /// <summary>
        /// Uploads CPU-side buffer data to the GL worker.
        /// Creates a copy of the data and transfers it (zero-copy for the copy).
        /// </summary>
        private void UploadBufferToWorker(WebGLMemoryBuffer memBuffer)
        {
            if (memBuffer.BackingArray == null) return;

            // Create a copy to transfer (original stays intact on main thread)
            using var dataCopy = memBuffer.BackingArray.Slice();
            var copyBuffer = dataCopy.Buffer;

            _glWorker!.PostMessage(new
            {
                type = "uploadBuffer",
                bufferId = memBuffer.WorkerBufferId,
                buffer = copyBuffer,
                byteOffset = 0,
                byteLength = (int)memBuffer.LengthInBytes
            }, new object[] { copyBuffer });

            memBuffer.NeedsUpload = false;
        }

        /// <summary>
        /// Sends a freeBuffer message to the GL worker.
        /// </summary>
        internal void FreeWorkerBuffer(int workerBufferId)
        {
            _glWorker?.PostMessage(new { type = "freeBuffer", bufferId = workerBufferId });
        }

        /// <summary>
        /// Worker-side GPU→GPU copy. The worker's <c>entry.data</c> is the canonical
        /// post-kernel state for any buffer that was a kernel output — the CPU-side
        /// <c>_backingArray</c> is NEVER refreshed by a Transform Feedback write, only
        /// by an explicit <see cref="ReadbackAndGetUint8ArrayAsync"/>. Routing GPU→GPU
        /// CopyTo/CopyFrom through the main thread reads stale zeros from _backingArray
        /// and silently produces a destination of zeros.
        ///
        /// This method bypasses the main thread entirely: it ensures both buffers are
        /// allocated in the worker (uploading any CPU-dirty data first via
        /// <see cref="EnsureBufferInWorker"/>) and posts a <c>copyBuffer</c> message that
        /// the worker handles in-order with prior dispatches. The destination texture is
        /// re-uploaded inside the worker so subsequent dispatches see the new bytes.
        ///
        /// After this call the destination's worker entry.data is correct; the
        /// destination's CPU _backingArray is left stale (a subsequent
        /// <see cref="ReadbackAndGetUint8ArrayAsync"/> will refresh it).
        /// </summary>
        internal void WorkerCopyBuffer(
            WebGLMemoryBuffer source, long srcByteOffset,
            WebGLMemoryBuffer destination, long dstByteOffset,
            long byteLength)
        {
            if (_glWorker == null)
                throw new InvalidOperationException("GL worker not initialized");
            EnsureBufferInWorker(source, source.GlslType);
            EnsureBufferInWorker(destination, destination.GlslType);
            _glWorker.PostMessage(new
            {
                type = "copyBuffer",
                srcBufferId = source.WorkerBufferId,
                srcByteOffset = (int)srcByteOffset,
                dstBufferId = destination.WorkerBufferId,
                dstByteOffset = (int)dstByteOffset,
                byteLength = (int)byteLength
            });
        }

        /// <summary>
        /// GPGPU scatter: dst[destIndex[i]] = src[i] for i in [0, n). WebGL2 transform feedback is
        /// gather-only, so scatter is done by rasterizing one GL_POINT per element at the destination
        /// texel and writing the value in the fragment shader (see glWorker.js handleScatter). The
        /// result lives in the destination's GPU TEXTURE (zero-copy — the next op reads it directly);
        /// the destination's CPU mirror is marked stale and refreshed lazily only on host readback.
        /// dst/src must share the element type; destIndex is int. Building block for WebGL RadixSort.
        /// </summary>
        internal void WorkerScatter(
            WebGLMemoryBuffer destination,
            WebGLMemoryBuffer source,
            WebGLMemoryBuffer destIndex,
            int count)
        {
            if (_glWorker == null)
                throw new InvalidOperationException("GL worker not initialized");
            EnsureBufferInWorker(destination, destination.GlslType);
            EnsureBufferInWorker(source, source.GlslType);
            EnsureBufferInWorker(destIndex, destIndex.GlslType);
            _glWorker.PostMessage(new
            {
                type = "scatter",
                dstBufferId = destination.WorkerBufferId,
                srcBufferId = source.WorkerBufferId,
                destBufferId = destIndex.WorkerBufferId,
                n = count
            });
        }

        /// <summary>
        /// Public GPGPU scatter over ILGPU views/buffers: destination[destIndex[i]] = source[i] for
        /// i in [0, count). destination and source must share the element type; destIndex is int.
        /// The result lives in the destination's GPU texture (zero-copy); a later CopyToHostAsync
        /// refreshes the CPU mirror lazily. Building block for a WebGL multi-pass RadixSort.
        /// </summary>
        public void Scatter(object destination, object source, object destIndex, int count,
            string valueGlslType = "int")
        {
            var dst = GetWebGLMemoryBuffer(destination)
                ?? throw new ArgumentException("Scatter: destination is not a WebGL buffer/view", nameof(destination));
            var src = GetWebGLMemoryBuffer(source)
                ?? throw new ArgumentException("Scatter: source is not a WebGL buffer/view", nameof(source));
            var idx = GetWebGLMemoryBuffer(destIndex)
                ?? throw new ArgumentException("Scatter: destIndex is not a WebGL buffer/view", nameof(destIndex));
            // The scattered values render into a color-renderable texture of the matching type
            // (R32I/R32UI in core WebGL2; R32F needs EXT_color_buffer_float). A buffer only ever used
            // in scatter (never a kernel dispatch) keeps the default GlslType "float"; set the real
            // types so the worker allocates the right texture format. Dest indices are always int.
            dst.GlslType = valueGlslType;
            src.GlslType = valueGlslType;
            idx.GlslType = "int";
            WorkerScatter(dst, src, idx, count);
        }

        /// <summary>
        /// Requests readback of a specific buffer from the GL worker and returns a Uint8Array view.
        /// This is the only path for GPU→CPU data transfer.
        /// </summary>
        internal async Task<Uint8Array> ReadbackAndGetUint8ArrayAsync(
            WebGLMemoryBuffer memBuffer, long sourceByteOffset = 0, long? copyBytes = null)
        {
            // Ensure the buffer exists in the worker (handles readback before any dispatch)
            EnsureBufferInWorker(memBuffer, memBuffer.GlslType);

            // Wait for any pending dispatches first
            if (PendingWorkTasks.Count > 0)
            {
                await Task.WhenAll(PendingWorkTasks);
                PendingWorkTasks.Clear();
            }

            // Request readback from worker
            var requestId = Interlocked.Increment(ref _nextReadbackRequestId);
            var tcs = new TaskCompletionSource<ArrayBuffer>();
            _pendingReadbacks[requestId] = new PendingReadback { Tcs = tcs, MemoryBuffer = memBuffer };

            _glWorker!.PostMessage(new
            {
                type = "readbackBuffer",
                bufferId = memBuffer.WorkerBufferId,
                requestId
            });

            // Wait for the worker to respond with the data
            var resultBuffer = await tcs.Task;

            // Update the CPU-side backing array
            memBuffer.ReplaceArrayBuffer(resultBuffer);

            // Return the requested slice
            if (memBuffer.BackingArray == null) return new Uint8Array();
            return copyBytes == null
                ? memBuffer.BackingArray.SubArray(sourceByteOffset)
                : memBuffer.BackingArray.SubArray(sourceByteOffset, copyBytes.Value + sourceByteOffset);
        }

        /// <summary>
        /// Requests the GL worker to create an ImageBitmap from the buffer's current pixel data
        /// and transfer it to the main thread. No GPU→CPU readback — entry.data is always
        /// current after dispatch. The returned ImageBitmap can be drawn directly via
        /// CanvasRenderingContext2D.DrawImage without any intermediate WebGL context.
        /// </summary>
        /// <summary>
        /// Sends a blitBuffer message to the worker, then — synchronously inside the
        /// message handler callback (before any JS event loop turn) — invokes
        /// <paramref name="draw"/> with the resulting <see cref="ImageBitmap"/>.
        /// This mirrors WebGPU's synchronous DrawImage pattern so the canvas cannot
        /// be cleared between the blit and the draw.
        /// </summary>
        public async Task BlitAndDrawAsync(WebGLMemoryBuffer memBuffer, int width, int height,
            Action<ImageBitmap> draw)
        {
            EnsureBufferInWorker(memBuffer, memBuffer.GlslType);

            if (PendingWorkTasks.Count > 0)
            {
                await Task.WhenAll(PendingWorkTasks);
                PendingWorkTasks.Clear();
            }

            var requestId = Interlocked.Increment(ref _nextBlitRequestId);
            var tcs = new TaskCompletionSource();
            _pendingBlits[requestId] = new PendingBlit(tcs, draw);

            _glWorker!.PostMessage(new
            {
                type = "blitBuffer",
                bufferId = memBuffer.WorkerBufferId,
                width,
                height,
                requestId
            });

            await tcs.Task;
        }

        #endregion

        #region Kernel Execution

        /// <summary>
        /// Executes a WebGL2 kernel by dispatching to the dedicated GL worker.
        /// This is fire-and-forget — actual GL work happens on the worker thread.
        /// Buffers are GPU-resident in the worker; no ArrayBuffer transfers per dispatch.
        /// Completion is tracked via PendingWorkTasks, awaited by SynchronizeAsync.
        /// </summary>
        public static void RunKernel(Kernel kernel, AcceleratorStream stream, object dimension, object[] args)
        {
            var webGlAccel = (WebGLAccelerator)kernel.Accelerator;
            if (webGlAccel.IsContextLost)
                throw new InvalidOperationException("WebGL context has been lost and cannot accept commands.");

            var webGlKernel = (WebGLKernel)kernel;
            var compiledKernel = webGlKernel.CompiledKernel;


            // Determine dispatch size
            int totalVertices = 1;
            int dimX = 1, dimY = 1, dimZ = 1;

            int groupDimX = 1, gridDimX = 1, gridDimY = 1;

            if (dimension is KernelConfig config)
            {
                dimX = config.GridDim.X * config.GroupDim.X;
                dimY = config.GridDim.Y * config.GroupDim.Y;
                dimZ = config.GridDim.Z * config.GroupDim.Z;
                totalVertices = dimX * dimY * dimZ;
                groupDimX = config.GroupDim.X;
                gridDimX = config.GridDim.X;
                gridDimY = config.GridDim.Y;
            }
            else if (dimension is Index1D i1) { dimX = i1.X; totalVertices = dimX; }
            else if (dimension is Index2D i2) { dimX = i2.X; dimY = i2.Y; totalVertices = dimX * dimY; }
            else if (dimension is Index3D i3) { dimX = i3.X; dimY = i3.Y; dimZ = i3.Z; totalVertices = dimX * dimY * dimZ; }
            else if (dimension is LongIndex1D l1) { dimX = (int)l1.X; totalVertices = dimX; }
            else if (dimension is LongIndex2D l2) { dimX = (int)l2.X; dimY = (int)l2.Y; totalVertices = dimX * dimY; }
            else if (dimension is LongIndex3D l3) { dimX = (int)l3.X; dimY = (int)l3.Y; dimZ = (int)l3.Z; totalVertices = dimX * dimY * dimZ; }

            if (VerboseLogging) Log($"[WebGL-Debug] Dispatch: {totalVertices} vertices (dim={dimX}x{dimY}x{dimZ})");

            // Marshal arguments — now returns buffer_ref params, no ArrayBuffer transfers
            var (jsParams, strideMap, outputs) = MarshalArguments(compiledKernel, args, webGlAccel);


            // Stable per-kernel program id for the glWorker program cache (programCache[programId]).
            // MUST NOT be GLSLSource.GetHashCode(): a non-unique string hash would let two distinct
            // shaders collide on one cache slot → worker runs the WRONG compiled program → silent wrong
            // output (same bug class as the Wasm kernelId). Uses the compiled kernel's monotonic ProgramId.
            var programId = compiledKernel.ProgramId;

            var varyingNames = compiledKernel.OutputVaryings
                .Select(o => o.VaryingName)
                .ToArray();

            // Assign unique dispatch ID for response correlation
            var dispatchId = Interlocked.Increment(ref webGlAccel._nextDispatchId);

            // Build dispatch message — no ArrayBuffer transfers needed
            var dispatchMsg = new
            {
                type = "dispatch",
                dispatchId,
                programId,
                source = compiledKernel.GLSLSource,
                varyingNames,
                totalVertices,
                dimX,
                dimY,
                dimZ,
                groupDimX,
                gridDimX,
                gridDimY,
                @params = jsParams.ToArray(),
                strides = strideMap,
                outputs = outputs.ToArray()
            };

            // Track this dispatch via TCS
            var tcs = new TaskCompletionSource();
            var pending = new PendingDispatch { Tcs = tcs };
            webGlAccel._pendingDispatches[dispatchId] = pending;


            // No transfer list — all data is GPU-resident in the worker
            webGlAccel._glWorker!.PostMessage(dispatchMsg);
            webGlAccel.PendingWorkTasks.Add(tcs.Task);
        }

        /// <summary>
        /// Handles all worker response messages (dispatch completion and readback results).
        /// </summary>
        private void HandleWorkerResponse(MessageEvent msg)
        {
            try
            {
                using var data = msg.GetData<JSObject>();

                // Check if this is a readback result
                var msgType = data.JSRef!.Get<string?>("type");
                if (msgType == "readbackResult")
                {
                    HandleReadbackResponse(data);
                    return;
                }

                if (msgType == "blitResult")
                {
                    HandleBlitResponse(data);
                    return;
                }

                if (msgType == "contextlost")
                {
                    IsContextLost = true;
                    if (VerboseLogging) Log("[WebGL] Context lost");
                    ContextLost?.Invoke("WebGL context lost");
                    return;
                }

                if (msgType == "contextrestored")
                {
                    IsContextLost = false;
                    if (VerboseLogging) Log("[WebGL] Context restored");
                    return;
                }

                // Otherwise it's a dispatch completion
                var dispatchId = data.JSRef!.Get<int>("dispatchId");

                if (!_pendingDispatches.TryRemove(dispatchId, out var pending))
                {
                    if (VerboseLogging) Log($"[WebGL] Warning: received response for unknown dispatchId {dispatchId}");
                    return;
                }

                var done = data.JSRef!.Get<bool>("done");

                if (!done)
                {
                    var errorMsg = data.JSRef!.Get<string?>("error") ?? "Unknown GL worker error";
                    pending.Tcs.TrySetException(new Exception($"[WebGL] GL Worker error: {errorMsg}"));
                    return;
                }

                pending.Tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                if (VerboseLogging) Log($"[WebGL] Error processing worker response: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles a blitResult response from the GL worker, resolving the pending ImageBitmap TCS.
        /// </summary>
        private void HandleBlitResponse(JSObject data)
        {
            var requestId = data.JSRef!.Get<int>("requestId");
            if (!_pendingBlits.TryRemove(requestId, out var blit))
            {
                if (VerboseLogging) Log($"[WebGL] Warning: received blitResult for unknown requestId {requestId}");
                return;
            }

            var error = data.JSRef!.Get<string?>("error");
            if (error != null)
            {
                blit.Tcs.TrySetException(new Exception($"[WebGL] Blit error: {error}"));
                return;
            }

            // Draw synchronously here, before any await continuation can resume.
            // This mirrors WebGPU's synchronous DrawImage so no Blazor re-render can
            // clear the canvas between the blit and the draw.
            using var bitmap = data.JSRef!.Get<ImageBitmap>("bitmap");
            blit.Draw?.Invoke(bitmap);
            blit.Tcs.TrySetResult();
        }

        /// <summary>
        /// Handles a readback response from the GL worker.
        /// </summary>
        private void HandleReadbackResponse(JSObject data)
        {
            var requestId = data.JSRef!.Get<int>("requestId");
            if (!_pendingReadbacks.TryRemove(requestId, out var pending))
            {
                if (VerboseLogging) Log($"[WebGL] Warning: received readback for unknown requestId {requestId}");
                return;
            }

            var error = data.JSRef!.Get<string?>("error");
            if (error != null)
            {
                pending.Tcs.TrySetException(new Exception($"[WebGL] Readback error: {error}"));
                return;
            }

            // Get the transferred ArrayBuffer (take ownership)
            var resultBuffer = data.JSRef!.Get<ArrayBuffer>("buffer");
            pending.Tcs.TrySetResult(resultBuffer);
        }

        /// <summary>
        /// Tracks a pending dispatch to the GL worker.
        /// </summary>
        private class PendingDispatch
        {
            public TaskCompletionSource Tcs { get; set; } = null!;
        }

        /// <summary>
        /// Tracks a pending readback request.
        /// </summary>
        private class PendingReadback
        {
            public TaskCompletionSource<ArrayBuffer> Tcs { get; set; } = null!;
            public WebGLMemoryBuffer MemoryBuffer { get; set; } = null!;
        }

        /// <summary>
        /// Marshals kernel arguments into JS-compatible parameter descriptors.
        /// Buffer arguments are sent as buffer_ref (referencing GPU-resident buffers in the worker).
        /// Returns (jsParams, strideMap, outputDescriptors).
        /// </summary>
        private static (List<object> jsParams, Dictionary<int, int[]> strides, List<object> outputs)
            MarshalArguments(WebGLCompiledKernel compiledKernel, object[] args, WebGLAccelerator webGlAccel)
        {
            var jsParams = new List<object>();
            var strideMap = new Dictionary<int, int[]>();

            // glslParamOffset must mirror GLSLKernelFunctionGenerator.KernelParamOffset:
            //   - Auto-grouped (Index1D/2D/3D + ...): 1 — IR param 0 is the implicit thread index
            //   - Explicit-launch (LoadStreamKernel<TParam>) with non-Index first arg: 0
            //   - Explicit-launch with Index1D/2D/3D first arg: 1
            //   - IndexType.None: 0
            // Without this alignment, the host's args[0] maps to a different GLSL binding than
            // the kernel expects, leaving body-struct field bindings unfilled (texelFetch returns
            // zero) — Tests23_RegisterHeavyBody_ExplicitOneByOne_NoLaunchFailure regression.
            int glslParamOffset = 1;
            var ep = compiledKernel.EntryPoint;
            if (ep.IndexType == IndexType.None)
            {
                glslParamOffset = 0;
            }
            else if (ep.IndexType == IndexType.KernelConfig)
            {
                if (ep.Parameters.Count > 0)
                {
                    var first = ep.Parameters[0];
                    glslParamOffset = (first == typeof(Index1D) || first == typeof(Index2D) || first == typeof(Index3D)) ? 1 : 0;
                }
                else
                {
                    glslParamOffset = 0;
                }
            }

            for (int pIdx = 0; pIdx < args.Length; pIdx++)
            {
                int glslParamIndex = pIdx + glslParamOffset;
                var arg = args[pIdx];

                // Body struct argument — decompose into per-field buffer_ref entries.
                // Detected by checking the compiled kernel's BodyStructManifest for the
                // current GLSL param index. Walks the user struct's fields via
                // reflection in IR-field order (which matches C# declaration order for
                // value types) and emits one buffer_ref per ArrayView field with a
                // synthetic paramIndex = (glslParamIndex + 1) * 1000 + fieldIdx.
                if (arg != null && compiledKernel.BodyStructManifest.Count > 0
                    && compiledKernel.BodyStructManifest.Any(e => e.ParamIndex == glslParamIndex))
                {
                    EmitBodyStructDispatch(arg, glslParamIndex, compiledKernel, webGlAccel, jsParams);
                    continue;
                }

                IArrayView? arrayView = arg as IArrayView;

                if (arrayView == null && arg != null)
                {
                    var refCache = GetOrCreateReflectionCache(arg.GetType());
                    if (refCache.BaseViewProperty != null)
                        arrayView = refCache.BaseViewProperty.GetValue(arg) as IArrayView;

                    // ILGPU's IR unwraps single-field structs whose only field is an
                    // ArrayView — `struct { ArrayView<T> S0 }` becomes `ArrayView<T>`
                    // at the IR level. Mirror that here: if the struct has exactly
                    // one IArrayView field, treat it as that field's view. Without
                    // this, the host passes the struct value (via FlattenStructFields
                    // for uniforms) but the GLSL kernel expects a single sampler
                    // binding for the unwrapped view, leaving the buffer unbound and
                    // texelFetch returning zero. Surfaced 2026-05-05 by
                    // Tests23_OnlyShortBodyStruct (single `ArrayView<short>` body field).
                    if (arrayView == null && arg.GetType().IsValueType && !arg.GetType().IsPrimitive)
                    {
                        var fields = arg.GetType().GetFields(
                            BindingFlags.Public | BindingFlags.Instance);
                        IArrayView? singleViewField = null;
                        int viewFieldCount = 0;
                        foreach (var f in fields)
                        {
                            var fv = f.GetValue(arg);
                            if (fv is IArrayView fva)
                            {
                                singleViewField = fva;
                                viewFieldCount++;
                                if (viewFieldCount > 1) break;
                            }
                            else if (fv != null)
                            {
                                var fvRc = GetOrCreateReflectionCache(f.FieldType);
                                if (fvRc.BaseViewProperty != null
                                    && fvRc.BaseViewProperty.GetValue(fv) is IArrayView fvbv)
                                {
                                    singleViewField = fvbv;
                                    viewFieldCount++;
                                    if (viewFieldCount > 1) break;
                                }
                            }
                        }
                        if (viewFieldCount == 1)
                            arrayView = singleViewField;
                    }
                }

                if (arrayView != null)
                {
                    // Buffer argument — use GPU-resident buffer reference
                    var contiguous = arrayView as IContiguousArrayView;
                    if (contiguous == null)
                    {
                        var viewRefCache = GetOrCreateReflectionCache(arrayView.GetType());
                        contiguous = (viewRefCache.BaseViewProperty != null ? viewRefCache.BaseViewProperty.GetValue(arrayView) : arrayView) as IContiguousArrayView;
                    }

                    if (contiguous == null)
                        throw new Exception($"Argument {pIdx} is not a contiguous buffer");

                    var memBuffer = contiguous.Buffer as WebGLMemoryBuffer;
                    if (memBuffer == null)
                        throw new Exception($"Argument {pIdx} has no WebGL memory buffer");

                    var elementSize = contiguous.ElementSize;
                    var length = (int)contiguous.Length;

                    // Get GLSL type from parameter binding
                    var binding = compiledKernel.ParameterBindings
                        .FirstOrDefault(b => b.ParamIndex == glslParamIndex && b.Kind == KernelParamKind.Buffer);
                    string bufferGlslType = binding.GlslType ?? "float";

                    // Ensure buffer is allocated and uploaded in the worker
                    webGlAccel.EnsureBufferInWorker(memBuffer, bufferGlslType);

                    // Send buffer reference with SubView element offset
                    // When a SubView starts at a non-zero index within the parent buffer,
                    // the shader must add this offset to all texelFetch indices.
                    int elementOffset = (int)contiguous.Index;
                    jsParams.Add(new
                    {
                        kind = "buffer_ref",
                        bufferId = memBuffer.WorkerBufferId,
                        paramIndex = glslParamIndex,
                        elementCount = length,
                        elementOffset
                    });

                    // Extract stride dimensions for multi-dim views
                    if (arg != null)
                    {
                        int[] dims = ExtractViewDimensions(arg, arg.GetType());
                        if (dims.Length >= 2)
                        {
                            strideMap[glslParamIndex] = dims;
                        }
                    }
                }
                else
                {
                    // Scalar argument (unchanged)
                    if (arg is double dVal && webGlAccel.Backend.EnableF64Emulation)
                    {
                        var bits = BitConverter.DoubleToUInt64Bits(dVal);
                        jsParams.Add(new
                        {
                            kind = "scalar_emu64",
                            paramIndex = glslParamIndex,
                            lo = (uint)(bits & 0xFFFFFFFF),
                            hi = (uint)(bits >> 32)
                        });
                    }
                    else if (arg is long lVal && webGlAccel.Backend.EnableI64Emulation)
                    {
                        var bits = (ulong)lVal;
                        jsParams.Add(new
                        {
                            kind = "scalar_emu64",
                            paramIndex = glslParamIndex,
                            lo = (uint)(bits & 0xFFFFFFFF),
                            hi = (uint)(bits >> 32)
                        });
                    }
                    else
                    {
                        // Determine scalar type from C# runtime type first.
                        // ILGPU IR maps all integer types to BasicValueType.Int32/Int64,
                        // so GLSL declares them as int/uint. Map all C# integer types here.
                        string scalarType = arg switch
                        {
                            int => "int",
                            uint => "uint",
                            float => "float",
                            double => "double",
                            bool => "bool",
                            byte => "int",
                            sbyte => "int",
                            short => "int",
                            ushort => "int",
                            long => "int",
                            ulong => "uint",
                            _ => null
                        };

                        // For primitive integer scalars, override with the GLSL type from the compiled kernel.
                        // ILGPU IR uses BasicValueType.Int32 for both int and uint, so the GLSL codegen
                        // declares all 32-bit integer scalars as 'int'. We must match this when choosing
                        // between gl.uniform1i/uniform1ui — a mismatch causes GL_INVALID_OPERATION.
                        if (scalarType == "int" || scalarType == "uint")
                        {
                            var scalarBinding = compiledKernel.ParameterBindings
                                .FirstOrDefault(b => b.ParamIndex == glslParamIndex && b.Kind == KernelParamKind.Scalar);
                            if (scalarBinding.Kind == KernelParamKind.Scalar && scalarBinding.GlslType != null)
                                scalarType = scalarBinding.GlslType;
                        }

                        if (scalarType != null)
                        {
                            // When the GLSL type is 'int' but the C# value is uint,
                            // reinterpret the bits as int to match the uniform type.
                            object value = arg switch
                            {
                                int iVal => scalarType == "uint" ? (object)(uint)iVal : iVal,
                                uint uiVal => scalarType == "int" ? (object)(int)uiVal : uiVal,
                                float fVal => fVal,
                                double dValFb => (float)dValFb,
                                bool blVal => blVal ? 1 : 0,
                                byte bVal => (int)bVal,
                                sbyte sbVal => (int)sbVal,
                                short sVal => (int)sVal,
                                ushort usVal => (int)usVal,
                                long lValFb => (int)lValFb,
                                ulong ulVal => scalarType == "int" ? (object)(int)(uint)ulVal : (uint)ulVal,
                                _ => throw new NotSupportedException($"Unsupported scalar: {arg?.GetType()}")
                            };

                            jsParams.Add(new
                            {
                                kind = "scalar",
                                paramIndex = glslParamIndex,
                                scalarType,
                                value
                            });
                        }
                        else if (arg != null && arg.GetType().IsDefined(
                            typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
                        {
                            // Display class (capturing lambda captures).
                            // Extract field values and pass as scalar/struct
                            // depending on field count.
                            var captFields = arg.GetType().GetFields(
                                System.Reflection.BindingFlags.Instance |
                                System.Reflection.BindingFlags.Public |
                                System.Reflection.BindingFlags.NonPublic);
                            if (captFields.Length == 1
                                && captFields[0].FieldType.IsPrimitive)
                            {
                                // Single primitive capture: IR collapses
                                // to a scalar. Pass as scalar uniform.
                                // Use GLSL type from compiled kernel to match uniform declaration.
                                var fieldVal = captFields[0].GetValue(arg);
                                var captBinding = compiledKernel.ParameterBindings
                                    .FirstOrDefault(b => b.ParamIndex == glslParamIndex && b.Kind == KernelParamKind.Scalar);
                                string? captGlslType = captBinding.Kind == KernelParamKind.Scalar ? captBinding.GlslType : null;
                                string st = captGlslType ?? fieldVal switch
                                {
                                    int => "int",
                                    uint => "uint",
                                    float => "float",
                                    double => "float",
                                    byte or sbyte or short or ushort => "int",
                                    long => "int",
                                    ulong => "uint",
                                    _ => "int"
                                };
                                object vl = fieldVal switch
                                {
                                    int iv => st == "uint" ? (object)(uint)iv : iv,
                                    uint uv => st == "int" ? (object)(int)uv : uv,
                                    float fvl => fvl,
                                    double dvl => (float)dvl,
                                    bool bvl => bvl ? 1 : 0,
                                    byte bvl2 => (int)bvl2,
                                    sbyte sbvl => (int)sbvl,
                                    short svl => (int)svl,
                                    ushort usvl => (int)usvl,
                                    long lvl => (int)lvl,
                                    ulong ulvl => st == "int" ? (object)(int)(uint)ulvl : (uint)ulvl,
                                    _ => fieldVal ?? 0
                                };
                                jsParams.Add(new
                                {
                                    kind = "scalar",
                                    paramIndex = glslParamIndex,
                                    scalarType = st,
                                    value = vl
                                });
                            }
                            else
                            {
                                // Multi-field capture: IR keeps as struct.
                                var flatFields = new List<object>();
                                FlattenStructFieldsForUniform(arg, "", flatFields);
                                jsParams.Add(new
                                {
                                    kind = "struct",
                                    paramIndex = glslParamIndex,
                                    fields = flatFields.ToArray()
                                });
                            }
                        }
                        else if (arg != null && arg.GetType().IsValueType && !arg.GetType().IsEnum)
                        {
                            var flatFields = new List<object>();
                            FlattenStructFieldsForUniform(arg, "", flatFields);
                            jsParams.Add(new
                            {
                                kind = "struct",
                                paramIndex = glslParamIndex,
                                fields = flatFields.ToArray()
                            });
                        }
                        else
                        {
                            throw new NotSupportedException($"Unsupported scalar argument type: {arg?.GetType()}");
                        }
                    }
                }
            }

            // Build output varying descriptors — reference bufferIds instead of argIndex.
            // For body-struct output view fields, the OutputVaryingInfo's ParamIndex is
            // a synthetic encoding (paramIdx+1)*1000 + irFieldIdx — look up the manifest
            // entry to get the C# field index for reflection (IR + C# indices diverge
            // when ArrayView1D wrappers expand into multiple IR fields).
            var outputs = new List<object>();
            var outputVaryings = compiledKernel.OutputVaryings;
            var manifest = compiledKernel.BodyStructManifest;
            for (int outIdx = 0; outIdx < outputVaryings.Count; outIdx++)
            {
                var outputInfo = outputVaryings[outIdx];
                int paramIndexEffective = outputInfo.ParamIndex;
                int bodyStructClrFieldIdx = -1;
                if (paramIndexEffective >= 1000)
                {
                    var manifestEntry = manifest.FirstOrDefault(e => e.SyntheticParamIndex == paramIndexEffective);
                    if (manifestEntry != null)
                    {
                        bodyStructClrFieldIdx = manifestEntry.ClrFieldIndex;
                        paramIndexEffective = manifestEntry.ParamIndex;
                    }
                }
                var argsIdx = paramIndexEffective - glslParamOffset;

                if (argsIdx >= 0 && argsIdx < args.Length)
                {
                    var arg = args[argsIdx];
                    IArrayView? arrView = arg as IArrayView;
                    if (arrView == null && arg != null && bodyStructClrFieldIdx >= 0)
                    {
                        // Body-struct output view field — extract via reflection on
                        // the user's struct value using the C# field index.
                        var bsFields = arg.GetType().GetFields(
                            BindingFlags.Public | BindingFlags.Instance);
                        if (bodyStructClrFieldIdx < bsFields.Length)
                        {
                            var fieldValue = bsFields[bodyStructClrFieldIdx].GetValue(arg);
                            arrView = fieldValue as IArrayView;
                            if (arrView == null && fieldValue != null)
                            {
                                var rc = GetOrCreateReflectionCache(bsFields[bodyStructClrFieldIdx].FieldType);
                                if (rc.BaseViewProperty != null)
                                    arrView = rc.BaseViewProperty.GetValue(fieldValue) as IArrayView;
                            }
                        }
                    }
                    if (arrView == null && arg != null)
                    {
                        var refCache = GetOrCreateReflectionCache(arg.GetType());
                        if (refCache.BaseViewProperty != null)
                            arrView = refCache.BaseViewProperty.GetValue(arg) as IArrayView;
                    }

                    if (arrView != null)
                    {
                        var contiguous = arrView as IContiguousArrayView;
                        if (contiguous == null)
                        {
                            var viewRefCache = GetOrCreateReflectionCache(arrView.GetType());
                            contiguous = (viewRefCache.BaseViewProperty != null ? viewRefCache.BaseViewProperty.GetValue(arrView) : arrView) as IContiguousArrayView;
                        }

                        if (contiguous?.Buffer is WebGLMemoryBuffer webGlMem)
                        {
                            // Detect sub-word element types for TF packing
                            int subWordElementSize = 0;
                            if (contiguous.ElementSize < 4)
                                subWordElementSize = contiguous.ElementSize;

                            outputs.Add(new
                            {
                                bufferId = webGlMem.WorkerBufferId,
                                paramIndex = outputInfo.ParamIndex,
                                outputIndex = outputInfo.OutputIndex,
                                varyingName = outputInfo.VaryingName,
                                isEmulated = outputInfo.IsEmulated,
                                emulatedSuffix = outputInfo.EmulatedSuffix ?? "",
                                fieldIndex = outputInfo.FieldIndex,
                                storeSlot = outputInfo.StoreSlot,
                                storeCount = outputInfo.StoreCount,
                                isAtomicVote = outputInfo.IsAtomicVote,
                                writeByteOffset = (int)(contiguous.Index * contiguous.ElementSize),
                                writeLengthBytes = (int)contiguous.LengthInBytes,
                                subWordElementSize
                            });
                        }
                    }
                }
            }

            return (jsParams, strideMap, outputs);
        }

        // ReceiveTransferredBuffers removed — no longer needed with GPU-resident buffers.
        // Data stays in the worker; readback happens only via ReadbackAndGetUint8ArrayAsync.

        #endregion

        #region Abstract Method Implementations

        protected override MemoryBuffer AllocateRawInternal(long length, int elementSize) =>
            new WebGLMemoryBuffer(this, length, elementSize);

        protected override AcceleratorStream CreateStreamInternal() => new WebGLStream(this);

        protected override void SynchronizeInternal()
        {
            // With worker offloading, synchronous Synchronize is a no-op
            // (use SynchronizeAsync extension method instead)
        }

        /// <summary>
        /// Real async drain. The synchronous <see cref="Accelerator.Synchronize"/> is a
        /// no-op under worker offloading; this awaits all pending GL-worker dispatches.
        /// Overrides the core default so algorithm-layer async readback is correct.
        /// </summary>
        public override Task SynchronizeAsync() =>
            WebGLAcceleratorExtensions.SynchronizeAsync(this);

        protected override void OnBind() { }
        protected override void OnUnbind() { }

        protected override void DisposeAccelerator_SyncRoot(bool disposing)
        {
            if (disposing)
            {
                // Terminate the GL worker
                _glWorker?.Terminate();
                _glWorker?.Dispose();
                _glWorker = null;
                _workerInitialized = false;

                Canvas?.Dispose();
            }
        }

        public override TExtension CreateExtension<TExtension, TExtensionProvider>(TExtensionProvider provider) => default;
        protected override PageLockScope<T> CreatePageLockFromPinnedInternal<T>(IntPtr ptr, long numElements) => throw new NotSupportedException();
        protected override int EstimateGroupSizeInternal(Kernel kernel, int dynamicSharedMemorySize, int maxGridSize, out int groupSize) { groupSize = 1; return 1; }
        protected override int EstimateGroupSizeInternal(Kernel kernel, Func<int, int> computeSharedMemorySize, int maxGridSize, out int groupSize) { groupSize = 1; return 1; }
        protected override int EstimateMaxActiveGroupsPerMultiprocessorInternal(Kernel kernel, int groupSize, int dynamicSharedMemorySize) => 1;
        protected override void EnablePeerAccessInternal(Accelerator other) { }
        protected override void DisablePeerAccessInternal(Accelerator other) { }
        protected override bool CanAccessPeerInternal(Accelerator other) => false;

        /// <summary>
        /// Extracts [width, height] or [width, height, depth] dimensions from an ArrayView2D/3D argument.
        /// Used for setting stride uniforms in multi-dimensional view parameters.
        /// </summary>
        private static int[] ExtractViewDimensions(object arg, Type argType)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

            // Try Extent property first (gives the logical dimensions)
            var extentProp = argType.GetProperty("Extent", flags) ?? argType.GetProperty("IntExtent", flags);
            if (extentProp != null)
            {
                try
                {
                    var extent = extentProp.GetValue(arg);
                    if (extent != null)
                    {
                        var extType = extent.GetType();
                        int x = -1, y = -1, z = -1;

                        var fX = extType.GetField("X", flags);
                        if (fX != null) x = Convert.ToInt32(fX.GetValue(extent));
                        else { var pX = extType.GetProperty("X", flags); if (pX != null) x = Convert.ToInt32(pX.GetValue(extent)); }

                        var fY = extType.GetField("Y", flags);
                        if (fY != null) y = Convert.ToInt32(fY.GetValue(extent));
                        else { var pY = extType.GetProperty("Y", flags); if (pY != null) y = Convert.ToInt32(pY.GetValue(extent)); }

                        var fZ = extType.GetField("Z", flags);
                        if (fZ != null) z = Convert.ToInt32(fZ.GetValue(extent));
                        else { var pZ = extType.GetProperty("Z", flags); if (pZ != null) z = Convert.ToInt32(pZ.GetValue(extent)); }

                        if (x > 0 && y > 0 && z > 0) return new[] { x, y, z };
                        if (x > 0 && y > 0) return new[] { x, y };
                    }
                }
                catch { }
            }

            // Fallback: scan non-primitive fields/properties looking for struct with X, Y, Z
            foreach (var field in argType.GetFields(flags))
            {
                if (field.FieldType.IsPrimitive || field.FieldType.IsPointer) continue;
                try
                {
                    var val = field.GetValue(arg);
                    if (val == null) continue;
                    var t = val.GetType();
                    var xF = t.GetField("X", flags);
                    var yF = t.GetField("Y", flags);
                    if (xF != null && yF != null)
                    {
                        int x = Convert.ToInt32(xF.GetValue(val));
                        int y = Convert.ToInt32(yF.GetValue(val));
                        if (x > 0 && y > 0)
                        {
                            var zF = t.GetField("Z", flags);
                            if (zF != null)
                            {
                                int z = Convert.ToInt32(zF.GetValue(val));
                                if (z > 0) return new[] { x, y, z };
                            }
                            return new[] { x, y };
                        }
                    }
                }
                catch { }
            }

            return Array.Empty<int>();
        }

        private class WebGLStream : AcceleratorStream
        {
            public WebGLStream(Accelerator acc) : base(acc) { }
            protected override void DisposeAcceleratorObject(bool disposing) { }
            public override void Synchronize() { }
            public override Task SynchronizeAsync() =>
                WebGLAcceleratorExtensions.SynchronizeAsync((WebGLAccelerator)Accelerator);
            protected override global::ILGPU.Runtime.ProfilingMarker AddProfilingMarkerInternal() => throw new NotSupportedException();
        }


        /// <summary>
        /// Recursively flattens a struct value into leaf fields with sequential field_N naming.
        /// The GLSL type generator flattens nested structs into a single-level struct:
        /// NestedOuterStruct { NestedInnerStruct { A, B }, Value } becomes
        /// struct_X { int field_0; int field_1; float field_2; }
        /// So uniform paths must be: field_0, field_1, field_2 (NOT field_0.field_0 etc.)
        /// </summary>
        private static void FlattenStructFieldsForUniform(object structValue, string prefix, List<object> results)
        {
            // Ignore the prefix parameter — we use a sequential counter instead
            int fieldCounter = 0;
            FlattenStructFieldsForUniformRecursive(structValue, results, ref fieldCounter);
        }

        /// <summary>
        /// Recursively extracts leaf primitive fields from a struct, assigning sequential field_N names.
        /// </summary>
        private static void FlattenStructFieldsForUniformRecursive(object structValue, List<object> results, ref int fieldCounter)
        {
            var fields = structValue.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in fields)
            {
                var fieldVal = field.GetValue(structValue);
                if (fieldVal != null && fieldVal.GetType().IsValueType && !fieldVal.GetType().IsPrimitive && !fieldVal.GetType().IsEnum)
                {
                    // Nested struct — recurse (flatten into same level)
                    FlattenStructFieldsForUniformRecursive(fieldVal, results, ref fieldCounter);
                }
                else
                {
                    // Leaf primitive field
                    string path = $"field_{fieldCounter}";
                    string scalarType = fieldVal switch
                    {
                        int => "int",
                        uint => "uint",
                        float => "float",
                        double => "float",
                        bool => "bool",
                        byte or sbyte or short or ushort or long => "int",
                        ulong => "uint",
                        _ => "float"
                    };
                    object value = fieldVal switch
                    {
                        int iVal => iVal,
                        uint uiVal => uiVal,
                        float fVal => fVal,
                        double dVal => (float)dVal,
                        bool bVal => bVal ? 1 : 0,
                        byte bv => (int)bv,
                        sbyte sbv => (int)sbv,
                        short sv => (int)sv,
                        ushort usv => (int)usv,
                        long lv => (int)lv,
                        ulong ulv => (uint)ulv,
                        _ => fieldVal ?? 0
                    };
                    results.Add(new { path, scalarType, value });
                    fieldCounter++;
                }
            }
        }

        /// <summary>
        /// Decomposes a body-struct kernel argument into per-field buffer_ref dispatch
        /// entries. For each ArrayView field of the user struct, emits a buffer_ref
        /// with synthetic paramIndex = (glslParamIndex + 1) * 1000 + fieldIdx so the
        /// GLSL kernel's per-field samplers (u_param{N}_f{M}) can be located by
        /// glWorker.js. Scalar (primitive) fields are emitted as standard scalar
        /// uniforms with the same synthetic param index encoding.
        /// </summary>
        private static void EmitBodyStructDispatch(
            object structArg,
            int glslParamIndex,
            WebGLCompiledKernel compiledKernel,
            WebGLAccelerator webGlAccel,
            List<object> jsParams)
        {
            var manifestEntries = compiledKernel.BodyStructManifest
                .Where(e => e.ParamIndex == glslParamIndex)
                .OrderBy(e => e.FieldIndex)
                .ToList();
            var clrFields = structArg.GetType().GetFields(
                BindingFlags.Public | BindingFlags.Instance);

            foreach (var entry in manifestEntries)
            {
                if (entry.ClrFieldIndex < 0 || entry.ClrFieldIndex >= clrFields.Length)
                    throw new Exception(
                        $"Body struct on param {glslParamIndex} has manifest C# field index " +
                        $"{entry.ClrFieldIndex} (IR field index {entry.FieldIndex}) but the C# struct " +
                        $"has {clrFields.Length} fields.");
                var clrField = clrFields[entry.ClrFieldIndex];
                var fieldValue = clrField.GetValue(structArg);

                if (entry.IsView)
                {
                    var view = fieldValue as IArrayView;
                    if (view == null)
                    {
                        var rc = GetOrCreateReflectionCache(clrField.FieldType);
                        if (rc.BaseViewProperty != null && fieldValue != null)
                            view = rc.BaseViewProperty.GetValue(fieldValue) as IArrayView;
                    }
                    if (view == null)
                        throw new Exception(
                            $"Body struct field '{clrField.Name}' on param {glslParamIndex} " +
                            $"is declared as a view but the runtime value is not an IArrayView.");

                    var contiguous = view as IContiguousArrayView;
                    if (contiguous == null)
                    {
                        var rc2 = GetOrCreateReflectionCache(view.GetType());
                        contiguous = (rc2.BaseViewProperty != null
                            ? rc2.BaseViewProperty.GetValue(view)
                            : view) as IContiguousArrayView;
                    }
                    if (contiguous == null)
                        throw new Exception(
                            $"Body struct field '{clrField.Name}' on param {glslParamIndex} " +
                            $"is not a contiguous buffer.");

                    var memBuffer = contiguous.Buffer as WebGLMemoryBuffer;
                    if (memBuffer == null)
                        throw new Exception(
                            $"Body struct field '{clrField.Name}' has no WebGL memory buffer.");

                    int length = (int)contiguous.Length;
                    int elementOffset = (int)contiguous.Index;

                    webGlAccel.EnsureBufferInWorker(memBuffer, entry.GlslElementType);

                    jsParams.Add(new
                    {
                        kind = "buffer_ref",
                        bufferId = memBuffer.WorkerBufferId,
                        paramIndex = entry.SyntheticParamIndex,
                        elementCount = length,
                        elementOffset
                    });
                }
                else if (entry.IsScalar)
                {
                    // Scalar field of body struct — emit as standard scalar uniform
                    // with the synthetic param index. glWorker.js will resolve the
                    // u_param{realN}_f{M} uniform name from the synthetic encoding.
                    string scalarType = fieldValue switch
                    {
                        int => "int",
                        uint => "uint",
                        float => "float",
                        double => "float",
                        bool => "bool",
                        byte or sbyte or short or ushort or long => "int",
                        ulong => "uint",
                        _ => "float"
                    };
                    object scalarValue = fieldValue switch
                    {
                        int iv => iv,
                        uint uv => uv,
                        float fv => fv,
                        double dv => (float)dv,
                        bool bv => bv ? 1 : 0,
                        byte bv => (int)bv,
                        sbyte sbv => (int)sbv,
                        short sv => (int)sv,
                        ushort usv => (int)usv,
                        long lv => (int)lv,
                        ulong ulv => (uint)ulv,
                        _ => fieldValue ?? 0
                    };
                    jsParams.Add(new
                    {
                        kind = "scalar",
                        paramIndex = entry.SyntheticParamIndex,
                        scalarType,
                        value = scalarValue
                    });
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// Represents a compiled WebGL2 kernel ready for execution.
    /// </summary>
    public class WebGLKernel : Kernel
    {
        public WebGLKernel(Accelerator accelerator, CompiledKernel compiledKernel, MethodInfo launcher)
            : base(accelerator, compiledKernel, launcher) { }

        /// <summary>
        /// Gets the WebGL-specific compiled kernel.
        /// </summary>
        public new WebGLCompiledKernel CompiledKernel => (WebGLCompiledKernel)base.CompiledKernel;

        /// <inheritdoc/>
        protected override void DisposeAcceleratorObject(bool disposing) { }
    }
}
