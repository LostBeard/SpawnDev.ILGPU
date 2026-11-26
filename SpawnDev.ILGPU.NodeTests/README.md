# SpawnDev.ILGPU.NodeTests

SpawnDev.ILGPU's GPU test suite, headless: a .NET WebAssembly console app that Node runs, with WebGPU from Google Dawn
(npm [`webgpu`](https://www.npmjs.com/package/webgpu)). No browser, no Playwright. It compiles the shared test files of
`SpawnDev.ILGPU.Demo.Shared/UnitTests` (the same source as the browser and desktop suites) except the ones that need the
demos' crypto / WebTorrent services or BlazorJS (`BackendTestBase.Peers.cs`, `.Autolykos2.cs`, `.StructBufferLayout.cs`,
`.Tests1.cs`), and runs them on the WebGPU backend.

```bash
cd SpawnDev.ILGPU.NodeTests
npm install
dotnet build -c Release
node bin/Release/net10.0/browser-wasm/AppBundle/main.mjs                  # all tests
node bin/Release/net10.0/browser-wasm/AppBundle/main.mjs RadixSort        # tests whose name contains RadixSort
node bin/Release/net10.0/browser-wasm/AppBundle/main.mjs --skip=A,B       # leave out tests whose name contains A or B
```

Output lines: `HOST:` (Node version and the adapter Dawn found), `READY: <n> tests`, one `TEST: name|result|ms|detail` per
test, `RESULTS: Failed: f Passed: p Skipped: s Ran: n`. Run it from this folder (not from inside `AppBundle`: a shell or
script whose working directory is there keeps the next build from replacing the folder).

Last full run (2026-11-26, SpawnDev.ILGPU 5.4.52, SpawnJS 3.0.11, Node 24.15.0, `webgpu` 0.6.2, RTX 4070 through D3D12):
**876 passed, 0 failed, 22 skipped of 898**, 113 s. 17 of the skips are other backends' tests (CUDA inline PTX, Wasm,
WebGL, CPU); 5 need something only a page has (a `document` for the canvas renderer, `OffscreenCanvas` / `ImageData` /
`VideoFrame` for the external-image copies) and say so. A test that needs a page feature checks for the feature and
skips; a failure here means something that should work under Node did not.

Its first run found a real gap: under Node the library never loaded its WebGPU dispatch helper script (no page URL, and a
console app does not deploy packages' `_content` files), so dispatch batching was off and dispatch plans threw. Fixed in
SpawnDev.ILGPU 5.4.52 (the helper loads from a copy embedded in the assembly).
