using SpawnDev.SpawnJS.Cryptography;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // The members that need the demos' crypto and WebTorrent services, in a file of their own so a host without them
    // (SpawnDev.ILGPU.NodeTests, headless under Node) compiles every other test file unchanged.
    public abstract partial class BackendTestBase
    {
        /// <summary>Platform-appropriate crypto provider (injected via DI). All crypto goes through this.</summary>
        protected IPortableCrypto Crypto { get; }

        /// <summary>DI-configured WebTorrent client with tracker discovery.</summary>
        protected SpawnDev.WebTorrent.WebTorrentClient WebTorrentClient { get; }

        protected BackendTestBase(IPortableCrypto crypto, SpawnDev.WebTorrent.WebTorrentClient webTorrentClient)
        {
            Crypto = crypto;
            WebTorrentClient = webTorrentClient;
        }

        /// <summary>Creates an Ed25519 signer with a fresh key pair from IPortableCrypto. Each call generates a new identity.</summary>
        protected async Task<SpawnDev.WebTorrent.Ed25519Signer> CreateEd25519Signer()
        {
            var signer = new SpawnDev.WebTorrent.Ed25519Signer(Crypto);
            await signer.GenerateKeyAsync();
            return signer;
        }
    }
}
