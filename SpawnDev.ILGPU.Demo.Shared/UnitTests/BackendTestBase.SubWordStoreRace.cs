using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A thread reads an element of a byte (or ushort) buffer while the thread that owns that element stores to it - here
    // the value it already holds. WebGPU keeps sub-word elements packed in atomic<u32> words; its store cleared the
    // element's bits (atomicAnd) and then set them (atomicOr), so for a moment the element read as 0, and a thread reading
    // it then copied 0. Found through SpawnDev.Codecs (Geordi 2026-10-27): the WebP cleanup copies a block's first pixel
    // into the hidden pixels of a run while that pixel's own thread stores it, and got Y = 0 where libwebp has 123.
    // Shapes: the leader is the first element of the reader's own word (neighbours contend on the word) or of another word.
    public abstract partial class BackendTestBase
    {
        static void SubWordRace_CopyLeaderByteKernel(Index1D i, ArrayView<byte> buf, int group)
        {
            int leader = i / group * group;
            buf[i] = buf[leader];
        }

        static void SubWordRace_CopyLeaderUShortKernel(Index1D i, ArrayView<ushort> buf, int group)
        {
            int leader = i / group * group;
            buf[i] = buf[leader];
        }

        [TestMethod]
        public async Task SubWordStore_ReadWhileOwnerStoresSameValue_NeverSeesZero() => await RunTest(async accelerator =>
        {
            const int n = 1 << 18;
            var byteKernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, int>(SubWordRace_CopyLeaderByteKernel);
            var shortKernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<ushort>, int>(SubWordRace_CopyLeaderUShortKernel);
            var rng = new Random(17);
            var failures = new List<string>();
            foreach (int group in new[] { 4, 64 })
            {
                for (int rep = 0; rep < 4; rep++)
                {
                    // Every leader is non-zero, so a 0 anywhere is a torn read
                    var bytes = new byte[n];
                    var shorts = new ushort[n];
                    for (int k = 0; k < n; k++) { bytes[k] = (byte)rng.Next(1, 256); shorts[k] = (ushort)rng.Next(1, 65536); }
                    using var dBytes = accelerator.Allocate1D(bytes);
                    using var dShorts = accelerator.Allocate1D(shorts);
                    byteKernel((Index1D)n, dBytes.View, group);
                    shortKernel((Index1D)n, dShorts.View, group);
                    await accelerator.SynchronizeAsync();
                    var gotBytes = await dBytes.CopyToHostAsync<byte>();
                    var gotShorts = await dShorts.CopyToHostAsync<ushort>();
                    int badBytes = 0, badShorts = 0, firstBad = -1;
                    for (int k = 0; k < n; k++)
                    {
                        int leader = k / group * group;
                        if (gotBytes[k] != bytes[leader]) { badBytes++; if (firstBad < 0) firstBad = k; }
                        if (gotShorts[k] != shorts[leader]) badShorts++;
                    }
                    if (badBytes + badShorts > 0)
                        failures.Add($"group {group} rep {rep}: {badBytes} bytes and {badShorts} ushorts differ from their leader" +
                            (firstBad >= 0 ? $" (byte {firstBad} = {gotBytes[firstBad]}, leader {bytes[firstBad / group * group]})" : ""));
                }
            }
            if (failures.Count > 0) throw new Exception(string.Join("; ", failures));
        });
    }
}
