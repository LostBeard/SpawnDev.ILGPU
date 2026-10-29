using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using System.Runtime.CompilerServices;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A byte / ushort of 0x80 / 0x8000 or more widened to a 64-bit or floating-point value, read where the backend cannot
    // tell from the kernel's parameters that it is unsigned: inside a non-inlined helper, or through a sub-view. Wasm loaded
    // it sign-extended there (`i32.load8_s`) and the unsigned widening then kept the high bits: `(ulong)buf[pos] << shift`
    // in a VP8 bool decoder helper filled its 64-bit window with 0xFF bytes, and SpawnDev.Codecs' VP8 keyframe decoder got
    // nearly every Y pixel wrong on Wasm only (Geordi 2026-10-28). Every backend, against the host.
    public abstract partial class BackendTestBase
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        static ulong SubWordWiden_ByteToULong(ArrayView<byte> b, int at, int shift) => (ulong)b[at] << shift;

        [MethodImpl(MethodImplOptions.NoInlining)]
        static float SubWordWiden_ByteToFloat(ArrayView<byte> b, int at) => b[at];

        [MethodImpl(MethodImplOptions.NoInlining)]
        static long SubWordWiden_UShortToLong(ArrayView<ushort> s, int at) => s[at];

        static void SubWordWideningKernel(Index1D i, ArrayView<byte> b, ArrayView<ushort> s, ArrayView<long> o)
        {
            o[i * 4 + 0] = (long)SubWordWiden_ByteToULong(b, i, 24);
            o[i * 4 + 1] = (long)SubWordWiden_ByteToFloat(b, i);
            o[i * 4 + 2] = SubWordWiden_UShortToLong(s, i);
            var tail = b.SubView(1, b.Length - 1);
            o[i * 4 + 3] = (long)((ulong)tail[i] << 40);
        }

        [TestMethod]
        public async Task SubWordWidening_InHelperAndSubView_IsUnsigned() => await RunTest(async accelerator =>
        {
            const int n = 64;
            var b = new byte[n + 1];
            var s = new ushort[n];
            for (int k = 0; k <= n; k++) b[k] = (byte)(0x7C + k * 3);   // crosses 0x80
            for (int k = 0; k < n; k++) s[k] = (ushort)(0x7FF0 + k * 1031);
            using var dB = accelerator.Allocate1D(b);
            using var dS = accelerator.Allocate1D(s);
            using var dO = accelerator.Allocate1D<long>(n * 4);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<ushort>, ArrayView<long>>(SubWordWideningKernel)(
                (Index1D)n, dB.View, dS.View, dO.View);
            await accelerator.SynchronizeAsync();
            var got = await dO.CopyToHostAsync<long>();
            string[] what = { "(ulong)byte << 24 in a helper", "(float)byte in a helper", "(long)ushort in a helper", "(ulong)byte << 40 through a sub-view" };
            for (int i = 0; i < n; i++)
            {
                var want = new[] { (long)((ulong)b[i] << 24), (long)(float)b[i], (long)s[i], (long)((ulong)b[i + 1] << 40) };
                for (int k = 0; k < 4; k++)
                    if (got[i * 4 + k] != want[k])
                        throw new Exception($"{what[k]}, thread {i}: 0x{got[i * 4 + k]:X}, expected 0x{want[k]:X}");
            }
        });
    }
}
