using System;
using System.Collections.Generic;
using System.Linq;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.OpenCL;

namespace SpawnDev.ILGPU;

/// <summary>
/// Desktop device ranking that picks the right GPU on hybrid (integrated + discrete) machines.
/// </summary>
/// <remarks>
/// ⚠️ Do NOT rank by <see cref="Device.MemorySize"/> alone (what <see cref="Context.GetPreferredDevice"/> does).
/// MEASURED 2026-10-01 on a laptop with an Intel HD 620 + NVIDIA 940MX: ILGPU enumerated
/// OpenCL "Intel HD Graphics 620" (3233 MB - shared system RAM), OpenCL "Intel Core i7-7500U CPU" (8102 MB -
/// Intel's OpenCL CPU runtime, which is AcceleratorType.OpenCL, not CPU) and CUDA "GeForce 940MX" (2048 MB).
/// GetPreferredDevice(preferCPU: false) picked the OpenCL CPU, and "first non-CPU device" picked the integrated
/// GPU - the discrete GPU came last both ways. Integrated GPUs and OpenCL CPU runtimes report system RAM as
/// device memory, so memory size is only meaningful as a tie-breaker between devices of the same kind.
/// </remarks>
public static class DevicePreference
{
    // cl_device_info CL_DEVICE_HOST_UNIFIED_MEMORY (cl_bool). Not in CLDeviceInfoType; deprecated in OpenCL 2.0 but
    // still answered by the Intel, AMD and NVIDIA runtimes. True for integrated GPUs (memory shared with the host).
    private const CLDeviceInfoType CL_DEVICE_HOST_UNIFIED_MEMORY = (CLDeviceInfoType)0x1035;

    /// <summary>
    /// Returns the devices ordered best-first:
    /// CUDA, then discrete OpenCL GPUs, then integrated OpenCL GPUs, then other OpenCL devices (CPU runtimes,
    /// accelerators), then any other non-CPU device, then CPU. Ties are broken by memory size (largest first),
    /// then by the original order.
    /// </summary>
    public static IReadOnlyList<Device> OrderByPreference(this IEnumerable<Device> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        return devices
            .Select((d, i) => (Device: d, Index: i))
            .OrderBy(x => Rank(x.Device))
            .ThenByDescending(x => x.Device.MemorySize)
            .ThenBy(x => x.Index)
            .Select(x => x.Device)
            .ToList();
    }

    /// <summary>
    /// Returns the best device on the context per <see cref="OrderByPreference"/>.
    /// Use this instead of <see cref="Context.GetPreferredDevice"/> on desktop.
    /// </summary>
    public static Device GetBestDevice(this Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Devices.OrderByPreference().FirstOrDefault()
            ?? throw new NotSupportedException("The context has no devices.");
    }

    private static int Rank(Device device)
    {
        switch (device.AcceleratorType)
        {
            case AcceleratorType.Cuda:
                return 0;
            case AcceleratorType.OpenCL when device is CLDevice cl:
                if (!cl.DeviceType.HasFlag(CLDeviceType.CL_DEVICE_TYPE_GPU)) return 3;
                return IsIntegrated(cl) ? 2 : 1;
            case AcceleratorType.OpenCL:
                return 3;
            case AcceleratorType.CPU:
                return 5;
            default:
                return 4;
        }
    }

    private static bool IsIntegrated(CLDevice device)
    {
        if (CLAPI.CurrentAPI.GetDeviceInfo(device.DeviceId, CL_DEVICE_HOST_UNIFIED_MEMORY, out int unified) == CLError.CL_SUCCESS)
            return unified != 0;
        // Query unsupported: Intel GPUs are almost always integrated (Arc is the exception, and still ranks above
        // OpenCL CPU runtimes).
        return device.Vendor == CLDeviceVendor.Intel;
    }
}
