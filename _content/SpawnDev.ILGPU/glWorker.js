'use strict';
let gl = null;
let canvas = null;
const programCache = {};

// ---- Cached GL objects (reused across dispatches) ----
let cachedTFBuffer = null;
let cachedTFBufferSize = 0;
let cachedTFObject = null;
let cachedDummyVBO = null;
let cachedDummyVBOSize = 0;
let cachedDummyVAO = null;
let cachedPaddedBuffer = null;
let cachedPaddedBufferSize = 0;
const textureCache = {};

// NOTE: GL constants are accessed from gl.* context directly (not hardcoded)
// to ensure compatibility with OffscreenCanvas WebGL2 in Worker contexts.

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
            // NOTE: No postMessage for init — avoids interfering with dispatch handlers
            break;

        case 'dispatch':
            try {
                const result = dispatchKernel(msg);
                self.postMessage(result.message, result.transferList);
            } catch (err) {
                console.error('[GLWorker] dispatch error:', err.message, err.stack);
                // CRITICAL: Always transfer ArrayBuffers back even on error
                const transferList = [];
                const buffers = [];
                if (msg.params) {
                    for (const p of msg.params) {
                        if (p.kind === 'buffer' && p.buffer && p.buffer.byteLength > 0) {
                            buffers.push({ index: p.argIndex, buffer: p.buffer });
                            transferList.push(p.buffer);
                        }
                    }
                }
                self.postMessage({ done: false, dispatchId: msg.dispatchId, error: err.message + '\n' + err.stack, buffers }, transferList);
            }
            break;
    }
};

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

