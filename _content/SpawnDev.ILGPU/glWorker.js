'use strict';
let gl = null;
let canvas = null;
const programCache = {};

// ---- GPU-resident buffer registry ----
// Maps bufferId → { texture, width, height, glslType, byteSize, data: Uint8Array }
const bufferRegistry = {};

// ---- Cached GL objects (reused across dispatches) ----
let cachedTFBuffer = null;
let cachedTFBufferSize = 0;
let cachedTFObject = null;
let cachedDummyVBO = null;
let cachedDummyVBOSize = 0;
let cachedDummyVAO = null;

self.onmessage = function (e) {
    const msg = e.data;
    switch (msg.type) {
        case 'init':
            canvas = msg.canvas;
            if (!canvas) {
                console.error('[GLWorker] INIT FAILED: No canvas received');
                return;
            }
            gl = canvas.getContext('webgl2');
            if (!gl) {
                console.error('[GLWorker] INIT FAILED: Could not create WebGL2 context');
            }
            // Monitor context loss/restoration
            canvas.addEventListener('webglcontextlost', function (ev) {
                ev.preventDefault(); // allows potential context restoration
                self.postMessage({ type: 'contextlost' });
            });
            canvas.addEventListener('webglcontextrestored', function () {
                gl = canvas.getContext('webgl2');
                self.postMessage({ type: 'contextrestored' });
            });
            break;

        case 'allocBuffer':
            handleAllocBuffer(msg);
            break;

        case 'uploadBuffer':
            handleUploadBuffer(msg);
            break;

        case 'readbackBuffer':
            handleReadbackBuffer(msg);
            break;

        case 'freeBuffer':
            handleFreeBuffer(msg);
            break;

        case 'dispatch':
            try {
                const result = dispatchKernel(msg);
                self.postMessage(result.message, result.transferList);
            } catch (err) {
                console.error('[GLWorker] dispatch error:', err.message, err.stack);
                self.postMessage({
                    done: false, dispatchId: msg.dispatchId,
                    error: err.message + '\n' + err.stack
                });
            }
            break;

        case 'blitBuffer':
            handleBlitBuffer(msg);
            break;
    }
};

// ---- Buffer Registry Operations ----

function computeTiling(byteSize) {
    const texelCount = Math.ceil(byteSize / 4);
    const maxTexSize = 16384;
    let width, height;
    if (texelCount > maxTexSize) {
        width = maxTexSize;
        height = Math.ceil(texelCount / width);
    } else {
        width = texelCount;
        height = 1;
    }
    return { width, height, totalTexels: width * height };
}

function createTextureForEntry(entry) {
    const tex = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, tex);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    uploadTextureData(tex, entry);
    return tex;
}

function uploadTextureData(tex, entry) {
    gl.bindTexture(gl.TEXTURE_2D, tex);
    const totalTexels = entry.width * entry.height;
    if (entry.glslType === 'int') {
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.R32I, entry.width, entry.height, 0,
            gl.RED_INTEGER, gl.INT, new Int32Array(entry.data.buffer, 0, totalTexels));
    } else if (entry.glslType === 'uint') {
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.R32UI, entry.width, entry.height, 0,
            gl.RED_INTEGER, gl.UNSIGNED_INT, new Uint32Array(entry.data.buffer, 0, totalTexels));
    } else {
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.R32F, entry.width, entry.height, 0,
            gl.RED, gl.FLOAT, new Float32Array(entry.data.buffer, 0, totalTexels));
    }
}

function handleAllocBuffer(msg) {
    const { bufferId, byteSize, glslType } = msg;
    const { width, height, totalTexels } = computeTiling(byteSize);
    const data = new Uint8Array(totalTexels * 4); // zero-filled
    const entry = { texture: null, width, height, glslType: glslType || 'float', byteSize, data };
    entry.texture = createTextureForEntry(entry);
    bufferRegistry[bufferId] = entry;
}

