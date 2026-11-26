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

Last full run (2026-11-26, master, SpawnDev.ILGPU 5.4.51, SpawnJS 3.0.11, Node 24.15.0, `webgpu` 0.6.2, RTX 4070 through
D3D12): **870 passed, 11 failed, 17 skipped of 898**, 117 s. The 17 are other backends' tests (CUDA inline PTX, Wasm,
WebGL, CPU). The 11 need a browser page: a canvas (`document`), image / video-frame sources, the page URL that the
dispatch-plan helper script is loaded from (4), the browser's yield before a kernel compile, and one scalar-arena check
(open: batching reads as off here).
