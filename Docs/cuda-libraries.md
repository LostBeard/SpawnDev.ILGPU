# CUDA Libraries

SpawnDev.ILGPU includes wrappers for several NVIDIA CUDA libraries. These provide hardware-accelerated functionality on NVIDIA GPUs that goes beyond kernel compute — image encoding/decoding, random number generation, linear algebra, FFT, and device monitoring.

> **CUDA-only.** These libraries require an NVIDIA GPU with CUDA drivers. They are not available on browser backends (WebGPU, WebGL, Wasm) or non-NVIDIA devices (OpenCL, CPU).

All libraries are enabled automatically when you call `builder.AllAcceleratorsAsync()` during context creation. They detect the installed NVIDIA library version at runtime and use the newest available.

### Availability Detection

Each library provides a static `IsAvailable` property to check if the required native library is installed:

```csharp
if (NvJpegAPI.IsAvailable)    Console.WriteLine("nvJPEG ready");
if (CuRandAPI.IsAvailable)    Console.WriteLine("cuRand ready");
if (CuBlasAPI.IsAvailable)    Console.WriteLine("cuBLAS ready");
if (CuFFTAPI.IsAvailable)     Console.WriteLine("cuFFT ready");
if (NvmlAPI.IsAvailable)      Console.WriteLine("NVML ready");
```

### Installation Requirements