function dispatchKernel(msg) {
    const { dispatchId, programId, source, varyingNames, totalVertices, dimX, dimY, dimZ, strides, outputs } = msg;
    const kernelParams = msg.params;
    const diag = [];  // Diagnostic log lines
    diag.push('gl=' + (gl ? 'OK' : 'NULL'));
    diag.push('canvas=' + (canvas ? 'OK' : 'NULL'));
    diag.push('paramsReceived=' + (kernelParams ? kernelParams.length : 'null/undefined'));

    // Flush any pre-existing GL errors
    while (gl.getError() !== 0) { }

    function checkErr(step) {
        const e = gl.getError();
        if (e !== 0) diag.push('ERR@' + step + '=' + e);
    }

    // ---- Step 1: Get or compile program ----
    const cached = getOrCompileProgram(programId, source, varyingNames || []);
    gl.useProgram(cached.program);

    // ---- ANGLE f64 workaround: set u_one = 1.0 (opaque to compiler) ----
    const uOneLoc = getUniformLoc(cached, 'u_one');
    if (uOneLoc !== null) gl.uniform1f(uOneLoc, 1.0);

    // ---- Step 2: Dimension uniforms ----
    const dimWLoc = getUniformLoc(cached, 'u_dimWidth');
    if (dimWLoc) gl.uniform1i(dimWLoc, dimX);
    const dimHLoc = getUniformLoc(cached, 'u_dimHeight');
    if (dimHLoc) gl.uniform1i(dimHLoc, dimY);

    // ---- Step 3: Bind parameters ----
    let textureUnit = 0;
    const bufferInfos = [];  // Track buffer params for transfer back

    for (const p of kernelParams) {
        if (p.kind === 'buffer') {
            bufferInfos.push(p);
            const texUnit = textureUnit++;
            const uniformName = 'u_param' + p.paramIndex;
            const uniformLoc = getUniformLoc(cached, uniformName);

            if (uniformLoc !== null) {
                // Create Uint8Array view into the transferred ArrayBuffer
                const srcData = new Uint8Array(p.buffer, p.byteOffset, p.byteLength);
                const elementSize = 4;  // All buffer types are 4 bytes in GLSL (int/uint/float)
                const texelCount = Math.ceil(p.byteLength / 4);

                // 2D tiling for large buffers
                const maxTexSize = 16384;
                let tileWidth, tileHeight;
                if (texelCount > maxTexSize) {
                    tileWidth = maxTexSize;
                    tileHeight = Math.ceil(texelCount / tileWidth);
                } else {
                    tileWidth = texelCount;
                    tileHeight = 1;
                }

                const totalTexels = tileWidth * tileHeight;
                const paddedByteSize = totalTexels * 4;

                // Reuse cached padded buffer
                if (!cachedPaddedBuffer || cachedPaddedBufferSize < paddedByteSize) {
                    cachedPaddedBuffer = new ArrayBuffer(paddedByteSize);
                    cachedPaddedBufferSize = paddedByteSize;
                }
                const paddedView = new Uint8Array(cachedPaddedBuffer, 0, paddedByteSize);
                paddedView.set(srcData);
                // Zero-fill padding
                if (paddedByteSize > p.byteLength) {
                    paddedView.fill(0, p.byteLength, paddedByteSize);
                }

                // Get or create cached GL texture for this slot
                gl.activeTexture(gl.TEXTURE0 + texUnit);
                let texEntry = textureCache[texUnit];
                if (!texEntry || texEntry.width !== tileWidth || texEntry.height !== tileHeight) {
                    if (texEntry && texEntry.texture) gl.deleteTexture(texEntry.texture);
                    const tex = gl.createTexture();
                    gl.bindTexture(gl.TEXTURE_2D, tex);
                    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
                    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
                    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
                    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
                    textureCache[texUnit] = { texture: tex, width: tileWidth, height: tileHeight };
                } else {
                    gl.bindTexture(gl.TEXTURE_2D, texEntry.texture);
                }

                // Upload via texImage2D
                if (p.glslType === 'int') {
                    gl.texImage2D(gl.TEXTURE_2D, 0, gl.R32I, tileWidth, tileHeight, 0, gl.RED_INTEGER, gl.INT, new Int32Array(cachedPaddedBuffer, 0, totalTexels));
                } else if (p.glslType === 'uint') {
                    gl.texImage2D(gl.TEXTURE_2D, 0, gl.R32UI, tileWidth, tileHeight, 0, gl.RED_INTEGER, gl.UNSIGNED_INT, new Uint32Array(cachedPaddedBuffer, 0, totalTexels));
                } else {
                    gl.texImage2D(gl.TEXTURE_2D, 0, gl.R32F, tileWidth, tileHeight, 0, gl.RED, gl.FLOAT, new Float32Array(cachedPaddedBuffer, 0, totalTexels));
                }
                gl.uniform1i(uniformLoc, texUnit);

                // Tile width uniform
                const tileWLoc = getUniformLoc(cached, 'u_param' + p.paramIndex + '_tileW');
                if (tileWLoc) gl.uniform1i(tileWLoc, tileWidth);
            }

            // Stride uniforms for ArrayView2D/3D
            if (strides && strides[p.paramIndex]) {
                const dims = strides[p.paramIndex];
                const strideLoc = getUniformLoc(cached, 'u_param' + p.paramIndex + '_stride[0]');
                if (strideLoc !== null) {
                    gl.uniform1iv(strideLoc, new Int32Array(dims));
                }
            }
        } else if (p.kind === 'scalar') {
            const uniformName = 'u_param' + p.paramIndex;
            const loc = getUniformLoc(cached, uniformName);
            if (loc !== null) {
                if (p.scalarType === 'int' || p.scalarType === 'bool' || p.scalarType === 'byte') {
                    gl.uniform1i(loc, p.value | 0);
                } else if (p.scalarType === 'uint') {
                    gl.uniform1ui(loc, p.value >>> 0);
                } else if (p.scalarType === 'float' || p.scalarType === 'double') {
                    gl.uniform1f(loc, p.value);
                }
            }
        } else if (p.kind === 'scalar_emu64') {
            const loLoc = getUniformLoc(cached, 'u_param' + p.paramIndex + '_lo');
            const hiLoc = getUniformLoc(cached, 'u_param' + p.paramIndex + '_hi');
            if (loLoc !== null) gl.uniform1ui(loLoc, p.lo >>> 0);
            if (hiLoc !== null) gl.uniform1ui(hiLoc, p.hi >>> 0);
        } else if (p.kind === 'struct') {
            // Struct uniform: set each leaf field individually via u_paramN.field_N
            // The GLSL type generator flattens nested structs into a single-level struct
            // with sequential field_N naming, so paths are always flat (field_0, field_1, etc.)
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

        // Reuse cached TF buffer
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

        // Reuse cached TF object
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
    checkErr('draw');

    // ---- Step 6: TF Readback ----
    if (tfBuffer && vNames.length > 0 && outputs && outputs.length > 0) {
        gl.bindBuffer(gl.TRANSFORM_FEEDBACK_BUFFER, tfBuffer);
        const tfFloatCount = totalVertices * vNames.length;
        const tfByteSize = tfFloatCount * 4;

        // Read TF data
        const readbackFloat = new Float32Array(tfFloatCount);
        gl.getBufferSubData(gl.TRANSFORM_FEEDBACK_BUFFER, 0, readbackFloat);
        checkErr('readback');

        const readbackBytes = new Uint8Array(readbackFloat.buffer);

        const varyingCount = vNames.length;
        const strideBytes = varyingCount * 4;

        // Process each output varying
        for (let oi = 0; oi < outputs.length; oi++) {
            const out = outputs[oi];

            // Skip 'hi' emulated varyings (read with their 'lo' counterpart)
            if (out.isEmulated && out.emulatedSuffix === 'hi') continue;

            // Find the matching buffer param to write back to
            const bufParam = bufferInfos.find(b => b.argIndex === out.argIndex);
            if (!bufParam || !bufParam.buffer) continue;

            const destView = new Uint8Array(bufParam.buffer);
            const writeOffset = out.writeByteOffset;

            if (out.isEmulated && out.emulatedSuffix === 'lo') {
                // Emulated 64-bit: interleave lo and hi into 8-byte pairs
                const hiOutIdx = out.outputIndex + 1;
                const elemCount = Math.min(totalVertices, Math.floor(out.writeLengthBytes / 8));
                for (let v = 0; v < elemCount; v++) {
                    const loSrc = v * strideBytes + out.outputIndex * 4;
                    const hiSrc = v * strideBytes + hiOutIdx * 4;
                    const dst = writeOffset + v * 8;
                    // Copy lo (4 bytes)
                    destView[dst] = readbackBytes[loSrc];
                    destView[dst + 1] = readbackBytes[loSrc + 1];
                    destView[dst + 2] = readbackBytes[loSrc + 2];
                    destView[dst + 3] = readbackBytes[loSrc + 3];
                    // Copy hi (4 bytes)
                    destView[dst + 4] = readbackBytes[hiSrc];
                    destView[dst + 5] = readbackBytes[hiSrc + 1];
                    destView[dst + 6] = readbackBytes[hiSrc + 2];
                    destView[dst + 7] = readbackBytes[hiSrc + 3];
                }
            } else if (out.fieldIndex >= 0 && out.fieldIndex === 0) {
                // Struct buffer: gather all field varyings for this param
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
                // Standard single-slot TF readback
                const elemCount = Math.min(totalVertices, Math.floor(out.writeLengthBytes / 4));
                for (let v = 0; v < elemCount; v++) {
                    const srcOff = v * strideBytes + out.outputIndex * 4;
                    const dstOff = writeOffset + v * 4;
                    if (srcOff + 4 <= readbackBytes.length) {
                        destView[dstOff] = readbackBytes[srcOff];
                        destView[dstOff + 1] = readbackBytes[srcOff + 1];
                        destView[dstOff + 2] = readbackBytes[srcOff + 2];
                        destView[dstOff + 3] = readbackBytes[srcOff + 3];
                    }
                }
            }
        }
    }

    // Unbind TF
    if (transformFeedback) {
        gl.bindTransformFeedback(gl.TRANSFORM_FEEDBACK, null);
    }
    gl.useProgram(null);

    // ---- Transfer ArrayBuffers back to main thread ----
    const transferList = [];
    const returnBuffers = [];
    for (const bi of bufferInfos) {
        if (bi.buffer && bi.buffer.byteLength > 0) {
            returnBuffers.push({ index: bi.argIndex, buffer: bi.buffer });
            transferList.push(bi.buffer);
        }
    }

    return {
        message: { done: true, dispatchId, buffers: returnBuffers, diag: diag.join('|') },
        transferList
    };
}
