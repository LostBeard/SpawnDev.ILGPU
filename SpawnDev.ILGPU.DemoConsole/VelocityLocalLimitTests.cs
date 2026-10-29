using System.Runtime.CompilerServices;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Velocity;
using SpawnDev.ILGPU;
using SpawnDev.UnitTesting;

/// <summary>
/// Velocity compiles a kernel to one .NET method with a warp-wide local per IR value, and IL has at most 65536 locals
/// (ldloc takes a 16-bit index). A kernel past that was invalid IL ("Common Language Runtime detected an invalid program"
/// at launch): SpawnDev.Codecs' CELT encoder, one thread per celt_encode_with_ec call, on every Velocity device. Values used
/// only in their own block now share locals (freed after their last use). This kernel is one block of about 114,000 values
/// (AggressiveInlining is not subject to the inlining budget): invalid IL before, equal to the host now. Velocity only: the
/// same kernel as a WGSL / GLSL / PTX shader would be a giant compile for nothing.
/// </summary>
public class VelocityLocalLimitTests
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Mix0(int x, int k) { x ^= x << 5; x += k * 40503; x ^= x >> 7; return x * 3 + k; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Mix1(int x, int k) => Mix0(Mix0(Mix0(Mix0(x, k), k + 1), k + 2), k + 3);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Mix2(int x, int k) => Mix1(Mix1(Mix1(Mix1(x, k), k + 5), k + 9), k + 13);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Mix3(int x, int k) => Mix2(Mix2(Mix2(Mix2(x, k), k + 17), k + 33), k + 49);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Mix4(int x, int k) => Mix3(Mix3(Mix3(Mix3(x, k), k + 65), k + 129), k + 193);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Mix5(int x, int k) => Mix4(Mix4(Mix4(Mix4(x, k), k + 257), k + 513), k + 769);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Mix6(int x, int k) => Mix5(Mix5(Mix5(Mix5(x, k), k + 1025), k + 2049), k + 3073);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Mix7(int x, int k) => Mix6(Mix6(Mix6(Mix6(x, k), k + 4097), k + 8193), k + 12289);

    static void MixKernel(Index1D i, ArrayView<int> a, ArrayView<int> o) => o[i] = Mix7(a[i], 1);

    static async Task MoreValuesThanIlLocals(VelocityDeviceType deviceType)
    {
        using var context = Context.Create(builder => builder.Velocity(deviceType));
        using var accelerator = context.GetVelocityDevice(0).CreateAccelerator(context);
        const int n = 37;
        var a = new int[n];
        for (int k = 0; k < n; k++) a[k] = k * 7919 - 100000;
        using var dA = accelerator.Allocate1D(a);
        using var dO = accelerator.Allocate1D<int>(n);
        accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(MixKernel)(n, dA.View, dO.View);
        await accelerator.SynchronizeAsync();
        var got = await dO.CopyToHostAsync<int>();
        for (int k = 0; k < n; k++)
        {
            int want = Mix7(a[k], 1);
            if (got[k] != want) throw new Exception($"{deviceType}: thread {k} = {got[k]}, expected {want}");
        }
    }

    [TestMethod]
    public Task Velocity_KernelWithMoreValuesThanIlLocals_MatchesHost_Vector128() => MoreValuesThanIlLocals(VelocityDeviceType.Vector128);

    [TestMethod]
    public Task Velocity_KernelWithMoreValuesThanIlLocals_MatchesHost_Scalar2() => MoreValuesThanIlLocals(VelocityDeviceType.Scalar2);
}
