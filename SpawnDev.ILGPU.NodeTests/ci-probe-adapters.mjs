// CI diagnostic: which requestAdapter() calls give an adapter on this machine (a GitHub runner has no GPU; Mesa's lavapipe is a
// CPU Vulkan device). Prints one line per attempt. Run: node ci-probe-adapters.mjs
import { create, globals } from 'webgpu'
Object.assign(globalThis, globals)

const attempts = [
    ['create([]), {}', [], {}],
    ['create([]), forceFallbackAdapter', [], { forceFallbackAdapter: true }],
    ['create([]), compatibility', [], { featureLevel: 'compatibility' }],
    ['backend=vulkan, {}', ['backend=vulkan'], {}],
    ['backend=vulkan, forceFallbackAdapter', ['backend=vulkan'], { forceFallbackAdapter: true }],
    ['backend=vulkan adapter=llvmpipe, {}', ['backend=vulkan', 'adapter=llvmpipe'], {}],
    ['backend=vulkan adapter=llvmpipe, forceFallbackAdapter', ['backend=vulkan', 'adapter=llvmpipe'], { forceFallbackAdapter: true }],
    ['adapter=SwiftShader, forceFallbackAdapter', ['adapter=SwiftShader'], { forceFallbackAdapter: true }],
]
for (const [label, flags, options] of attempts) {
    try {
        const gpu = create(flags)
        const adapter = await gpu.requestAdapter(options)
        if (!adapter) { console.log(`${label}: null`); continue }
        const i = adapter.info || {}
        const device = await adapter.requestDevice().catch(e => { console.log(`  requestDevice failed: ${e.message}`); return null })
        console.log(`${label}: ${[i.vendor, i.architecture, i.device, i.description].filter(Boolean).join(' / ')} fallback=${i.isFallbackAdapter} device=${device ? 'ok' : 'none'}`)
        device?.destroy()
    } catch (e) { console.log(`${label}: threw ${e.message}`) }
}
process.exit(0)
