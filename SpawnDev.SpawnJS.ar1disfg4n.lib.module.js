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
// Calls flow through _spawnJSInteropCall (sync) / _spawnJSInteropCallAsync (async): .Net names a static
// method here plus a returnType code (see ReturnType.cs), and _serializeToNet shapes the result to match.
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
        //   [JSMarshalAs<JSType.Function<JSType.Number, JSType.Number, JSType.Number>>] Action<double, double, double> onCallback)
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
            handleCallback
            ) {
            if (!dotnet) throw new Error('dotnet not set');
            var instanceInfo = SpawnJSInterop._getInstaceFromDotNet(dotnet);
            if (instanceInfo) return instanceInfo.dotnetId;

            var dotnetId = SpawnJSInterop.spawnJSObjectHold(dotnet);

            // attach the heap buffer to instanceInfo so it can be monitored for detach events
            var heapBuffer = SpawnJSInterop.wasmMemoryBuffer(dotnet);

            var instanceInfo = { heapBuffer, dotnet, dotnetId, onMethodAdded, resolveVoid, resolveDouble, resolveBoolean, resolveString, resolveDoubleNullable, resolveBooleanNullable, resolveInt32, resolveInt32Nullable, handleCallback, onDetachedHeap };
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
        // Main .Net to JS entrypoint (synchronous)
        // The args array is fetched AND replaced with a fresh empty [] in the same slot, so the .Net side's
        // pooled argument-array reference (same id) comes back emptied and ready to reuse on the next call.
        // JSImport
        // bool? _spawnJSInteropCallBooleanNullable(int returnType, int methodIndex, double argsId);
        // double? _spawnJSInteropCallDoubleNullable(int returnType, int methodIndex, double argsId);
        // bool _spawnJSInteropCallBoolean(int returnType, int methodIndex, double argsId);
        // int _spawnJSInteropCallInt32(int returnType, int methodIndex, double argsId);
        // int? _spawnJSInteropCallInt32Nullable(int returnType, int methodIndex, double argsId);
        // double _spawnJSInteropCallDouble(int returnType, int methodIndex, double argsId);
        // string _spawnJSInteropCallString(int returnType, int methodIndex, double argsId);
        // void _spawnJSInteropCallVoid(int returnType, int methodIndex, double argsId);
        static _spawnJSInteropCall(returnType, methodName, argsId, replacerId, replacerConfig) {
            SpawnJSInterop.detachedEventCheck();
            var target = typeof methodName === 'string' ? SpawnJSInterop[methodName] : SpawnJSInterop._methodMap[methodName];
            var args = argsId === null || argsId === undefined ? null : SpawnJSInterop.spawnJSObjectGetAndReplace(argsId, []);
            // iterate _revivers to process args if needed
            if (args) {
                for (let i = 0; i < args.length; i++) {
                    args[i] = SpawnJSInterop.reviveValue(i, args[i], false);
                }
            }
            var ret = !args ? target() : target(...args);
            // run replacers
            var replacer = !replacerId ? null : typeof replacerId === 'string' ? SpawnJSInterop[replacerId] : SpawnJSInterop._methodMap[replacerId];
            if (replacer) ret = replacer(null, ret, true, replacerConfig);
            else ret = SpawnJSInterop.replaceValue(null, ret, false);
            // shape the result to match the .Net side's expected returnType
            ret = SpawnJSInterop._serializeToNet(returnType, ret);
            return ret;
        }
        // Main .Net to JS entrypoint (asynchronous)
        // JSImport
        // void _spawnJSInteropCallAsync(int returnType, double dotnetId, double asyncCallId, double methodIndex, double argsId);
        static async _spawnJSInteropCallAsync(returnType, dotnetId, asyncCallId, methodName, argsId, replacerId, replacerConfig) {
            SpawnJSInterop.detachedEventCheck();
            var target = typeof methodName === 'string' ? SpawnJSInterop[methodName] : SpawnJSInterop._methodMap[methodName];
            var instance = SpawnJSInterop.getInstace(dotnetId);
            var dotnet = SpawnJSInterop.spawnJSObjectGet(dotnetId);
            var args = argsId === null || argsId === undefined ? null : SpawnJSInterop.spawnJSObjectGetAndReplace(argsId, []);
            if (args) {
                for (let i = 0; i < args.length; i++) {
                    args[i] = SpawnJSInterop.reviveValue(i, args[i], false);
                }
            }
            var error = null;
            var ret = null;
            try {
                ret = !args ? target() : target(...args);
                ret = await ret;
                // run replacers
                var replacer = !replacerId ? null : typeof replacerId === 'string' ? SpawnJSInterop[replacerId] : SpawnJSInterop._methodMap[replacerId];
                if (replacer) ret = replacer(null, ret, true, replacerConfig);
                else ret = SpawnJSInterop.replaceValue(null, ret, false);
                // prepare using returnType
                ret = SpawnJSInterop._serializeToNet(returnType, ret);
            } catch (ex) {
                // call the exceptionCallbackId
                error = SpawnJSInterop.errorToString(ex);
                ret = null;
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
        // creates a new Array, adds it to the hold and returns it
        // JSImport 
        // double _spawnJSObjectNewArray();
        static spawnJSObjectNewArray() {
            return SpawnJSInterop.spawnJSObjectHold([]);
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
            var ret = SpawnJSInterop.spawnJSObjects[sjsId];
            delete SpawnJSInterop.spawnJSObjects[sjsId];
            return ret;
        }
        // removes the object from the hold, JSON.stringifies it and returns it
        // JSImport
        // string SpawnJSObjectReleaseJson(double sjsId);
        static spawnJSObjectReleaseAsJson(sjsId) {
            var ret = SpawnJSInterop.spawnJSObjects[sjsId];
            delete SpawnJSInterop.spawnJSObjects[sjsId];
            return JSON.stringify(ret);
        }
        // returns true if the item id exists in the hold
        // JSImport
        // bool SpawnJSObjectHoldExists(double sjsId);
        static spawnJSObjectHoldExists(sjsId) {
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
        // JSImport
        // string _propertyGetString(double sjsId, string key);
        // double _propertyGetDouble(double sjsId, string key);
        // double? _propertyGetDoubleNullable(double sjsId, string key);
        // bool _propertyGetBoolean(double sjsId, string key);
        // bool? _propertyGetBooleanNullable(double sjsId, string key);
        // int _propertyGetInt32(double sjsId, string key);
        // int? _propertyGetInt32(double sjsId, string key);
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
        // get a property as a SpawnJSObjectReference
        // JSImport
        // double _propertyGetSpawnJSObjectReference(double sjsId, string key, bool force);
        static propertyGetSpawnJSObjectReference(sjsId, key, force) {
            var ret = SpawnJSInterop.propertyGet(sjsId, key);
            if (force) {
                ret = SpawnJSInterop.spawnJSObjectHold(ret);
            } else {
                ret = ret === null || ret === undefined ? null : SpawnJSInterop.spawnJSObjectHold(ret);
            }
            return ret;
        }
        // get a property as json
        // JSImport
        // string? _propertyGetJson(double sjsId, string key);
        static propertyGetJson(sjsId, key) {
            var ret = SpawnJSInterop.propertyGet(sjsId, key);
            ret = JSON.stringify(ret);
            return ret;
        }
        // get a property
        // JSImport
        // string _propertyGetWithReplacerString(double sjsId, string key, double methodIndex);
        // double _propertyGetWithReplacerDouble(double sjsId, string key, double methodIndex);
        // double? _propertyGetWithReplacerDoubleNullable(double sjsId, string key, double methodIndex);
        // bool _propertyGetWithReplacerBoolean(double sjsId, string key, double methodIndex);
        // bool? _propertyGetWithReplacerBooleanNullable(double sjsId, string key, double methodIndex);
        // string _propertyGetWithReplacerString(double sjsId, string key, double methodIndex, double replacerConfig);
        // double _propertyGetWithReplacerDouble(double sjsId, string key, double methodIndex, double replacerConfig);
        // double? _propertyGetWithReplacerDoubleNullable(double sjsId, string key, double methodIndex, double replacerConfig);
        // bool _propertyGetWithReplacerBoolean(double sjsId, string key, double methodIndex, double replacerConfig);
        // bool? _propertyGetWithReplacerBooleanNullable(double sjsId, string key, double methodIndex, double replacerConfig);
        // string _propertyGetWithReplacerString(double sjsId, string key, double methodIndex, string replacerConfig);
        // double _propertyGetWithReplacerDouble(double sjsId, string key, double methodIndex, string replacerConfig);
        // double? _propertyGetWithReplacerDoubleNullable(double sjsId, string key, double methodIndex, string replacerConfig);
        // bool _propertyGetWithReplacerBoolean(double sjsId, string key, double methodIndex, string replacerConfig);
        // bool? _propertyGetWithReplacerBooleanNullable(double sjsId, string key, double methodIndex, string replacerConfig);
        static propertyGetWithReplacer(sjsId, key, methodIndex, replacerConfig) {
            var ret = undefined;
            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
            var pathInfo = SpawnJSInterop.pathObjectInfo(obj, key);
            if (pathInfo.shortCircuit) return ret;
            if (typeof pathInfo.target === 'function') {
                ret = pathInfo.target.bind(pathInfo.parent);
            } else {
                ret = pathInfo.target;
            }
            var replacer = typeof methodIndex === 'string' ? SpawnJSInterop[methodIndex] : SpawnJSInterop._methodMap[methodIndex];
            if (!replacer) throw new Error(`Reviver not found: ${methodName}`);
            ret = !replacer ? ret : replacer(pathInfo.propertyName, ret, true, replacerConfig);
            return ret;
        }
        // set property
        // JSImport
        // void _propertySet(double sjsId, string key, string value);
        // void _propertySet(double sjsId, string key, bool value);
        // void _propertySet(double sjsId, string key, double value);
        // void _propertySet(double sjsId, string key, int value);
        // void _propertySet(double sjsId, string key, bool? value);
        // void _propertySet(double sjsId, string key, double? value);
        static propertySet(sjsId, key, value) {
            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
            if (shortCircuit) return;
            // revivers
            value = SpawnJSInterop.reviveValue(propertyName, value, false);
            parent[propertyName] = value;
        }
        // This is where SpawnJSObjectReference revives itself into JS via its sjsId
        // set property to a SpawnJSObjectReference
        // JSImport
        // void _propertySetSpawnJSObject(double sjsId, string key, double value);
        static propertySetSpawnJSObject(sjsId, key, valueId) {
            var value = SpawnJSInterop.spawnJSObjectGet(valueId);
            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
            if (shortCircuit) return;
            parent[propertyName] = value;
        }
        // set property to a Json
        // JSImport
        // void _propertySetJson(double sjsId, string key, string value);
        static propertySetJson(sjsId, key, json) {
            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
            if (shortCircuit) return;
            var value = JSON.parse(json);
            parent[propertyName] = value;
        }
        // set property to a HeapView
        // JSImport
        // void _propertySetHeapView(double sjsId, string key, double dotnetId, double viewType, double offset, double length, bool copy);
        static propertySetHeapView(sjsId, key, dotnetId, viewType, offset, length, copy) {
            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
            if (shortCircuit) return;
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
            var value = SpawnJSInterop.heapViewRefresh(heapViewInfo);
            // set to the proeprty
            parent[propertyName] = value;
        }
        // set property null
        // JSImport
        // void _propertySetNull(double sjsId, string key);
        static propertySetNull(sjsId, key) {
            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
            if (shortCircuit) return;
            parent[propertyName] = null;
        }
        // set property undefined
        // JSImport
        // void _propertySetUndefined(double sjsId, string key);
        static propertySetUndefined(sjsId, key) {
            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
            if (shortCircuit) return;
            parent[propertyName] = undefined;
        }
        // JSImport
        // void _propertySetCallback(double sjsId, string key, double dotnetId, double callbackId, bool once);
        static propertySetCallback(sjsId, key, dotnetId, callbackId, once) {
            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
            if (shortCircuit) return;
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
                    var { handleCallback } = SpawnJSInterop.getInstace(dotnetId);
                    // get argsCnt because after when we call handleCallback .Net will write
                    // the return value to at the end of the array after the last argument (index argsCnt)
                    // unless the return value should be undefined
                    var argsCnt = args.length;
                    // get a temporary hold of the args (released after we notify SpawnJSRuntime)
                    var argsId = SpawnJSInterop.spawnJSObjectHold(args);
                    // notify SpawnJSRuntime with the argsId and the cnt
                    handleCallback(callbackId, argsId, argsCnt);
                    // release the args
                    SpawnJSInterop.spawnJSObjectRelease(argsId);
                    // if it was a 1 time use callback, release it
                    if (once) delete SpawnJSInterop._callbacks[callbackIdPair];
                    // return what is in index argsCnt (the designated place .Net will write to if there is a return value)
                    return args[argsCnt];
                };
                SpawnJSInterop._callbacks[callbackIdPair] = value;
            }
            parent[propertyName] = value;
        }
        // set property to using a SpawnJSInterop[methodName](value) call
        // methodName can be a SpawnJSInterop methodName or a methodIndex
        // revivers are Javascript methods
        // JSImport
        // void _propertySetWithReviver(double sjsId, string key, string value, double reviverIndex);
        // void _propertySetWithReviver(double sjsId, string key, double value, double reviverIndex);
        // void _propertySetWithReviver(double sjsId, string key, string value, double reviverIndex, string reviverConfig);
        // void _propertySetWithReviver(double sjsId, string key, double value, double reviverIndex, string reviverConfig);
        // void _propertySetWithReviver(double sjsId, string key, string value, double reviverIndex, double reviverConfig);
        // void _propertySetWithReviver(double sjsId, string key, double value, double reviverIndex, double reviverConfig);
        // void _propertySetWithReviver(double sjsId, string key, string value, double reviverIndex, bool reviverConfig);
        // void _propertySetWithReviver(double sjsId, string key, double value, double reviverIndex, bool reviverConfig);
        static propertySetWithReviver(sjsId, key, value, methodName, reviverConfig) {
            var obj = SpawnJSInterop.spawnJSObjectGet(sjsId);
            if (obj === void 0 || obj === null) throw new Error('obj null or undefined');
            var { parent, propertyName, shortCircuit } = SpawnJSInterop.pathObjectInfo(obj, key);
            if (shortCircuit) return;
            var reviver = typeof methodName === 'string' ? SpawnJSInterop[methodName] : SpawnJSInterop._methodMap[methodName];
            // if SpawnJSInterop does not have the reviver, try the glboalThis. This allows global types to be used
            if (!reviver) reviver = globalThis[methodName];
            if (!reviver) throw new Error(`Reviver not found: ${methodName}`);
            value = reviver(propertyName, value, true, reviverConfig);
            parent[propertyName] = value;
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
        // Revivers are called as (key, value, directCall, config) by propertySetWithReviver - a plain
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
        static reviveValue(key, initialValue, directCall) {
            // Pipe the value through each reviver sequentially
            return SpawnJSInterop._revivers.reduce((currentValue, currentReviver) => {
                // Short-circuit: if a previous reviver dropped the value, skip the rest
                if (currentValue === undefined) return undefined;
                return currentReviver(key, currentValue, directCall); // the true tells the reviver this is a call revive as opposed to a propertySet revive
            }, initialValue);
        }
        static replaceValue(key, initialValue, directCall) {
            // Pipe the value through each reviver sequentially
            return SpawnJSInterop._replacers.reduce((currentValue, currentReplacer) => {
                // Short-circuit: if a previous reviver dropped the value, skip the rest
                if (currentValue === undefined) return undefined;
                return currentReplacer(key, currentValue, directCall); // the true tells the reviver this is a call revive as opposed to a propertySet revive
            }, initialValue);
        }
        // returns the types the object inherits from
        // returns string[]
        static getPropertyConstructorNames(parent, key) {
            return SpawnJSInterop.getConstructorNames(parent[key]);
        }
        // converts to an error string or returns a generic error string
        static errorToString(error) {
            if (!error) return "Unknown error";
            // Handle native Error objects (e.g., new Error(), TypeError)
            if (error instanceof Error) {
                error = error.message;
            }
            else if (typeof error === 'object') {
                try {
                    error = error.message || JSON.stringify(error);
                } catch {
                    error = String(error);
                }
            }
            error ??= String(error);
            error ??= "Unknown error";
            return error;
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
        // Normalizes any URL that lives under the app's _framework/ folder (or the app root itself) to the
        // app root with a trailing slash: drops a trailing file name, then a trailing "_framework/" segment.
        static #appRootFromLoadUrl(raw) {
            if (typeof raw !== 'string' || raw.length === 0) return '';
            if (raw.startsWith('blob:')) return '';
            var url;
            try { url = new URL(raw, self?.location?.href); } catch (ex) { return ''; }
            var path = url.href.replace(/[?#].*$/, '');
            // strip a trailing file name (a last segment containing a dot), leaving a trailing slash
            if (!path.endsWith('/')) path = path.substring(0, path.lastIndexOf('/') + 1);
            // strip a trailing _framework/ so the base is the app root that main.* sits at
            path = path.replace(/(^|\/)_framework\/$/, '$1');
            return path;
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