| Library | Included With | Separate Install Required |
|---------|--------------|--------------------------|
| **cuRand** | NVIDIA Driver | No — included in driver runtime |
| **cuBLAS** | NVIDIA Driver | No — included in driver runtime |
| **cuFFT** | CUDA Toolkit | Yes — [CUDA Toolkit](https://developer.nvidia.com/cuda-toolkit) |
| **nvJPEG** | Separate | Yes — [nvJPEG](https://developer.nvidia.com/nvjpeg) (removed from CUDA Toolkit in CUDA 13.x) |
| **NVML** | NVIDIA Driver | No — included in driver |

> **Note:** Starting with CUDA 13.x, nvJPEG is no longer bundled with the CUDA Toolkit. It must be downloaded and installed separately from NVIDIA's website.

---

## nvJPEG — JPEG Encode & Decode

Hardware-accelerated JPEG encoding and decoding using NVIDIA's nvJPEG library.

**Namespace:** `ILGPU.Runtime.Cuda`
**API Namespace:** `ILGPU.Runtime.Cuda.API`
**Supported Versions:** V11, V12

### Quick Start

```csharp
using ILGPU.Runtime.Cuda;
using ILGPU.Runtime.Cuda.API;

// Initialize
var nvjpeg = new NvJpeg();
using var library = nvjpeg.CreateSimple();
```

### Decoding a JPEG

```csharp
byte[] jpegBytes = File.ReadAllBytes("image.jpg");

// Get image info
library.GetImageInfo(jpegBytes, out int numComponents,
    out NvJpegChromaSubsampling subsampling, out int[] widths, out int[] heights);

// Allocate GPU buffers for decoded image
var image = NvJpegImage.Create(cudaAccelerator, widths[0], heights[0], numComponents);

// Decode
using var state = library.CreateState();
NvJpegException.ThrowIfFailed(
    library.Decode(state, jpegBytes, NvJpegOutputFormat.NVJPEG_OUTPUT_RGB, image));

accelerator.Synchronize();

// Read back pixel data
var redPixels = new byte[widths[0] * heights[0]];
image.Channel[0]!.View.CopyToCPU(redPixels);
```

### Encoding to JPEG

```csharp
// Create encoder state and parameters
using var encoderState = library.CreateEncoderState();
using var encoderParams = library.CreateEncoderParams();

// Configure encoding
NvJpegException.ThrowIfFailed(library.EncoderParamsSetQuality(encoderParams, 95));
NvJpegException.ThrowIfFailed(library.EncoderParamsSetSamplingFactors(
    encoderParams, NvJpegChromaSubsampling.NVJPEG_CSS_444));

// Optional: Set encoding type and optimized Huffman
NvJpegException.ThrowIfFailed(library.EncoderParamsSetEncoding(
    encoderParams, NvJpegJpegEncoding.NVJPEG_ENCODING_BASELINE_DCT));
NvJpegException.ThrowIfFailed(library.EncoderParamsSetOptimizedHuffman(encoderParams, 1));

// Encode (sourceImage is an NvJpegImage with GPU buffers)
NvJpegException.ThrowIfFailed(
    library.EncodeImage(encoderState, encoderParams, sourceImage,
        NvJpegInputFormat.NVJPEG_INPUT_RGB, width, height));

// Retrieve bitstream size
NvJpegException.ThrowIfFailed(
    library.EncodeRetrieveBitstream(encoderState, Span<byte>.Empty, out ulong size));

// Retrieve actual JPEG bytes
var jpegOutput = new byte[size];
NvJpegException.ThrowIfFailed(
    library.EncodeRetrieveBitstream(encoderState, jpegOutput.AsSpan(), out _));
```

### Classes

| Class | Description |
|-------|-------------|
| `NvJpeg` | Entry point. Detects library version, provides `CreateSimple()` for library handle. |
| `NvJpegLibrary` | High-level wrapper. Decode, encode, state/params management. |
| `NvJpegState` | Decoder state handle (disposable). |
| `NvJpegEncoderState` | Encoder state handle (disposable). |
| `NvJpegEncoderParams` | Encoder parameters handle (disposable). |

### Enums

| Enum | Values |
|------|--------|
| `NvJpegOutputFormat` | `NVJPEG_OUTPUT_UNCHANGED`, `_YUV`, `_Y`, `_RGB`, `_BGR`, `_RGBI`, `_BGRI` |
| `NvJpegInputFormat` | `NVJPEG_INPUT_YUV` (1), `_RGB` (3), `_BGR` (4), `_RGBI` (5), `_BGRI` (6), `_NV12` (8) |
| `NvJpegChromaSubsampling` | `NVJPEG_CSS_444`, `_422`, `_420`, `_440`, `_411`, `_410`, `_GRAY`, `_UNKNOWN` |
| `NvJpegJpegEncoding` | `NVJPEG_ENCODING_UNKNOWN`, `_BASELINE_DCT` (0xC0), `_EXTENDED_SEQUENTIAL_DCT_HUFFMAN` (0xC1), `_PROGRESSIVE_DCT_HUFFMAN` (0xC2), `_LOSSLESS_HUFFMAN` (0xC3) |
| `NvJpegStatus` | Status codes for error handling via `NvJpegException.ThrowIfFailed()` |

### Key Methods (NvJpegLibrary)

| Method | Description |
|--------|-------------|
| `CreateState()` | Create decoder state |
| `CreateEncoderState(stream?)` | Create encoder state |
| `CreateEncoderParams(stream?)` | Create encoder parameters |
| `GetImageInfo(bytes, ...)` | Get JPEG dimensions, components, subsampling |
| `Decode(state, bytes, format, image, stream?)` | Decode JPEG to GPU buffers |
| `EncodeImage(state, params, image, format, w, h, stream?)` | Encode from planar RGB/BGR/etc. |
| `EncodeYUV(state, params, image, subsampling, w, h, stream?)` | Encode from YUV |
| `EncoderParamsSetQuality(params, quality)` | Set JPEG quality (1-100) |
| `EncoderParamsSetSamplingFactors(params, subsampling)` | Set chroma subsampling |
| `EncoderParamsSetEncoding(params, encoding)` | Set encoding type (baseline, progressive, etc.) |
| `EncoderParamsSetOptimizedHuffman(params, enabled)` | Enable optimized Huffman (smaller files) |
| `EncodeRetrieveBitstream(state, data, out length)` | Retrieve encoded JPEG bytes |

> **Two-call pattern for EncodeRetrieveBitstream:** Call once with `Span<byte>.Empty` to get the size, then again with an allocated buffer.

---

## cuRand — Random Number Generation

Hardware-accelerated random number generation on NVIDIA GPUs and CPU.

**Namespace:** `ILGPU.Runtime.Cuda`
**API Namespace:** `ILGPU.Runtime.Cuda.API`
**Supported Versions:** V9, V10, V11, V12

### Quick Start (GPU)

```csharp
using ILGPU.Runtime.Cuda;

// Create GPU random generator
using var rand = CuRand.CreateGPU(cudaAccelerator, CuRandRngType.CURAND_RNG_PSEUDO_DEFAULT);
rand.SetSeed(42L);

// Fill buffer with uniform floats [0, 1)
using var buf = accelerator.Allocate1D<float>(4096);
rand.FillUniform(accelerator.DefaultStream, buf.View);
accelerator.Synchronize();
```

### Quick Start (CPU)

```csharp
using var rand = CuRand.CreateCPU(context, CuRandRngType.CURAND_RNG_PSEUDO_DEFAULT);

var data = new float[4096];
rand.FillUniform(data.AsSpan());
```

### Distributions

| Method (GPU) | Method (CPU) | Description |
|-------------|-------------|-------------|
| `FillUniform(stream, ArrayView<float>)` | `FillUniform(Span<float>)` | Uniform floats in [0, 1) |
| `FillUniform(stream, ArrayView<double>)` | `FillUniform(Span<double>)` | Uniform doubles in [0, 1) |
| `FillUniform(stream, ArrayView<int>)` | `FillUniform(Span<int>)` | Positive random integers |
| `FillUniform(stream, ArrayView<uint>)` | `FillUniform(Span<uint>)` | Random unsigned integers |
| `FillUniform(stream, ArrayView<long>)` | `FillUniform(Span<long>)` | Positive random longs |
| `FillUniform(stream, ArrayView<ulong>)` | `FillUniform(Span<ulong>)` | Random unsigned longs |
| `FillNormal(stream, view, mean, stddev)` | `FillNormal(span, mean, stddev)` | Normal (Gaussian) distribution |
| `FillLogNormal(stream, view, mean, stddev)` | `FillLogNormal(span, mean, stddev)` | Log-normal distribution (always positive) |
| `FillPoisson(stream, ArrayView<uint>, lambda)` | `FillPoisson(Span<uint>, lambda)` | Poisson distribution |

### Configuration

| Method | Description |
|--------|-------------|
| `SetSeed(long)` | Set the RNG seed for reproducibility |
| `GenerateRandomSeeds()` | Randomize the seed from hardware entropy |
| `SetOffset(ulong)` | Set generator offset (skip ahead in sequence) |
| `SetOrdering(CuRandOrdering)` | Set generation ordering |
| `SetQuasiRandomDimensions(uint)` | Set dimensions for quasi-random generators |

### Generator Types

| Type | Description |
|------|-------------|
| `CURAND_RNG_PSEUDO_DEFAULT` | Default pseudo-random (XORWOW) |
| `CURAND_RNG_PSEUDO_XORWOW` | XORWOW generator |
| `CURAND_RNG_PSEUDO_MRG32K3A` | MRG32k3a (good for parallel streams) |
| `CURAND_RNG_PSEUDO_MTGP32` | Mersenne Twister for GPU |
| `CURAND_RNG_PSEUDO_MT19937` | Classic Mersenne Twister |
| `CURAND_RNG_PSEUDO_PHILOX4_32_10` | Philox (fast, counter-based) |
| `CURAND_RNG_QUASI_SOBOL32` | Sobol quasi-random (low-discrepancy) |
| `CURAND_RNG_QUASI_SCRAMBLED_SOBOL32` | Scrambled Sobol |
| `CURAND_RNG_QUASI_SOBOL64` | 64-bit Sobol |
| `CURAND_RNG_QUASI_SCRAMBLED_SOBOL64` | 64-bit scrambled Sobol |

### Ordering Types

| Ordering | Description |
|----------|-------------|
| `CURAND_ORDERING_PSEUDO_BEST` | Best quality ordering |
| `CURAND_ORDERING_PSEUDO_DEFAULT` | Default ordering |
| `CURAND_ORDERING_PSEUDO_SEEDED` | Seeded ordering |
| `CURAND_ORDERING_PSEUDO_LEGACY` | Legacy compatibility |
| `CURAND_ORDERING_PSEUDO_DYNAMIC` | Dynamic ordering |
| `CURAND_ORDERING_QUASI_DEFAULT` | Default quasi-random ordering |

---

## cuBLAS — Linear Algebra

Hardware-accelerated BLAS (Basic Linear Algebra Subprograms) operations on NVIDIA GPUs.

**Namespace:** `ILGPU.Runtime.Cuda`
**API Namespace:** `ILGPU.Runtime.Cuda.API`
**Supported Versions:** V10, V11, V12

### Quick Start

```csharp
using ILGPU.Runtime.Cuda;

var cublas = new CuBlas(cudaAccelerator);   // non-generic: defaults to ManualMode pointer-mode management
```

### BLAS Levels

| Level | Description | Examples |
|-------|-------------|----------|
| **Level 1** | Vector-vector operations | AXPY, DOT, NRM2, SCAL, AMAX, AMIN, ASUM, COPY, SWAP, ROT |
| **Level 2** | Matrix-vector operations | GEMV, SYMV, TRMV, TRSV, GER, SYR, SPR |
| **Level 3** | Matrix-matrix operations | GEMM, SYMM, TRMM, TRSM, SYRK, SYR2K |

### Pointer Modes

cuBLAS reads scalar parameters (alpha, beta) from either host or device memory. The runtime `CuBlasPointerMode` enum selects which:

| `CuBlasPointerMode` | Description |
|---------------------|-------------|
| `Host` | Scalars on CPU (default, most convenient) |
| `Device` | Scalars on GPU (avoids CPU-GPU sync for chained operations) |

*How* the pointer mode is managed is chosen by the `CuBlas<TPointerModeHandler>` type parameter — a struct implementing `ICuBlasPointerModeHandler<T>`, from `CuBlasPointerModeHandlers`:

| Handler | Behavior |
|---------|----------|
| `CuBlasPointerModeHandlers.ManualMode` | You set `cublas.PointerMode` yourself (the non-generic `CuBlas` uses this) |
| `CuBlasPointerModeHandlers.AutomaticMode` | The wrapper switches pointer mode per call as needed |

### Configuration

| Property | Description |
|----------|-------------|
| `Stream` | Gets/sets the CUDA stream for async execution |
| `AtomicsMode` | Gets/sets atomic mode for deterministic results |
| `MathMode` | Gets/sets math mode (TF32 acceleration, etc.) |

---

## cuFFT — Fast Fourier Transform

Hardware-accelerated FFT on NVIDIA GPUs with full FFTW compatibility layer.

**Namespace:** `ILGPU.Runtime.Cuda`
**API Namespace:** `ILGPU.Runtime.Cuda.API`
**Supported Versions:** V10, V11, V12

### Capabilities

| Feature | Description |
|---------|-------------|
| **Dimensions** | 1D, 2D, 3D transforms |
| **Precision** | Single (float) and double precision |
| **Transform types** | C2C, R2C, C2R, Z2Z, D2Z, Z2D |
| **Plans** | Basic, extensible, and batched (PlanMany) |
| **Work area** | Auto or caller-allocated |
| **Streaming** | Async execution via CUDA streams |

### FFTW Compatibility

The cuFFTW wrapper provides a drop-in replacement for FFTW. Functions like `fftw_plan_dft_1d`, `fftwf_execute`, etc. are available with the same calling conventions as FFTW, but execute on the GPU.

---

## NVML — Device Monitoring

NVIDIA Management Library for GPU monitoring, temperature, clock, and power queries.

**Namespace:** `ILGPU.Runtime.Cuda`
**API Namespace:** `ILGPU.Runtime.Cuda.API`

### Capabilities

NVML provides 113+ query functions for comprehensive GPU monitoring:

| Category | Examples |
|----------|---------|
| **Clocks** | Core clock, memory clock, SM clock |
| **Temperature** | GPU temperature, thermal thresholds |
| **Power** | Power usage, power limit, power state |
| **Memory** | Total, used, free memory |
| **ECC** | Error counts, ECC mode |
| **Performance** | P-states, utilization, throttle reasons |
| **Fan** | Fan speed, fan count |
| **PCIe** | Link width, link speed, throughput |
| **Compute** | Compute mode, process info |
| **Driver** | Driver version, CUDA version |

---

## Error Handling

All CUDA libraries use a consistent error handling pattern via exception classes:

```csharp
// Throws if status != SUCCESS
NvJpegException.ThrowIfFailed(status);
CuRandException.ThrowIfFailed(status);
CuBlasException.ThrowIfFailed(status);
CuFFTException.ThrowIfFailed(status);
NvmlException.ThrowIfFailed(status);
```

Each exception carries the native error code in its `Error` property for diagnostics.

---

## Library Version Detection

All libraries auto-detect the installed NVIDIA library version. When using `Create()` without a version argument, the wrapper tries versions from newest to oldest and uses the first one that loads successfully.

```csharp
// Auto-detect best version
var nvjpeg = new NvJpeg();                    // Tries V12, falls back to V11
var api = CuRandAPI.Create(null);             // Tries V12, V11, V10, V9

// Explicit version
var nvjpeg = new NvJpeg(NvJpegAPIVersion.V12);
var api = CuRandAPI.Create(CuRandAPIVersion.V12);
```

Supported platform/version matrix:

| Library | Windows | Linux | macOS |
|---------|---------|-------|-------|
| nvJPEG | V11, V12 | V11, V12 | - |
| cuRand | V9-V12 | V9-V12 | V9, V10 |
| cuBLAS | V10-V12 | V10-V12 | V10 |
| cuFFT | V10-V12 | V10-V12 | V10 |
| NVML | V6 | V6 | V6 |
