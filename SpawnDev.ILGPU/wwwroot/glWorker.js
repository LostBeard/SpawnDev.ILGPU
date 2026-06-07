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
            } else {
                // Required to render to R32F/RGBA32F color attachments (GPGPU scatter of float buffers).
                // Integer formats (R32I/R32UI) are color-renderable in core WebGL2 without this.
                gl.getExtension('EXT_color_buffer_float');
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

        case 'copyBuffer':
            handleCopyBuffer(msg);
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

        case 'scatter':
            try {
                handleScatter(msg);
            } catch (err) {
                console.error('[GLWorker] scatter error:', err.message, err.stack);
                self.postMessage({ done: false, dispatchId: msg.dispatchId, error: err.message + '\n' + err.stack });
            }
            break;
    }
};

// ---- GPGPU scatter (render points to a texture; dst[destIdx[i]] = src[i]) ----
// WebGL2 transform feedback is gather-only (an invocation writes only its own output slot).
// Scatter writes to a COMPUTED position by rasterizing one GL_POINT per element at the dest texel
// and letting the fragment shader write the value to the dst texture (the render target). This stays
// GPU-side: the result lives in the dst buffer's texture; we mark the CPU mirror stale and read it
// back lazily only if the host actually reads the buffer (handleReadbackBuffer).
const scatterPrograms = {}; // glslType -> { program, locs }
let scatterFBO = null;

function getOrCompileScatterProgram(glslType) {
    if (scatterPrograms[glslType]) return scatterPrograms[glslType];
    const isInt = glslType === 'int';
    const isUint = glslType === 'uint';
    const samp = isInt ? 'isampler2D' : isUint ? 'usampler2D' : 'sampler2D';
    const valType = isInt ? 'int' : isUint ? 'uint' : 'float';
    const vs = `#version 300 es
precision highp float; precision highp int;
uniform highp ${samp} u_src;
uniform highp isampler2D u_dest;
uniform int u_srcTileW; uniform int u_destTileW; uniform int u_dstW; uniform int u_dstH;
flat out ${valType} v_val;
void main() {
    int i = gl_VertexID;
    int sx = i % u_srcTileW; int sy = i / u_srcTileW;
    ${valType} val = texelFetch(u_src, ivec2(sx, sy), 0).r;
    int di = i % u_destTileW; int dj = i / u_destTileW;
    int dest = texelFetch(u_dest, ivec2(di, dj), 0).r;
    int tx = dest % u_dstW; int ty = dest / u_dstW;
    float ndcx = (float(tx) + 0.5) / float(u_dstW) * 2.0 - 1.0;
    float ndcy = (float(ty) + 0.5) / float(u_dstH) * 2.0 - 1.0;
    gl_Position = vec4(ndcx, ndcy, 0.0, 1.0);
    gl_PointSize = 1.0;
    v_val = val;
}`;
    const vec4Type = isInt ? 'ivec4' : isUint ? 'uvec4' : 'vec4';
    const zero = isInt ? '0' : isUint ? '0u' : '0.0';
    const fs = `#version 300 es
precision highp float; precision highp int;
flat in ${valType} v_val;
layout(location = 0) out highp ${vec4Type} o_val;
void main() { o_val = ${vec4Type}(v_val, ${zero}, ${zero}, ${zero}); }`;

    function compile(type, src) {
        const s = gl.createShader(type);
        gl.shaderSource(s, src); gl.compileShader(s);
        if (!gl.getShaderParameter(s, gl.COMPILE_STATUS))
            throw new Error('[scatter ' + glslType + '] shader compile: ' + gl.getShaderInfoLog(s) + '\n' + src);
        return s;
    }
    const program = gl.createProgram();
    gl.attachShader(program, compile(gl.VERTEX_SHADER, vs));
    gl.attachShader(program, compile(gl.FRAGMENT_SHADER, fs));
    gl.linkProgram(program);
    if (!gl.getProgramParameter(program, gl.LINK_STATUS))
        throw new Error('[scatter ' + glslType + '] link: ' + gl.getProgramInfoLog(program));
    const locs = {
        u_src: gl.getUniformLocation(program, 'u_src'),
        u_dest: gl.getUniformLocation(program, 'u_dest'),
        u_srcTileW: gl.getUniformLocation(program, 'u_srcTileW'),
        u_destTileW: gl.getUniformLocation(program, 'u_destTileW'),
        u_dstW: gl.getUniformLocation(program, 'u_dstW'),
        u_dstH: gl.getUniformLocation(program, 'u_dstH'),
    };
    const entry = { program, locs };
    scatterPrograms[glslType] = entry;
    return entry;
}

