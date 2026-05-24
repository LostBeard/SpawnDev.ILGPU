using global::ILGPU;
using global::ILGPU.Backends;
using global::ILGPU.Runtime;
using SpawnDev.BlazorJS.JSObjects;
using SpawnDev.ILGPU.WebGPU.Backend;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Array = System.Array;

namespace SpawnDev.ILGPU.WebGPU
{
    /// <summary>
    /// WebGPU accelerator implementation for ILGPU.
    /// Provides kernel compilation and execution capabilities using WebGPU compute shaders.
    /// </summary>
    public class WebGPUAccelerator : KernelAccelerator<WebGPUCompiledKernel, WebGPUKernel>
    {
        #region Pre-compiled Regex Patterns

        // @workgroup_size(x[,y[,z]]) extraction
        private static readonly Regex s_workgroupSizePattern =
            new(@"@workgroup_size\((\d+)(?:\s*,\s*(\d+))?(?:\s*,\s*(\d+))?\)",
                RegexOptions.Compiled);

        // const workgroup_size declaration patching
        private static readonly Regex s_constWorkgroupSizePattern =
            new(@"const workgroup_size : vec3<u32> = vec3<u32>\(\d+u, \d+u, \d+u\);",
                RegexOptions.Compiled);

        // @workgroup_size(x) — first dimension only (for dispatch log)
        private static readonly Regex s_workgroupSizeSimplePattern =
            new(@"@workgroup_size\((\d+)",
                RegexOptions.Compiled);

        #endregion

        /// <summary>
        /// Gets the native WebGPU accelerator for low-level GPU access.
        /// </summary>
        public WebGPUNativeAccelerator NativeAccelerator { get; private set; } = null!;

        /// <summary>
        /// Gets the WebGPU backend used for kernel compilation.
        /// </summary>
        public WebGPUBackend Backend { get; private set; } = null!;

        /// <summary>
        /// Effective F64 emulation mode for kernels compiled on this accelerator. Reads
        /// from / writes to the underlying <see cref="WebGPUBackend.F64Mode"/>. Use this
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
        /// Gets the set of WebGPU features enabled on this device.
        /// </summary>
        public HashSet<string> EnabledFeatures => NativeAccelerator?.EnabledFeatures ?? new HashSet<string>();

        /// <inheritdoc/>
        public override int MaxStorageBufferBindings =>
            NativeAccelerator?.MaxStorageBuffersPerShaderStage ?? 10;

        /// <summary>
        /// WebGPU requires storage buffer binding offsets to be aligned to this value.
        /// Spec default: 256 bytes. Query from device.limits.minStorageBufferOffsetAlignment.
        /// </summary>
        private const ulong MinStorageBufferOffsetAlignment = 256UL;

        /// <summary>
        /// True if the underlying GPU device has been lost (driver crash, GPU reset, etc.).
        /// </summary>
        public bool IsDeviceLost => NativeAccelerator?.IsDeviceLost ?? false;

        /// <summary>
        /// Fired when the GPU device is lost. Parameters are (reason, message).
        /// </summary>
        public event Action<string, string>? DeviceLost;

        /// <summary>
        /// Method info for the static RunKernel method used by kernel launchers.
        /// </summary>
        public static readonly MethodInfo RunKernelMethod = typeof(WebGPUAccelerator).GetMethod(
            nameof(RunKernel),
            BindingFlags.Public | BindingFlags.Static)!;

        #region Caching Infrastructure

        // Reflection cache: caches PropertyInfo/FieldInfo for dimension extraction per type
        private static readonly ConcurrentDictionary<Type, ReflectionMetadataCache> _reflectionCache = new();

        // Buffer pool for scalar arguments (per-device pools would require instance field)
        [ThreadStatic]
        private static List<GPUBuffer>? _scalarBufferPool;

        // Reusable lists to avoid per-dispatch allocations
        [ThreadStatic]
        private static List<GPUBuffer>? _reusableScalarReturnList;
        [ThreadStatic]
        private static List<GPUBindGroupEntry>? _reusableEntryList;
        [ThreadStatic]
        private static List<object?>? _reusableExpandedArgs;
        [ThreadStatic]
        private static Dictionary<int, List<int>>? _reusableBodyStructMap;
        [ThreadStatic]
        private static HashSet<int>? _reusableBodyStructScalarSet;
        [ThreadStatic]
        private static Dictionary<int, ScalarPackingEntry>? _reusablePackedScalarLookup;

        #endregion

        #region Dispatch Log

        /// <summary>
        /// Record of a single kernel dispatch for post-mortem debugging.
        /// </summary>
        public record DispatchRecord(
            string KernelName,
            int WorkgroupSize,
            uint GridDimX, uint GridDimY, uint GridDimZ,
            int BindingCount,
            bool WorkgroupSizePatched,
            DateTime Timestamp);

        private static readonly Queue<DispatchRecord> _dispatchLog = new();

        /// <summary>
        /// Ring buffer of recent kernel dispatches. Inspect after test failures
        /// to see exact parameters for each launch.
        /// </summary>
        public static IReadOnlyCollection<DispatchRecord> DispatchLog => _dispatchLog;

        /// <summary>
        /// Maximum number of dispatch records to retain. Default 100.
        /// </summary>
        public static int MaxDispatchLogSize { get; set; } = 100;

        #endregion

        // Cache for CopyStructToBytes<T> MethodInfo per type (avoids repeated MakeGenericMethod)
        private static readonly ConcurrentDictionary<Type, MethodInfo> _copyStructMethodCache = new();

