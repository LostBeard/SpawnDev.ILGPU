// Web Worker for browser barrier test
self.onmessage = async (e) => {
    const { wasmBytes, memory, workerIdx, workerCount, numPhases, useWait32 } = e.data;
    const module = new WebAssembly.Module(wasmBytes);
    const instance = new WebAssembly.Instance(module, { env: { memory } });
    instance.exports.run(workerIdx, workerCount, numPhases, useWait32);
    self.postMessage({ workerIdx, done: true });
};
