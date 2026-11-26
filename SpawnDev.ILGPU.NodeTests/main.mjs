// Headless WebGPU for the .NET WebAssembly runtime, supplied by Google Dawn through npm `webgpu` (dawn-gpu/node-webgpu,
// the Dawn project's own Node binding). Node has no built-in navigator.gpu: the REAL navigator is wrapped in a Proxy that
// answers `gpu` with Dawn and forwards everything else (userAgent, hardwareConcurrency), which the runtime needs to boot.
// A spread drops those (non-enumerable getters); Object.create throws (they read private fields of the real navigator).
// Chosen over @kmamal/gpu on 2026-11-26 by running this suite on both: 870 / 11 / 17 in 182 s against 845 / 35 / 17 in
// 873 s, plus one shader the older Dawn in @kmamal/gpu could not compile in 20 minutes; nothing passed only there.
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { create, globals } from 'webgpu'

Object.assign(globalThis, globals)   // GPUBufferUsage, GPUMapMode, ... as a browser has them
const gpu = create([])

const realNav = globalThis.navigator
const navProxy = new Proxy(realNav, {
    get(target, prop) {
        if (prop === 'gpu') return gpu
        const v = Reflect.get(target, prop, target)
        return typeof v === 'function' ? v.bind(target) : v
    },
    has(target, prop) { return prop === 'gpu' || Reflect.has(target, prop) },
})
Object.defineProperty(globalThis, 'navigator', { value: navProxy, configurable: true, writable: true })

// which adapter the code will get, printed for the record
const adapter = await gpu.requestAdapter()
const info = adapter?.info
console.log(`HOST: node ${process.version}, adapter: ${info ? [info.vendor, info.architecture, info.device, info.description].filter(Boolean).join(' / ') : '(none)'}`)

// SpawnJS's interop script (fingerprinted in the package, so found by pattern): a console app does not load JS initializers
const here = path.dirname(fileURLToPath(import.meta.url))
const spawnJs = fs.readdirSync(here).find(f => /^SpawnDev\.SpawnJS\..*lib\.module\.js$/.test(f))
if (!spawnJs) throw new Error('SpawnDev.SpawnJS interop script not found next to main.mjs')
await import('./' + spawnJs)
const { dotnet } = await import('./_framework/dotnet.js')
const { runMainAndExit } = await dotnet.withDiagnosticTracing(false).withApplicationArguments(...process.argv.slice(2)).create()
await runMainAndExit()
