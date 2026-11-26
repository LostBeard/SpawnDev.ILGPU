# 08 - A kernel on WebGPU under Node, no browser

Example 01's kernel and code, in a different host: a **.NET WebAssembly console app** that **Node** runs. Node has no
WebGPU of its own; `main.mjs` gives the .NET runtime a real one from Google Dawn through the npm package
[`webgpu`](https://www.npmjs.com/package/webgpu) (dawn-gpu/node-webgpu, the Dawn project's own Node binding), so
SpawnDev.ILGPU's WebGPU backend runs on your actual GPU, with no browser involved.

Needs the .NET 10 SDK with the WebAssembly workload (`dotnet workload install wasm-tools`) and Node 22 or later.

```bash
cd Examples/08-NodeWebGPU
npm install
dotnet build -c Release
node bin/Release/net10.0/browser-wasm/AppBundle/main.mjs
```

Output on an RTX 4070 (Windows, Node 24.15.0, `webgpu` 0.6.2, SpawnDev.ILGPU 5.4.51):

```
HOST: node v24.15.0, adapter: nvidia / lovelace / nvidia-geforce-rtx-4070 / D3D12 driver version 32.0.16.2114
Running on: nvidia-geforce-rtx-4070  (WebGPU)
     0 +    0 = 0
     1 +   10 = 11
     2 +   20 = 22
  ...
    15 +  150 = 165
Done.
```

## What to notice
- **Same C# as example 01.** Only the host changed: `RuntimeIdentifier` `browser-wasm`, `OutputType` `Exe`, and a
  `WasmMainJSPath` pointing at `main.mjs`. `Context.CreateAsync(... AllAcceleratorsAsync())` finds WebGPU because
  `navigator.gpu` exists.
- **`main.mjs` does three things:** puts Dawn on `navigator.gpu` (wrapping the real `navigator` in a Proxy, because the
  .NET runtime also reads `userAgent` and `hardwareConcurrency` from it), imports SpawnJS's interop script (a console app
  does not load JS initializers the way a Blazor app does), and starts the .NET runtime.
- **Async, as in the browser.** WebGPU has no blocking wait: `SynchronizeAsync()` and `CopyToHostAsync()`.
- **Why `webgpu` and not `@kmamal/gpu`.** Both are Dawn bindings. SpawnDev.ILGPU's own test suite, run under Node on
  each (2026-11-26, RTX 4070): `webgpu` 0.6.2 870 passed / 11 failed / 17 skipped in 182 s; `@kmamal/gpu` 0.2.1 845 / 35 /
  17 in 873 s, and one double-precision shader it did not compile in 20 minutes. Every test that passed on `@kmamal/gpu`
  also passed on `webgpu`. (Of those 11, five need a page and now skip with the reason; the other six were a library gap
  under Node, fixed in 5.4.52: 876 passed / 0 failed / 22 skipped.)
- The same project shape runs SpawnDev.ILGPU's whole GPU test suite under Node: see `SpawnDev.ILGPU.NodeTests`.