function handleUploadBuffer(msg) {
    const { bufferId, buffer, byteOffset, byteLength } = msg;
    const entry = bufferRegistry[bufferId];
    if (!entry) {
        console.error('[GLWorker] uploadBuffer: unknown bufferId', bufferId);
        return;
    }
    const srcData = new Uint8Array(buffer, byteOffset || 0, byteLength || buffer.byteLength);
    entry.data.set(srcData);
    // Zero-fill padding
    if (srcData.length < entry.data.length) {
        entry.data.fill(0, srcData.length);
    }
    uploadTextureData(entry.texture, entry);
}

function handleReadbackBuffer(msg) {
    const { bufferId, requestId } = msg;
    const entry = bufferRegistry[bufferId];
    if (!entry) {
        self.postMessage({ type: 'readbackResult', requestId, bufferId, error: 'unknown bufferId' });
        return;
    }
    // Create a copy of the data to transfer back
    const copy = new ArrayBuffer(entry.byteSize);
    new Uint8Array(copy).set(new Uint8Array(entry.data.buffer, 0, entry.byteSize));
    self.postMessage({ type: 'readbackResult', requestId, bufferId, buffer: copy }, [copy]);
}

function handleFreeBuffer(msg) {
    const { bufferId } = msg;
    const entry = bufferRegistry[bufferId];
    if (entry) {
        if (entry.texture) gl.deleteTexture(entry.texture);
        delete bufferRegistry[bufferId];
    }
}

// ---- Shader Compilation ----

function getOrCompileProgram(programId, source, varyingNames) {
    if (programCache[programId] && !programCache[programId].disposed) {
        return programCache[programId];
    }

    const vs = gl.createShader(gl.VERTEX_SHADER);
    gl.shaderSource(vs, source);
    gl.compileShader(vs);
    if (!gl.getShaderParameter(vs, gl.COMPILE_STATUS)) {
        const log = gl.getShaderInfoLog(vs);
        gl.deleteShader(vs);
        throw new Error('Vertex shader compile error: ' + log);
    }

    const fsSource = '#version 300 es\nprecision mediump float;\nvoid main() {}\n';
    const fs = gl.createShader(gl.FRAGMENT_SHADER);
    gl.shaderSource(fs, fsSource);
    gl.compileShader(fs);
    if (!gl.getShaderParameter(fs, gl.COMPILE_STATUS)) {
        const log = gl.getShaderInfoLog(fs);
        gl.deleteShader(vs);
        gl.deleteShader(fs);
        throw new Error('Fragment shader compile error: ' + log);
    }

    const program = gl.createProgram();
    gl.attachShader(program, vs);
    gl.attachShader(program, fs);

    if (varyingNames && varyingNames.length > 0) {
        gl.transformFeedbackVaryings(program, varyingNames, gl.INTERLEAVED_ATTRIBS);
    }

    gl.linkProgram(program);
    if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
        const log = gl.getProgramInfoLog(program);
        gl.deleteShader(vs);
        gl.deleteShader(fs);
        gl.deleteProgram(program);
        throw new Error('Program link error: ' + log);
    }

    const cached = { program, vs, fs, uniformCache: {}, disposed: false };
    programCache[programId] = cached;
    return cached;
}

function getUniformLoc(cached, name) {
    if (name in cached.uniformCache) return cached.uniformCache[name];
    const loc = gl.getUniformLocation(cached.program, name);
    cached.uniformCache[name] = loc;
    return loc;
}

// ---- Kernel Dispatch ----

