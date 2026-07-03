// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.WebGPU
//                 WebGPU Compute Library for Blazor WebAssembly
//
// File: WebGPUComputeShader.cs
// ---------------------------------------------------------------------------------------

using SpawnDev.BlazorJS.JSObjects;

namespace SpawnDev.ILGPU.WebGPU
{
    /// <summary>
    /// Represents a WebGPU compute shader.
    /// </summary>
    public sealed class WebGPUComputeShader : IDisposable
    {
        #region Instance

        private GPUShaderModule? _shaderModule;
        private GPUComputePipeline? _pipeline;
        private GPUBindGroup? _bindGroup;
        private GPUBindGroupLayout? _bindGroupLayout;
        private readonly List<WebGPUBufferBinding> _bindings = new();
        private bool _disposed;

        /// <summary>
        /// Constructs a new compute shader from WGSL source.
        /// </summary>
        internal WebGPUComputeShader(
            WebGPUNativeAccelerator accelerator,
            string wgslSource,
            string entryPoint,
            Dictionary<string, object>? overrideConstants = null,
            string? label = null)
        {
            Accelerator = accelerator ?? throw new ArgumentNullException(nameof(accelerator));
            WGSLSource = wgslSource ?? throw new ArgumentNullException(nameof(wgslSource));
            EntryPoint = entryPoint ?? throw new ArgumentNullException(nameof(entryPoint));

            var device = accelerator.NativeDevice;
            if (device == null)
                throw new InvalidOperationException("GPU device not initialized");

            // Create shader module from WGSL source
            accelerator._lastCompiledWGSL = wgslSource;
            // DEBUG: Store WGSL in a JS global for browser-side inspection (only when verbose logging is on)
            if (Backend.WebGPUBackend.VerboseLogging)
            {
                try { BlazorJS.BlazorJSRuntime.JS.Set("wgslDebug", wgslSource); } catch { }
            }
            // Push validation error scope BEFORE shader/pipeline creation to capture both
            device.PushErrorScope(GPUErrorFilter.Validation);

            var shaderDescriptor = new GPUShaderModuleDescriptor
            {
                Code = wgslSource
            };
            _shaderModule = device.CreateShaderModule(shaderDescriptor);

            // Create compute pipeline with optional override constants
            var programmableStage = new GPUProgrammableStage
            {
                Module = _shaderModule,
                EntryPoint = entryPoint,
                Constants = overrideConstants
            };

            var pipelineDescriptor = new GPUComputePipelineDescriptor
            {
                Layout = "auto",
                Compute = programmableStage,
                // Kernel name as the pipeline label: names the pipeline in GPUError messages /
                // console warnings AND is what the dispatch-plan timed replay aggregates GPU
                // time by (webgpuDispatchPlan.js replayTimed reads pipeline.label).
                Label = label ?? entryPoint
            };
            _pipeline = device.CreateComputePipeline(pipelineDescriptor);

            // Get bind group layout
            _bindGroupLayout = _pipeline.GetBindGroupLayout(0);

            // Background shader validation. Errors fed into _pendingGpuErrors via AddShaderError
            // so ThrowIfGpuErrors() surfaces them on the NEXT Synchronize. Track the Task so
            // SynchronizeAsync can await it before calling ThrowIfGpuErrors — otherwise the
            // CHECK may not have completed by the time the caller's sync runs, and the queued
            // error leaks into a later dispatch's sync (the rc.13 ILGPUReduceHalfTest leak).
            var capturedModule = _shaderModule;
            accelerator.TrackShaderCheck(CheckShaderAsync(capturedModule, entryPoint, device, accelerator));
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the parent accelerator.
        /// </summary>
        public WebGPUNativeAccelerator Accelerator { get; }

        /// <summary>
        /// Returns the WGSL source code.
        /// </summary>
        public string WGSLSource { get; }

        /// <summary>
        /// Returns the entry point function name.
        /// </summary>
        public string EntryPoint { get; }

        /// <summary>
        /// Returns the shader module.
        /// </summary>
        public GPUShaderModule? ShaderModule => _shaderModule;

        /// <summary>
        /// Returns the compute pipeline.
        /// </summary>
        public GPUComputePipeline? Pipeline => _pipeline;

        /// <summary>
        /// Returns the current bind group.
        /// </summary>
        public GPUBindGroup? BindGroup => _bindGroup;

        /// <summary>
        /// Returns the cached bind group layout (fetched once at construction).
        /// </summary>
        public GPUBindGroupLayout? BindGroupLayout => _bindGroupLayout;

        #endregion

        #region Methods

        /// <summary>
        /// Binds a buffer to the specified binding index.
        /// </summary>
        public WebGPUComputeShader SetBuffer<T>(int bindingIndex, WebGPUBuffer<T> buffer) where T : unmanaged
        {
            if (buffer?.NativeBuffer == null)
                throw new ArgumentNullException(nameof(buffer));

            // Remove any existing binding at this index and insert sorted
            for (int i = _bindings.Count - 1; i >= 0; i--)
            {
                if (_bindings[i].Index == bindingIndex)
                {
                    _bindings.RemoveAt(i);
                    break; // indices are unique
                }
            }

            // Insert in sorted order by index
            var newBinding = new WebGPUBufferBinding(bindingIndex, buffer.NativeBuffer);
            int insertAt = _bindings.BinarySearch(newBinding);
            if (insertAt < 0) insertAt = ~insertAt;
            _bindings.Insert(insertAt, newBinding);

            // Rebuild bind group
            RebuildBindGroup();

            return this;
        }

        /// <summary>
        /// Dispatches the compute shader.
        /// </summary>
        public void Dispatch(uint workgroupCountX, uint workgroupCountY = 1, uint workgroupCountZ = 1)
        {
            Accelerator.Dispatch(this, workgroupCountX, workgroupCountY, workgroupCountZ);
        }

        private static async Task CheckShaderAsync(GPUShaderModule shaderModule, string entryPoint, GPUDevice device, WebGPUNativeAccelerator accelerator)
        {
            try
            {
                // Always pop the error scope - leaving it open leaks and masks subsequent errors
                using var error = await device.PopErrorScope();
                if (error != null)
                {
                    // ALWAYS build the error message and feed into pending errors for hard failure.
                    // Invalid shader = throw on next sync. No silent zeros.
                    var errorMsg = $"[WebGPU] Shader '{entryPoint}' failed validation: {error.Message}";
                    if (Backend.WebGPUBackend.VerboseLogging)
                        Backend.WebGPUBackend.Log(errorMsg);

                    // Get detailed compilation info for the error message
                    try
                    {
                        using var info = await shaderModule.GetCompilationInfo();
                        foreach (var msg in info.Messages)
                        {
                            if (msg.Type == "error")
                            {
                                errorMsg += $"\n  L{msg.LineNum}:{msg.LinePos} - {msg.Message}";
                                if (Backend.WebGPUBackend.VerboseLogging)
                                    Backend.WebGPUBackend.Log($"[WGSL-ERROR] {entryPoint} L{msg.LineNum}:{msg.LinePos} - {msg.Message}");
                            }
                        }
                    }
                    catch { }

                    // Feed into pending errors so ThrowIfGpuErrors() surfaces the REAL error
                    accelerator.AddShaderError(errorMsg);
                }
                else if (Backend.WebGPUBackend.VerboseLogging)
                {
                    // No validation error - still check for warnings if verbose
                    using var info = await shaderModule.GetCompilationInfo();
                    foreach (var msg in info.Messages)
                    {
                        if (msg.Type == "warning")
                            Backend.WebGPUBackend.Log($"[WGSL-WARNING] {entryPoint} L{msg.LineNum}:{msg.LinePos} - {msg.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                // Don't swallow - feed the exception into pending errors
                var msg = $"[WebGPU] Shader check failed for '{entryPoint}': {ex.Message}";
                if (Backend.WebGPUBackend.VerboseLogging)
                    Backend.WebGPUBackend.Log(msg);
                accelerator.AddShaderError(msg);
            }
        }

        private void RebuildBindGroup()
        {
            var device = Accelerator.NativeDevice;
            if (device == null || _bindGroupLayout == null)
                return;

            // Dispose old bind group
            _bindGroup?.Dispose();

            // Create new bind group (bindings are already sorted by index)
            var entries = new GPUBindGroupEntry[_bindings.Count];
            for (int i = 0; i < _bindings.Count; i++)
            {
                entries[i] = new GPUBindGroupEntry
                {
                    Binding = (uint)_bindings[i].Index,
                    Resource = new GPUBufferBinding { Buffer = _bindings[i].Buffer }
                };
            }

            var descriptor = new GPUBindGroupDescriptor
            {
                Layout = _bindGroupLayout,
                Entries = entries
            };

            _bindGroup = device.CreateBindGroup(descriptor);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _bindGroup?.Dispose();
            _pipeline?.Dispose();
            _shaderModule?.Dispose();
            _bindGroupLayout?.Dispose();
        }

        #endregion

        #region Nested Types

        private readonly struct WebGPUBufferBinding : IComparable<WebGPUBufferBinding>
        {
            public WebGPUBufferBinding(int index, GPUBuffer buffer)
            {
                Index = index;
                Buffer = buffer;
            }

            public int Index { get; }
            public GPUBuffer Buffer { get; }

            public int CompareTo(WebGPUBufferBinding other) => Index.CompareTo(other.Index);
        }

        #endregion
    }
}
