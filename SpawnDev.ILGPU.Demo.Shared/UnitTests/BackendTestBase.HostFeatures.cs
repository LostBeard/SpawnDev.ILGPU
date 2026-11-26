using SpawnDev.SpawnJS;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // The browser backends also run outside a page: a .NET WebAssembly console app under Node (SpawnDev.ILGPU.NodeTests)
    // has WebGPU and an event loop but no DOM, no canvas and no video frames. A test that needs one of those must SKIP
    // there with the reason, not fail: a failure has to mean that something which should work did not (TJ, 2026-11-26).
    // Check the feature itself, never "am I in Node": a browser without the feature skips for the same honest reason.
    public abstract partial class BackendTestBase
    {
        /// <summary>
        /// Skips the test (<see cref="UnsupportedTestException"/>) when the JS global <paramref name="name"/> (a constructor
        /// such as <c>OffscreenCanvas</c>, or an object such as <c>document</c>) does not exist in this host. Call it only on a
        /// browser backend, after the backend check.
        /// </summary>
        protected static void RequireJsGlobal(string name, string neededFor)
        {
            bool present;
            try
            {
                using var value = SpawnJSRuntime.Instance.Get<SpawnJSObject?>(name);
                present = value != null;
            }
            catch { present = false; }
            if (!present)
                throw new UnsupportedTestException($"{neededFor} needs the JS global '{name}', which this host does not have (no page: e.g. Node)");
        }
    }
}