        /// <summary>
        /// Copies the raw bytes of a value-type struct into the destination byte array.
        /// Uses MemoryMarshal.AsBytes to reinterpret the struct as bytes — no GCHandle pinning required.
        /// This works for all value types including closed generic structs.
        /// </summary>
        private static void CopyStructToBytes<T>(T value, byte[] dest) where T : struct
        {
            var span = System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref value, 1));
            span.CopyTo(dest);
        }

        private static MethodInfo GetCopyStructMethod(Type type)
        {
            return _copyStructMethodCache.GetOrAdd(type, t =>
                typeof(WebGPUAccelerator)
                    .GetMethod(nameof(CopyStructToBytes), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(t));
        }

        /// <summary>
        /// Checks if a value-type struct contains any pointer-like fields (IntPtr, UIntPtr, or pointer types)
        /// at any nesting level. Such structs cannot be safely copied with MemoryMarshal.AsBytes in Blazor WASM
        /// and must be decomposed into their constituent fields.
        /// This catches IGridStrideKernelBody implementations like InitializerImplementation and
        /// ReductionImplementation which contain ArrayView fields (which internally hold an IntPtr).
        /// </summary>
        private static readonly ConcurrentDictionary<Type, bool> _containsPointerCache = new();
        private static bool ContainsPointerFields(Type type)
        {
            if (!type.IsValueType || type.IsPrimitive) return false;
            if (type == typeof(IntPtr) || type == typeof(UIntPtr)) return true;
            return _containsPointerCache.GetOrAdd(type, t =>
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var field in t.GetFields(flags))
                {
                    var ft = field.FieldType;
                    // MemoryMarshal.AsBytes fails for structs containing:
                    // 1. Raw pointer types (T*)
                    // 2. IntPtr / UIntPtr
                    // 3. Reference types (class instances, e.g. MemoryBuffer inside ArrayView<T>)
                    if (ft.IsPointer || ft == typeof(IntPtr) || ft == typeof(UIntPtr)) return true;
                    if (!ft.IsValueType) return true; // reference type field (class)
                    if (!ft.IsPrimitive && ContainsPointerFields(ft)) return true;
                }
                return false;
            });
        }

        /// <summary>
        /// Flattens a body struct into its constituent field values (in declaration order).
        /// This mirrors what the ILGPU compiler does when it inlines struct parameters into
        /// separate IR parameters.
        /// </summary>
        private static List<object?> FlattenStructFields(object structValue)
        {
            var result = new List<object?>();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = structValue.GetType();
            foreach (var field in type.GetFields(flags))
            {
                var fieldVal = field.GetValue(structValue);
                var ft = field.FieldType;
                // Recursively flatten nested structs that also contain pointers
                // but stop at IArrayView — those are leaf nodes handled by the view binding path
                if (fieldVal != null && ft.IsValueType && !ft.IsPrimitive
                    && !typeof(IArrayView).IsAssignableFrom(ft)
                    && ContainsPointerFields(ft))
                {
                    result.AddRange(FlattenStructFields(fieldVal));
                }
                else
                {
                    result.Add(fieldVal);
                }
            }
            return result;
        }

        private class ReflectionMetadataCache
        {
            public PropertyInfo? BaseViewProperty { get; set; }
            public PropertyInfo? IntLengthProperty { get; set; }
            public PropertyInfo? LengthProperty { get; set; }
            public PropertyInfo? WidthProperty { get; set; }
            public FieldInfo? XField { get; set; }
            public FieldInfo? YField { get; set; }
            public FieldInfo? ZField { get; set; }
            public PropertyInfo? XProperty { get; set; }
            public PropertyInfo? YProperty { get; set; }
            public PropertyInfo? ZProperty { get; set; }
        }

        private static ReflectionMetadataCache GetOrCreateReflectionCache(Type type)
        {
            if (!WebGPUBackend.EnableReflectionCaching)
                return BuildReflectionCache(type);

            return _reflectionCache.GetOrAdd(type, BuildReflectionCache);
        }

        private static ReflectionMetadataCache BuildReflectionCache(Type type)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            return new ReflectionMetadataCache
            {
                BaseViewProperty = type.GetProperty("BaseView", flags),
                IntLengthProperty = type.GetProperty("IntLength", flags),
                LengthProperty = type.GetProperty("Length", flags),
                WidthProperty = type.GetProperty("Width", flags),
                XField = type.GetField("X", flags),
                YField = type.GetField("Y", flags),
                ZField = type.GetField("Z", flags),
                XProperty = type.GetProperty("X", flags),
                YProperty = type.GetProperty("Y", flags),
                ZProperty = type.GetProperty("Z", flags)
            };
        }

        private static GPUBuffer GetPooledScalarBuffer(GPUDevice device)
        {
            if (!WebGPUBackend.EnableBufferPooling)
                return CreateScalarBuffer(device);

            _scalarBufferPool ??= new List<GPUBuffer>();

            // Try to find a reusable buffer
            if (_scalarBufferPool.Count > 0)
            {
                var buffer = _scalarBufferPool[_scalarBufferPool.Count - 1];
                _scalarBufferPool.RemoveAt(_scalarBufferPool.Count - 1);
                return buffer;
            }

            return CreateScalarBuffer(device);
        }

        private static void ReturnPooledScalarBuffer(GPUBuffer buffer)
        {
            if (!WebGPUBackend.EnableBufferPooling)
            {
                buffer.Destroy();
                buffer.Dispose();
                return;
            }

            _scalarBufferPool ??= new List<GPUBuffer>();
            // Limit pool size to prevent memory bloat (increased for batching headroom)
            if (_scalarBufferPool.Count < 64)
            {
                _scalarBufferPool.Add(buffer);
            }
            else
            {
                buffer.Destroy();
                buffer.Dispose();
            }
        }

        private static GPUBuffer CreateScalarBuffer(GPUDevice device)
        {
            return device.CreateBuffer(new GPUBufferDescriptor
            {
                Label = "PooledScalar",
                Size = 256,
                Usage = GPUBufferUsage.Storage | GPUBufferUsage.CopyDst,
                MappedAtCreation = false
            });
        }

        private WebGPUAccelerator(Context context, Device device) : base(context, device) { }

        /// <summary>
        /// Creates a new WebGPU accelerator asynchronously with default options.
        /// </summary>
        /// <param name="context">The ILGPU context.</param>
        /// <param name="device">The WebGPU device to use.</param>
        /// <returns>A task that represents the async creation of the accelerator.</returns>
        public static Task<WebGPUAccelerator> CreateAsync(Context context, WebGPUILGPUDevice device)
            => CreateAsync(context, device, null);

        /// <summary>
        /// Creates a new WebGPU accelerator asynchronously with the specified options.
        /// </summary>
        /// <param name="context">The ILGPU context.</param>
        /// <param name="device">The WebGPU device to use.</param>
        /// <param name="options">The backend configuration options (null for defaults).</param>
        /// <returns>A task that represents the async creation of the accelerator.</returns>
        public static async Task<WebGPUAccelerator> CreateAsync(Context context, WebGPUILGPUDevice device, WebGPUBackendOptions? options)
        {
            var accelerator = new WebGPUAccelerator(context, device);
            accelerator.NativeAccelerator = await device.NativeDevice.CreateAcceleratorAsync();
            accelerator.NativeAccelerator.DeviceLost += (reason, msg) => accelerator.DeviceLost?.Invoke(reason, msg);
            accelerator.Backend = new WebGPUBackend(context, options ?? WebGPUBackendOptions.Default, accelerator.NativeAccelerator.EnabledFeatures);
            accelerator.Backend.DefaultMaxWorkgroupSize = accelerator.MaxNumThreadsPerGroup;
            if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WebGPU-Init] DefaultMaxWorkgroupSize set to {accelerator.Backend.DefaultMaxWorkgroupSize} (MaxNumThreadsPerGroup={accelerator.MaxNumThreadsPerGroup})");
            accelerator.Init(accelerator.Backend);
            accelerator.DefaultStream = accelerator.CreateStreamInternal();
            // Wire flush callback so WebGPUBuffer readback operations auto-flush pending dispatches
            accelerator.NativeAccelerator.FlushPendingCommands = () => accelerator.FlushPendingCommands();

            // Update device capabilities from actual enabled features (Float16, Float64, Int64)
            if (device.Capabilities is WebGPUCapabilityContext webCaps)
                webCaps.UpdateFromEnabledFeatures(accelerator.NativeAccelerator.EnabledFeatures);

            // Always log detected features (important for diagnostics)
            var features = accelerator.NativeAccelerator.EnabledFeatures;
            if (features.Count > 0)
            {
                if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WebGPU] Enabled features ({features.Count}): {string.Join(", ", features)}");
            }
            else
                if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log("[WebGPU] No optional features detected");

            return accelerator;
        }

        /// <summary>
        /// Creates a new WebGPU accelerator from an externally-provided GPUDevice.
        /// This is used when sharing a device with another library (e.g., ONNX Runtime Web).
        /// The external device is used directly — no adapter probing or device creation.
        /// </summary>
        /// <param name="context">The ILGPU context.</param>
        /// <param name="externalDevice">An existing GPUDevice (e.g., from ort.env.webgpu.device).</param>
        /// <param name="options">The backend configuration options (null for defaults).</param>
        /// <returns>A new WebGPU accelerator using the external device.</returns>
        public static WebGPUAccelerator CreateFromExternalDevice(Context context, GPUDevice externalDevice, WebGPUBackendOptions? options = null)
        {
            var ilgpuDevice = new WebGPUILGPUDevice(externalDevice);
            var accelerator = new WebGPUAccelerator(context, ilgpuDevice);
            accelerator.NativeAccelerator = WebGPUNativeAccelerator.CreateFromExternalDevice(externalDevice);
            accelerator.NativeAccelerator.DeviceLost += (reason, msg) => accelerator.DeviceLost?.Invoke(reason, msg);
            accelerator.Backend = new WebGPUBackend(context, options ?? WebGPUBackendOptions.Default, accelerator.NativeAccelerator.EnabledFeatures);
            accelerator.Backend.DefaultMaxWorkgroupSize = accelerator.MaxNumThreadsPerGroup;
            accelerator.Init(accelerator.Backend);
            accelerator.DefaultStream = accelerator.CreateStreamInternal();
            accelerator.NativeAccelerator.FlushPendingCommands = () => accelerator.FlushPendingCommands();

            // Update device capabilities from actual enabled features (Float16, Float64, Int64)
            if (ilgpuDevice.Capabilities is WebGPUCapabilityContext webCaps)
                webCaps.UpdateFromEnabledFeatures(accelerator.NativeAccelerator.EnabledFeatures);

            var features = accelerator.NativeAccelerator.EnabledFeatures;
            if (features.Count > 0)
            {
                if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WebGPU] Enabled features ({features.Count}): {string.Join(", ", features)}");
            }
            else
                if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log("[WebGPU] No optional features detected");

            if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log("[WebGPU] Accelerator created from external GPUDevice");
            return accelerator;
        }

        /// <inheritdoc/>
        protected override WebGPUKernel CreateKernel(WebGPUCompiledKernel compiledKernel)
        {
            return new WebGPUKernel(this, compiledKernel, null);
        }

        /// <inheritdoc/>
        protected override WebGPUKernel CreateKernel(WebGPUCompiledKernel compiledKernel, MethodInfo launcher)
        {
            return new WebGPUKernel(this, compiledKernel, launcher);
        }

        /// <inheritdoc/>
        protected override MethodInfo GenerateKernelLauncherMethod(WebGPUCompiledKernel kernel, int customGroupSize)
        {
            var parameters = kernel.EntryPoint.Parameters;
            var indexType = kernel.EntryPoint.KernelIndexType;
            var argTypes = new List<Type> { typeof(Kernel), typeof(AcceleratorStream), indexType };
            for (int i = 0; i < parameters.Count; i++) argTypes.Add(parameters[i]);

            var dynamicMethod = new DynamicMethod("WebGPULauncher", typeof(void), argTypes.ToArray(), typeof(WebGPUAccelerator).Module);
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

        // Cache for dimension extraction: per-type function that extracts int[] dimensions from a view
        private static readonly ConcurrentDictionary<Type, Func<object, int[]>> _dimensionExtractorCache = new();

        // Helper to robustly extract dimensions (X, Y, Z) using Duck Typing, with caching
        private static int[] ExtractDimensionsFromView(object view, Type viewType)
        {
            var extractor = _dimensionExtractorCache.GetOrAdd(viewType, BuildDimensionExtractor);
            return extractor(view);
        }

        private static Func<object, int[]> BuildDimensionExtractor(Type viewType)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // Strategy 1: Find a sub-struct field with X/Y/Z members (e.g., Extent, Index)
            foreach (var field in viewType.GetFields(flags))
            {
                if (field.FieldType.IsPrimitive || field.FieldType.IsPointer) continue;
                var xyzAccessor = BuildXYZAccessor(field.FieldType);
                if (xyzAccessor != null)
                {
                    var capturedField = field;
                    var capturedAccessor = xyzAccessor;
                    return (obj) =>
                    {
                        try
                        {
                            var val = capturedField.GetValue(obj);
                            if (val != null)
                            {
                                var res = capturedAccessor(val);
                                if (res.Length > 0 && res[0] > 0) return res;
                            }
                        }
                        catch { }
                        return Array.Empty<int>();
                    };
                }
            }

            // Strategy 2: IntLength property (1D ArrayView)
            var pIntLength = viewType.GetProperty("IntLength", flags);
            if (pIntLength != null)
                return (obj) => { try { return new int[] { (int)pIntLength.GetValue(obj)! }; } catch { return Array.Empty<int>(); } };

            // Strategy 3: Length property
            var pLength = viewType.GetProperty("Length", flags);
            if (pLength != null && (pLength.PropertyType == typeof(int) || pLength.PropertyType == typeof(long)))
                return (obj) => { try { return new int[] { Convert.ToInt32(pLength.GetValue(obj)) }; } catch { return Array.Empty<int>(); } };

            // Strategy 4: Properties with X/Y/Z sub-structs
            foreach (var prop in viewType.GetProperties(flags))
            {
                if (prop.GetIndexParameters().Length > 0) continue;
                if (prop.PropertyType.IsPrimitive || prop.PropertyType.IsPointer) continue;
                var xyzAccessor = BuildXYZAccessor(prop.PropertyType);
                if (xyzAccessor != null)
                {
                    var capturedProp = prop;
                    var capturedAccessor = xyzAccessor;
                    return (obj) =>
                    {
                        try
                        {
                            var val = capturedProp.GetValue(obj);
                            if (val != null)
                            {
                                var res = capturedAccessor(val);
                                if (res.Length > 0 && res[0] > 0) return res;
                            }
                        }
                        catch { }
                        return Array.Empty<int>();
                    };
                }
            }

            // Strategy 5: Direct Width property
            var directWidth = viewType.GetProperty("Width", flags);
            if (directWidth != null)
                return (obj) => { try { int x = Convert.ToInt32(directWidth.GetValue(obj)); return x > 0 ? new int[] { x, 0 } : Array.Empty<int>(); } catch { return Array.Empty<int>(); } };

            // No dimension info found
            return (_) => Array.Empty<int>();
        }

        // Build a function that extracts X/Y/Z from a sub-struct type. Returns null if type has no X/Y/Z.
        private static Func<object, int[]>? BuildXYZAccessor(Type type)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // Resolve X accessor
            var fX = type.GetField("X", flags);
            var pX = fX == null ? type.GetProperty("X", flags) : null;
            if (fX == null && pX == null) return null;

            // Resolve Y accessor
            var fY = type.GetField("Y", flags);
            var pY = fY == null ? type.GetProperty("Y", flags) : null;

            // Resolve Z accessor
            var fZ = type.GetField("Z", flags);
            var pZ = fZ == null ? type.GetProperty("Z", flags) : null;

            return (obj) =>
            {
                int x = fX != null ? Convert.ToInt32(fX.GetValue(obj)) : (pX != null ? Convert.ToInt32(pX.GetValue(obj)) : -1);
                if (x < 0) return Array.Empty<int>();

                int y = fY != null ? Convert.ToInt32(fY.GetValue(obj)) : (pY != null ? Convert.ToInt32(pY.GetValue(obj)) : -1);
                if (y < 0) return new int[] { x };

                int z = fZ != null ? Convert.ToInt32(fZ.GetValue(obj)) : (pZ != null ? Convert.ToInt32(pZ.GetValue(obj)) : -1);
                return z >= 0 ? new int[] { x, y, z } : new int[] { x, y };
            };
        }

        /// <summary>
        /// Executes a WebGPU kernel with the specified parameters.
        /// </summary>
        /// <param name="kernel">The kernel to execute.</param>
        /// <param name="stream">The accelerator stream (not used in WebGPU).</param>
        /// <param name="dimension">The launch dimensions.</param>
        /// <param name="args">The kernel arguments.</param>
        public static void RunKernel(Kernel kernel, AcceleratorStream stream, object dimension, object[] args)
        {
            var webGpuAccel = (WebGPUAccelerator)kernel.Accelerator;
            var nativeAccel = webGpuAccel.NativeAccelerator;

            if (webGpuAccel.IsDeviceLost)
                throw new InvalidOperationException("WebGPU device has been lost — cannot dispatch kernel.");

            var webGpuKernel = (WebGPUKernel)kernel;
            var compiledKernel = webGpuKernel.CompiledKernel;

            if ((WebGPUBackend.DiagnosticFlags & WGSLDiagnostics.Dispatch) != 0)
            {
                WebGPUBackend.Log($"\n[WebGPU] ---- WGSL for dispatch ----");
                WebGPUBackend.Log(compiledKernel.WGSLSource);
                WebGPUBackend.Log("[WebGPU] ----------------------------\n");
            }

            // Build override constants for dynamic shared memory
            Dictionary<string, object>? overrideConstants = null;
            if (compiledKernel.HasDynamicSharedMemory && dimension is KernelConfig kernelConfig && kernelConfig.UsesDynamicSharedMemory)
            {
                overrideConstants = new Dictionary<string, object>();
                var sharedMemConfig = kernelConfig.SharedMemoryConfig;
                foreach (var overrideInfo in compiledKernel.DynamicSharedOverrides)
                {
                    // SharedMemoryConfig provides total bytes = NumElements * ElementSize
                    // The WGSL override sizes the array in elements of the declared type,
                    // so we need: total bytes / element size of the WGSL type
                    int numElements = sharedMemConfig.NumElements;
                    if (sharedMemConfig.ElementSize != overrideInfo.ElementSize && overrideInfo.ElementSize > 0)
                    {
                        // The element types may differ between C# request and WGSL declaration.
                        // Convert from bytes to elements of the WGSL type.
                        numElements = (sharedMemConfig.NumElements * sharedMemConfig.ElementSize) / overrideInfo.ElementSize;
                    }
                    overrideConstants[overrideInfo.ConstantName] = (double)numElements;
                    if (WebGPUBackend.VerboseLogging)
                        WebGPUBackend.Log($"[WebGPU-Debug] Dynamic shared memory override: {overrideInfo.ConstantName} = {numElements}");
                }
            }

            // For auto-grouped (implicitly grouped) kernels, pass the user dimension as an
            // override constant so the WGSL range check can prevent excess threads from
            // executing. Without this, WGSL's clamped array indexing causes OOB writes to
            // overwrite the last valid element.
            if (!compiledKernel.EntryPoint.IsExplicitlyGrouped)
            {
                uint userDim = 0;
                if (dimension is Index1D i1d) userDim = (uint)i1d.X;
                else if (dimension is Index2D i2d) userDim = (uint)(i2d.X * i2d.Y);
                else if (dimension is Index3D i3d) userDim = (uint)(i3d.X * i3d.Y * i3d.Z);
                else if (dimension is LongIndex1D l1d) userDim = (uint)l1d.X;
                else if (dimension is LongIndex2D l2d) userDim = (uint)(l2d.X * l2d.Y);
                else if (dimension is LongIndex3D l3d) userDim = (uint)(l3d.X * l3d.Y * l3d.Z);

                if (userDim > 0)
                {
                    overrideConstants ??= new Dictionary<string, object>();
                    overrideConstants["_ilgpu_user_dim"] = (double)userDim;
                }
            }

            // For explicitly grouped kernels (KernelConfig), the dispatch's GroupDim
            // specifies the number of threads per workgroup. WebGPU bakes this into the
            // shader as @workgroup_size at compile time, but the ILGPU algorithm kernels
            // (RadixSort, Scan, etc.) choose GroupDim at runtime based on device limits.
            // If the compiled @workgroup_size doesn't match the dispatch's GroupDim, patch
            // the WGSL source so @workgroup_size and the const workgroup_size variable agree
            // with the actual dispatch dimensions. The shader cache handles deduplication.
            string wgslSource = compiledKernel.WGSLSource;
            bool wasPatched = false;
            if (dimension is KernelConfig kcWg)
            {
                int reqX = kcWg.GroupDim.X;
                int reqY = kcWg.GroupDim.Y;
                int reqZ = kcWg.GroupDim.Z;

                var wgMatch = s_workgroupSizePattern.Match(wgslSource);
                if (wgMatch.Success)
                {
                    int compiledX = int.Parse(wgMatch.Groups[1].Value);
                    int compiledY = wgMatch.Groups[2].Success ? int.Parse(wgMatch.Groups[2].Value) : 1;
                    int compiledZ = wgMatch.Groups[3].Success ? int.Parse(wgMatch.Groups[3].Value) : 1;

                    if (compiledX != reqX || compiledY != reqY || compiledZ != reqZ)
                    {
                        wasPatched = true;
                        // Always log workgroup_size patching as a warning — this indicates
                        // the kernel was loaded without KernelSpecialization matching the
                        // dispatch's GroupDim. Adding KernelSpecialization to the LoadKernel
                        // call eliminates the need for runtime WGSL patching.
                        if (WebGPUBackend.VerboseLogging)
                            WebGPUBackend.Log(
                                $"[WebGPU] WARNING: Runtime @workgroup_size patching: " +
                                $"compiled=({compiledX},{compiledY},{compiledZ}), " +
                                $"dispatch=({reqX},{reqY},{reqZ}). " +
                                $"Kernel: {compiledKernel.EntryPoint?.Name ?? "unknown"}. " +
                                $"Consider adding KernelSpecialization to LoadKernel.");

                        wgslSource = wgslSource.Replace(
                            wgMatch.Value,
                            $"@workgroup_size({reqX}, {reqY}, {reqZ})");

                        var constMatch = s_constWorkgroupSizePattern.Match(wgslSource);
                        if (constMatch.Success)
                        {
                            wgslSource = wgslSource.Replace(
                                constMatch.Value,
                                $"const workgroup_size : vec3<u32> = vec3<u32>({reqX}u, {reqY}u, {reqZ}u);");
                        }
                    }
                }
            }

            var shader = nativeAccel.GetOrCreateComputeShader(wgslSource, "main", overrideConstants);
            var device = nativeAccel.NativeDevice!;

            // Track scalar buffers for pool return (reuse list to avoid per-frame allocation)
            _reusableScalarReturnList ??= new List<GPUBuffer>();
            _reusableScalarReturnList.Clear();
            var scalarBuffersToReturn = _reusableScalarReturnList;

            // Coalesced storage buffers allocated this dispatch (one per CoalesceGroup).
            // Declared at this scope so the catch + finally blocks can clean up on error.
            List<GPUBuffer>? coalescedBuffersToDestroyAfterDispatch = null;

            try
            {
                int currentBindingIndex = 0;
                _reusableEntryList ??= new List<GPUBindGroupEntry>();
                _reusableEntryList.Clear();
                var entries = _reusableEntryList;

                // Track element offsets per binding index for view offset packing.
                // When binding at offset=0 for sub-views, the element offset needs
                // to be packed into the scalar buffer so the WGSL can read it.
                var viewElementOffsets = new Dictionary<int, int>();
                // For packed-struct views, the element COUNT is also sent to the GPU since
                // arrayLength() returns CPU-allocation-size/4, not the logical element count.
                var viewElementCounts = new Dictionary<int, int>();

                // Build a lookup of packed scalar params by their args-array index.
                // ScalarPackingManifest stores IR param.Index (0-based, includes implicit index param).
                // The WGSL generator (KernelParamOffset) always skips param 0 if it's an Index type,
                // regardless of KernelConfig grouping. We must replicate this logic here.
                //
                // For auto-grouped kernels: KernelIndexParameterOffset=1, args excludes Index
                // For stream kernels with Index: KernelIndexParameterOffset=0, args INCLUDES Index
                // But the WGSL generator skips Index param in both cases (KernelParamOffset=1).
                //
                // So we need a "runtime skip" count to align args[] with WGSL bindings.
                int kernelParamOffset = compiledKernel.EntryPoint.KernelIndexParameterOffset;
                var paramTypes = compiledKernel.EntryPoint.Parameters;

                // Detect if args[0] is a short Index type that should be skipped for bindings.
                // This happens with explicitly grouped stream kernels where KernelIndexParameterOffset=0
                // but the WGSL generator still skips the Index param (mapped to global_id.x).
                // Note: LongIndex types are NOT skipped — they are data counts for grid-stride kernels.
                int runtimeIndexSkip = 0;
                if (kernelParamOffset == 0 && paramTypes.Count > 0)
                {
                    var first = paramTypes[0];
                    if (first == typeof(Index1D) || first == typeof(Index2D) || first == typeof(Index3D))
                        runtimeIndexSkip = 1;
                }

                // The effective offset for mapping IR param indices to args[] indices:
                // effectiveOffset = kernelParamOffset + runtimeIndexSkip
                // For auto-grouped: effectiveOffset = 1 + 0 = 1
                // For stream with Index: effectiveOffset = 0 + 1 = 1
                // For stream without Index: effectiveOffset = 0 + 0 = 0
                int effectiveOffset = kernelParamOffset + runtimeIndexSkip;

                // --- PRE-EXPAND: Flatten body structs in args[] ---
                // IGridStrideKernelBody implementations (e.g. InitializerImplementation, ReductionImplementation)
                // contain IArrayView fields. The ILGPU compiler inlines these structs into separate IR
                // parameters, so the WGSL generator creates one binding per field. We must match this
                // at runtime by expanding the args array to replace each body struct with its fields.
                //
                // We also build argsToEffectiveOffset: for each original args[i], the starting index
                // in effectiveArgs where args[i]'s contribution begins. This lets Phase 2 (scalar packing)
                // correctly look up scalar values from effectiveArgs using IR param indices.
                _reusableExpandedArgs ??= new List<object?>();
                _reusableExpandedArgs.Clear();
                var expandedArgs = _reusableExpandedArgs;
                // argsToEffectiveOffset[i] = index in effectiveArgs where args[i] starts
                var argsToEffectiveOffset = new int[args.Length];
                for (int i = 0; i < args.Length; i++)
                {
                    argsToEffectiveOffset[i] = expandedArgs.Count;
                    var a = args[i];
                    if (a != null && a.GetType().IsValueType && !(a is IArrayView) && ContainsPointerFields(a.GetType()))
                    {
                        if (WebGPUBackend.VerboseLogging)
                            WebGPUBackend.Log($"[WebGPU-Debug] Pre-expand: Flattening body struct {a.GetType().Name} at args[{i}]");
                        expandedArgs.AddRange(FlattenStructFields(a));
                    }
                    else
                    {
                        expandedArgs.Add(a);
                    }
                }
                // Use expandedArgs list directly as indexable span to avoid ToArray()
                var effectiveArgsCount = expandedArgs.Count;

                // Build packed scalar lookup mapping effectiveArgs index → ScalarPackingEntry.
                //
                // For regular params: effectiveArgsIdx = paramIndex - kernelParamOffset
                // For body struct scalar fields: ScalarPackingManifest uses synthetic ParamIndex
                //   = bodyStructParamIndex * 1000 + irFieldIndex
                // We need to find the effectiveArgs index for the non-view backing field.
                //
                // Strategy: track which effectiveArgs indices are non-view backing fields of body structs.
                // bodyStructScalarEffectiveIdx[argsIdx][nthNonViewField] = effectiveArgsIdx
                _reusableBodyStructMap ??= new Dictionary<int, List<int>>();
                _reusableBodyStructMap.Clear();
                var bodyStructScalarEffectiveIdxMap = _reusableBodyStructMap;
                for (int i = 0; i < args.Length; i++)
                {
                    var a = args[i];
                    if (a != null && a.GetType().IsValueType && !(a is IArrayView) && ContainsPointerFields(a.GetType()))
                    {
                        // This is a body struct — track which effectiveArgs entries are non-view fields
                        var nonViewIndices = new List<int>();
                        int baseIdx = argsToEffectiveOffset[i];
                        var fields = a.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        int fieldEffIdx = baseIdx;
                        foreach (var field in fields)
                        {
                            var fieldVal = field.GetValue(a);
                            if (fieldVal is IArrayView)
                                fieldEffIdx++; // View field → buffer binding, not scalar
                            else
                            {
                                nonViewIndices.Add(fieldEffIdx); // Non-view field → scalar
                                fieldEffIdx++;
                            }
                        }
                        bodyStructScalarEffectiveIdxMap[i] = nonViewIndices;
                    }
                }

                _reusableBodyStructScalarSet ??= new HashSet<int>();
                _reusableBodyStructScalarSet.Clear();
                var allBodyStructScalarEffectiveIdxSet = _reusableBodyStructScalarSet;
                foreach (var kvp in bodyStructScalarEffectiveIdxMap)
                    foreach (var idx in kvp.Value)
                        allBodyStructScalarEffectiveIdxSet.Add(idx);

                // ── COALESCE PROCESSING ──────────────────────────────────────
                // For kernels whose raw binding count would exceed maxStorageBuffersPerShaderStage,
                // the codegen groups same-type body-struct ArrayView fields into shared bindings.
                // At dispatch time we allocate ONE GPU buffer per group, GPU→GPU copy each member's
                // data into it at running offsets, and bind the coalesced buffer ONCE to the leader's
                // binding slot. Non-leader members are skipped in Phase 1; their per-field element
                // offset is written to _scalar_params via IsCoalesceFieldOffset entries in Phase 2.
                //
                // The coalesced buffer is created fresh per dispatch and destroyed after submission.
                // Caching across dispatches when struct-field identity is stable is a future optimization.
                Dictionary<int, GPUBuffer>? coalesceLeaderEffArgsToBuffer = null;
                HashSet<int>? coalesceNonLeaderSkipSet = null;
                Dictionary<(int paramIdx, int fieldIdx), int>? coalesceFieldElementOffsets = null;
                if (compiledKernel.HasCoalesceGroups)
                {
                    coalesceLeaderEffArgsToBuffer = new Dictionary<int, GPUBuffer>();
                    coalesceNonLeaderSkipSet = new HashSet<int>();
                    coalesceFieldElementOffsets = new Dictionary<(int, int), int>();
                    coalescedBuffersToDestroyAfterDispatch = new List<GPUBuffer>();

                    foreach (var group in compiledKernel.CoalesceManifest)
                    {
                        // Build the per-member effective-args index list. Two paths:
                        //   A) IsDirectParam=false (body-struct):   IR field index → expandedArgs[baseEff + fieldIdx]
                        //   B) IsDirectParam=true  (direct-param):  IR param index → expandedArgs[argsToEffectiveOffset[paramIdx - kernelParamOffset]]
                        // After this prelude both paths flow through the same memberInfos list and
                        // the same buffer-allocation + GPU→GPU copy + scalar-offset record loop.
                        var memberInfos = new List<(int fieldIdx, int effArgsIdx, IContiguousArrayView contig, ulong lengthBytes, long elementCount)>();
                        if (group.IsDirectParam)
                        {
                            foreach (int paramIdx in group.MemberDirectParamIndices)
                            {
                                int dpArgsIdx = paramIdx - kernelParamOffset;
                                if (dpArgsIdx < 0 || dpArgsIdx >= args.Length)
                                    throw new InvalidOperationException(
                                        $"[WebGPU-Coalesce] Direct-param index {paramIdx} (kernelParamOffset={kernelParamOffset}) maps to args[{dpArgsIdx}] which is out of range.");
                                int effIdx = argsToEffectiveOffset[dpArgsIdx];
                                if (effIdx < 0 || effIdx >= effectiveArgsCount)
                                    throw new InvalidOperationException(
                                        $"[WebGPU-Coalesce] Direct-param {paramIdx} maps to expandedArgs[{effIdx}] which is out of range.");
                                var memberArg = expandedArgs[effIdx];
                                IArrayView? memberView = memberArg as IArrayView;
                                if (memberView == null && memberArg != null)
                                {
                                    var rc = GetOrCreateReflectionCache(memberArg.GetType());
                                    if (rc.BaseViewProperty != null)
                                        memberView = rc.BaseViewProperty.GetValue(memberArg) as IArrayView;
                                }
                                if (memberView == null)
                                    throw new InvalidOperationException(
                                        $"[WebGPU-Coalesce] Direct-param {paramIdx} is not an IArrayView at expandedArgs[{effIdx}] (got {memberArg?.GetType().Name ?? "null"}).");
                                var contig = memberView as IContiguousArrayView;
                                if (contig == null)
                                {
                                    var vc = GetOrCreateReflectionCache(memberView.GetType());
                                    contig = (vc.BaseViewProperty != null ? vc.BaseViewProperty.GetValue(memberView) : memberView) as IContiguousArrayView;
                                }
                                if (contig == null)
                                    throw new InvalidOperationException(
                                        $"[WebGPU-Coalesce] Direct-param {paramIdx} is not a contiguous view (group {group.BindingName}).");

                                // For direct-param groups we use the IR param index in the fieldIdx slot
                                // — IsCoalesceFieldOffset entries with CoalesceFieldIndex=-1 indicate
                                // direct-param mode, and CoalesceBodyStructParamIndex carries paramIdx.
                                memberInfos.Add((paramIdx, effIdx, contig, (ulong)contig.LengthInBytes, contig.Length));
                            }
                        }
                        else
                        {
                            int bsArgsIdx = group.BodyStructParamIndex - kernelParamOffset;
                            if (bsArgsIdx < 0 || bsArgsIdx >= args.Length)
                                throw new InvalidOperationException(
                                    $"[WebGPU-Coalesce] Body-struct param index {group.BodyStructParamIndex} (kernelParamOffset={kernelParamOffset}) maps to args[{bsArgsIdx}] which is out of range.");
                            int baseEffArgsIdx = argsToEffectiveOffset[bsArgsIdx];

                            // Map IR field index → effectiveArgs index. v1 assumes flat body struct
                            // (no nested struct fields with pointer recursion); FlattenStructFields
                            // walks fields in declaration order so fieldIdx N ↔ expandedArgs[baseIdx + N]
                            // for a flat struct. We defensively verify each member is an IArrayView.
                            foreach (int fieldIdx in group.MemberFieldIndices)
                            {
                                int effIdx = baseEffArgsIdx + fieldIdx;
                                if (effIdx < 0 || effIdx >= effectiveArgsCount)
                                    throw new InvalidOperationException(
                                        $"[WebGPU-Coalesce] Member field {fieldIdx} of body-struct param {group.BodyStructParamIndex} maps to expandedArgs[{effIdx}] which is out of range.");
                                var memberArg = expandedArgs[effIdx];
                                IArrayView? memberView = memberArg as IArrayView;
                                if (memberView == null && memberArg != null)
                                {
                                    var rc = GetOrCreateReflectionCache(memberArg.GetType());
                                    if (rc.BaseViewProperty != null)
                                        memberView = rc.BaseViewProperty.GetValue(memberArg) as IArrayView;
                                }
                                if (memberView == null)
                                    throw new InvalidOperationException(
                                        $"[WebGPU-Coalesce] Member field {fieldIdx} of body-struct param {group.BodyStructParamIndex} is not an IArrayView at expandedArgs[{effIdx}] (got {memberArg?.GetType().Name ?? "null"}).");
                                var contig = memberView as IContiguousArrayView;
                                if (contig == null)
                                {
                                    var vc = GetOrCreateReflectionCache(memberView.GetType());
                                    contig = (vc.BaseViewProperty != null ? vc.BaseViewProperty.GetValue(memberView) : memberView) as IContiguousArrayView;
                                }
                                if (contig == null)
                                    throw new InvalidOperationException(
                                        $"[WebGPU-Coalesce] Member field {fieldIdx} is not a contiguous view (group {group.BindingName}).");

                                memberInfos.Add((fieldIdx, effIdx, contig, (ulong)contig.LengthInBytes, contig.Length));
                            }
                        }

                        // Sum total bytes (4-byte aligned). All members share one element type so
                        // element-stride is uniform within the group.
                        ulong totalBytes = 0;
                        foreach (var m in memberInfos) totalBytes += (ulong)WebGPUAlignment.AlignTo4((long)m.lengthBytes);
                        if (totalBytes == 0) totalBytes = 4; // never bind a zero-size buffer

                        var coalescedBuffer = device.CreateBuffer(new GPUBufferDescriptor
                        {
                            Label = $"Coalesce-{group.BindingName}",
                            Size = totalBytes,
                            Usage = GPUBufferUsage.Storage | GPUBufferUsage.CopyDst | GPUBufferUsage.CopySrc,
                            MappedAtCreation = false,
                        });
                        coalescedBuffersToDestroyAfterDispatch.Add(coalescedBuffer);

                        // GPU→GPU copy each member's data into the coalesced buffer at running offset.
                        // Run on a dedicated command encoder + immediate submit so the data is in
                        // place before the compute pass that follows on the main encoder.
                        var copyEnc = device.CreateCommandEncoder(new GPUCommandEncoderDescriptor { Label = $"CoalesceCopy-{group.BindingName}" });
                        ulong runningByteOffset = 0;
                        // Element offset is per-u32-slot for the WGSL access expression
                        //   coalesced_binding[scalar_params[ViewOffsetSlot] + idx * stride]
                        // The codegen's emit applies the stride multiplier inside `_base_idx`, so the
                        // offset value we send is the raw u32-slot offset for direct (stride=1) bindings
                        // and the *element* offset for emu_64 (stride=2) bindings — matching the
                        // _base_idx formula `i32(_scalar_params[voSlot]) + i32(offset) * 2;` in
                        // LoadElementAddress for emu_64 body-struct fields.
                        // Both reduce to: u32-slot offset for unitary types, element-count offset for emu_64.
                        // We compute both via element-count (group.ElementWordsPerSlot already stored).
                        long runningElementCount = 0; // counted in the group's logical element units
                        foreach (var m in memberInfos)
                        {
                            var srcBuffer = (m.contig.Buffer as WebGPUMemoryBuffer)!.NativeBuffer.NativeBuffer!;
                            ulong srcByteOffset = (ulong)m.contig.IndexInBytes;
                            ulong copySize = (ulong)WebGPUAlignment.AlignTo4((long)m.lengthBytes);
                            // Clamp to actual source buffer length (some buffers are 4-byte padded already).
                            ulong srcBufferSize = (ulong)WebGPUAlignment.AlignTo4(((WebGPUMemoryBuffer)m.contig.Buffer).LengthInBytes);
                            if (srcByteOffset + copySize > srcBufferSize)
                                copySize = srcBufferSize - srcByteOffset;

                            if (copySize > 0)
                                copyEnc.CopyBufferToBuffer(srcBuffer, srcByteOffset, coalescedBuffer, runningByteOffset, copySize);

                            // Per-field offset within coalesced buffer, expressed in u32 slots.
                            //
                            // The codegen's LEA for body-struct view fields emits one of:
                            //   non-emu (stride=1):  &binding[i32(_scalar_params[voSlot]) + offsetExpr]
                            //   emu_64  (stride=2):  base_idx = i32(_scalar_params[voSlot]) + i32(offsetExpr) * 2
                            //                        binding[base_idx], binding[base_idx + 1]
                            //
                            // In both cases _scalar_params[voSlot] is the U32 SLOT OFFSET where this
                            // field's data starts in the coalesced array<u32>/array<i32>/array<f32>.
                            // So the value we send is simply byteOffset / 4 — the stride multiplier
                            // is applied inside the WGSL via offsetExpr * stride for emu_64 reads.
                            // Per-member offset value depends on the group's element type:
                            //   non-sub-word (i32/u32/f32/emu_64 / body-struct):
                            //     u32-slot offset = runningByteOffset / 4. The WGSL formula
                            //     `binding[u32SlotOffset + i]` (i32/u32/f32) or
                            //     `binding[u32SlotOffset + i*2]` (emu_64) gives the correct
                            //     u32-word index for element i.
                            //   sub-word (Int8/UInt8/Int16/UInt16/Float16):
                            //     ELEMENT-count offset = runningByteOffset / SubWordElementByteSize.
                            //     The sub-word Load formula `binding[(u32(elemIdx)/elemsPerWord)]`
                            //     consumes the element index directly; storing element offset means
                            //     elemIdx = elementOffset + i is the global element index in the
                            //     shared atomic<u32> array, and dividing by elemsPerWord (=4 for
                            //     1-byte elements, =2 for 2-byte) gives the correct u32-word index.
                            int slotValue;
                            if (group.IsDirectParam && group.IsSubWord)
                            {
                                int elemBytes = group.SubWordElementByteSize > 0 ? group.SubWordElementByteSize : 1;
                                slotValue = (int)(runningByteOffset / (ulong)elemBytes);
                            }
                            else
                            {
                                slotValue = (int)(runningByteOffset / 4UL);
                            }
                            // Key the offsets dict to match what the codegen wrote into IsCoalesceFieldOffset
                            // entries (CoalesceBodyStructParamIndex, CoalesceFieldIndex):
                            //   body-struct: (BodyStructParamIndex, fieldIdx)  — m.fieldIdx is the IR field index
                            //   direct-param: (paramIdx, -1)                   — m.fieldIdx slot is reused for paramIdx
                            var offsetKey = group.IsDirectParam
                                ? (m.fieldIdx, -1)
                                : (group.BodyStructParamIndex, m.fieldIdx);
                            coalesceFieldElementOffsets[offsetKey] = slotValue;

                            runningByteOffset += copySize;
                            runningElementCount += m.elementCount;
                        }
                        var copyCmd = copyEnc.Finish(new GPUCommandBufferDescriptor());
                        device.Queue.Submit(new[] { copyCmd });
                        copyCmd.Dispose();
                        copyEnc.Dispose();

                        // Mark members for Phase 1 routing: leader emits the coalesced binding;
                        // non-leaders are skipped entirely.
                        bool first = true;
                        foreach (var m in memberInfos)
                        {
                            if (first)
                            {
                                coalesceLeaderEffArgsToBuffer[m.effArgsIdx] = coalescedBuffer;
                                first = false;
                            }
                            else
                            {
                                coalesceNonLeaderSkipSet.Add(m.effArgsIdx);
                            }
                        }

                        if (WebGPUBackend.VerboseLogging)
                            WebGPUBackend.Log($"[WebGPU-Coalesce] Group '{group.BindingName}' bound {memberInfos.Count} fields → 1 binding ({totalBytes} bytes total)");
                    }
                }

                _reusablePackedScalarLookup ??= new Dictionary<int, ScalarPackingEntry>();
                _reusablePackedScalarLookup.Clear();
                var packedScalarLookup = _reusablePackedScalarLookup;
                if (compiledKernel.HasScalarPacking)
                {
                    // Pre-compute total manifest entry count per body-struct param index.
                    // The WGSL generator may add scalar manifest entries for IR-level fields that
                    // live *inside* an IArrayView (e.g. Extent from ArrayView1D<long, Dense>).
                    // Those do NOT have corresponding C# runtime args — they are derived from
                    // arrayLength() in the WGSL itself. We must skip the leading "IR-only" entries
                    // and only map the trailing entries (actual user scalar fields) to runtime slots.
                    //
                    // IMPORTANT: The WGSL generator encodes the body-struct param index as
                    //   (param.Index + 1) * 1000 + fieldIndex
                    // so that grid-stride kernels with param.Index = 0 still produce synthetic
                    // values >= 1000 (distinguishable from real param indices).
                    // Decoding: bodyStructIRParamIdx = (entry.ParamIndex / 1000) - 1
                    var manifestCountPerBodyStructParam = new Dictionary<int, int>();
                    foreach (var e in compiledKernel.ScalarPackingManifest)
                    {
                        if (e.ParamIndex >= 1000 && !e.IsViewOffset && !e.IsViewCount)
                        {
                            int bsIdx = (e.ParamIndex / 1000) - 1; // true IR param index
                            manifestCountPerBodyStructParam.TryGetValue(bsIdx, out int c);
                            manifestCountPerBodyStructParam[bsIdx] = c + 1;
                        }
                    }

                    foreach (var entry in compiledKernel.ScalarPackingManifest)
                    {
                        if (entry.ParamIndex >= 1000)
                        {
                            // View offset/count entries for body struct fields are handled in a
                            // separate phase — don't try to map them to C# struct fields.
                            if (entry.IsViewOffset || entry.IsViewCount) continue;

                            // Synthetic param index: body struct scalar field
                            // Decoded: bodyStructIRParamIdx = (entry.ParamIndex / 1000) - 1
                            int bodyStructIRParamIdx = (entry.ParamIndex / 1000) - 1;
                            int bodyStructArgsIdx = bodyStructIRParamIdx - kernelParamOffset;

                            // Count how many non-view-offset scalar manifest entries for this
                            // body struct come before this one (by ByteOffset order).
                            int nthManifestEntry = 0;
                            foreach (var e2 in compiledKernel.ScalarPackingManifest)
                            {
                                if (e2.ParamIndex >= 1000 && !e2.IsViewOffset && !e2.IsViewCount && (e2.ParamIndex / 1000) - 1 == bodyStructIRParamIdx)
                                {
                                    if (e2.ByteOffset < entry.ByteOffset)
                                        nthManifestEntry++;
                                }
                            }

                            if (bodyStructScalarEffectiveIdxMap.TryGetValue(bodyStructArgsIdx, out var nonViewIdxList))
                            {
                                // Total manifest entries may exceed C# non-view fields because the WGSL
                                // generator also emits manifest entries for IR-level fields inside IArrayView
                                // (e.g., the Extent i32 from ArrayView1D<long, Dense>). These leading
                                // entries don't correspond to any runtime arg — skip them.
                                int totalManifest = manifestCountPerBodyStructParam.TryGetValue(bodyStructIRParamIdx, out int tc) ? tc : 0;
                                int extraIROnlyEntries = totalManifest - nonViewIdxList.Count;
                                int nthCSharpScalarField = nthManifestEntry - extraIROnlyEntries;

                                if (nthCSharpScalarField >= 0 && nthCSharpScalarField < nonViewIdxList.Count)
                                {
                                    int effectiveArgsIdx = nonViewIdxList[nthCSharpScalarField];
                                    packedScalarLookup[effectiveArgsIdx] = entry;
                                    if (WebGPUBackend.VerboseLogging)
                                        WebGPUBackend.Log($"[WebGPU-Debug] Body struct scalar: ParamIndex={entry.ParamIndex}, effectiveArgsIdx={effectiveArgsIdx}, nthManifest={nthManifestEntry}, nthCSharp={nthCSharpScalarField}");
                                }
                                else
                                {
                                    if (WebGPUBackend.VerboseLogging)
                                        WebGPUBackend.Log($"[WebGPU-Debug] Body struct scalar: ParamIndex={entry.ParamIndex}, SKIPPED (IR-only, nthManifest={nthManifestEntry}, extra={extraIROnlyEntries})");
                                }
                            }
                        }
                        else
                        {
                            // Regular param: map IR param index to effectiveArgs index
                            // Skip IsViewOffset / IsViewCount / IsCoalesceFieldOffset entries — they're
                            // metadata for view bindings (offset/count/coalesced-member-offset), not
                            // C# scalar args. Adding them to packedScalarLookup would cause Phase 2's
                            // scalar fill to try to serialize the underlying ArrayView arg as a struct
                            // (CopyStructToBytes fails: ArrayView has pointer fields).
                            if (entry.IsViewOffset || entry.IsViewCount || entry.IsCoalesceFieldOffset) continue;
                            // Use argsToEffectiveOffset to map past body-struct expansion. Each body
                            // struct param consumes its IR slot (1 args index) but its FIELDS occupy
                            // multiple expandedArgs slots. So a trailing scalar at args[N] is actually
                            // at expandedArgs[argsToEffectiveOffset[N]], not at expandedArgs[N].
                            // Without this lookup, scaleA in a kernel like
                            //   (Index1D, BodyStructF, BodyStructI, float scaleA, float scaleB, int mode)
                            // reads from inside the first body struct's expanded user-scalar fields.
                            int argsIdx = entry.ParamIndex - kernelParamOffset;
                            if (argsIdx < 0 || argsIdx >= argsToEffectiveOffset.Length) continue;
                            int effectiveArgsIdx = argsToEffectiveOffset[argsIdx];
                            if (effectiveArgsIdx >= 0 && effectiveArgsIdx < effectiveArgsCount)
                                packedScalarLookup[effectiveArgsIdx] = entry;
                        }
                    }
                }

                // --- Phase 1: Emit bindings for non-packed params (views, structs, atomics) ---
                // effectiveArgs[] has body structs pre-expanded into their constituent fields.
                // Skip the index param (effectiveArgs[0..runtimeIndexSkip-1]) and packed scalars.
                for (int i = 0; i < effectiveArgsCount; i++)
                {
                    // Skip the Index type param at effectiveArgs[0] for explicitly grouped kernels
                    if (i < runtimeIndexSkip)
                        continue;

                    // Skip packed scalars — they'll be handled in Phase 2
                    if (packedScalarLookup.ContainsKey(i))
                        continue;

                    // Skip body-struct non-view scalar fields that have no manifest entry.
                    // These are shader-local variables (e.g., ReducedValue in ReductionImplementation)
                    // that the WGSL handles internally — they don't correspond to GPU buffer bindings.
                    // Without this check, they'd create spurious bindings and shift the scalar
                    // pack buffer to the wrong binding index.
                    if (allBodyStructScalarEffectiveIdxSet.Contains(i))
                        continue;

                    // Skip coalesce non-leader members entirely — leader emits the shared binding.
                    if (coalesceNonLeaderSkipSet != null && coalesceNonLeaderSkipSet.Contains(i))
                        continue;

                    // Coalesce LEADER: bind the COALESCED GPU BUFFER instead of the leader's
                    // individual buffer. The shared buffer was allocated and populated above.
                    if (coalesceLeaderEffArgsToBuffer != null && coalesceLeaderEffArgsToBuffer.TryGetValue(i, out var coalescedGpuBuffer))
                    {
                        ulong coalescedSize = coalescedGpuBuffer.Size > 0 ? coalescedGpuBuffer.Size : 4UL;
                        var coalescedResource = new GPUBufferBinding
                        {
                            Buffer = coalescedGpuBuffer,
                            Offset = 0,
                            Size = coalescedSize,
                        };
                        entries.Add(new GPUBindGroupEntry { Binding = (uint)currentBindingIndex, Resource = coalescedResource });
                        // Record element-offset = 0 for the leader (its data starts at the buffer head).
                        // Phase 2 IsCoalesceFieldOffset entries override this for non-leaders.
                        viewElementOffsets[currentBindingIndex] = 0;
                        currentBindingIndex++;
                        if (WebGPUBackend.VerboseLogging)
                            WebGPUBackend.Log($"[WebGPU-Coalesce] Bound coalesced buffer at binding {currentBindingIndex - 1} (size={coalescedSize})");
                        continue;
                    }

                    var arg = expandedArgs[i];

                    IArrayView? arrayView = arg as IArrayView;
                    int[] dims = Array.Empty<int>();

                    if (arg != null)
                    {
                        var argType = arg.GetType();
                        var argCache = GetOrCreateReflectionCache(argType);
                        if (argType.Name.Contains("ArrayView"))
                        {
                            dims = ExtractDimensionsFromView(arg, argType);
                        }

                        if (arrayView == null && argCache.BaseViewProperty != null)
                        {
                            arrayView = argCache.BaseViewProperty.GetValue(arg) as IArrayView;
                        }
                    }

                    GPUBufferBinding? resource = null;

                    if (arrayView != null)
                    {
                        var contiguous = arrayView as IContiguousArrayView;
                        if (contiguous == null)
                        {
                            var viewCache = GetOrCreateReflectionCache(arrayView.GetType());
                            contiguous = (viewCache.BaseViewProperty != null ? viewCache.BaseViewProperty.GetValue(arrayView) : arrayView) as IContiguousArrayView;
                        }

                        if (contiguous == null) throw new Exception($"Argument {i} is not a contiguous WebGPU buffer");

                        var nativeBuffer = contiguous.Buffer as WebGPUMemoryBuffer;
                        var gpuBuffer = nativeBuffer!.NativeBuffer.NativeBuffer!;

                        if (WebGPUBackend.VerboseLogging)
                            WebGPUBackend.Log($"[WebGPU-Debug] Arg {i}: Binding Buffer. Size={contiguous.LengthInBytes}, Offset={contiguous.IndexInBytes}");

                        // WebGPU requires storage buffer binding offsets to be aligned
                        // to minStorageBufferOffsetAlignment (256 bytes). ILGPU sub-views
                        // may have arbitrary element-aligned offsets (e.g., 4612 bytes).
                        //
                        // FIX: Round the byte offset DOWN to the nearest 256-byte boundary.
                        // The remainder (padding) becomes an element offset packed into
                        // _scalar_params so the WGSL reads the correct slice.
                        //
                        // This avoids aliasing: two sub-views of the same buffer at
                        // different 256-aligned positions get NON-OVERLAPPING binding ranges,
                        // satisfying WebGPU's writable storage buffer aliasing rules.
                        ulong rawOffset = (ulong)((long)contiguous.IndexInBytes);
                        ulong alignedOffset = rawOffset & ~(MinStorageBufferOffsetAlignment - 1);
                        ulong padding = rawOffset - alignedOffset;
                        ulong bindingSize = (ulong)WebGPUAlignment.AlignTo4((long)(padding + (ulong)contiguous.LengthInBytes));
                        // Clamp to actual GPU buffer size (which was allocated with AlignTo4)
                        ulong bufferSize = (ulong)WebGPUAlignment.AlignTo4(nativeBuffer.LengthInBytes);
                        if (alignedOffset + bindingSize > bufferSize)
                            bindingSize = bufferSize - alignedOffset;
                        
                        resource = new GPUBufferBinding
                        {
                            Buffer = gpuBuffer,
                            Offset = alignedOffset,
                            Size = bindingSize
                        };

                        // Record the u32 offset for this binding so Phase 2 can pack it.
                        // We store padding/4 (u32 units) rather than padding/elementSize (element units)
                        // because the WGSL formula is: base_idx = u32Offset + i * packed_stride
                        // This correctly handles packed structs where CPU element size != GPU packed size.
                        // For regular 4-byte views: u32Offset == elementOffset (no change in behavior).
                        // For emu_f64/i64 (8-byte CPU, 2 u32s): formulas are mathematically equivalent.
                        int elementOffset = (int)(padding / 4UL);
                        viewElementOffsets[currentBindingIndex] = elementOffset;
                        // Record the logical element count for packed-struct views.
                        // contiguous.Length gives the exact count regardless of CPU vs GPU element size.
                        viewElementCounts[currentBindingIndex] = (int)contiguous.Length;
                        
                        if (WebGPUBackend.VerboseLogging)
                            WebGPUBackend.Log($"[WebGPU-Debug] Arg {i}: rawOffset={rawOffset}, alignedOffset={alignedOffset}, padding={padding}, elemOffset={elementOffset}, bindingSize={bindingSize}");
                    }
                    else
                    {
                        // Non-packed, non-view param (struct scalar with own binding)
                        var size = 256;
                        var uBuffer = GetPooledScalarBuffer(device);
                        scalarBuffersToReturn.Add(uBuffer);

                        if (WebGPUBackend.VerboseLogging)
                            WebGPUBackend.Log($"[WebGPU-Debug] Arg {i}: Binding Struct Scalar. Value={arg}");

                        if (arg != null && arg.GetType().IsValueType)
                        {
                            Type argType = arg.GetType();

                            // Use Interop.SizeOf(Type) which handles generic structs via Unsafe.SizeOf<T>
                            // (Marshal.SizeOf fails on generic types like ReductionImplementation<T,S,R>).
                            int structSize = global::ILGPU.Interop.SizeOf(argType);
                            int paddedSize = (int)WebGPUAlignment.AlignTo4(structSize);
                            byte[] bytes = new byte[paddedSize];

                            // GCHandle.Alloc(Pinned) fails for boxed generic structs in Blazor WASM
                            // (ArgumentException_NotIsomorphic). Use our CopyStructToBytes<T> helper
                            // which uses MemoryMarshal.AsBytes — no GC pinning required.
                            GetCopyStructMethod(argType).Invoke(null, new object[] { arg, bytes });

                            device.Queue.WriteBuffer(uBuffer, 0, bytes);
                            if (WebGPUBackend.VerboseLogging)
                                WebGPUBackend.Log($"[WebGPU-Debug] Arg {i}: Struct scalar {argType.Name}, Size={structSize} bytes");
                        }
                        else if (arg != null && arg.GetType().IsDefined(
                            typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
                        {
                            // Capturing lambda display class — flatten fields
                            // like a struct for GPU buffer binding
                            var fields = arg.GetType().GetFields(
                                System.Reflection.BindingFlags.Instance |
                                System.Reflection.BindingFlags.Public |
                                System.Reflection.BindingFlags.NonPublic);
                            int totalSize = 0;
                            foreach (var f in fields)
                                totalSize += global::ILGPU.Interop.SizeOf(f.FieldType);
                            int paddedSize = (int)WebGPUAlignment.AlignTo4(totalSize);
                            byte[] bytes = new byte[paddedSize];
                            int offset = 0;
                            foreach (var f in fields)
                            {
                                var val = f.GetValue(arg)!;
                                int fieldSize = global::ILGPU.Interop.SizeOf(f.FieldType);
                                var fieldBytes = new byte[fieldSize];
                                GetCopyStructMethod(f.FieldType).Invoke(
                                    null, new object[] { val, fieldBytes });
                                Array.Copy(fieldBytes, 0, bytes, offset, fieldSize);
                                offset += fieldSize;
                            }
                            device.Queue.WriteBuffer(uBuffer, 0, bytes);
                            if (WebGPUBackend.VerboseLogging)
                                WebGPUBackend.Log($"[WebGPU-Debug] Arg {i}: Display class captures, Size={totalSize} bytes");
                        }
                        else throw new NotSupportedException($"Unsupported non-packed non-view argument type: {arg?.GetType()}");

                        resource = new GPUBufferBinding { Buffer = uBuffer, Offset = 0, Size = (ulong)size };
                    }

                    entries.Add(new GPUBindGroupEntry { Binding = (uint)currentBindingIndex, Resource = resource! });
                    currentBindingIndex++;

                    if (dims.Length > 1)
                    {
                        if (WebGPUBackend.VerboseLogging)
                            WebGPUBackend.Log($"[WebGPU-Debug] Arg {i}: Binding Stride Buffer. Values=[{string.Join(", ", dims)}]");

                        var strideSize = 256;
                        var strideBuffer = GetPooledScalarBuffer(device);
                        scalarBuffersToReturn.Add(strideBuffer);

                        var strideData = new int[dims.Length];
                        Array.Copy(dims, strideData, dims.Length);
                        var byteData = new byte[dims.Length * 4];
                        Buffer.BlockCopy(strideData, 0, byteData, 0, byteData.Length);

                        device.Queue.WriteBuffer(strideBuffer, 0, byteData);

                        entries.Add(new GPUBindGroupEntry { Binding = (uint)currentBindingIndex, Resource = new GPUBufferBinding { Buffer = strideBuffer, Offset = 0, Size = (ulong)strideSize } });
                        currentBindingIndex++;
                    }
                }

                // --- Phase 2: Pack all scalar args into single buffer ---
                if (compiledKernel.HasScalarPacking)
                {
                    var manifest = compiledKernel.ScalarPackingManifest;

                    // Calculate total packed buffer size (round up to 4-byte alignment, min 256 for WebGPU)
                    int totalSlots = 0;
                    foreach (var entry in manifest)
                        totalSlots = Math.Max(totalSlots, entry.ByteOffset / 4 + entry.SlotCount);
                    int totalBytes = Math.Max(totalSlots * 4, 4);

                    var packedData = new byte[totalBytes];

                    // Use packedScalarLookup (effectiveArgsIdx → entry) which correctly handles
                    // both regular params and body struct scalar fields (synthetic param indices).
                    foreach (var kvp in packedScalarLookup)
                    {
                        int effectiveArgsIdx = kvp.Key;
                        var entry = kvp.Value;
                        var arg = expandedArgs[effectiveArgsIdx];
                        int byteOffset = entry.ByteOffset;

                        if (WebGPUBackend.VerboseLogging)
                            WebGPUBackend.Log($"[WebGPU-Debug] Packing scalar param {entry.ParamIndex} (effectiveArgs[{effectiveArgsIdx}]) at byte offset {byteOffset}: {arg}");

                        // Unwrap SpecializedValue<T> to extract the inner T value
                        if (arg != null && arg.GetType().IsGenericType && 
                            arg.GetType().Name.StartsWith("SpecializedValue"))
                        {
                            arg = arg.GetType().GetProperty("Value")!.GetValue(arg);
                        }

                        if (arg is int iVal)
                            BitConverter.GetBytes(iVal).CopyTo(packedData, byteOffset);
                        else if (arg is float fVal)
                            BitConverter.GetBytes(fVal).CopyTo(packedData, byteOffset);
                        else if (arg is uint uiVal)
                            BitConverter.GetBytes(uiVal).CopyTo(packedData, byteOffset);
                        else if (arg is long lVal)
                        {
                            // emu_i64: pack as two u32 values (low word, high word)
                            BitConverter.GetBytes((uint)(lVal & 0xFFFFFFFFL)).CopyTo(packedData, byteOffset);
                            if (byteOffset + 4 < packedData.Length)
                                BitConverter.GetBytes((uint)((lVal >> 32) & 0xFFFFFFFFL)).CopyTo(packedData, byteOffset + 4);
                        }
                        else if (arg is ulong ulVal)
                        {
                            // emu_u64: pack as two u32 values (low word, high word)
                            BitConverter.GetBytes((uint)(ulVal & 0xFFFFFFFFL)).CopyTo(packedData, byteOffset);
                            if (byteOffset + 4 < packedData.Length)
                                BitConverter.GetBytes((uint)((ulVal >> 32) & 0xFFFFFFFFL)).CopyTo(packedData, byteOffset + 4);
                        }
                        else if (arg is LongIndex1D li1)
                        {
                            // LongIndex1D wraps a long — pack as emu_i64 (two u32 values)
                            long rawVal = li1.X;
                            BitConverter.GetBytes((uint)(rawVal & 0xFFFFFFFFL)).CopyTo(packedData, byteOffset);
                            if (byteOffset + 4 < packedData.Length)
                                BitConverter.GetBytes((uint)((rawVal >> 32) & 0xFFFFFFFFL)).CopyTo(packedData, byteOffset + 4);
                        }
                        else if (arg is LongIndex2D li2)
                        {
                            // LongIndex2D: pack X then Y as two emu_i64 pairs
                            BitConverter.GetBytes((uint)(li2.X & 0xFFFFFFFFL)).CopyTo(packedData, byteOffset);
                            if (byteOffset + 4 < packedData.Length)
                                BitConverter.GetBytes((uint)((li2.X >> 32) & 0xFFFFFFFFL)).CopyTo(packedData, byteOffset + 4);
                            if (byteOffset + 8 < packedData.Length)
                                BitConverter.GetBytes((uint)(li2.Y & 0xFFFFFFFFL)).CopyTo(packedData, byteOffset + 8);
                            if (byteOffset + 12 < packedData.Length)
                                BitConverter.GetBytes((uint)((li2.Y >> 32) & 0xFFFFFFFFL)).CopyTo(packedData, byteOffset + 12);
                        }
                        else if (arg is LongIndex3D li3)
                        {
                            // LongIndex3D: pack X, Y, Z as three emu_i64 pairs
                            BitConverter.GetBytes((uint)(li3.X & 0xFFFFFFFFL)).CopyTo(packedData, byteOffset);
                            if (byteOffset + 4 < packedData.Length)
                                BitConverter.GetBytes((uint)((li3.X >> 32) & 0xFFFFFFFFL)).CopyTo(packedData, byteOffset + 4);
                            if (byteOffset + 8 < packedData.Length)
                                BitConverter.GetBytes((uint)(li3.Y & 0xFFFFFFFFL)).CopyTo(packedData, byteOffset + 8);
                            if (byteOffset + 12 < packedData.Length)
                                BitConverter.GetBytes((uint)((li3.Y >> 32) & 0xFFFFFFFFL)).CopyTo(packedData, byteOffset + 12);
                            if (byteOffset + 16 < packedData.Length)
                                BitConverter.GetBytes((uint)(li3.Z & 0xFFFFFFFFL)).CopyTo(packedData, byteOffset + 16);
                            if (byteOffset + 20 < packedData.Length)
                                BitConverter.GetBytes((uint)((li3.Z >> 32) & 0xFFFFFFFFL)).CopyTo(packedData, byteOffset + 20);
                        }
                        else if (arg is double dVal)
                        {
                            if (webGpuAccel.Backend.EnableF64Emulation)
                            {
                                // Write full 64-bit IEEE-754 as 2 u32 values
                                BitConverter.GetBytes(dVal).CopyTo(packedData, byteOffset);
                            }
                            else
                            {
                                BitConverter.GetBytes((float)dVal).CopyTo(packedData, byteOffset);
                            }
                        }
                        else if (arg is global::ILGPU.Half hVal)
                        {
                            // Half occupies 2 bytes but is packed into a u32 slot.
                            // Place the raw f16 bits in the low 16 bits so the WGSL
                            // bitcast<vec2<f16>>(u32).x pattern extracts the correct value.
                            ushort rawBits = global::ILGPU.Interop.FloatAsInt(hVal);
                            BitConverter.GetBytes(rawBits).CopyTo(packedData, byteOffset);
                        }
                        else if (arg is byte bVal)
                            BitConverter.GetBytes((uint)bVal).CopyTo(packedData, byteOffset);
                        else if (arg is bool blVal)
                            BitConverter.GetBytes(blVal ? 1u : 0u).CopyTo(packedData, byteOffset);
                        else if (arg is Index1D idx1)
                            BitConverter.GetBytes(idx1.X).CopyTo(packedData, byteOffset);
                        else if (arg is Index2D idx2)
                        {
                            BitConverter.GetBytes(idx2.X).CopyTo(packedData, byteOffset);
                            if (byteOffset + 4 < packedData.Length)
                                BitConverter.GetBytes(idx2.Y).CopyTo(packedData, byteOffset + 4);
                        }
                        else if (arg is Index3D idx3)
                        {
                            BitConverter.GetBytes(idx3.X).CopyTo(packedData, byteOffset);
                            if (byteOffset + 4 < packedData.Length)
                                BitConverter.GetBytes(idx3.Y).CopyTo(packedData, byteOffset + 4);
                            if (byteOffset + 8 < packedData.Length)
                                BitConverter.GetBytes(idx3.Z).CopyTo(packedData, byteOffset + 8);
                        }
                        else if (arg != null && (arg.GetType().IsValueType
                            || arg.GetType().IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)))
                        {
                            // Fallback for value types and capturing lambda display classes:
                            // serialize field bytes directly
                            if (arg.GetType().IsValueType)
                            {
                                int structSize = global::ILGPU.Interop.SizeOf(arg.GetType());
                                byte[] bytes = new byte[structSize];
                                GetCopyStructMethod(arg.GetType()).Invoke(null, new object[] { arg, bytes });
                                int copyLen = Math.Min(structSize, packedData.Length - byteOffset);
                                Array.Copy(bytes, 0, packedData, byteOffset, copyLen);
                            }
                            else
                            {
                                // Display class: flatten instance fields
                                var fields = arg.GetType().GetFields(
                                    System.Reflection.BindingFlags.Instance |
                                    System.Reflection.BindingFlags.Public |
                                    System.Reflection.BindingFlags.NonPublic);
                                int localOffset = byteOffset;
                                foreach (var f in fields)
                                {
                                    var val = f.GetValue(arg)!;
                                    int fieldSize = global::ILGPU.Interop.SizeOf(f.FieldType);
                                    var fieldBytes = new byte[fieldSize];
                                    GetCopyStructMethod(f.FieldType).Invoke(
                                        null, new object[] { val, fieldBytes });
                                    int copyLen = Math.Min(fieldSize,
                                        packedData.Length - localOffset);
                                    Array.Copy(fieldBytes, 0, packedData,
                                        localOffset, copyLen);
                                    localOffset += fieldSize;
                                }
                            }
                        }
                        else if (arg != null)
                            throw new NotSupportedException($"Unsupported packed scalar type: {arg.GetType()}");

                    }

                    // --- Pack view element offsets and counts ---
                    // For each IsViewOffset entry in the manifest, pack the element offset.
                    // For each IsViewCount entry, pack the true element count for packed-struct views.
                    // For each IsCoalesceFieldOffset entry, pack the field's u32-slot offset within
                    // its coalesced shared buffer (computed during coalesce-processing pre-pass).
                    foreach (var entry in manifest)
                    {
                        if (entry.IsCoalesceFieldOffset)
                        {
                            int byteOffset = entry.ByteOffset;
                            if (byteOffset + 4 > packedData.Length)
                            {
                                var newData = new byte[byteOffset + 4];
                                Array.Copy(packedData, newData, packedData.Length);
                                packedData = newData;
                            }
                            int slotOffset = 0;
                            if (coalesceFieldElementOffsets != null
                                && coalesceFieldElementOffsets.TryGetValue((entry.CoalesceBodyStructParamIndex, entry.CoalesceFieldIndex), out int co))
                                slotOffset = co;
                            BitConverter.GetBytes(slotOffset).CopyTo(packedData, byteOffset);
                            if (WebGPUBackend.VerboseLogging)
                                WebGPUBackend.Log($"[WebGPU-Coalesce] Packed coalesce field offset: param={entry.CoalesceBodyStructParamIndex}, field={entry.CoalesceFieldIndex}, slotOffset={slotOffset}, byteOffset={byteOffset}");
                            continue;
                        }
                        if (entry.IsViewOffset)
                        {
                            int byteOffset = entry.ByteOffset;
                            if (byteOffset + 4 > packedData.Length)
                            {
                                // Extend packedData if needed (view offsets may extend beyond user scalars)
                                var newData = new byte[byteOffset + 4];
                                Array.Copy(packedData, newData, packedData.Length);
                                packedData = newData;
                            }
                            int elemOffset = viewElementOffsets.TryGetValue(entry.ViewBindingIndex, out int eo) ? eo : 0;
                            BitConverter.GetBytes(elemOffset).CopyTo(packedData, byteOffset);
                            if (WebGPUBackend.VerboseLogging)
                                WebGPUBackend.Log($"[WebGPU-Debug] Packed view offset: binding={entry.ViewBindingIndex}, elemOffset={elemOffset}, byteOffset={byteOffset}");
                        }
                        else if (entry.IsViewCount)
                        {
                            int byteOffset = entry.ByteOffset;
                            if (byteOffset + 4 > packedData.Length)
                            {
                                var newData = new byte[byteOffset + 4];
                                Array.Copy(packedData, newData, packedData.Length);
                                packedData = newData;
                            }
                            int elemCount = viewElementCounts.TryGetValue(entry.ViewCountBindingIndex, out int ec) ? ec : 0;
                            BitConverter.GetBytes(elemCount).CopyTo(packedData, byteOffset);
                            if (WebGPUBackend.VerboseLogging)
                                WebGPUBackend.Log($"[WebGPU-Debug] Packed view count: binding={entry.ViewCountBindingIndex}, elemCount={elemCount}, byteOffset={byteOffset}");
                        }
                    }

                    var packedBuffer = GetPooledScalarBuffer(device);
                    scalarBuffersToReturn.Add(packedBuffer);
                    device.Queue.WriteBuffer(packedBuffer, 0, packedData);

                    entries.Add(new GPUBindGroupEntry
                    {
                        Binding = (uint)currentBindingIndex,
                        Resource = new GPUBufferBinding { Buffer = packedBuffer, Offset = 0, Size = 256 }
                    });
                    currentBindingIndex++;

                    if (WebGPUBackend.VerboseLogging)
                        WebGPUBackend.Log($"[WebGPU-Debug] Packed {packedScalarLookup.Count} scalars into 1 buffer ({totalBytes} bytes used, binding {currentBindingIndex - 1})");
                }


                // Allocate and bind spinlock buffers for i64 Min/Max/Exchange atomics
                if (compiledKernel.HasI64Spinlocks)
                {
                    foreach (var lockKey in compiledKernel.I64SpinlockParamIndices.OrderBy(k => k.ParamIdx).ThenBy(k => k.FieldIdx))
                    {
                        // Find the data buffer for this param to determine lock count
                        // Each i64 element = 2 u32s, so lock count = data buffer element count / 2
                        // Use a fixed size that covers any reasonable buffer
                        var lockBuffer = GetPooledScalarBuffer(device);
                        // Zero the lock buffer before dispatch (all locks start unlocked)
                        device.Queue.WriteBuffer(lockBuffer, 0, new byte[256]);
                        scalarBuffersToReturn.Add(lockBuffer);
                        entries.Add(new GPUBindGroupEntry
                        {
                            Binding = (uint)currentBindingIndex,
                            Resource = new GPUBufferBinding { Buffer = lockBuffer, Offset = 0, Size = 256 }
                        });
                        currentBindingIndex++;
                    }
                }

                // Validate/fix entry count to match WGSL layout (avoids "binding index N not present" errors)
                int expectedCount = compiledKernel.ExpectedBindingCount;
                // When WGSL declares only 1 binding (the _scalar_params buffer) but the runtime
                // built 2 entries [view, scalar], the view was folded into _scalar_params at
                // compile time. Only apply this workaround when expectedCount confirms the
                // layout truly has 1 binding — otherwise we'd drop legitimate view bindings.
                bool applyScalarOnlyWorkaround = entries.Count == 2
                    && compiledKernel.HasScalarPacking
                    && expectedCount == 1;
                if (applyScalarOnlyWorkaround)
                {
                    // Layout expects 1 binding (scalar-only) but runtime built 2 [view, scalar].
                    // This happens when WGSL packs a view's offset into _scalar_params but omits
                    // the view buffer (e.g. certain body-struct or length-only cases). Use only
                    // the scalar buffer at binding 0.
                    var scalarEntry = entries[1]; // Phase 2 adds scalar buffer after Phase 1's views
                    entries.Clear();
                    entries.Add(new GPUBindGroupEntry { Binding = 0, Resource = scalarEntry.Resource });
                    if (WebGPUBackend.VerboseLogging)
                        WebGPUBackend.Log($"[WebGPU-BindGroup] Workaround: using scalar-only bindings for kernel '{compiledKernel.Name}'");
                }
                else if (expectedCount > 0 && entries.Count > expectedCount)
                {
                    // The WGSL binding count is the source of truth. The runtime may create
                    // extra entries (e.g. Phase 2 scalar buffer for ArrayView extents that the
                    // WGSL handles via arrayLength()). Trim to match the WGSL layout.
                    if (WebGPUBackend.VerboseLogging)
                    {
                        var kernelName = compiledKernel.Name ?? "unknown";
                        WebGPUBackend.Log($"[WebGPU-BindGroup] Trimming entries from {entries.Count} to {expectedCount} for kernel '{kernelName}' (HasScalarPacking={compiledKernel.HasScalarPacking})");
                    }
                    while (entries.Count > expectedCount)
                        entries.RemoveAt(entries.Count - 1);
                }
                else if (expectedCount > 0 && entries.Count < expectedCount)
                {
                    var kernelName = compiledKernel.Name ?? "unknown";
                    throw new InvalidOperationException(
                        $"[WebGPU] Bind group mismatch for kernel '{kernelName}': " +
                        $"layout expects {expectedCount} binding(s), but only {entries.Count} entries were built.");
                }

                // ── Binding count limit check ──────────────────────────────
                // Validate that the kernel's storage buffer binding count does not
                // exceed the device's maxStorageBuffersPerShaderStage limit.
                // Without this check, the pipeline silently fails and produces
                // cryptic "Invalid BindGroupLayout due to a previous error" messages.
                var maxBindings = webGpuAccel.MaxStorageBufferBindings;
                var finalBindingCount = entries.Count;
                if (finalBindingCount > maxBindings)
                {
                    var kernelName = compiledKernel.Name ?? "unknown";
                    // Background CheckShaderAsync for this dispatch's shader module is in
                    // flight. It will queue a "12 bindings exceeds 10" validation error
                    // via AddShaderError when it completes — but the caller has already
                    // received OUR exception below. If we don't consume that pending
                    // error here it will leak into the NEXT Synchronize call and surface
                    // there with a stale message, breaking unrelated tests (the rc.13
                    // ManyViews_11Views → ILGPUReduceHalfTest leak). Discard it on the
                    // background thread so this runtime exception is the ONLY one the
                    // caller sees from this dispatch.
                    var nativeAccelForDrain = webGpuAccel.NativeAccelerator;
                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            await nativeAccelForDrain.DrainShaderChecksAsync();
                            try { nativeAccelForDrain.ThrowIfGpuErrors(); } catch { /* swallow — caller already got our InvalidOperationException */ }
                        }
                        catch { }
                    });
                    throw new InvalidOperationException(
                        $"[WebGPU] Kernel '{kernelName}' requires {finalBindingCount} storage buffer bindings " +
                        $"but this device only supports {maxBindings} (maxStorageBuffersPerShaderStage). " +
                        $"Reduce the number of ArrayView parameters by combining related data into structs.");
                }

                if (WebGPUBackend.VerboseLogging)
                {
                    if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WebGPU-BindGroup] Creating bind group with {entries.Count} entries (expected={expectedCount}) for kernel: {compiledKernel.Name}");
                    for (int ei = 0; ei < entries.Count; ei++)
                        WebGPUBackend.Log($"  entries[{ei}]: binding={entries[ei].Binding}");
                }

                // ── WebGPU aliasing check ──────────────────────────────────
                // WebGPU forbids binding the same buffer region to multiple
                // read_write storage bindings in the same dispatch. This happens
                // when the caller passes the same ArrayView as two different
                // kernel parameters. Detect and throw a clear error instead of
                // silently producing zeros.
                {
                    var entryArr = entries;
                    for (int ei = 0; ei < entryArr.Count; ei++)
                    {
                        var bindA = entryArr[ei].Resource?.Value as GPUBufferBinding;
                        if (bindA == null) continue;

                        for (int ej = ei + 1; ej < entryArr.Count; ej++)
                        {
                            var bindB = entryArr[ej].Resource?.Value as GPUBufferBinding;
                            if (bindB == null) continue;

                            // Same underlying GPU buffer?
                            if (bindA.Buffer == null || bindB.Buffer == null) continue;
                            if (!ReferenceEquals(bindA.Buffer, bindB.Buffer)) continue;

                            // Check for overlapping ranges
                            ulong startA = bindA.Offset ?? 0;
                            ulong endA = startA + (bindA.Size ?? 0);
                            ulong startB = bindB.Offset ?? 0;
                            ulong endB = startB + (bindB.Size ?? 0);

                            if (startA < endB && startB < endA)
                            {
                                var kernelName = compiledKernel.Name ?? "unknown";
                                throw new InvalidOperationException(
                                    $"[WebGPU] Storage buffer aliasing detected in kernel '{kernelName}': " +
                                    $"binding {entryArr[ei].Binding} and binding {entryArr[ej].Binding} " +
                                    $"reference the same GPU buffer with overlapping ranges " +
                                    $"([{startA}..{endA}) and [{startB}..{endB})). " +
                                    $"WebGPU forbids binding the same buffer to multiple read_write storage slots. " +
                                    $"Use separate buffers for each kernel parameter.");
                            }
                        }
                    }
                }

                var bindGroupDesc = new GPUBindGroupDescriptor
                {
                    Layout = shader.BindGroupLayout!,
                    Entries = entries.ToArray()
                };

                var bindGroup = device.CreateBindGroup(bindGroupDesc);

                uint workX = 1, workY = 1, workZ = 1;

                // Handle KernelConfig for explicit launches
                if (dimension is KernelConfig config)
                {
                    workX = (uint)config.GridDim.X;
                    workY = (uint)config.GridDim.Y;
                    workZ = (uint)config.GridDim.Z;
                    // 2D fallback for 1D explicitly grouped kernels exceeding 65535 workgroups.
                    // Splits workX into (workX, workY) — Grid.IdxX/DimX linearization in the
                    // WGSL code generator handles reconstructing the flat workgroup index.
                    // Only applies when the grid is logically 1D (Y=1, Z=1).
                    if (workX > 65535u && workY == 1u && workZ == 1u)
                    {
                        uint totalWG = workX;
                        workX = 65535u;
                        workY = (totalWG + 65534u) / 65535u;
                    }
                }
                else if (dimension is Index1D i1)
                {
                    // WebGPU limits each dispatch dimension to 65535 workgroups.
                    // For large 1D ranges (e.g. 14.7M elements → 229,954 workgroups),
                    // spill into workY. The kernel's index formula accounts for this via
                    // group_id.y * num_workgroups.x in WGSLKernelFunctionGenerator.
                    uint totalWG = (uint)Math.Ceiling(i1.X / 64.0);
                    if (totalWG > 65535u) { workX = 65535u; workY = (totalWG + 65534u) / 65535u; }
                    else workX = totalWG;
                }
                else if (dimension is Index2D i2) { workX = (uint)Math.Ceiling(i2.X / 8.0); workY = (uint)Math.Ceiling(i2.Y / 8.0); }
                else if (dimension is Index3D i3) { workX = (uint)Math.Ceiling(i3.X / 4.0); workY = (uint)Math.Ceiling(i3.Y / 4.0); workZ = (uint)Math.Ceiling(i3.Z / 4.0); }
                else if (dimension is LongIndex1D l1)
                {
                    uint totalWG = (uint)Math.Ceiling(l1.X / 64.0);
                    if (totalWG > 65535u) { workX = 65535u; workY = (totalWG + 65534u) / 65535u; }
                    else workX = totalWG;
                }
                else if (dimension is LongIndex2D l2) { workX = (uint)Math.Ceiling(l2.X / 8.0); workY = (uint)Math.Ceiling(l2.Y / 8.0); }
                else if (dimension is LongIndex3D l3) { workX = (uint)Math.Ceiling(l3.X / 4.0); workY = (uint)Math.Ceiling(l3.Y / 4.0); workZ = (uint)Math.Ceiling(l3.Z / 4.0); }

                if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WebGPU] Dispatching: ({workX}, {workY}, {workZ})");

                // Use the stream's shared encoder for batched submission
                var webGpuStream = stream as WebGPUStream ?? (WebGPUStream)webGpuAccel.DefaultStream;
                var encoder = webGpuStream.GetOrCreateEncoder();
                using var pass = encoder.BeginComputePass();
                pass.SetPipeline(shader.Pipeline);
                pass.SetBindGroup(0, bindGroup);
                pass.DispatchWorkgroups(workX, workY, workZ);
                pass.End();
                webGpuStream.IncrementPassCount();

                // Log dispatch for post-mortem debugging
                var dispatchKernelName = compiledKernel.EntryPoint.MethodInfo?.Name ?? "unknown";
                int dispatchWgSize = 0;
                var wgSizeMatch = s_workgroupSizeSimplePattern.Match(wgslSource);
                if (wgSizeMatch.Success) dispatchWgSize = int.Parse(wgSizeMatch.Groups[1].Value);
                _dispatchLog.Enqueue(new DispatchRecord(
                    dispatchKernelName, dispatchWgSize, workX, workY, workZ,
                    compiledKernel.ExpectedBindingCount, wasPatched, DateTime.UtcNow));
                while (_dispatchLog.Count > MaxDispatchLogSize)
                    _dispatchLog.Dequeue();

                // Defer resource cleanup until the batch is flushed
                webGpuStream.DeferBindGroupDisposal(bindGroup);
                foreach (var buffer in scalarBuffersToReturn)
                    webGpuStream.DeferScalarReturn(buffer);
                scalarBuffersToReturn.Clear();
                // Coalesced buffers are allocated fresh per dispatch (not pooled — sizes vary
                // widely with kernel parameter shape). Defer destruction until the batch is
                // flushed so the GPU has finished using them.
                if (coalescedBuffersToDestroyAfterDispatch != null)
                {
                    foreach (var cBuf in coalescedBuffersToDestroyAfterDispatch)
                        webGpuStream.DeferCoalesceBufferDestroy(cBuf);
                    coalescedBuffersToDestroyAfterDispatch.Clear();
                }
            }
            catch (Exception ex)
            {
                if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WebGPU] Error running kernel: {ex}");
                // Error path: destroy coalesced buffers immediately to prevent leaks.
                if (coalescedBuffersToDestroyAfterDispatch != null)
                {
                    foreach (var cBuf in coalescedBuffersToDestroyAfterDispatch)
                    {
                        try { cBuf.Destroy(); cBuf.Dispose(); }
                        catch { /* swallow — already failing */ }
                    }
                    coalescedBuffersToDestroyAfterDispatch.Clear();
                }
                throw;
            }
            finally
            {
                // Return any scalar buffers not deferred (error path)
                foreach (var buffer in scalarBuffersToReturn)
                {
                    ReturnPooledScalarBuffer(buffer);
                }
            }
        }

        protected override MemoryBuffer AllocateRawInternal(long length, int elementSize) => new WebGPUMemoryBuffer(this, length, elementSize);
        protected override AcceleratorStream CreateStreamInternal() => new WebGPUStream(this);
        protected override void SynchronizeInternal()
        {
            if (NativeAccelerator.IsDeviceLost)
                throw new InvalidOperationException("WebGPU device has been lost and cannot accept commands.");

            // Flush any pending batched commands on the default stream
            ((WebGPUStream)DefaultStream).Flush();
            // Surface any GPU validation errors that occurred during dispatch
            NativeAccelerator.ThrowIfGpuErrors();
        }
        protected override void OnBind() { }
        protected override void OnUnbind() { }
        protected override void DisposeAccelerator_SyncRoot(bool disposing) { if (disposing) NativeAccelerator.Dispose(); }
        public override TExtension CreateExtension<TExtension, TExtensionProvider>(TExtensionProvider provider) => default;
        protected override PageLockScope<T> CreatePageLockFromPinnedInternal<T>(IntPtr ptr, long numElements) => throw new NotSupportedException();
        protected override int EstimateGroupSizeInternal(Kernel kernel, int dynamicSharedMemorySize, int maxGridSize, out int groupSize) { groupSize = 64; return 64; }
        protected override int EstimateGroupSizeInternal(Kernel kernel, Func<int, int> computeSharedMemorySize, int maxGridSize, out int groupSize) { groupSize = 64; return 64; }
        protected override int EstimateMaxActiveGroupsPerMultiprocessorInternal(Kernel kernel, int groupSize, int dynamicSharedMemorySize) => 1;
        protected override void EnablePeerAccessInternal(Accelerator other) { }
        protected override void DisablePeerAccessInternal(Accelerator other) { }
        protected override bool CanAccessPeerInternal(Accelerator other) => false;

        /// <summary>
        /// Flush any pending batched kernel dispatches to the GPU.
        /// Call this after kernel sequences before consuming results externally.
        /// </summary>
        public void FlushPendingCommands() => ((WebGPUStream)DefaultStream).Flush();

        /// <summary>
        /// Records a <c>clearBuffer</c> command on the given stream's encoder,
        /// zeroing a region of a GPU buffer.  Unlike <c>Queue.WriteBuffer</c>,
        /// this goes through the command encoder pipeline and gets proper
        /// implicit barriers with adjacent compute passes.
        /// </summary>
        internal void RecordClearBuffer(AcceleratorStream stream, GPUBuffer buffer, ulong offset, ulong size)
        {
            var webGpuStream = stream as WebGPUStream ?? (WebGPUStream)DefaultStream;
            var encoder = webGpuStream.GetOrCreateEncoder();
            encoder.ClearBuffer(buffer, offset, size);
        }

        /// <summary>
        /// Wraps an externally-owned <see cref="GPUBuffer"/> as an ILGPU <see cref="ArrayView{T}"/>.
        /// The buffer is NOT owned — it will not be destroyed when the returned view is no longer used.
        /// <para>
        /// Use this to pass an external GPU buffer (e.g. from ONNX Runtime Web) directly to ILGPU kernels
        /// without copying data. Both the external buffer and this accelerator must share the same GPUDevice.
        /// </para>
        /// </summary>
        /// <typeparam name="T">The element type to interpret the buffer as.</typeparam>
        /// <param name="externalBuffer">The externally-owned GPU buffer to wrap.</param>
        /// <param name="elementCount">Number of elements of type <typeparamref name="T"/> in the buffer.</param>
        /// <returns>
        /// An <see cref="ArrayView{T}"/> backed by the external buffer.
        /// The caller must ensure <paramref name="externalBuffer"/> remains valid for the duration of any kernel that uses this view.
        /// Dispose the returned <see cref="ExternalWebGPUMemoryBuffer"/> when done to release the non-owning wrapper.
        /// </returns>
        public (ArrayView<T> View, ExternalWebGPUMemoryBuffer Buffer) WrapExternalBuffer<T>(GPUBuffer externalBuffer, int elementCount)
            where T : unmanaged
        {
            int elementSize = System.Runtime.InteropServices.Marshal.SizeOf<T>();
            var memBuffer = new ExternalWebGPUMemoryBuffer(this, externalBuffer, elementCount, elementSize);
            // Build a typed ArrayView<T> over the full buffer (byte buffer reinterpreted as T)
            var rawView = memBuffer.AsRawArrayView();
            var typedView = rawView.Cast<T>();
            return (typedView, memBuffer);
        }

        /// <summary>
        /// WebGPU stream that batches multiple kernel dispatches into a single command buffer.
        /// Compute passes are accumulated in a shared GPUCommandEncoder and submitted together
        /// when Synchronize() or Flush() is called, matching CUDA's streaming dispatch model.
        /// </summary>
        private class WebGPUStream : AcceleratorStream
        {
            private readonly WebGPUAccelerator _webGpuAccelerator;
            private GPUCommandEncoder? _encoder;
            private readonly List<GPUBindGroup> _pendingBindGroups = new();
            private readonly List<GPUBuffer> _pendingScalarBuffers = new();
            private readonly List<GPUBuffer> _pendingCoalesceBuffers = new();
            private int _pendingPassCount;

            public WebGPUStream(Accelerator acc) : base(acc)
            {
                _webGpuAccelerator = (WebGPUAccelerator)acc;
            }

            /// <summary>
            /// Gets or creates the shared command encoder for this batch.
            /// </summary>
            internal GPUCommandEncoder GetOrCreateEncoder()
            {
                if (_encoder == null)
                {
                    var device = _webGpuAccelerator.NativeAccelerator.NativeDevice!;
                    _encoder = device.CreateCommandEncoder();
                    _pendingPassCount = 0;
                }
                return _encoder;
            }

            /// <summary>
            /// Defer a bind group's disposal until the batch is flushed.
            /// </summary>
            internal void DeferBindGroupDisposal(GPUBindGroup bg) => _pendingBindGroups.Add(bg);

            /// <summary>
            /// Defer a scalar buffer's return to pool until the batch is flushed.
            /// </summary>
            internal void DeferScalarReturn(GPUBuffer buf) => _pendingScalarBuffers.Add(buf);

            /// <summary>
            /// Defer destruction of a coalesced storage buffer until the batch is flushed.
            /// Coalesce buffers are allocated fresh per dispatch (size varies with kernel
            /// parameter shape) and not pooled; we destroy them after the GPU has consumed
            /// the bind group. Skipping the pool keeps memory usage bounded for varied workloads.
            /// </summary>
            internal void DeferCoalesceBufferDestroy(GPUBuffer buf) => _pendingCoalesceBuffers.Add(buf);

            /// <summary>
            /// Flush accumulated compute passes: Finish the command encoder and submit to GPU queue.
            /// After submission, dispose deferred bind groups and return scalar buffers to pool.
            /// </summary>
            public void Flush()
            {
                if (_encoder == null) return;

                if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WebGPU] Flushing batch: {_pendingPassCount} compute passes");

                using var cmd = _encoder.Finish();
                _webGpuAccelerator.NativeAccelerator.Queue!.Submit(new[] { cmd });
                _encoder.Dispose();
                _encoder = null;

                // Clean up deferred resources
                foreach (var bg in _pendingBindGroups)
                    bg.Dispose();
                _pendingBindGroups.Clear();

                foreach (var buf in _pendingScalarBuffers)
                    ReturnPooledScalarBuffer(buf);
                _pendingScalarBuffers.Clear();

                foreach (var buf in _pendingCoalesceBuffers)
                {
                    try { buf.Destroy(); buf.Dispose(); } catch { /* swallow on flush */ }
                }
                _pendingCoalesceBuffers.Clear();

                _pendingPassCount = 0;
            }

            /// <summary>
            /// Track that a compute pass was appended to the current batch.
            /// </summary>
            internal void IncrementPassCount() => _pendingPassCount++;

            public override void Synchronize() => Flush();

            protected override void DisposeAcceleratorObject(bool disposing)
            {
                if (disposing) Flush();
            }

            protected override global::ILGPU.Runtime.ProfilingMarker AddProfilingMarkerInternal() => throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Represents a compiled WebGPU kernel ready for execution.
    /// </summary>
    public class WebGPUKernel : Kernel
    {
        /// <summary>
        /// Creates a new WebGPU kernel instance.
        /// </summary>
        /// <param name="accelerator">The parent accelerator.</param>
        /// <param name="compiledKernel">The compiled kernel.</param>
        /// <param name="launcher">The launcher method info.</param>
        public WebGPUKernel(Accelerator accelerator, CompiledKernel compiledKernel, MethodInfo launcher) : base(accelerator, compiledKernel, launcher) { }

        /// <summary>
        /// Gets the WebGPU-specific compiled kernel.
        /// </summary>
        public new WebGPUCompiledKernel CompiledKernel => (WebGPUCompiledKernel)base.CompiledKernel;

        /// <inheritdoc/>
        protected override void DisposeAcceleratorObject(bool disposing) { }
    }
}