function handleScatter(msg) {
    const { dstBufferId, srcBufferId, destBufferId, n } = msg;
    while (gl.getError() !== 0) { }
    const dst = bufferRegistry[dstBufferId];
    const src = bufferRegistry[srcBufferId];
    const dest = bufferRegistry[destBufferId];
    if (!dst || !src || !dest) throw new Error('scatter: unknown buffer(s)');

    const glslType = dst.glslType || 'float';
    const sp = getOrCompileScatterProgram(glslType);
    gl.useProgram(sp.program);

    gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, src.texture); gl.uniform1i(sp.locs.u_src, 0);
    gl.activeTexture(gl.TEXTURE1); gl.bindTexture(gl.TEXTURE_2D, dest.texture); gl.uniform1i(sp.locs.u_dest, 1);
    gl.uniform1i(sp.locs.u_srcTileW, src.width);
    gl.uniform1i(sp.locs.u_destTileW, dest.width);
    gl.uniform1i(sp.locs.u_dstW, dst.width);
    gl.uniform1i(sp.locs.u_dstH, dst.height);

    // Render directly into the dst buffer's texture (the next op reads this — zero-copy).
    if (!scatterFBO) scatterFBO = gl.createFramebuffer();
    gl.bindFramebuffer(gl.FRAMEBUFFER, scatterFBO);
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, dst.texture, 0);
    const fbStatus = gl.checkFramebufferStatus(gl.FRAMEBUFFER);
    if (fbStatus !== gl.FRAMEBUFFER_COMPLETE) {
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
        throw new Error('scatter: incomplete FBO status=' + fbStatus + ' glslType=' + glslType +
            ' (R32F render targets need EXT_color_buffer_float; R32I/R32UI are core-renderable)');
    }
    gl.drawBuffers([gl.COLOR_ATTACHMENT0]);
    gl.viewport(0, 0, dst.width, dst.height);
    gl.disable(gl.RASTERIZER_DISCARD);
    gl.disable(gl.BLEND);          // blending is illegal for integer color buffers
    gl.disable(gl.DEPTH_TEST);
    gl.disable(gl.SCISSOR_TEST);
    gl.disable(gl.CULL_FACE);
    gl.colorMask(true, true, true, true);
    gl.drawArrays(gl.POINTS, 0, n);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);

    // Result lives in dst.texture (zero-copy); refresh the CPU mirror lazily on host readback.
    dst.dataStale = true;
}

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
    entry.dataStale = false; // CPU mirror + GPU texture now consistent
}

// Lazily refresh the CPU mirror (entry.data) from the GPU texture when it was last written by a
// scatter (render-to-texture) and not yet read back. This is the ONLY readPixels — intermediate
// scatter results stay GPU-side and are never read back.
function ensureCpuFresh(entry) {
    if (!entry || !entry.dataStale) return;
    if (!scatterFBO) scatterFBO = gl.createFramebuffer();
    gl.bindFramebuffer(gl.FRAMEBUFFER, scatterFBO);
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, entry.texture, 0);
    const total = entry.width * entry.height;
    let format, type, view;
    if (entry.glslType === 'int') { format = gl.RED_INTEGER; type = gl.INT; view = new Int32Array(entry.data.buffer, 0, total); }
    else if (entry.glslType === 'uint') { format = gl.RED_INTEGER; type = gl.UNSIGNED_INT; view = new Uint32Array(entry.data.buffer, 0, total); }
    else { format = gl.RED; type = gl.FLOAT; view = new Float32Array(entry.data.buffer, 0, total); }
    gl.readPixels(0, 0, entry.width, entry.height, format, type, view);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    entry.dataStale = false;
}

