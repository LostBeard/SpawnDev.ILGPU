using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // 2026-10-25 (SpawnDev.Codecs SilkShellCoderTestKernel, found by Geordi): a kernel parameter that is a user struct
    // with ONE view and some ints failed every WebGPU dispatch with "layout expects 5 binding(s), but only 4 entries
    // were built". The WGSL generator classified struct parameters by their flattened field count, so
    // `{ ArrayView<byte> Bytes; int A, B, C, D, E; }` (6 fields) looked like an ArrayView3D and got a stride binding;
    // the dispatcher flattens any non-IArrayView struct with views into its fields and never builds one. With 2 or 3
    // ints the same struct looked like an ArrayView1D or 2D. Fix: a parameter whose .NET type is a value type that is
    // not an IArrayView is a body struct, as the dispatcher binds it.
    public abstract partial class BackendTestBase
    {
        public struct OneViewFiveInts { public ArrayView<byte> Bytes; public int A, B, C, D, E; }
        public struct OneViewTwoInts { public ArrayView<byte> Bytes; public int A, B; }
        public struct OneViewThreeInts { public ArrayView<byte> Bytes; public int A, B, C; }

        static void OneViewFiveIntsKernel(Index1D i, ArrayView<byte> packet, int start, int storage, OneViewFiveInts t, int total, ArrayView<int> outp)
        {
            int k = i;
            outp[k] = packet[start + k] + t.Bytes[t.A + k] * 3 + t.Bytes[t.E] * 5 + t.B * 7 + t.C * 11 + t.D * 13 + storage + total * 17;
        }

        static void OneViewTwoIntsKernel(Index1D i, OneViewTwoInts t, ArrayView<int> outp)
        {
            int k = i;
            outp[k] = t.Bytes[t.A + k] + t.B * 3;
        }

        static void OneViewThreeIntsKernel(Index1D i, OneViewThreeInts t, ArrayView<int> outp)
        {
            int k = i;
            outp[k] = t.Bytes[t.A + k] + t.B * 3 + t.C * 5;
        }

        [TestMethod]
        public async Task StructParam_OneViewAndInts_BindsAndReadsFields() => await RunTest(async accelerator =>
        {
            const int n = 64;
            var packetData = new byte[n + 9];
            var tableData = new byte[200];
            for (int k = 0; k < packetData.Length; k++) packetData[k] = (byte)(k * 7 + 1);
            for (int k = 0; k < tableData.Length; k++) tableData[k] = (byte)(k * 13 + 5);
            using var packet = accelerator.Allocate1D(packetData);
            using var tables = accelerator.Allocate1D(tableData);
            using var outp = accelerator.Allocate1D<int>(n);

            // Five ints (was read as an ArrayView3D)
            var five = new OneViewFiveInts { Bytes = tables.View, A = 3, B = 4, C = 5, D = 6, E = 150 };
            var k5 = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, int, int, OneViewFiveInts, int, ArrayView<int>>(OneViewFiveIntsKernel);
            k5(n, packet.View, 9, 2, five, 1000, outp.View);
            await accelerator.SynchronizeAsync();
            var got = await outp.CopyToHostAsync();
            for (int k = 0; k < n; k++)
            {
                int want = packetData[9 + k] + tableData[3 + k] * 3 + tableData[150] * 5 + 4 * 7 + 5 * 11 + 6 * 13 + 2 + 1000 * 17;
                if (got[k] != want)
                    throw new Exception($"struct {{view, 5 ints}} on {BackendName}, element {k}: got {got[k]}, expected {want}");
            }

            // Two ints (was read as an ArrayView1D)
            var two = new OneViewTwoInts { Bytes = tables.View, A = 11, B = 21 };
            var k2 = accelerator.LoadAutoGroupedStreamKernel<Index1D, OneViewTwoInts, ArrayView<int>>(OneViewTwoIntsKernel);
            k2(n, two, outp.View);
            await accelerator.SynchronizeAsync();
            got = await outp.CopyToHostAsync();
            for (int k = 0; k < n; k++)
                if (got[k] != tableData[11 + k] + 21 * 3)
                    throw new Exception($"struct {{view, 2 ints}} on {BackendName}, element {k}: got {got[k]}, expected {tableData[11 + k] + 21 * 3}");

            // Three ints (was read as an ArrayView2D)
            var three = new OneViewThreeInts { Bytes = tables.View, A = 17, B = 23, C = 29 };
            var k3 = accelerator.LoadAutoGroupedStreamKernel<Index1D, OneViewThreeInts, ArrayView<int>>(OneViewThreeIntsKernel);
            k3(n, three, outp.View);
            await accelerator.SynchronizeAsync();
            got = await outp.CopyToHostAsync();
            for (int k = 0; k < n; k++)
            {
                int want = tableData[17 + k] + 23 * 3 + 29 * 5;
                if (got[k] != want)
                    throw new Exception($"struct {{view, 3 ints}} on {BackendName}, element {k}: got {got[k]}, expected {want}");
            }
            Console.WriteLine($"[StructOneView] structs with one view and 2, 3 or 5 ints bind and read correctly on {BackendName} ✓");
        });
    }
}
