'strict';

// SpawnJSInterop - the Javascript half of SpawnJS.
//
// Architecture: every JS value that .Net needs to reference is kept in an id-keyed table
// (spawnJSObjects) and addressed from .Net by that numeric id. .Net never receives a live JS object
// handle (no Microsoft JSObject), only ids and primitives - which is what lets the .Net side avoid
// JSObject and its disposal quirk entirely. .Net "holds" a value (spawnJSObjectHold -> id) and later
// "releases" it (spawnJSObjectRelease) so it can be garbage-collected.
//
// Negative ids are reserved sentinels resolved without a table lookup:
//   -1 globalThis, -2 undefined, -3 null, -4 the spawnJSObjects table, SpawnJSInterop.
//
// Calls flow through _spawnJSInteropCall (sync) / _spawnJSInteropCallAsync (async). The whole call - which static
// method here, the returnType code (see ReturnType.cs), every argument - is written by .Net onto its call tape
// (JSTape.cs) in .Net memory and crosses ONCE: the call reads it straight off the WASM heap and writes the result
// back over the same frame. Each call carries its dotnetId and frame address, so nothing here is shared between
// .Net instances beyond this class.
(function () {
    if (globalThis.SpawnJSInterop) return;
    class SpawnJSInterop {
        // enables verbose logging
        static verbose = globalThis.spawnJSInteropVerbose;
        // ArrayBufferView constructors
        static HeapViewCtors = [
            globalThis.BigInt64Array,                     // 0: BigInt64Array
            globalThis.BigUint64Array,                    // 1: BigUInt64Array
            globalThis.Float16Array,                      // 2: Float16Array
            globalThis.Float32Array,                      // 3: Float32Array
            globalThis.Float64Array,                      // 4: Float64Array
            globalThis.Int16Array,                        // 5: Int16Array
            globalThis.Int32Array,                        // 6: Int32Array
            globalThis.Int8Array,                         // 7: Int8Array
            globalThis.Uint16Array,                       // 8: Uint16Array
            globalThis.Uint32Array,                       // 9: Uint32Array
            globalThis.Uint8Array,                        // 10: Uint8Array
            globalThis.Uint8ClampedArray,                 // 11: Uint8ClampedArray
            globalThis.DataView                           // 12: DataView
        ];
        static _revivers = [];
        static _replacers = [];
        // method names for index based calling as an alternative to string
        static _methodMapNames = [];
        // methods mapped by index for index based calling as an alternative to string
        static _methodMap = [];
        // The id -> JS value table. Holds every value .Net currently references.
        static spawnJSObjects = {};
        // Monotonic id source; never reused, so a stale .Net id can never collide with a live value.
        static _sjsObjectIdNext = 0;
        // .Net Wasm app instance infos
        static _instances = {};
        // .Net callbacks
        static _callbacks = {};
        static _detachedSentinelId = null;
        // static constructor
        static {
            // SpawnJSInterop.registerReviver('__reviverTest', (key, value) => {
            //     if (SpawnJSInterop.verbose) console.log('[SpawnJSInterop] reviver test', key, value);
            //     return value;
            // });
            // SpawnJSInterop.registerReplacer('__replacerTest', (key, value) => {
            //     if (SpawnJSInterop.verbose) console.log('[SpawnJSInterop] replacer test', key, value);
            //     return value;
            // });
            SpawnJSInterop.refreshMethodMap();
            // detachedHeap sentinel
            this._detachedSentinelId = setInterval(() => SpawnJSInterop.detachedEventCheck(), 500);
        }

        // JSImports
        // Methods accessed via JSImport only send and recieve basic
        // data types and are used to support the marshalling framework
        // that allows working with much more advanced data types.

        // JSImport
        // double _registerInstance(
        //   JSObject dotnetInstance,
        //   [JSMarshalAs<JSType.Function>] Action onMethodAdded,
        //   [JSMarshalAs<JSType.Function<JSType.Number, JSType.String>>] Action<double, string> onAsyncResolvedVoid,
        //   [JSMarshalAs<JSType.Function<JSType.Number, JSType.Number, JSType.String>>] Action<double, double, string> onAsyncResolvedDouble,
        //   [JSMarshalAs<JSType.Function<JSType.Number, JSType.Boolean, JSType.String>>] Action<double, bool, string> onAsyncResolvedBool,
        //   [JSMarshalAs<JSType.Function<JSType.Number, JSType.String, JSType.String>>] Action<double, string, string> onAsyncResolvedString,
        //   [JSMarshalAs<JSType.Function<JSType.Number, JSType.Any, JSType.String>>] Action<double, object, string> onAsyncResolvedDoubleNullable,
        //   [JSMarshalAs<JSType.Function<JSType.Number, JSType.Any, JSType.String>>] Action<double, object, string> onAsyncResolvedBooleanNullable,
        //   [JSMarshalAs<JSType.Function<JSType.Number, JSType.Number, JSType.String>>] Action<double, int, string> onAsyncResolvedInt32,
        //   [JSMarshalAs<JSType.Function<JSType.Number, JSType.Any, JSType.String>>] Action<double, object, string> onAsyncResolvedInt32Nullable,
        //   [JSMarshalAs<JSType.Function<JSType.Number, JSType.Number>>] Action<long, long> onDetachedHeap,
        //   [JSMarshalAs<JSType.Function<JSType.Number, JSType.Number, JSType.Number>>] Action<double, double, double> onCallback,
        //   [JSMarshalAs<JSType.Function<JSType.Number, JSType.Number, JSType.String>>] Action<double, int, string> onAsyncResolvedTape)
        static _registerInstance(
            dotnet,
            onMethodAdded,
            resolveVoid,
            resolveDouble,
            resolveBoolean,
            resolveString,
            resolveDoubleNullable,
            resolveBooleanNullable,
            resolveInt32,
            resolveInt32Nullable,
            onDetachedHeap,
            handleCallback,
            resolveTape
            ) {
            if (!dotnet) throw new Error('dotnet not set');
            var instanceInfo = SpawnJSInterop._getInstaceFromDotNet(dotnet);
            if (instanceInfo) return instanceInfo.dotnetId;

            var dotnetId = SpawnJSInterop.spawnJSObjectHold(dotnet);

            // attach the heap buffer to instanceInfo so it can be monitored for detach events
            var heapBuffer = SpawnJSInterop.wasmMemoryBuffer(dotnet);

            var instanceInfo = { heapBuffer, dotnet, dotnetId, onMethodAdded, resolveVoid, resolveDouble, resolveBoolean, resolveString, resolveDoubleNullable, resolveBooleanNullable, resolveInt32, resolveInt32Nullable, handleCallback, onDetachedHeap, resolveTape };
            SpawnJSInterop._instances[dotnetId] = instanceInfo;

            instanceInfo.heapBufferSize = heapBuffer.byteLength;
            instanceInfo.getHeap = () => {
                if (instanceInfo.heapBuffer.detached) {
                    if (SpawnJSInterop.verbose) console.log(`Detached heap detected for: ${dotnetId}`);
                    // get the new heap buffer
                    instanceInfo.heapBuffer = SpawnJSInterop.wasmMemoryBuffer(dotnet);
                    const oldSize = instanceInfo.heapBufferSize;
                    instanceInfo.heapBufferSize = instanceInfo.heapBuffer.byteLength;
                    // notify the instance
                    instanceInfo.onDetachedHeap(oldSize, instanceInfo.heapBuffer.byteLength);
                }
                return instanceInfo.heapBuffer;
            };

            if (SpawnJSInterop.verbose) console.log('[SpawnJSInterop] Instance registered', instanceInfo);
            return dotnetId;
        }
        // Call tape value tags - JSTape.Tag* in JSTape.cs must match
        static TapeTag = { Undefined: 0, Null: 1, Number: 2, Boolean: 3, String: 4, Ref: 5, Callback: 6, Absent: 8, Object: 9, Shape: 10, Array: 11, Numbers: 12, HeapView: 13, Record: 14, Revive: 15 };
        // TagNumbers kinds - JSTape.NumberKind must match
        static TapeNumberCtors = [Int8Array, Uint8Array, Int16Array, Uint16Array, Int32Array, Uint32Array, Float32Array, Float64Array];
        // what a member written as TagAbsent reads as: the property is not assigned
        static _tapeAbsent = Object.freeze({});

        // Main .Net to JS entrypoint (synchronous)
        // The frame at address holds the whole call (see JSTape.cs). The result is written back over the frame and
        // the return value is its byte length - or minus the bytes it needs when it does not fit in capacity, in
        // which case it is kept on the instance for _spawnJSInteropCallResult.
        // JSImport
        // int _spawnJSInteropCall(double dotnetId, double address, int length, int capacity);
        static _spawnJSInteropCall(dotnetId, address, length, capacity) {
            // no detachedEventCheck(): _tapeViews checks this instance's heap, and walking every OTHER instance on
            // every call was redundant - each is checked on its own calls and by the 500 ms timer
            var instance = SpawnJSInterop.getInstace(dotnetId);
            var call = SpawnJSInterop._tapeReadCall(instance, address, length, dotnetId);
            var ret = call.target(...call.args);
            ret = SpawnJSInterop.replaceValue(null, ret, false);
            return SpawnJSInterop._tapeWriteResult(instance, call.returnType, ret, address, capacity);
        }
        // The result that did not fit behind its frame
        // JSImport
        // int _spawnJSInteropCallResult(double dotnetId, double address, int capacity);
        static _spawnJSInteropCallResult(dotnetId, address, capacity) {
            var instance = SpawnJSInterop.getInstace(dotnetId);
            var bytes = instance.tapePendingResult;
            instance.tapePendingResult = undefined;
            if (!bytes) throw new Error('SpawnJSInterop: no tape result is being held');
            if (bytes.byteLength > capacity) throw new Error(`SpawnJSInterop: a held tape result of ${bytes.byteLength} bytes does not fit in ${capacity}`);
            var views = SpawnJSInterop._tapeViews(instance, address + bytes.byteLength);
            new Uint8Array(views.buffer, address, bytes.byteLength).set(bytes);
            return bytes.byteLength;
        }
        // Main .Net to JS entrypoint (asynchronous)
        // Reads the frame and starts the call before returning, because .Net releases the frame as soon as this returns.
        // Returns 1 when the frame was read and the call started, 0 when it could not be read - that failure has already
        // gone to the resolver, so the awaiting Task fails rather than hanging.
        // JSImport
        // int _spawnJSInteropCallAsync(double dotnetId, double asyncCallId, double address, int length);
        static _spawnJSInteropCallAsync(dotnetId, asyncCallId, address, length) {
            var instance = SpawnJSInterop.getInstace(dotnetId);
            var returnType = SpawnJSInterop._tapeViews(instance, address + length).f64[(address >>> 3) + 1];
            var call;
            try {
                call = SpawnJSInterop._tapeReadCall(instance, address, length, dotnetId);
            } catch (ex) {
                SpawnJSInterop._resolveAsync(instance, returnType, asyncCallId, null, SpawnJSInterop.errorToString(ex));
                return 0;
            }
            SpawnJSInterop._runAsync(instance, returnType, asyncCallId, call);
            return 1;
        }
        static async _runAsync(instance, returnType, asyncCallId, call) {
            var error = null;
            var ret = null;
            var heldBytes = -1;
            try {
                ret = call.target(...call.args);
                ret = await ret;
                ret = SpawnJSInterop.replaceValue(null, ret, false);
                if (returnType > 10) {
                    // a composite schema: encoded here and held; .Net fetches it with _spawnJSInteropCallResult
                    heldBytes = SpawnJSInterop._tapeHoldResult(instance, SpawnJSInterop._tapeSchema(instance, returnType), ret);
                } else {
                    // prepare using returnType
                    ret = SpawnJSInterop._serializeToNet(returnType, ret);
                }
            } catch (ex) {
                error = SpawnJSInterop.errorToString(ex);
                ret = null;
            }
            if (heldBytes >= 0) instance.resolveTape(asyncCallId, heldBytes, null);
            else SpawnJSInterop._resolveAsync(instance, returnType, asyncCallId, ret, error);
        }
        static _resolveAsync(instance, returnType, asyncCallId, ret, error) {
            if (returnType > 10) {
                // a composite schema that failed: nothing is held
                instance.resolveTape(asyncCallId, 0, error ?? 'SpawnJSInterop: the call produced no result');
                return;
            }
            switch (returnType) {
                case 0: // void
                    instance.resolveVoid(asyncCallId, error);
                    break;
                case 1: // Double
                    instance.resolveDouble(asyncCallId, ret, error);
                    break;
                case 2: // Boolean
                    instance.resolveBoolean(asyncCallId, ret, error);
                    break;
                case 3: // DoubleNullable
                    instance.resolveDoubleNullable(asyncCallId, ret, error);
                    break;
                case 4: // BooleanNullable
                    instance.resolveBooleanNullable(asyncCallId, ret, error);
                    break;
                case 5: // String
                    instance.resolveString(asyncCallId, ret, error);
                    break;
                case 6: // SpawnJSObject
                    instance.resolveDouble(asyncCallId, ret, error);
                    break;
                case 7: // SpawnJSObjectNonNullable
                    instance.resolveDouble(asyncCallId, ret, error);
                    break;
                case 8: // Json
                    instance.resolveString(asyncCallId, ret, error);
                    break;
                case 9: // Int32
                    instance.resolveInt32(asyncCallId, ret, error);
                    break;
                case 10: // Int32Nullable
                    instance.resolveInt32Nullable(asyncCallId, ret, error);
                    break;
                default:
                    throw new Error(`Unsupported returnType ${returnType}`);
                    break;
            }
        }
        // The heap views a tape is read through. Kept on the instance - never a page global - and rebuilt when the
        // heap buffer changes: growth detaches it, and a shared (threaded) heap's buffer does not detach but can be
        // shorter than the memory, so end is checked too.
        static _tapeViews(instance, end) {
            var buffer = instance.getHeap();
            if (buffer.byteLength < end) {
                buffer = instance.heapBuffer = SpawnJSInterop.wasmMemoryBuffer(instance.dotnet);
                instance.heapBufferSize = buffer.byteLength;
            }
            var views = instance.tapeViews;
            if (!views || views.buffer !== buffer) {
                views = instance.tapeViews = { buffer, f64: new Float64Array(buffer), i32: new Int32Array(buffer), u16: new Uint16Array(buffer) };
            }
            return views;
        }
        // Reads a call frame: header, then one tagged value per argument, each passed through the revivers as the
        // v2 argument array was.
        static _tapeReadCall(instance, address, length, dotnetId) {
            var views = SpawnJSInterop._tapeViews(instance, address + length);
            var f64 = views.f64;
            var p = address >>> 3;
            var methodIndex = f64[p];
            var returnType = f64[p + 1];
            var argCount = f64[p + 2];
            // one reader per call: a nested call (a reviver can run .Net) gets its own
            var reader = { instance, dotnetId, address, length, views, p: p + 3 };
            var args = new Array(argCount);
            for (var i = 0; i < argCount; i++) {
                var value = SpawnJSInterop._tapeRead(reader);
                args[i] = SpawnJSInterop.reviveValue(i, value === SpawnJSInterop._tapeAbsent ? undefined : value, false);
            }
            // then the definitions of any schema the result is written by (JSTape.WriteSchemaDefinitions)
            var end = (address + length) >>> 3;
            while (reader.p < end) SpawnJSInterop._tapeReadSchemaDefinition(reader);
            var target = SpawnJSInterop._methodMap[methodIndex];
            if (!target) throw new Error(`SpawnJSInterop: no method at index ${methodIndex}`);
            return { target, returnType, args };
        }
        // Reads one tagged value at reader.p and advances it. Object members and array elements go through the
        // revivers with their key, as v2's per-property set did.
        static _tapeRead(reader) {
            // a reviver can run .Net (a heap view refresh reports the detach), which can grow the heap
            if (reader.views.buffer.detached) reader.views = SpawnJSInterop._tapeViews(reader.instance, reader.address + reader.length);
            var f64 = reader.views.f64;
            var tag = f64[reader.p++];
            switch (tag) {
                case 0: return undefined;
                case 1: return null;
                case 2: return f64[reader.p++];
                case 3: return f64[reader.p++] !== 0;
                case 4: return SpawnJSInterop._tapeReadChars(reader);
                case 5: return SpawnJSInterop.spawnJSObjectGet(f64[reader.p++]);
                case 6: {
                    var callbackId = f64[reader.p++];
                    var once = f64[reader.p++] !== 0;
                    // the schemas its arguments are written by (defined after this frame's arguments when new)
                    var argumentCount = f64[reader.p++];
                    var argumentSchemas = new Array(argumentCount);
                    for (var i = 0; i < argumentCount; i++) argumentSchemas[i] = f64[reader.p++];
                    return SpawnJSInterop._callbackFunction(reader.dotnetId, callbackId, once, argumentSchemas);
                }
                case 8: return SpawnJSInterop._tapeAbsent;
                case 9: {
                    var names = reader.instance.tapeShapes?.[f64[reader.p]];
                    if (!names) throw new Error(`SpawnJSInterop: unknown tape shape ${f64[reader.p]}`);
                    reader.p++;
                    var obj = {};
                    for (var i = 0; i < names.length; i++) {
                        var value = SpawnJSInterop._tapeRead(reader);
                        if (value !== SpawnJSInterop._tapeAbsent) obj[names[i]] = SpawnJSInterop.reviveValue(names[i], value, false);
                    }
                    return obj;
                }
                case 10: {
                    // a shape definition - kept per instance, since each runtime numbers its own - then the value it
                    // precedes. The same definition can arrive again until .Net knows a call carrying it completed.
                    var shapeId = f64[reader.p++];
                    var count = f64[reader.p++];
                    var names = new Array(count);
                    for (var i = 0; i < count; i++) names[i] = SpawnJSInterop._tapeReadChars(reader);
                    (reader.instance.tapeShapes ??= [])[shapeId] = names;
                    return SpawnJSInterop._tapeRead(reader);
                }
                case 11: {
                    var count = f64[reader.p++];
                    var array = new Array(count);
                    for (var i = 0; i < count; i++) {
                        var value = SpawnJSInterop._tapeRead(reader);
                        array[i] = SpawnJSInterop.reviveValue(i, value === SpawnJSInterop._tapeAbsent ? undefined : value, false);
                    }
                    return array;
                }
                case 12: {
                    var ctor = SpawnJSInterop.TapeNumberCtors[f64[reader.p++]];
                    var count = f64[reader.p++];
                    var view = new ctor(reader.views.buffer, reader.p * 8, count);
                    reader.p += (count * ctor.BYTES_PER_ELEMENT + 7) >>> 3;
                    // a plain Array, as element-by-element writing produced; a TypedArray stays opt in
                    return Array.from(view);
                }
                case 14: {
                    var count = reader.views.f64[reader.p++];
                    var record = {};
                    for (var i = 0; i < count; i++) {
                        var key = SpawnJSInterop._tapeReadChars(reader);
                        var value = SpawnJSInterop._tapeRead(reader);
                        record[key] = SpawnJSInterop.reviveValue(key, value === SpawnJSInterop._tapeAbsent ? undefined : value, false);
                    }
                    return record;
                }
                case 15: {
                    var reviver = SpawnJSInterop._methodMap[reader.views.f64[reader.p++]];
                    if (!reviver) throw new Error('SpawnJSInterop: tape reviver not found');
                    // (key, value, directCall) - the v2 property reviver convention; there is no property key here
                    return reviver(null, SpawnJSInterop._tapeRead(reader), true);
                }
                case 13: {
                    var f = reader.views.f64;
                    var viewType = f[reader.p], offset = f[reader.p + 1], count = f[reader.p + 2], copy = f[reader.p + 3] !== 0;
                    reader.p += 4;
                    return SpawnJSInterop._heapView(reader.dotnetId, viewType, offset, count, copy);
                }
                default: throw new Error(`SpawnJSInterop: unknown tape tag ${tag} at cell ${reader.p - 1}`);
            }
        }
        // length, then UTF-16 code units padded to a cell
        static _tapeReadChars(reader) {
            var length = reader.views.f64[reader.p++];
            var value = SpawnJSInterop._tapeString(reader.views.u16, reader.p * 4, length);
            reader.p += (length + 3) >>> 2;
            return value;
        }
        // String.fromCharCode keeps every UTF-16 code unit exactly - a lone surrogate included, which a TextDecoder
        // would replace. Chunked, because apply() has an argument count limit.
        static _tapeString(u16, index, length) {
            if (length <= 1024) return String.fromCharCode.apply(null, u16.subarray(index, index + length));
            var parts = [];
            for (var i = 0; i < length; i += 1024) {
                parts.push(String.fromCharCode.apply(null, u16.subarray(index + i, index + Math.min(i + 1024, length))));
            }
            return parts.join('');
        }
        // ---- results: written back by the schema .Net reads them with (JSSchema.cs, JSTapeReader.cs) ----
        // Ids 0-10 are the built-in ReturnType kinds; composite schemas are defined per instance, after a call's arguments.
        static _builtinSchemas = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map(kind => Object.freeze({ kind }));
        // thrown by a writer over .Net memory that is out of room (only a string can be; it is written again)
        static _tapeOverflow = Object.freeze({ tapeOverflow: true });
        static _tapeSchema(instance, id) {
            if (id <= 10) return SpawnJSInterop._builtinSchemas[id];
            var schema = instance.tapeSchemas?.[id];
            if (!schema) throw new Error(`SpawnJSInterop: unknown tape schema ${id}`);
            return schema;
        }
        // TagSchema, id, kind, then per kind - member / element schemas are ids, resolved when a value is written, so a
        // schema can refer to one defined after it, or to itself
        static _tapeReadSchemaDefinition(reader) {
            if (reader.views.buffer.detached) reader.views = SpawnJSInterop._tapeViews(reader.instance, reader.address + reader.length);
            var f64 = reader.views.f64;
            var tag = f64[reader.p++];
            if (tag !== 16) throw new Error(`SpawnJSInterop: expected a tape schema definition, found tag ${tag}`);
            var id = f64[reader.p++];
            var schema = { kind: f64[reader.p++] };
            switch (schema.kind) {
                case 16: {
                    var count = f64[reader.p++];
                    schema.names = new Array(count);
                    for (var i = 0; i < count; i++) schema.names[i] = SpawnJSInterop._tapeReadChars(reader);
                    schema.members = new Array(count);
                    for (var i = 0; i < count; i++) schema.members[i] = reader.views.f64[reader.p++];
                    break;
                }
                case 20: {
                    var count = f64[reader.p++];
                    schema.members = new Array(count);
                    for (var i = 0; i < count; i++) schema.members[i] = f64[reader.p++];
                    break;
                }
                case 17: case 19: schema.element = f64[reader.p++]; break;
                case 18: schema.numberKind = f64[reader.p++]; break;
            }
            (reader.instance.tapeSchemas ??= [])[id] = schema;
        }
        // Writes a call's result back over its own frame. A built-in kind goes straight into .Net memory (only a string
        // can outgrow the frame's segment); a composite is written into Javascript memory first, then copied - it can
        // hold references and run getters, so it is written exactly once. A result that does not fit is held for
        // _spawnJSInteropCallResult, and the return value is minus its size.
        static _tapeWriteResult(instance, schemaId, ret, address, capacity) {
            var schema = SpawnJSInterop._tapeSchema(instance, schemaId);
            if (schema.kind === 0) return 0;
            if (schemaId <= 10) {
                var views = SpawnJSInterop._tapeViews(instance, address + capacity);
                var w = { buffer: views.buffer, f64: views.f64, i32: views.i32, u16: views.u16, p: address >>> 3, end: (address + capacity) >>> 3, scratch: false };
                try {
                    SpawnJSInterop._tapeEncode(instance, w, schema, ret);
                    return (w.p - (address >>> 3)) * 8;
                } catch (ex) {
                    if (ex !== SpawnJSInterop._tapeOverflow) throw ex;
                }
            }
            var s = SpawnJSInterop._tapeScratchWriter(instance);
            try {
                SpawnJSInterop._tapeEncode(instance, s, schema, ret);
                var bytes = s.p * 8;
                if (bytes <= capacity) {
                    var heap = SpawnJSInterop._tapeViews(instance, address + bytes);
                    new Uint8Array(heap.buffer, address, bytes).set(new Uint8Array(s.buffer, 0, bytes));
                    return bytes;
                }
                instance.tapePendingResult = new Uint8Array(s.buffer.slice(0, bytes));
                return -bytes;
            } finally {
                SpawnJSInterop._tapeScratchRelease(instance, s);
            }
        }
        // encodes a result and holds it for _spawnJSInteropCallResult; returns its size
        static _tapeHoldResult(instance, schema, value) {
            var s = SpawnJSInterop._tapeScratchWriter(instance);
            try {
                SpawnJSInterop._tapeEncode(instance, s, schema, value);
                instance.tapePendingResult = new Uint8Array(s.buffer.slice(0, s.p * 8));
                return s.p * 8;
            } finally {
                SpawnJSInterop._tapeScratchRelease(instance, s);
            }
        }
        // Javascript-side result buffers, pooled per instance (a nested call's result uses its own)
        static _tapeScratchWriter(instance) {
            var w = instance.tapeScratch?.pop();
            if (!w) {
                w = { scratch: true, p: 0 };
                SpawnJSInterop._tapeScratchResize(w, 512);
            }
            w.p = 0;
            return w;
        }
        static _tapeScratchRelease(instance, w) {
            // a very large one is dropped rather than kept for the life of the page
            if (w.end <= 1 << 20) (instance.tapeScratch ??= []).push(w);
        }
        static _tapeScratchResize(w, cells) {
            var buffer = new ArrayBuffer(cells * 8);
            if (w.buffer) new Uint8Array(buffer).set(new Uint8Array(w.buffer, 0, w.p * 8));
            w.buffer = buffer;
            w.f64 = new Float64Array(buffer);
            w.i32 = new Int32Array(buffer);
            w.u16 = new Uint16Array(buffer);
            w.end = cells;
        }
        static _tapeNeed(w, cells) {
            if (w.p + cells <= w.end) return;
            if (!w.scratch) throw SpawnJSInterop._tapeOverflow;
            SpawnJSInterop._tapeScratchResize(w, Math.max(w.end * 2, w.p + cells));
        }
        // One value by its schema. The built-in kinds convert exactly as v2's typed returns did: null/undefined read
        // as 0 / false, a number is stored through a Float64Array (ToNumber) and an int through an Int32Array (ToInt32).
        // A composite reads each member / element with that member's schema - what v2's per-property Get<T> read.
        static _tapeEncode(instance, w, schema, v) {
            var isNull = v === null || v === undefined;
            switch (schema.kind) {
                case 0: return;
                case 1:
                    SpawnJSInterop._tapeNeed(w, 1);
                    w.f64[w.p++] = isNull ? 0 : v;
                    return;
                case 2:
                    SpawnJSInterop._tapeNeed(w, 1);
                    w.f64[w.p++] = v ? 1 : 0;
                    return;
                case 3:
                    if (isNull) { SpawnJSInterop._tapeNeed(w, 1); w.f64[w.p++] = 1; return; }
                    SpawnJSInterop._tapeNeed(w, 2);
                    w.f64[w.p++] = 2;
                    w.f64[w.p++] = v;
                    return;
                case 4:
                    if (isNull) { SpawnJSInterop._tapeNeed(w, 1); w.f64[w.p++] = 1; return; }
                    SpawnJSInterop._tapeNeed(w, 2);
                    w.f64[w.p++] = 3;
                    w.f64[w.p++] = v ? 1 : 0;
                    return;
                case 5:
                    if (!isNull && typeof v !== 'string') v = Object(v).toString();
                    SpawnJSInterop._tapeEncodeString(w, isNull ? null : v);
                    return;
                case 6: case 7:
                    SpawnJSInterop._tapeNeed(w, 1);
                    w.f64[w.p++] = SpawnJSInterop.spawnJSObjectHold(v);
                    return;
                case 8:
                    SpawnJSInterop._tapeEncodeString(w, JSON.stringify(v));
                    return;
                case 9:
                    SpawnJSInterop._tapeNeed(w, 1);
                    w.i32[w.p * 2] = isNull ? 0 : v;
                    w.p++;
                    return;
                case 10:
                    if (isNull) { SpawnJSInterop._tapeNeed(w, 1); w.f64[w.p++] = 1; return; }
                    SpawnJSInterop._tapeNeed(w, 2);
                    w.f64[w.p] = 2;
                    w.i32[(w.p + 1) * 2] = v;
                    w.p += 2;
                    return;
                case 16: {
                    if (isNull) { SpawnJSInterop._tapeNeed(w, 1); w.f64[w.p++] = 1; return; }
                    SpawnJSInterop._tapeNeed(w, 1);
                    w.f64[w.p++] = 9;
                    var names = schema.names, members = schema.members;
                    for (var i = 0; i < names.length; i++) SpawnJSInterop._tapeEncode(instance, w, SpawnJSInterop._tapeSchema(instance, members[i]), v[names[i]]);
                    return;
                }
                case 17: {
                    if (isNull) { SpawnJSInterop._tapeNeed(w, 1); w.f64[w.p++] = 1; return; }
                    var count = Number(v.length) || 0;
                    SpawnJSInterop._tapeNeed(w, 2);
                    w.f64[w.p++] = 11;
                    w.f64[w.p++] = count;
                    var element = SpawnJSInterop._tapeSchema(instance, schema.element);
                    for (var i = 0; i < count; i++) SpawnJSInterop._tapeEncode(instance, w, element, v[i]);
                    return;
                }
                case 18: {
                    if (isNull) { SpawnJSInterop._tapeNeed(w, 1); w.f64[w.p++] = 1; return; }
                    var count = Number(v.length) || 0;
                    var int32 = schema.numberKind === 9;
                    var cells = int32 ? (count + 1) >>> 1 : count;
                    SpawnJSInterop._tapeNeed(w, 2 + cells);
                    w.f64[w.p++] = 12;
                    w.f64[w.p++] = count;
                    // element by element, not typedArray.set(): an undefined element must read as 0, as Get<T> read it
                    if (int32) {
                        var view = new Int32Array(w.buffer, w.p * 8, count);
                        for (var i = 0; i < count; i++) { var e = v[i]; view[i] = e === null || e === undefined ? 0 : e; }
                    } else {
                        var view = new Float64Array(w.buffer, w.p * 8, count);
                        for (var i = 0; i < count; i++) { var e = v[i]; view[i] = e === null || e === undefined ? 0 : e; }
                    }
                    w.p += cells;
                    return;
                }
                case 19: {
                    if (isNull) { SpawnJSInterop._tapeNeed(w, 1); w.f64[w.p++] = 1; return; }
                    // own enumerable keys - inherited ones belong to the prototype chain, not the record
                    var keys = Object.keys(v);
                    SpawnJSInterop._tapeNeed(w, 2);
                    w.f64[w.p++] = 14;
                    w.f64[w.p++] = keys.length;
                    var element = SpawnJSInterop._tapeSchema(instance, schema.element);
                    for (var i = 0; i < keys.length; i++) {
                        SpawnJSInterop._tapeEncodeChars(w, keys[i]);
                        SpawnJSInterop._tapeEncode(instance, w, element, v[keys[i]]);
                    }
                    return;
                }
                case 20: {
                    if (isNull) { SpawnJSInterop._tapeNeed(w, 1); w.f64[w.p++] = 1; return; }
                    var members = schema.members;
                    SpawnJSInterop._tapeNeed(w, 2);
                    w.f64[w.p++] = 11;
                    w.f64[w.p++] = members.length;
                    for (var i = 0; i < members.length; i++) SpawnJSInterop._tapeEncode(instance, w, SpawnJSInterop._tapeSchema(instance, members[i]), v[i]);
                    return;
                }
                case 21: {
                    if (isNull) { SpawnJSInterop._tapeNeed(w, 1); w.f64[w.p++] = 1; return; }
                    var bytes = v instanceof ArrayBuffer ? new Uint8Array(v)
                        : ArrayBuffer.isView(v) ? new Uint8Array(v.buffer, v.byteOffset, v.byteLength)
                        : null;
                    if (!bytes) throw new TypeError('SpawnJSInterop: expected an ArrayBuffer or ArrayBufferView');
                    var cells = (bytes.length + 7) >>> 3;
                    SpawnJSInterop._tapeNeed(w, 2 + cells);
                    w.f64[w.p++] = 12;
                    w.f64[w.p++] = bytes.length;
                    new Uint8Array(w.buffer, w.p * 8, bytes.length).set(bytes);
                    w.p += cells;
                    return;
                }
            }
            throw new Error(`SpawnJSInterop: unsupported tape schema kind ${schema.kind}`);
        }
        // null, or tag, length, UTF-16 code units
        static _tapeEncodeString(w, value) {
            if (value === null || value === undefined) {
                SpawnJSInterop._tapeNeed(w, 1);
                w.f64[w.p++] = 1;
                return;
            }
            SpawnJSInterop._tapeNeed(w, 1);
            w.f64[w.p++] = 4;
            SpawnJSInterop._tapeEncodeChars(w, value);
        }
        // length, UTF-16 code units padded to a cell
        static _tapeEncodeChars(w, value) {
            var length = value.length;
            SpawnJSInterop._tapeNeed(w, 1 + ((length + 3) >>> 2));
            w.f64[w.p++] = length;
            var u16 = w.u16;
            var index = w.p * 4;
            for (var i = 0; i < length; i++) u16[index + i] = value.charCodeAt(i);
            w.p += (length + 3) >>> 2;
        }
        // refreshes the method map by looking for any new methods and adds them
        // JSImport
        // string[] _refreshMethodMap();
        static refreshMethodMap() {
            var changed = false;
            var keys = Reflect.ownKeys(SpawnJSInterop);
            for (const pName of keys) {
                if (SpawnJSInterop._methodMapNames.indexOf(pName) !== -1) continue;
                var propVal = SpawnJSInterop[pName];
                if (typeof propVal === 'function') {
                    var fn = propVal.bind(SpawnJSInterop);
                    changed = true;
                    SpawnJSInterop._methodMap.push(fn);
                    SpawnJSInterop._methodMapNames.push(pName);
                    if (pName.indexOf('__reviver') === 0) {
                        SpawnJSInterop._revivers.push(fn);
                    } else if (pName.indexOf('__replacer') === 0) {
                        SpawnJSInterop._replacers.push(fn);
                    }
                }
            }
            if (changed) {
                // notify existing instances
                for (const dotnetIdExisting in SpawnJSInterop._instances) {
                    if (Object.hasOwn(SpawnJSInterop._instances, dotnetIdExisting)) {
                        var existingInfo = SpawnJSInterop._instances[dotnetIdExisting];
                        try {
                            existingInfo.onMethodAdded();
                        } catch { }
                    }
                }
            }
            return SpawnJSInterop._methodMapNames;
        }
        // JSImport
        // void _releaseCallback(double dotnetId, double callbackId);
        static releaseCallback(dotnetId, callbackId) {
            var callbackIdPair = `${dotnetId}_${callbackId}`;
            delete SpawnJSInterop._callbacks[callbackIdPair];
        }
        // removes the object from the hold and returns it
        // JSImport
        // void SpawnJSObjectRelease(double sjsId);
        // bool SpawnJSObjectReleaseBoolean(double sjsId);
        // double SpawnJSObjectReleaseDouble(double sjsId);
        // int SpawnJSObjectReleaseInt32(double sjsId);
        // bool? SpawnJSObjectReleaseBooleanNullable(double sjsId);
        // int? SpawnJSObjectReleaseInt32Nullable(double sjsId);
        // double? SpawnJSObjectReleaseDoubleNullable(double sjsId);
        static spawnJSObjectRelease(sjsId) {
            var ret = SpawnJSInterop.spawnJSObjectGet(sjsId);
            delete SpawnJSInterop.spawnJSObjects[sjsId];
            if (SpawnJSInterop.verbose) console.log('spawnJSObjectRelease. Count:', Object.keys(SpawnJSInterop.spawnJSObjects).length, 'Id:', sjsId);
            return ret;
        }
        // removes the object from the hold, JSON.stringifies it and returns it
        // JSImport
        // string SpawnJSObjectReleaseJson(double sjsId);
        static spawnJSObjectReleaseAsJson(sjsId) {
            var ret = SpawnJSInterop.spawnJSObjectGet(sjsId);
            delete SpawnJSInterop.spawnJSObjects[sjsId];
            return JSON.stringify(ret);
        }
        // returns true if the item id exists in the hold
        // JSImport
        // bool SpawnJSObjectHoldExists(double sjsId);
        static spawnJSObjectHoldExists(sjsId) {
            switch (sjsId) {
                case -1: return true;
                case -2: return true;
                case -3: return true;
                case -4: return true;
                case -5: return true;
            }
            return sjsId in SpawnJSInterop.spawnJSObjects;
        }
        // returns string
        // JSImport
        // string _getTypeInfo(double sjsId);
        static getTypeInfo(sjsId) {
            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
            var jsClass = Object.prototype.toString.call(obj).split(' ')[1].slice(0, -1);
            var jsType = typeof (obj);
            return `${jsType} ${jsClass}`;
        }

        // **************************************************************************************
        // SpawnJSObjectReference imports
        // Note: only `string key` imports shown. Also exists: `double key`, `int key`
        // **************************************************************************************

        // property info
        // returns string or null if the property was not found
        // JSImport
        // string _propertyTypeInfo(double sjsId, string key);
        static propertyTypeInfo(sjsId, key) {
            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
            if (shortCircuit) return null;
            if (!SpawnJSInterop._in(propertyName, parent)) return null;
            var value = parent[propertyName];
            var jsClass = Object.prototype.toString.call(value).split(' ')[1].slice(0, -1);
            var jsType = typeof (value);
            return `${jsType} ${jsClass}`;
        }
        // deletes property
        // JSImport
        // bool _propertyDelete(double sjsId, string key);
        static propertyDelete(sjsId, key) {
            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
            if (shortCircuit) return true;
            return delete parent[propertyName];
        }
        // returns bool if the key is in the target
        // JSImport
        // bool _propertyIn(double sjsId, string key);
        static propertyIn(sjsId, key) {
            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
            if (shortCircuit) return false;
            return SpawnJSInterop._in(propertyName, parent);
        }
        // get a property
        // InteropCall (dispatched through the call tape)
        static propertyGet(sjsId, key) {
            var ret = undefined;
            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
            var pathInfo = SpawnJSInterop.pathObjectInfo(obj, key);
            if (pathInfo.shortCircuit) return ret;
            if (typeof pathInfo.target === 'function') {
                ret = pathInfo.target.bind(pathInfo.parent);
            } else {
                ret = pathInfo.target;
            }
            return ret;
        }
        // set property
        // InteropCall (dispatched through the call tape)
        static propertySet(sjsId, key, value) {
            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
            if (shortCircuit) return;
            // revivers
            value = SpawnJSInterop.reviveValue(propertyName, value, false);
            parent[propertyName] = value;
        }
        // A view of - or with copy, a copy of - .Net memory. A live view carries its _heapViewInfo so the heap view
        // reviver can rebuild it after the heap grows. Built when the tape reads a TagHeapView.
        static _heapView(dotnetId, viewType, offset, length, copy) {
            // create the heapView meta data
            var heapViewInfo = { dotnetId, viewType, offset, length, copy };
            // viewType is an INDEX into HeapViewCtors, and index 0 (BigInt64Array) is a real value - a
            // falsy check would read it as "missing" and silently rewrite it. The default is the
            // Uint8Array index, not its name: this value is used as an index, never as a lookup key.
            if (heapViewInfo.viewType === undefined || heapViewInfo.viewType === null) heapViewInfo.viewType = 10;
            heapViewInfo.instance = SpawnJSInterop.getInstace(heapViewInfo.dotnetId);
            heapViewInfo.dotnet = SpawnJSInterop.spawnJSObjectGet(heapViewInfo.dotnetId);
            heapViewInfo.sizeHistory = [];
            heapViewInfo.ctor = SpawnJSInterop.getArrayBufferViewConstructor(heapViewInfo.viewType);
            // refresh the heapView
            return SpawnJSInterop.heapViewRefresh(heapViewInfo);
        }
        // The Javascript function that invokes a .Net Callback, created once per callback and reused
        // .Net gives each instance a pinned buffer that a callback's arguments are written into before it calls .Net
        // JSImport
        // void _registerInbound(double dotnetId, double address, int cells);
        static _registerInbound(dotnetId, address, cells) {
            // cell 0 carries the offset of the arguments of the callback being called
            SpawnJSInterop.getInstace(dotnetId).inbound = { address, cells, top: 1 };
        }
        // Writes a callback's arguments by their schemas into the instance's inbound buffer, as a stack (a callback can
        // run inside another), and puts their offset in cell 0. Arguments too large for the space left are held for
        // _spawnJSInteropCallResult instead, and cell 0 is minus their size. Returns the top to restore afterwards.
        static _tapeWriteInbound(instance, argumentSchemas, args) {
            var inbound = instance.inbound;
            var top = inbound.top;
            var s = SpawnJSInterop._tapeScratchWriter(instance);
            try {
                for (var i = 0; i < argumentSchemas.length; i++) {
                    SpawnJSInterop._tapeEncode(instance, s, SpawnJSInterop._tapeSchema(instance, argumentSchemas[i]), args[i]);
                }
                var views = SpawnJSInterop._tapeViews(instance, inbound.address + inbound.cells * 8);
                var offset;
                if (top + s.p <= inbound.cells) {
                    new Uint8Array(views.buffer, inbound.address + top * 8, s.p * 8).set(new Uint8Array(s.buffer, 0, s.p * 8));
                    offset = top;
                    inbound.top = top + s.p;
                } else {
                    instance.tapePendingResult = new Uint8Array(s.buffer.slice(0, s.p * 8));
                    offset = -(s.p * 8);
                }
                views.f64[inbound.address >>> 3] = offset;
            } finally {
                SpawnJSInterop._tapeScratchRelease(instance, s);
            }
            return top;
        }
        static _callbackFunction(dotnetId, callbackId, once, argumentSchemas) {
            if (!callbackId || !dotnetId) {
                return null;
            }
            // get callback id that is globally unique
            var callbackIdPair = `${dotnetId}_${callbackId}`;
            // check if it exists and createa new function if not
            var value = SpawnJSInterop._callbacks[callbackIdPair];
            if (!value) {
                value = function (...args) {
                    // check if the callback has been removed
                    if (!SpawnJSInterop._callbacks[callbackIdPair]) {
                        return;
                    }
                    // get the SpawnJSRuntime instance's method that is used to report the callback 
                    var instance = SpawnJSInterop.getInstace(dotnetId);
                    var { handleCallback } = instance;
                    // get argsCnt because after when we call handleCallback .Net will write
                    // the return value to at the end of the array after the last argument (index argsCnt)
                    // unless the return value should be undefined
                    var argsCnt = args.length;
                    // get a temporary hold of the args (released after we notify SpawnJSRuntime)
                    var argsId = SpawnJSInterop.spawnJSObjectHold(args);
                    // ⚠️ try/finally, NOT straight-line. .Net deliberately does not release this slot
                    // itself - that would cost an extra crossing per callback - so the ONLY release is
                    // here. If handleCallback throws (any exception escaping the .Net handler crosses
                    // back through here) a straight-line release is SKIPPED and the args array is
                    // stranded in spawnJSObjects for the life of the page. Same for the `once` cleanup:
                    // a one-shot callback that threw would never be removed and would keep firing.
                    // MEASURED 2026-09-02: dumping the table mid-run in the ML demo showed ~30 stranded
                    // empty arrays alongside the retained adapters.
                    // the arguments go to .Net memory now, so the call below is the only crossing they need
                    var inboundTop = SpawnJSInterop._tapeWriteInbound(instance, argumentSchemas ?? [], args);
                    try {
                        // notify SpawnJSRuntime with the argsId and the cnt
                        handleCallback(callbackId, argsId, argsCnt);
                    } finally {
                        instance.inbound.top = inboundTop;
                        // release the args
                        SpawnJSInterop.spawnJSObjectRelease(argsId);
                        // if it was a 1 time use callback, release it
                        if (once) delete SpawnJSInterop._callbacks[callbackIdPair];
                    }
                    // return what is in index argsCnt (the designated place .Net will write to if there is a return value)
                    return args[argsCnt];
                };
                SpawnJSInterop._callbacks[callbackIdPair] = value;
            }
            return value;
        }

        // Interop Calls
        // Interop calls are calls made indirectly via _spawnJSInteropCall and _spawnJSInteropCallAsync.
        // Because they go through those methods they can return and recieve any data type the marshallers support

        // call a property constructor
        // InteropCall
        // SpawnJSObjectReference
        // <double, string, object?[]?, SpawnJSObjectReference>
        static propertyNewApply(sjsId, key, args) {
            var ret = undefined;
            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
            var pathInfo = SpawnJSInterop.pathObjectInfo(obj, key);
            if (pathInfo.shortCircuit) return ret;
            var ret = !args ? new pathInfo.target() : new pathInfo.target(...args);
            return ret;
        }
        // call a property constructor
        // InteropCall
        // SpawnJSObjectReference
        // <double, string, ..., T>
        static propertyNew(sjsId, key, ...args) {
            var ret = undefined;
            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
            var pathInfo = SpawnJSInterop.pathObjectInfo(obj, key);
            if (pathInfo.shortCircuit) return ret;
            var ret = !args ? new pathInfo.target() : new pathInfo.target(...args);
            return ret;
        }
        // call a property
        // InteropCall
        // SpawnJSObjectReference
        // <double, string, object?[]?, T>
        static propertyCallApply(sjsId, key, args) {
            var ret = undefined;
            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
            var pathInfo = SpawnJSInterop.pathObjectInfo(obj, key);
            if (pathInfo.shortCircuit) return ret;
            var ret = pathInfo.target.apply(pathInfo.parent, args);
            if (typeof ret === 'function') {
                ret = ret.bind(pathInfo.parent);
            }
            return ret;
        }
        // call a property
        // InteropCall
        // SpawnJSObjectReference
        // <double, string, ..., T>
        static propertyCall(sjsId, key, ...args) {
            var ret = undefined;
            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
            var pathInfo = SpawnJSInterop.pathObjectInfo(obj, key);
            if (pathInfo.shortCircuit) return ret;
            var ret = pathInfo.target.apply(pathInfo.parent, args);
            if (typeof ret === 'function') {
                ret = ret.bind(pathInfo.parent);
            }
            return ret;
        }
        // useful when .Net Wasm wants to clonme a JSObjectReference or simply convert from one type to another
        // InteropCall
        // SpawnJSObjectReference
        // <TIn, TResult>
        static returnMe(value) {
            return value;
        }
        // InteropCall
        // ByteArrayMarshaller
        // <double, SpawnJSObjectReference, double, double, double, VoidType>
        static writeArrayBufferViewToHeap(dotnetId, arrayBufferView, srcOffset, destOffset, byteLength) {
            if (!arrayBufferView) throw new Error('writeArrayBufferViewToHeap arrayBufferView is required');
            if (byteLength === 0) return 0;
            var srcLength = arrayBufferView.byteLength;
            if (byteLength == -1) byteLength = srcLength;
            if (byteLength < -1) throw new Error('Invalid byteLength');
            // get the .Net heap
            var instance = SpawnJSInterop.getInstace(dotnetId);
            var buffer = instance.getHeap();
            var bufferView = new Uint8Array(buffer, destOffset, byteLength);
            // get a view of the exact source we want
            var offset = arrayBufferView.byteOffset + srcOffset;
            var sourceView = new Uint8Array(arrayBufferView.buffer, offset, byteLength);
            // copy to the .Net heap
            bufferView.set(sourceView);
            // return the bytes copied
            return byteLength;
        }
        // called using Call<>
        // InteropCall
        // TaskMarshaller
        // <double, int, SpawnJSObjectReference>
        static propertySetNewPromise(sjsId, key) {
            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
            if (shortCircuit) return;
            var promise = SpawnJSInterop.newEasyPromise();
            parent[propertyName] = promise;
            return promise;
        }
        // called using Call<>
        // InteropCall
        // TaskMarshaller
        // <double, string, VoidType>
        static propertySetResolvedPromise(sjsId, key, value) {
            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
            if (shortCircuit) return;
            parent[propertyName] = Promise.resolve(value);
        }
        // called using Call<>
        // InteropCall
        // TaskMarshaller
        // <double, string, string, VoidType>
        static propertySetRejectedPromise(sjsId, key, value) {
            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
            if (shortCircuit) return;
            parent[propertyName] = Promise.reject(value);
        }
        // InteropCall
        // SpawnJSRuntime
        // <double, long>
        static getHeapSize(dotnetId) {
            var instance = SpawnJSInterop.getInstace(dotnetId);
            var buffer = instance.getHeap();
            return buffer.byteLength;
        }
        // full ? strict equality : loose equality
        // InteropCall
        // SpawnJSRuntime
        // <T1, T2, bool, bool>
        static objectEquals(obj1, obj2, full) {
            return full ? obj1 === obj2 : obj1 == obj2;
        }

        static detachedEventCheck() {
            for (const dotnetIdExisting in SpawnJSInterop._instances) {
                if (Object.hasOwn(SpawnJSInterop._instances, dotnetIdExisting)) {
                    var instanceInfo = SpawnJSInterop._instances[dotnetIdExisting];
                    instanceInfo.getHeap();
                }
            }
        }
        static getInstace(dotnetId) {
            return SpawnJSInterop._instances[dotnetId];
        }
        static _getInstaceFromDotNet(dotnet) {
            for (const dotnetIdExisting in SpawnJSInterop._instances) {
                if (Object.hasOwn(SpawnJSInterop._instances, dotnetIdExisting)) {
                    var existingInfo = SpawnJSInterop._instances[dotnetIdExisting];
                    if (existingInfo.dotnet == dotnet) {
                        return existingInfo;
                    }
                }
            }
        }
        static __replacerJson(key, value, directCall) {
            if (directCall) value = JSON.stringify(value);
            return value;
        }
        static __reviverJson(key, value, directCall) {
            if (directCall) value = JSON.parse(value);
            return value;
        }
        static _unregisterInstance(dotnetId) {
            delete SpawnJSInterop._instances[dotnetId];
        }
        static _getMappedMethodNames() {
            return SpawnJSInterop._methodMapNames;
        }
        // array of { name, reviver }
        static registerReplacers(replacers) {
            var cnt = 0;
            if (!replacers) return cnt;
            for (var replacerObj of replacers) {
                var succ = SpawnJSInterop.registerReplacer(replacerObj.name, replacerObj.replacer);
                if (succ) cnt++;
            }
            return cnt;
        }
        static registerReplacer(name, replacer) {
            if (name.indexOf('__replacer') !== 0) throw new Error('Replacer names must start with __replacer');
            if (SpawnJSInterop[name]) {
                // already exists. fail quitely as it could just mean another app loaded that uses the same replacers
                return false;
            }
            SpawnJSInterop[name] = replacer;
            SpawnJSInterop.refreshMethodMap();
            return true;
        }
        // array of { name, reviver }
        static registerRevivers(revivers) {
            var cnt = 0;
            if (!revivers) return cnt;
            for (var reviverObj of revivers) {
                var succ = SpawnJSInterop.registerReviver(reviverObj.name, reviverObj.reviver);
                if (succ) cnt++;
            }
            return cnt;
        }
        static registerReviver(name, reviver) {
            if (name.indexOf('__reviver') !== 0) throw new Error('Reviver names must start with __reviver');
            if (SpawnJSInterop[name]) {
                // already exists. fail quitely as it could just mean another app loaded that uses the same revivers
                return false;
            }
            SpawnJSInterop[name] = reviver;
            SpawnJSInterop.refreshMethodMap();
            return true;
        }
        // puts objectToHold (value of ANY type) into spawnJSObjects and returns the id
        // the object will stay held until released and it must be released to prevent memory leaks
        static spawnJSObjectHold(objectToHold) {
            if (objectToHold === globalThis) return -1;
            if (objectToHold === undefined) return -2;
            if (objectToHold === null) return -3;
            if (objectToHold === SpawnJSInterop.spawnJSObjects) return -4;
            if (objectToHold === SpawnJSInterop) return -5;
            var sjsId = ++SpawnJSInterop._sjsObjectIdNext;
            SpawnJSInterop.spawnJSObjects[sjsId] = objectToHold;
            if (SpawnJSInterop.verbose) console.log('spawnJSObjectHold. Count:', Object.keys(SpawnJSInterop.spawnJSObjects).length, 'Id:', sjsId, 'Object:', objectToHold);
            return sjsId;
        }
        // get an object from the hold
        static spawnJSObjectGet(sjsId) {
            switch (sjsId) {
                case -1: return globalThis;
                case -2: return undefined;
                case -3: return null;
                case -4: return SpawnJSInterop.spawnJSObjects;
                case -5: return SpawnJSInterop;
            }
            if (!SpawnJSInterop.spawnJSObjectHoldExists(sjsId)) {
                throw new Error('SpawnJSObjectGet object not found.');
            }
            var ret = SpawnJSInterop.spawnJSObjects[sjsId];
            // passive revive (allows things like refreshing heap views)
            ret = SpawnJSInterop.reviveValue(null, ret, false);
            return ret;
        }// get an obejct from the hold and replace it with a new one
        static spawnJSObjectGetAndReplace(sjsId, newValue) {
            switch (sjsId) {
                case -1: return globalThis;
                case -2: return undefined;
                case -3: return null;
                case -4: return SpawnJSInterop.spawnJSObjects;
                case -5: return SpawnJSInterop;
            }
            if (!SpawnJSInterop.spawnJSObjectHoldExists(sjsId)) {
                throw new Error('SpawnJSObjectGet object not found.');
            }
            var ret = SpawnJSInterop.spawnJSObjects[sjsId];
            SpawnJSInterop.spawnJSObjects[sjsId] = newValue;
            return ret;
        }
        static heapViewRefresh(heapViewInfo) {
            var needsCreate = !heapViewInfo.buffer;
            var needsRefresh = !heapViewInfo.buffer || heapViewInfo.buffer.detached;
            if (needsRefresh) {
                var instance = SpawnJSInterop.getInstace(heapViewInfo.dotnetId);
                heapViewInfo.buffer = instance.getHeap();
                var value = null;
                var length = heapViewInfo.length === -1 ? /* entire buffer */ heapViewInfo.buffer.byteLength - heapViewInfo.offset : heapViewInfo.length;
                if (heapViewInfo.viewType === 13) {
                    // ArrayBuffer requested
                    if (heapViewInfo.copy) {
                        // create a copy
                        value = heapViewInfo.buffer.slice(heapViewInfo.offset, heapViewInfo.offset + length);
                    } else {
                        // can't use offet and length when not copying the heap asn ArrayBuffer view
                        if (heapViewInfo.offset != 0) throw new Error('Offset and length not supported creating an ArrayBuffer heap view without a copy');
                        value = heapViewInfo.buffer;
                    }
                } else if (heapViewInfo.viewType === 14) {
                    // SharedArrayBuffer requested
                    if (heapViewInfo.copy) {
                        // create a copy
                        var uint8ArrayHeap = new Uint8Array(heapViewInfo.buffer, heapViewInfo.offset, length);
                        value = new globalThis.SharedArrayBuffer(length);
                        var uint8ArrayDest = new Uint8Array(value);
                        value.set(uint8ArrayHeap);
                    } else {
                        // can't get a live SharedArrayBuffer view of the heap
                        throw new Error('Cannot get a live SharedArrayBuffer view of the heap');
                    }
                } else {
                    // ArrayBufferView (TypedArray or DataView)
                    var liveView = new heapViewInfo.ctor(heapViewInfo.buffer, heapViewInfo.offset, length);
                    value = heapViewInfo.copy ? liveView.slice() : liveView;
                }
                // copies do not get (or need) their view refreshed as it will not detach
                if (!heapViewInfo.copy) value._heapViewInfo = heapViewInfo;
                heapViewInfo.bufferLength = heapViewInfo.buffer.byteLength;
                heapViewInfo.sizeHistory.push(heapViewInfo.bufferLength);
                heapViewInfo.value = value;
                if (SpawnJSInterop.verbose) {
                    if (!needsCreate) console.log('Heapview refreshed:', heapViewInfo);
                    else console.log('Heapview created:', heapViewInfo);
                }
            }
            return heapViewInfo.value;
        }
        static getArrayBufferViewConstructor(viewType) {
            var ctor = SpawnJSInterop.HeapViewCtors[viewType];
            if (viewType === 13 || viewType === 14) return null;   // ArrayBuffer
            if (!ctor) throw new Error(`Unsupported or missing ArrayBufferView constructor for enum index: ${viewType}`);
            return ctor;
        }
        static __replacerHeapView(key, value, directCall, reviverConfig) {
            if (directCall) {
                if (value && typeof value === 'object' && SpawnJSInterop._in('_heapViewInfo', value)) {
                    // .Net wants HeapViewDescriptor
                    //SpawnJSInterop.heapViewRefresh(heapViewInfo);
                }
            }
            return value;
        }
        static __reviverHeapView(key, value, directCall, reviverConfig) {
            if (!directCall && value && typeof value === 'object' && SpawnJSInterop._in('_heapViewInfo', value)) {
                // auto-reattach if needed
                // this allows creating a fresh HeapView if needed on `set` and `call` (with the exception of call reattach only working if the view is in teh root args list. no object walking is done)
                var heapViewInfo = value._heapViewInfo;
                value = SpawnJSInterop.heapViewRefresh(heapViewInfo);
            }
            return value;
        }
        // tape revivers (JSTape.WriteRevived) for a Task that completed before it was written
        static promiseResolved(key, value) { return Promise.resolve(value); }
        static promiseRejected(key, value) { return Promise.reject(value); }
        // create a new Promsie with the resolve and reject methods attached to the promise for easy calling from .Net
        static newEasyPromise() {
            var _resolve = null;
            var _reject = null;
            var promise = new Promise((resolve, reject) => {
                _resolve = resolve;
                _reject = reject;
            });
            promise.resolve = _resolve;
            promise.reject = _reject;
            return promise
        }
        // Reviver used by BigIntegerMarshaller: a BigInteger crosses as its decimal string, because a JS
        // number cannot hold one exactly, and is revived into a real BigInt here.
        // Revivers are called as (key, value, directCall) by the tape (TagRevive, JSTape.WriteRevived) - a plain
        // (value) signature would silently revive the PROPERTY NAME instead of the value.
        static stringToBigInt(key, value) {
            if (!globalThis.BigInt) throw new Error('BigInt not supported on this platform');
            return value === undefined || value === null ? null : globalThis.BigInt(value);
        }
        // object constructor names
        // returns string[]
        static getConstructorNames(obj) {
            var constructorNames = [];
            if (obj === void 0 || obj === null) return constructorNames;
            var o = obj;
            var cName;
            while (1) {
                o = Object.getPrototypeOf(o);
                cName = o?.constructor?.name;
                if (!cName) break;
                if (constructorNames.indexOf(cName) !== -1) continue;
                constructorNames.push(cName);
            }
            return constructorNames;
        }
        // returns string[] of the target's property names.
        // hasOwnProperty true restricts to the object's own enumerable keys (Object.keys); false walks the
        // prototype chain too, which is what you need to enumerate a DOM object's API rather than just the
        // handful of own properties it happens to carry.
        static objectKeys(target, hasOwnProperty) {
            if (target === void 0 || target === null) return [];
            if (hasOwnProperty) return Object.keys(target);
            var keys = [];
            for (var key in target) {
                if (keys.indexOf(key) === -1) keys.push(key);
            }
            return keys;
        }
        // Pipe the value through each reviver sequentially; a reviver that drops the value (undefined) skips the rest.
        // A plain loop: this runs for every argument of every call, and reduce() allocated a closure each time.
        static reviveValue(key, value, directCall) {
            var revivers = SpawnJSInterop._revivers;
            for (var i = 0; i < revivers.length && value !== undefined; i++) {
                value = revivers[i](key, value, directCall); // directCall tells the reviver this is a call revive as opposed to a propertySet revive
            }
            return value;
        }
        static replaceValue(key, value, directCall) {
            var replacers = SpawnJSInterop._replacers;
            for (var i = 0; i < replacers.length && value !== undefined; i++) {
                value = replacers[i](key, value, directCall);
            }
            return value;
        }
        // returns the types the object inherits from
        // returns string[]
        static getPropertyConstructorNames(parent, key) {
            return SpawnJSInterop.getConstructorNames(parent[key]);
        }
        // converts to an error string or returns a generic error string.
        // A NAMED error (Error subclasses, DOMException - NotFoundError, NotAllowedError, AbortError ... -
        // and OverconstrainedError, which often has an EMPTY message) is sent as
        // "\u0001" + name + "\u0002" + message so .Net can rebuild a JSException with its Name
        // (JSException.FromInteropError). Only the message used to be sent: the name was lost, and an
        // empty-message OverconstrainedError from getUserMedia arrived as a blank exception.
        static errorToString(error) {
            if (!error) return "Unknown error";
            if (typeof error === 'string') return error;
            let name = null;
            let message = null;
            if (typeof error === 'object') {
                try { if (typeof error.name === 'string' && error.name) name = error.name; } catch { }
                try { if (typeof error.message === 'string') message = error.message; } catch { }
                if (message === null && !(error instanceof Error)) {
                    try { message = JSON.stringify(error); } catch { message = String(error); }
                }
                // OverconstrainedError reports the failing constraint separately
                try { if (typeof error.constraint === 'string' && error.constraint) message = (message ? message + ' ' : '') + '(constraint: ' + error.constraint + ')'; } catch { }
            }
            else {
                message = String(error);
            }
            if (!message) message = name ? '' : 'Unknown error';
            return name ? '\u0001' + name + '\u0002' + message : message;
        }
        // Main .Net to JS entrypoint
        static async _spawnJSInteropLoadExportsAsync(dotnetId, assemblyName) {
            var dotnet = SpawnJSInterop.spawnJSObjectGet(dotnetId);
            var assemblyExports = await dotnet.getAssemblyExports(assemblyName);
            var spawnJSExports = assemblyName.split('.').reduce((acc, key) => acc[key], assemblyExports);
            dotnet.spawnJSExports = spawnJSExports;
        }
        // prepares the variable for .Net based on returnType
        static _serializeToNet(returnType, ret) {
            switch (returnType) {
                case 0:  // void
                    return;
                case 6:  // SpawnJSObject - Number
                case 7:  // SpawnJSObjectNonNullable - Number
                    return SpawnJSInterop.spawnJSObjectHold(ret);
                case 8:  // Json
                    return JSON.stringify(ret);
                case 1:  // Double
                case 2:  // Boolean
                case 3:  // DoubleNullable
                case 4:  // BooleanNullable
                case 9:  // Int32
                case 10: // Int32Nullable
                    return ret;
                case 5:  // String
                    if (ret && typeof ret !== 'string') {
                        ret = Object(ret).toString();
                    }
                    return ret;
            }
            // the default is to return as is
            return ret;
        }
        static wasmMemoryBuffer(dotnet) {
            var found = SpawnJSInterop.#findWasmMemory(dotnet);
            if (!found) throw new Error('SpawnJSInterop: could not reach the WebAssembly memory buffer');
            return found.buffer;
        }
        // returns the name of the path the memory buffer was found under, or '' if it was not found
        static wasmMemoryBufferSource(dotnet) {
            var found = SpawnJSInterop.#findWasmMemory(dotnet);
            return found ? found.source : '';
        }
        static #findWasmMemory(dotnet) {
            var rt = dotnet;
            if (!rt) return null;
            var candidates = [
                ['Module.HEAPU8.buffer', () => rt.Module?.HEAPU8?.buffer],
                ['Module.wasmMemory.buffer', () => rt.Module?.wasmMemory?.buffer],
                ['localHeapViewU8().buffer', () => rt.localHeapViewU8?.()?.buffer],
                ['getHeapU8().buffer', () => rt.getHeapU8?.()?.buffer],
            ];
            for (var i = 0; i < candidates.length; i++) {
                var buffer;
                try { buffer = candidates[i][1](); } catch (ex) { continue; }
                if (buffer && typeof buffer.byteLength === 'number' && buffer.byteLength > 0) {
                    return { buffer: buffer, source: candidates[i][0] };
                }
            }
            return null;
        }
        // safely checks for a property existence
        // safely means will not throw, which is needed as simply checking for a
        // a proeprty can throw an exception (notably on cross-origin windows)
        static _in(key, obj) {
            if (obj === null || obj === void 0) return false;
            try {
                return key in Object(obj);
            } catch { }
            return false;
        }
        // returns the path info based on a base object and a property path: `window?.location.href`
        static pathObjectInfo(rootObject, path) {
            if (rootObject === null || rootObject === void 0) {
                // callers must call with the globalThis if they wish to use it as the rootObject.
                throw new Error('spawnJSInterop.pathObjectInfo error: rootObject cannot be null');
            }
            var parent = rootObject;
            var target;
            var propertyName;
            var shortCircuit = false;
            if (typeof path === 'string' && !(SpawnJSInterop._in(path, parent))) {
                var parts = path.split('.');
                propertyName = parts[parts.length - 1];
                var part;
                for (var i = 0; i < parts.length - 1; i++) {
                    part = parts[i];
                    if (part[part.length - 1] === '?') {
                        // ? null conditonal found
                        // if parent does not exist allow undefined/null parent instead of throwing exception
                        part = part.substring(0, part.length - 1);
                        parent = parent[part];
                        if (parent === void 0 || parent === null) {
                            shortCircuit = true;
                            break;
                        }
                    }
                    else {
                        parent = parent[part];
                    }
                }
                if (!shortCircuit) {
                    target = parent[propertyName];
                }
            }
            else {
                propertyName = path;
                target = parent[propertyName];
            }
            return {
                shortCircuit,   // bool - true if the pathfinding short circuited due to a null-conditional
                parent,         // any - only null or undefined if short circuited due to a null-conditional
                propertyName,   // any
                target,         // any
            };
        }

        // The URL this app was LOADED from - the origin of its own main.* / _framework, NOT the host
        // page's document.baseURI. Under a CDN load the page and the app live at different URLs, and every
        // worker entry (main.classic.js / main.module.js / _framework/*) must resolve against the APP's
        // origin. document.baseURI is a page-coupled Blazor-ism that hands back the page root instead.
        //
        // Derived per-runtime from THIS app's OWN dotnetRuntime, so two SpawnJS apps loaded from different
        // origins on one page each get their own base - a module-scope import.meta.url could not, because
        // the class-definition guard means app B's lib.module.js body never re-runs.
        //
        // Fail-loud multi-candidate, the same shape as #findWasmMemory: the runtime exposes its origin
        // under different shapes across scopes/versions, so every known shape is tried and the one that
        // worked is reportable via appBaseUriSource(). Returns '' if none resolve, so the caller can fall
        // back rather than silently build worker URLs against a wrong base.
        static appBaseUri(dotnet) {
            var found = this.#findAppBaseUri(dotnet);
            return found ? found.uri : '';
        }
        // Which candidate produced appBaseUri(), or '' - diagnostic, mirrors wasmMemoryBufferSource().
        static appBaseUriSource(dotnet) {
            var found = this.#findAppBaseUri(dotnet);
            return found ? found.source : '';
        }
        static import(src) {
            return import(src);
        }
        // The app-root normalizer, exposed so it is diagnosable and testable directly. The test suite
        // drives THIS function rather than a copy of the logic, so the production path is what is covered.
        static appRootFromLoadUrl(raw) {
            return this.#appRootFromLoadUrl(raw);
        }
        // Normalizes a URL some runtime artifact was loaded from into the app root, with a trailing slash.
        //
        // Every boot artifact - the runtime entry (dotnet.js / dotnet.<fingerprint>.js) and every resource
        // in the boot manifest (.wasm/.dll assemblies, ICU .dat/.blat, .pdb symbols) - lives in the app's
        // framework folder, so the app root is that folder's PARENT. Anything else was loaded from the app
        // root itself: a bundled entrypoint (main.classic.js / main.module.js) sits beside index.html, and
        // that is what SpawnDev.SpawnJS.WebWorkers bundles before 2.1.9 reported here.
        //
        // The framework folder is identified by WHAT was loaded, never by what the folder is NAMED. It is
        // "_framework" in a normal publish, but a published app may rename it - WebWorkers'
        // SpawnJSWebWorkersFrameworkFolderName does exactly that, because a browser extension may not have
        // a root folder starting with '_'. Matching the literal name returned the framework folder itself
        // as the app root there, and every URL built on it (worker entrypoints above all) resolved one
        // level too deep.
        static #appRootFromLoadUrl(raw) {
            if (typeof raw !== 'string' || raw.length === 0) return '';
            if (raw.startsWith('blob:')) return '';
            var url;
            try { url = new URL(raw, self?.location?.href); } catch (ex) { return ''; }
            var segments = url.pathname.split('/');   // leading '' from the root slash
            var file = segments.pop();                // '' when the url already names a directory
            // A boot artifact is one level below the app root, whatever its folder is called. Guarded on
            // length so an artifact served from the origin root cannot walk above it.
            if (file && this.#isBootArtifact(file) && segments.length > 1) segments.pop();
            return url.origin + segments.join('/') + '/';
        }
        // True for the runtime entry and for boot-manifest resources - the files that only ever live in
        // the app's framework folder. Deliberately does NOT match a bundled entrypoint (main.*.js), which
        // sits at the app root.
        static #isBootArtifact(fileName) {
            return /^dotnet(\..+)?\.m?js$/i.test(fileName)
                || /\.(wasm|dll|dat|blat|pdb)$/i.test(fileName);
        }
        static #findAppBaseUri(dotnet) {
            if (!dotnet) return null;
            var candidates = [
                // PROVEN primary (measured across scopes): dotnet.js's own module URL, i.e.
                // appRoot/_framework/dotnet.<fp>.js - itself import.meta-derived, so it is the real CDN
                // origin under a CDN load, not the host page. Can be a blob: URL in some worker configs,
                // which #appRootFromLoadUrl rejects so the resolver falls through to the next candidate.
                ['Module.mainScriptUrlOrBlob', () => dotnet.Module?.mainScriptUrlOrBlob],
                // Robust backup: every boot resource carries an absolute resolvedUrl (appRoot/_framework/*),
                // always populated even when mainScriptUrlOrBlob is a blob.
                ['getConfig().resources.assembly[0].resolvedUrl', () => dotnet.getConfig?.()?.resources?.assembly?.[0]?.resolvedUrl],
            ];
            for (var i = 0; i < candidates.length; i++) {
                var raw;
                try { raw = candidates[i][1](); } catch (ex) { continue; }
                var uri = this.#appRootFromLoadUrl(raw);
                if (uri) return { uri: uri, source: candidates[i][0] };
            }
            return null;
        }
    }
    globalThis.SpawnJSInterop = SpawnJSInterop;
})();