function handleReadbackBuffer(msg) {
    const { bufferId, requestId } = msg;
    const entry = bufferRegistry[bufferId];
    if (!entry) {
        self.postMessage({ type: 'readbackResult', requestId, bufferId, error: 'unknown bufferId' });
        return;
    }
    ensureCpuFresh(entry); // pull a scattered result back to the CPU mirror only when actually read
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

// Worker-side GPU->GPU copy. The CPU-side _backingArray is never refreshed
// after a kernel TF write (only the worker's entry.data is), so routing
// CopyTo/CopyFrom GPU->GPU through the main thread reads stale zeros from
// _backingArray and produces a destination of zeros. By doing the copy
// here (worker's entry.data -> worker's entry.data) the canonical
// post-kernel state is preserved, then we re-upload the destination
// texture so subsequent dispatches see the new data.
function handleCopyBuffer(msg) {
    const { srcBufferId, srcByteOffset, dstBufferId, dstByteOffset, byteLength } = msg;
    const srcEntry = bufferRegistry[srcBufferId];
    const dstEntry = bufferRegistry[dstBufferId];
    if (!srcEntry || !dstEntry) {
        console.error('[GLWorker] copyBuffer: unknown bufferId(s)', srcBufferId, dstBufferId);
        return;
    }
    ensureCpuFresh(srcEntry); // a scattered source must be pulled back before a CPU-side copy
    const srcOff = srcByteOffset | 0;
    const dstOff = dstByteOffset | 0;
    const len = byteLength | 0;
    // Bounds clamp - never overrun either typed array.
    const srcEnd = Math.min(srcOff + len, srcEntry.data.length);
    const copyLen = Math.max(0, srcEnd - srcOff);
    if (copyLen <= 0) return;
    if (dstOff + copyLen > dstEntry.data.length) {
        console.error('[GLWorker] copyBuffer: destination overrun', dstOff, copyLen, dstEntry.data.length);
        return;
    }
    dstEntry.data.set(srcEntry.data.subarray(srcOff, srcOff + copyLen), dstOff);
    // Push the new bytes to the destination texture so subsequent kernel
    // dispatches reading via texelFetch see the updated values.
    uploadTextureData(dstEntry.texture, dstEntry);
    dstEntry.dataStale = false; // CPU mirror + GPU texture now consistent
}

// ---- Shader Compilation ----

function getOrCompileProgram(programId, source, varyingNames) {
    const cached = programCache[programId];
    if (cached && !cached.disposed && cached.source === source) {
        return cached;
    }
    if (cached && !cached.disposed) {
        gl.deleteProgram(cached.program);
        gl.deleteShader(cached.vs);
        gl.deleteShader(cached.fs);
        cached.disposed = true;
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

    const entry = { program, vs, fs, uniformCache: {}, disposed: false, source };
    programCache[programId] = entry;
    return entry;
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

    // Resolve a param index to its uniform-name prefix.
    // Direct param: paramIndex < 1000 -> "u_param{N}".
    // Body-struct synthetic field: paramIndex >= 1000 -> "u_param{realN}_f{fi}"
    // where realN = floor(paramIndex/1000)-1 and fi = paramIndex % 1000. Matches
    // GLSLKernelFunctionGenerator.GetParamBindingName.
    function resolveParamPrefix(paramIndex) {
        if (paramIndex >= 1000) {
            const realN = Math.floor(paramIndex / 1000) - 1;
            const fi = paramIndex % 1000;
            return 'u_param' + realN + '_f' + fi;
        }
        return 'u_param' + paramIndex;
    }

    for (const p of kernelParams) {
        if (p.kind === 'buffer_ref') {
            // GPU-resident buffer — bind existing texture from registry
            const entry = bufferRegistry[p.bufferId];
            if (!entry) throw new Error('Unknown bufferId: ' + p.bufferId);

            const texUnit = textureUnit++;
            const uniformPrefix = resolveParamPrefix(p.paramIndex);
            const uniformLoc = getUniformLoc(cached, uniformPrefix);

            if (uniformLoc !== null) {
                gl.activeTexture(gl.TEXTURE0 + texUnit);
                gl.bindTexture(gl.TEXTURE_2D, entry.texture);
                gl.uniform1i(uniformLoc, texUnit);

                // Tile width uniform
                const tileWLoc = getUniformLoc(cached, uniformPrefix + '_tileW');
                if (tileWLoc) gl.uniform1i(tileWLoc, entry.width);
            }

            // Element count uniform for GetViewLength support
            if (p.elementCount !== undefined) {
                const lenLoc = getUniformLoc(cached, uniformPrefix + '_length');
                if (lenLoc !== null) gl.uniform1i(lenLoc, p.elementCount | 0);
            }

            // SubView element offset — added to texelFetch indices when buffer is a SubView.
            // Always set the uniform (including 0) so a prior dispatch's non-zero value
            // doesn't leak across dispatches. WebGL uniforms persist on the program object
            // across draw calls; if the previous dispatch with the same kernel had
            // elementOffset=N (non-zero), the next dispatch with elementOffset=0 would
            // read at offset N. Surfaced 2026-05-04 by Data's StyleMosaic Gather
            // first-divergent-node trace where node 55 Gather output was 0 on WebGL while
            // matching WebGPU exactly through node 54 (which read SubView(0, ...) and
            // set the offset uniform to 0 implicitly via default; subsequent ops that
            // read SubView(non-zero, ...) leaked their offset back into Gather's read).
            const offsetLoc = getUniformLoc(cached, uniformPrefix + '_offset');
            if (offsetLoc !== null) gl.uniform1i(offsetLoc, (p.elementOffset | 0));

            // Stride uniforms for ArrayView2D/3D
            if (strides && strides[p.paramIndex]) {
                const dims = strides[p.paramIndex];
                const strideLoc = getUniformLoc(cached, uniformPrefix + '_stride[0]');
                if (strideLoc !== null) {
                    gl.uniform1iv(strideLoc, new Int32Array(dims));
                }
            }

            bufferParamMap.push({ bufferId: p.bufferId, paramIndex: p.paramIndex });

        } else if (p.kind === 'scalar') {
            const uniformName = resolveParamPrefix(p.paramIndex);
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
            const emuPrefix = resolveParamPrefix(p.paramIndex);
            const loLoc = getUniformLoc(cached, emuPrefix + '_lo');
            const hiLoc = getUniformLoc(cached, emuPrefix + '_hi');
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
        const tfElemCount = totalVertices * vNames.length;
        // Read as raw bytes via Uint8Array — passing Float32Array as the destination
        // makes Chrome canonicalize specific bit patterns through the float view
        // (observed 2026-05-04: writing 0x80000000u via TF reads back as 0x80000001
        // when getBufferSubData destination is Float32Array; Uint8Array preserves
        // the bit pattern exactly). Tests23_BareUintShift / NormalizeShape_BareCondition
        // surfaced this — see _DevComms/SpawnDev.ILGPU.
        const readbackBytes = new Uint8Array(tfElemCount * 4);
        gl.getBufferSubData(gl.TRANSFORM_FEEDBACK_BUFFER, 0, readbackBytes);
        const readbackFloat = new Float32Array(readbackBytes.buffer);
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
