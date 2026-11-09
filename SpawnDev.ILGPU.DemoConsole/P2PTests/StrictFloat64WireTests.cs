using SpawnDev.ILGPU.P2P;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.DemoConsole.P2PTests;

/// <summary>
/// Unit tests for the rc.23 strict-f64 wire additions:
///   - <see cref="PeerCapabilities.F64ModesSupported"/> advertisement
///   - <see cref="PeerCapabilities.SupportsStrictFloat64"/> coordinator filter
///   - <see cref="KernelDispatchRequest.F64Mode"/> dispatch field
///
/// These tests are pure object-model checks - no network, no tracker, no real
/// accelerator. They cover the protocol contract that real-WebRTC tests depend on.
/// </summary>
public class StrictFloat64WireTests
{
    [TestMethod]
    public Task PeerCapabilities_NativeF64_SupportsStrictFloat64Returns_True_WithoutModeList()
    {
        // CPU/CUDA/OpenCL/Wasm peers don't need to advertise modes - they're always strict.
        foreach (var nativeBackend in new[] { "CPU", "Cuda", "OpenCL", "Wasm" })
        {
            var caps = new PeerCapabilities { PreferredBackend = nativeBackend };
            if (!caps.SupportsStrictFloat64())
                throw new Exception(
                    $"Native-f64 backend '{nativeBackend}' should report SupportsStrictFloat64()=true " +
                    $"regardless of F64ModesSupported.");
        }
        return Task.CompletedTask;
    }

    // 5.4.19: strict f64 on WebGPU is the Ieee mode (per-operation binary64). Ozaki keeps ~96 bits between operations,
    // so a WebGPU peer that offers only Dekker/Ozaki (pre-5.4.19) cannot give .NET's results.
    [TestMethod]
    public Task PeerCapabilities_WebGPU_IeeeAdvertised_SupportsStrictFloat64()
    {
        var caps = new PeerCapabilities
        {
            PreferredBackend = "WebGPU",
            F64ModesSupported = new[] { "Dekker", "Ozaki", "Ieee" },
        };
        if (!caps.SupportsStrictFloat64())
            throw new Exception(
                "WebGPU peer advertising Ieee should report SupportsStrictFloat64()=true.");
        return Task.CompletedTask;
    }

    [TestMethod]
    public Task PeerCapabilities_WebGPU_OzakiOnly_DoesNotSupportStrict()
    {
        var caps = new PeerCapabilities
        {
            PreferredBackend = "WebGPU",
            F64ModesSupported = new[] { "Dekker", "Ozaki" },
        };
        if (caps.SupportsStrictFloat64())
            throw new Exception(
                "WebGPU peer without Ieee (Ozaki is not per-operation IEEE) must not satisfy strict-f64.");
        return Task.CompletedTask;
    }

    [TestMethod]
    public Task PeerCapabilities_WebGL_NoOzaki_DoesNotSupportStrict()
    {
        // Old-style peer or one configured with a Dekker-only build.
        var caps = new PeerCapabilities
        {
            PreferredBackend = "WebGL",
            F64ModesSupported = new[] { "Dekker" },
        };
        if (caps.SupportsStrictFloat64())
            throw new Exception(
                "WebGL peer without Ieee in F64ModesSupported must not satisfy strict-f64.");
        return Task.CompletedTask;
    }

    [TestMethod]
    public Task PeerCapabilities_WebGPU_NullModeList_DoesNotSupportStrict()
    {
        // Defensive: a peer that didn't advertise at all = treated as no Ozaki support.
        var caps = new PeerCapabilities { PreferredBackend = "WebGPU" };
        if (caps.SupportsStrictFloat64())
            throw new Exception(
                "WebGPU peer with null F64ModesSupported must not satisfy strict-f64 (defensive default).");
        return Task.CompletedTask;
    }

    [TestMethod]
    public Task PeerCapabilities_UnknownBackend_DoesNotSupportStrict()
    {
        var caps = new PeerCapabilities { PreferredBackend = "MysteryBackendV99" };
        if (caps.SupportsStrictFloat64())
            throw new Exception("Unknown backends should default-deny on strict-f64.");
        return Task.CompletedTask;
    }

    [TestMethod]
    public Task KernelDispatchRequest_DefaultF64ModeIsNull()
    {
        // Backwards compat: dispatches that don't set F64Mode preserve rc.10-rc.22 wire shape.
        var request = new KernelDispatchRequest();
        if (request.F64Mode != null)
            throw new Exception($"Expected default F64Mode=null, got '{request.F64Mode}'.");
        return Task.CompletedTask;
    }

    [TestMethod]
    public Task KernelDispatchRequest_RoundTripsOzaki()
    {
        var request = new KernelDispatchRequest { F64Mode = "Ozaki" };
        if (request.F64Mode != "Ozaki")
            throw new Exception("Round-trip of F64Mode='Ozaki' must preserve the value.");
        return Task.CompletedTask;
    }
}