function dispatchKernel(msg) {
    const { dispatchId, programId, source, varyingNames, totalVertices, dimX, dimY, dimZ, strides, outputs } = msg;
    const kernelParams = msg.params;

    // Flush any pre-existing GL errors
    while (gl.getError() !== 0) { }

    // ---- Step 1: Get or compile program ----
    const cached = getOrCompileProgram(programId, source, varyingNames || []);
    gl.useProgram(cached.program);

    // ---- ANGLE f64 workaround: set u_one = 1.0 ----
    const uOneLoc = getUniformLoc(cached, 'u_one');
    if (uOneLoc !== null) gl.uniform1f(uOneLoc, 1.0);

    // ---- Step 2: Dimension uniforms ----
    const dimWLoc = getUniformLoc(cached, 'u_dimWidth');
    if (dimWLoc) gl.uniform1i(dimWLoc, dimX);
    const dimHLoc = getUniformLoc(cached, 'u_dimHeight');
    if (dimHLoc) gl.uniform1i(dimHLoc, dimY);

    // Grid/group dimension uniforms (for Grid.IdxX/Y, Group.IdxX, Group.DimX)
    const groupDimXLoc = getUniformLoc(cached, 'u_groupDimX');
    if (groupDimXLoc) gl.uniform1i(groupDimXLoc, msg.groupDimX || 1);
    const gridDimXLoc = getUniformLoc(cached, 'u_gridDimX');
    if (gridDimXLoc) gl.uniform1i(gridDimXLoc, msg.gridDimX || 1);
    const gridDimYLoc = getUniformLoc(cached, 'u_gridDimY');
    if (gridDimYLoc) gl.uniform1i(gridDimYLoc, msg.gridDimY || 1);

    // ---- Step 3: Bind parameters ----
    let textureUnit = 0;
    const bufferParamMap = [];  // Track which params map to which bufferIds

    for (const p of kernelParams) {
        if (p.kind === 'buffer_ref') {
            // GPU-resident buffer — bind existing texture from registry
            const entry = bufferRegistry[p.bufferId];
            if (!entry) throw new Error('Unknown bufferId: ' + p.bufferId);

            const texUnit = textureUnit++;
            const uniformName = 'u_param' + p.paramIndex;
            const uniformLoc = getUniformLoc(cached, uniformName);

            if (uniformLoc !== null) {
                gl.activeTexture(gl.TEXTURE0 + texUnit);
                gl.bindTexture(gl.TEXTURE_2D, entry.texture);
                gl.uniform1i(uniformLoc, texUnit);

                // Tile width uniform
                const tileWLoc = getUniformLoc(cached, 'u_param' + p.paramIndex + '_tileW');
                if (tileWLoc) gl.uniform1i(tileWLoc, entry.width);
            }

            // Element count uniform for GetViewLength support
            if (p.elementCount !== undefined) {
                const lenLoc = getUniformLoc(cached, 'u_param' + p.paramIndex + '_length');
                if (lenLoc !== null) gl.uniform1i(lenLoc, p.elementCount | 0);
            }

            // SubView element offset — added to texelFetch indices when buffer is a SubView
            if (p.elementOffset !== undefined && p.elementOffset !== 0) {
                const offsetLoc = getUniformLoc(cached, 'u_param' + p.paramIndex + '_offset');
                if (offsetLoc !== null) gl.uniform1i(offsetLoc, p.elementOffset | 0);
            }

            // Stride uniforms for ArrayView2D/3D
            if (strides && strides[p.paramIndex]) {
                const dims = strides[p.paramIndex];
                const strideLoc = getUniformLoc(cached, 'u_param' + p.paramIndex + '_stride[0]');
                if (strideLoc !== null) {
                    gl.uniform1iv(strideLoc, new Int32Array(dims));
                }
            }

            bufferParamMap.push({ bufferId: p.bufferId, paramIndex: p.paramIndex });

        } else if (p.kind === 'scalar') {
            const uniformName = 'u_param' + p.paramIndex;
            const loc = getUniformLoc(cached, uniformName);
            if (loc !== null) {
                if (p.scalarType === 'int' || p.scalarType === 'bool' || p.scalarType === 'byte'
                    || p.scalarType === 'sbyte' || p.scalarType === 'short' || p.scalarType === 'ushort'
                    || p.scalarType === 'long') {
                    gl.uniform1i(loc, p.value | 0);
                } else if (p.scalarType === 'uint' || p.scalarType === 'ulong') {
                    gl.uniform1ui(loc, p.value >>> 0);
                } else if (p.scalarType === 'float' || p.scalarType === 'double') {
                    gl.uniform1f(loc, p.value);
                } else {
                    console.warn('[GLWorker] Unknown scalar type:', p.scalarType, 'param:', p.paramIndex);
                }
            }
        } else if (p.kind === 'scalar_emu64') {
            const loLoc = getUniformLoc(cached, 'u_param' + p.paramIndex + '_lo');
            const hiLoc = getUniformLoc(cached, 'u_param' + p.paramIndex + '_hi');
            if (loLoc !== null) gl.uniform1ui(loLoc, p.lo >>> 0);
            if (hiLoc !== null) gl.uniform1ui(hiLoc, p.hi >>> 0);
        } else if (p.kind === 'struct') {
            for (const f of p.fields) {
                const fieldLoc = getUniformLoc(cached, 'u_param' + p.paramIndex + '.' + f.path);
                if (fieldLoc !== null) {
                    if (f.scalarType === 'int' || f.scalarType === 'bool') {
                        gl.uniform1i(fieldLoc, f.value | 0);
                    } else if (f.scalarType === 'uint') {
                        gl.uniform1ui(fieldLoc, f.value >>> 0);
                    } else {
                        gl.uniform1f(fieldLoc, f.value);
                    }
                }
            }
        }
    }

    // ---- Step 4: Transform Feedback setup ----
    let tfBuffer = null;
    let transformFeedback = null;
    const vNames = varyingNames || [];

    if (vNames.length > 0) {
        const tfFloatCount = totalVertices * vNames.length;
        const tfByteSize = tfFloatCount * 4;

        if (!cachedTFBuffer || cachedTFBufferSize < tfByteSize) {
            if (cachedTFBuffer) gl.deleteBuffer(cachedTFBuffer);
            cachedTFBuffer = gl.createBuffer();
            cachedTFBufferSize = tfByteSize;
            gl.bindBuffer(gl.TRANSFORM_FEEDBACK_BUFFER, cachedTFBuffer);
            gl.bufferData(gl.TRANSFORM_FEEDBACK_BUFFER, tfByteSize, gl.DYNAMIC_READ);
        } else {
            gl.bindBuffer(gl.TRANSFORM_FEEDBACK_BUFFER, cachedTFBuffer);
        }
        tfBuffer = cachedTFBuffer;

        if (!cachedTFObject) {
            cachedTFObject = gl.createTransformFeedback();
        }
        transformFeedback = cachedTFObject;
        gl.bindTransformFeedback(gl.TRANSFORM_FEEDBACK, transformFeedback);
        gl.bindBufferBase(gl.TRANSFORM_FEEDBACK_BUFFER, 0, tfBuffer);
    }

    // ---- Step 5: Dispatch via DrawArrays ----
    if (!cachedDummyVAO) {
        cachedDummyVAO = gl.createVertexArray();
    }
    gl.bindVertexArray(cachedDummyVAO);

    if (!cachedDummyVBO || cachedDummyVBOSize < totalVertices * 4) {
        if (cachedDummyVBO) gl.deleteBuffer(cachedDummyVBO);
        cachedDummyVBO = gl.createBuffer();
        cachedDummyVBOSize = totalVertices * 4;
        gl.bindBuffer(gl.ARRAY_BUFFER, cachedDummyVBO);
        gl.bufferData(gl.ARRAY_BUFFER, cachedDummyVBOSize, gl.STATIC_DRAW);
        gl.enableVertexAttribArray(0);
        gl.vertexAttribPointer(0, 1, gl.FLOAT, false, 0, 0);
    } else {
        gl.bindBuffer(gl.ARRAY_BUFFER, cachedDummyVBO);
    }

    gl.enable(gl.RASTERIZER_DISCARD);
    if (transformFeedback) gl.beginTransformFeedback(gl.POINTS);
    gl.drawArrays(gl.POINTS, 0, totalVertices);
    if (transformFeedback) gl.endTransformFeedback();
    gl.disable(gl.RASTERIZER_DISCARD);

    // Unbind TF before readback (WebGL2 spec requirement)
    if (transformFeedback) {
        gl.bindBufferBase(gl.TRANSFORM_FEEDBACK_BUFFER, 0, null);
        gl.bindTransformFeedback(gl.TRANSFORM_FEEDBACK, null);
    }

    // ---- Step 6: TF Readback → update GPU-resident buffers ----
    if (tfBuffer && vNames.length > 0 && outputs && outputs.length > 0) {
        // gl.finish() blocks until all submitted GL commands (including TF) complete.
        // Note: ANGLE emits a cosmetic "READ-usage buffer" warning here because it
        // doesn't track gl.finish() as a fence. This warning is harmless — the GPU
        // has completed all writes before we read. Using async fenceSync would
        // eliminate the warning but adds unacceptable scheduling overhead for
        // real-time rendering (~4-50ms per dispatch).
        gl.finish();

        gl.bindBuffer(gl.TRANSFORM_FEEDBACK_BUFFER, tfBuffer);
        const tfFloatCount = totalVertices * vNames.length;
        const readbackFloat = new Float32Array(tfFloatCount);
        gl.getBufferSubData(gl.TRANSFORM_FEEDBACK_BUFFER, 0, readbackFloat);

        const readbackBytes = new Uint8Array(readbackFloat.buffer);
        const varyingCount = vNames.length;
        const strideBytes = varyingCount * 4;

        // Set of bufferIds that were modified (need texture re-upload)
        const modifiedBufferIds = new Set();

        for (let oi = 0; oi < outputs.length; oi++) {
            const out = outputs[oi];

            // Atomic vote: each thread emitted its increment amount; sum all and add to buffer[element].
            // This emulates Atomic.Add without true GPU atomics (WebGL2 vertex shader limitation).
            if (out.isAtomicVote) {
                const entry = bufferRegistry[out.bufferId];
                if (!entry) continue;
                const destView = entry.data;
                const writeOffset = out.writeByteOffset;
                // Sum all per-vertex vote values (stored as int in the TF buffer)
                const int32TF = new Int32Array(readbackFloat.buffer);
                let sum = 0;
                for (let v = 0; v < totalVertices; v++) {
                    sum += int32TF[(v * strideBytes + out.outputIndex * 4) >> 2];
                }
                // Accumulate (not replace) the target buffer element using Int32 arithmetic
                const destInt32 = new Int32Array(destView.buffer);
                const destIdx = writeOffset >> 2;
                destInt32[destIdx] = destInt32[destIdx] + sum;
                modifiedBufferIds.add(out.bufferId);
                continue;
            }

            // Skip 'hi' emulated varyings (read with their 'lo' counterpart)
            if (out.isEmulated && out.emulatedSuffix === 'hi') continue;

            // Find the registry entry for this output
            const entry = bufferRegistry[out.bufferId];
            if (!entry) continue;
            const destView = entry.data;
            const writeOffset = out.writeByteOffset;

            if (out.isEmulated && out.emulatedSuffix === 'lo') {
                const hiOutIdx = out.outputIndex + 1;
                const elemCount = Math.min(totalVertices, Math.floor(out.writeLengthBytes / 8));
                for (let v = 0; v < elemCount; v++) {
                    const loSrc = v * strideBytes + out.outputIndex * 4;
                    const hiSrc = v * strideBytes + hiOutIdx * 4;
                    const dst = writeOffset + v * 8;
                    destView[dst] = readbackBytes[loSrc];
                    destView[dst + 1] = readbackBytes[loSrc + 1];
                    destView[dst + 2] = readbackBytes[loSrc + 2];
                    destView[dst + 3] = readbackBytes[loSrc + 3];
                    destView[dst + 4] = readbackBytes[hiSrc];
                    destView[dst + 5] = readbackBytes[hiSrc + 1];
                    destView[dst + 6] = readbackBytes[hiSrc + 2];
                    destView[dst + 7] = readbackBytes[hiSrc + 3];
                }
            } else if (out.fieldIndex >= 0 && out.fieldIndex === 0) {
                const structFields = outputs.filter(o => o.paramIndex === out.paramIndex && o.fieldIndex >= 0)
                    .sort((a, b) => a.fieldIndex - b.fieldIndex);
                const fieldCount = structFields.length;
                const structElemSize = fieldCount * 4;
                const elemCount = Math.min(totalVertices, Math.floor(out.writeLengthBytes / structElemSize));
                for (let v = 0; v < elemCount; v++) {
                    for (let fi = 0; fi < fieldCount; fi++) {
                        const fieldOut = structFields[fi];
                        const srcOff = v * strideBytes + fieldOut.outputIndex * 4;
                        const dstOff = writeOffset + v * structElemSize + fi * 4;
                        if (srcOff + 4 <= readbackBytes.length) {
                            destView[dstOff] = readbackBytes[srcOff];
                            destView[dstOff + 1] = readbackBytes[srcOff + 1];
                            destView[dstOff + 2] = readbackBytes[srcOff + 2];
                            destView[dstOff + 3] = readbackBytes[srcOff + 3];
                        }
                    }
                }
            } else if (out.fieldIndex < 0) {
                const storeCount = out.storeCount || 1;
                const storeSlot = out.storeSlot >= 0 ? out.storeSlot : 0;
                const bytesPerVertex = storeCount * 4;

                // Sub-word packing: TF outputs one i32 per element, but the destination
                // buffer stores packed sub-word values (e.g. 2 shorts per i32).
                // Pack by reading each TF i32 and writing only the sub-word portion.
                if (out.subWordElementSize && out.subWordElementSize < 4) {
                    const swSize = out.subWordElementSize;
                    const elemCount = Math.min(totalVertices, Math.floor(out.writeLengthBytes / swSize));
                    const int32TF = new Int32Array(readbackFloat.buffer);
                    for (let v = 0; v < elemCount; v++) {
                        const srcIdx = (v * strideBytes + out.outputIndex * 4) >> 2;
                        const val = int32TF[srcIdx];
                        const dstOff = writeOffset + v * swSize;
                        if (swSize === 2) {
                            // Pack as 16-bit (little-endian)
                            destView[dstOff] = val & 0xFF;
                            destView[dstOff + 1] = (val >> 8) & 0xFF;
                        } else if (swSize === 1) {
                            destView[dstOff] = val & 0xFF;
                        }
                    }
                } else {
                    const elemCount = Math.min(totalVertices, Math.floor(out.writeLengthBytes / bytesPerVertex));
                    for (let v = 0; v < elemCount; v++) {
                        const srcOff = v * strideBytes + out.outputIndex * 4;
                        const dstOff = writeOffset + v * bytesPerVertex + storeSlot * 4;
                        if (srcOff + 4 <= readbackBytes.length) {
                            destView[dstOff] = readbackBytes[srcOff];
                            destView[dstOff + 1] = readbackBytes[srcOff + 1];
                            destView[dstOff + 2] = readbackBytes[srcOff + 2];
                            destView[dstOff + 3] = readbackBytes[srcOff + 3];
                        }
                    }
                }
            }

            modifiedBufferIds.add(out.bufferId);
        }

        // Re-upload modified buffers to their GPU textures (GPU-resident update)
        for (const bid of modifiedBufferIds) {
            const entry = bufferRegistry[bid];
            if (entry) {
                uploadTextureData(entry.texture, entry);
            }
        }

    }

    gl.useProgram(null);

    // No ArrayBuffer transfers — data stays GPU-resident in the worker
    return {
        message: { done: true, dispatchId },
        transferList: []
    };
}

// ---- ImageBitmap blit ----
// Creates an ImageBitmap from an RGBA pixel buffer and transfers it to the main thread.
// The browser compositor manages the bitmap as a GPU texture — no pixel copy on transfer.
function handleBlitBuffer(msg) {
    const { bufferId, width, height, requestId } = msg;
    const entry = bufferRegistry[bufferId];
    if (!entry) {
        self.postMessage({ type: 'blitResult', requestId, error: 'unknown bufferId' });
        return;
    }
    // entry.data is always current after dispatch (TF readback keeps it in sync).
    // Wrap as Uint8ClampedArray for ImageData — no copy, just a typed view.
    const rgba = new Uint8ClampedArray(entry.data.buffer, 0, width * height * 4);
    const imageData = new ImageData(rgba, width, height);
    createImageBitmap(imageData).then(function(bitmap) {
        self.postMessage({ type: 'blitResult', requestId, bitmap }, [bitmap]);
    }).catch(function(err) {
        self.postMessage({ type: 'blitResult', requestId, error: err.message });
    });
}
