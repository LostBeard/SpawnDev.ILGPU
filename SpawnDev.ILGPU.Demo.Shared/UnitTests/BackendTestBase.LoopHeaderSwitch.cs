using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A do-while loop whose body starts with an inlined switch helper, so the loop header ends in a switch, against the
    // host. The WGSL structured walker emitted a loop header's body only for an if or unconditional terminator: for a
    // switch it emitted the header's own values and dropped the switch and the rest of the body, leaving `loop { }` with
    // no exit. Tint rejects that ("loop does not exit"), so the kernel never compiled on WebGPU (naga accepts it). Found
    // through SpawnDev.Codecs: libopus icwrs (`do { ...; i += CELT_PVQ_U(n - j, k); ... } while (j > 0);`, the row start
    // a switch) in the CELT encoder's band kernel.
    public abstract partial class BackendTestBase
    {
        static int LoopHeaderSwitchRow(int r)
        {
            switch (r)
            {
                case 0: return 0;
                case 1: return 176;
                case 2: return 351;
                case 3: return 525;
                case 4: return 698;
                case 5: return 870;
                case 6: return 1041;
                default: return 1257;
            }
        }

        static int LoopHeaderSwitchHost(int[] y, int at, int n)
        {
            int j = n - 1;
            int acc = y[at + j] < 0 ? 1 : 0;
            int k = Math.Abs(y[at + j]);
            do
            {
                j--;
                acc += LoopHeaderSwitchRow(Math.Min(n - j, k)) + Math.Max(n - j, k);
                k += Math.Abs(y[at + j]);
                if (y[at + j] < 0) acc += LoopHeaderSwitchRow(Math.Min(n - j, k + 1)) + 3;
            }
            while (j > 0);
            return acc;
        }

        static void LoopHeaderSwitchKernel(Index1D t, ArrayView<int> y, ArrayView<int> o)
        {
            int n = 6, at = t * 8;
            int j = n - 1;
            int acc = y[at + j] < 0 ? 1 : 0;
            int k = Math.Abs(y[at + j]);
            do
            {
                j--;
                acc += LoopHeaderSwitchRow(Math.Min(n - j, k)) + Math.Max(n - j, k);
                k += Math.Abs(y[at + j]);
                if (y[at + j] < 0) acc += LoopHeaderSwitchRow(Math.Min(n - j, k + 1)) + 3;
            }
            while (j > 0);
            o[t] = acc;
        }

        [TestMethod]
        public async Task LoopHeaderSwitch_DoWhileStartingWithASwitch_MatchesHost() => await RunTest(async accelerator =>
        {
            const int n = 32;
            var y = new int[n * 8];
            var rng = new Random(5);
            for (int i = 0; i < y.Length; i++) y[i] = rng.Next(-3, 4);
            using var dY = accelerator.Allocate1D(y);
            using var dO = accelerator.Allocate1D<int>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(LoopHeaderSwitchKernel)((Index1D)n, dY.View, dO.View);
            await accelerator.SynchronizeAsync();
            var got = await dO.CopyToHostAsync<int>();
            for (int t = 0; t < n; t++)
            {
                int want = LoopHeaderSwitchHost(y, t * 8, 6);
                if (got[t] != want)
                    throw new Exception($"thread {t}: {got[t]}, expected {want}");
            }
        });
    }
}
