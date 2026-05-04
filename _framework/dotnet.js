//! Licensed to the .NET Foundation under one or more agreements.
//! The .NET Foundation licenses this file to you under the MIT license.

var e=!1;const t=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,4,1,96,0,0,3,2,1,0,10,8,1,6,0,6,64,25,11,11])),o=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,5,1,96,0,1,123,3,2,1,0,10,15,1,13,0,65,1,253,15,65,2,253,15,253,128,2,11])),n=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,5,1,96,0,1,123,3,2,1,0,10,10,1,8,0,65,0,253,15,253,98,11])),r=Symbol.for("wasm promise_control");function i(e,t){let o=null;const n=new Promise((function(n,r){o={isDone:!1,promise:null,resolve:t=>{o.isDone||(o.isDone=!0,n(t),e&&e())},reject:e=>{o.isDone||(o.isDone=!0,r(e),t&&t())}}}));o.promise=n;const i=n;return i[r]=o,{promise:i,promise_control:o}}function s(e){return e[r]}function a(e){e&&function(e){return void 0!==e[r]}(e)||Be(!1,"Promise is not controllable")}const l="__mono_message__",c=["debug","log","trace","warn","info","error"],d="MONO_WASM: ";let u,f,m,g,p,h;function w(e){g=e}function b(e){if(Pe.diagnosticTracing){const t="function"==typeof e?e():e;console.debug(d+t)}}function y(e,...t){console.info(d+e,...t)}function v(e,...t){console.info(e,...t)}function E(e,...t){console.warn(d+e,...t)}function _(e,...t){if(t&&t.length>0&&t[0]&&"object"==typeof t[0]){if(t[0].silent)return;if(t[0].toString)return void console.error(d+e,t[0].toString())}console.error(d+e,...t)}function x(e,t,o){return function(...n){try{let r=n[0];if(void 0===r)r="undefined";else if(null===r)r="null";else if("function"==typeof r)r=r.toString();else if("string"!=typeof r)try{r=JSON.stringify(r)}catch(e){r=r.toString()}t(o?JSON.stringify({method:e,payload:r,arguments:n.slice(1)}):[e+r,...n.slice(1)])}catch(e){m.error(`proxyConsole failed: ${e}`)}}}function j(e,t,o){f=t,g=e,m={...t};const n=`${o}/console`.replace("https://","wss://").replace("http://","ws://");u=new WebSocket(n),u.addEventListener("error",A),u.addEventListener("close",S),function(){for(const e of c)f[e]=x(`console.${e}`,T,!0)}()}function R(e){let t=30;const o=()=>{u?0==u.bufferedAmount||0==t?(e&&v(e),function(){for(const e of c)f[e]=x(`console.${e}`,m.log,!1)}(),u.removeEventListener("error",A),u.removeEventListener("close",S),u.close(1e3,e),u=void 0):(t--,globalThis.setTimeout(o,100)):e&&m&&m.log(e)};o()}function T(e){u&&u.readyState===WebSocket.OPEN?u.send(e):m.log(e)}function A(e){m.error(`[${g}] proxy console websocket error: ${e}`,e)}function S(e){m.debug(`[${g}] proxy console websocket closed: ${e}`,e)}function D(){Pe.preferredIcuAsset=O(Pe.config);let e="invariant"==Pe.config.globalizationMode;if(!e)if(Pe.preferredIcuAsset)Pe.diagnosticTracing&&b("ICU data archive(s) available, disabling invariant mode");else{if("custom"===Pe.config.globalizationMode||"all"===Pe.config.globalizationMode||"sharded"===Pe.config.globalizationMode){const e="invariant globalization mode is inactive and no ICU data archives are available";throw _(`ERROR: ${e}`),new Error(e)}Pe.diagnosticTracing&&b("ICU data archive(s) not available, using invariant globalization mode"),e=!0,Pe.preferredIcuAsset=null}const t="DOTNET_SYSTEM_GLOBALIZATION_INVARIANT",o=Pe.config.environmentVariables;if(void 0===o[t]&&e&&(o[t]="1"),void 0===o.TZ)try{const e=Intl.DateTimeFormat().resolvedOptions().timeZone||null;e&&(o.TZ=e)}catch(e){y("failed to detect timezone, will fallback to UTC")}}function O(e){var t;if((null===(t=e.resources)||void 0===t?void 0:t.icu)&&"invariant"!=e.globalizationMode){const t=e.applicationCulture||(ke?globalThis.navigator&&globalThis.navigator.languages&&globalThis.navigator.languages[0]:Intl.DateTimeFormat().resolvedOptions().locale),o=e.resources.icu;let n=null;if("custom"===e.globalizationMode){if(o.length>=1)return o[0].name}else t&&"all"!==e.globalizationMode?"sharded"===e.globalizationMode&&(n=function(e){const t=e.split("-")[0];return"en"===t||["fr","fr-FR","it","it-IT","de","de-DE","es","es-ES"].includes(e)?"icudt_EFIGS.dat":["zh","ko","ja"].includes(t)?"icudt_CJK.dat":"icudt_no_CJK.dat"}(t)):n="icudt.dat";if(n)for(let e=0;e<o.length;e++){const t=o[e];if(t.virtualPath===n)return t.name}}return e.globalizationMode="invariant",null}(new Date).valueOf();const C=class{constructor(e){this.url=e}toString(){return this.url}};async function k(e,t){try{const o="function"==typeof globalThis.fetch;if(Se){const n=e.startsWith("file://");if(!n&&o)return globalThis.fetch(e,t||{credentials:"same-origin"});p||(h=Ne.require("url"),p=Ne.require("fs")),n&&(e=h.fileURLToPath(e));const r=await p.promises.readFile(e);return{ok:!0,headers:{length:0,get:()=>null},url:e,arrayBuffer:()=>r,json:()=>JSON.parse(r),text:()=>{throw new Error("NotImplementedException")}}}if(o)return globalThis.fetch(e,t||{credentials:"same-origin"});if("function"==typeof read)return{ok:!0,url:e,headers:{length:0,get:()=>null},arrayBuffer:()=>new Uint8Array(read(e,"binary")),json:()=>JSON.parse(read(e,"utf8")),text:()=>read(e,"utf8")}}catch(t){return{ok:!1,url:e,status:500,headers:{length:0,get:()=>null},statusText:"ERR28: "+t,arrayBuffer:()=>{throw t},json:()=>{throw t},text:()=>{throw t}}}throw new Error("No fetch implementation available")}function I(e){return"string"!=typeof e&&Be(!1,"url must be a string"),!M(e)&&0!==e.indexOf("./")&&0!==e.indexOf("../")&&globalThis.URL&&globalThis.document&&globalThis.document.baseURI&&(e=new URL(e,globalThis.document.baseURI).toString()),e}const U=/^[a-zA-Z][a-zA-Z\d+\-.]*?:\/\//,P=/[a-zA-Z]:[\\/]/;function M(e){return Se||Ie?e.startsWith("/")||e.startsWith("\\")||-1!==e.indexOf("///")||P.test(e):U.test(e)}let L,N=0;const $=[],z=[],W=new Map,F={"js-module-threads":!0,"js-module-runtime":!0,"js-module-dotnet":!0,"js-module-native":!0,"js-module-diagnostics":!0},B={...F,"js-module-library-initializer":!0},V={...F,dotnetwasm:!0,heap:!0,manifest:!0},q={...B,manifest:!0},H={...B,dotnetwasm:!0},J={dotnetwasm:!0,symbols:!0},Z={...B,dotnetwasm:!0,symbols:!0},Q={symbols:!0};function G(e){return!("icu"==e.behavior&&e.name!=Pe.preferredIcuAsset)}function K(e,t,o){null!=t||(t=[]),Be(1==t.length,`Expect to have one ${o} asset in resources`);const n=t[0];return n.behavior=o,X(n),e.push(n),n}function X(e){V[e.behavior]&&W.set(e.behavior,e)}function Y(e){Be(V[e],`Unknown single asset behavior ${e}`);const t=W.get(e);if(t&&!t.resolvedUrl)if(t.resolvedUrl=Pe.locateFile(t.name),F[t.behavior]){const e=ge(t);e?("string"!=typeof e&&Be(!1,"loadBootResource response for 'dotnetjs' type should be a URL string"),t.resolvedUrl=e):t.resolvedUrl=ce(t.resolvedUrl,t.behavior)}else if("dotnetwasm"!==t.behavior)throw new Error(`Unknown single asset behavior ${e}`);return t}function ee(e){const t=Y(e);return Be(t,`Single asset for ${e} not found`),t}let te=!1;async function oe(){if(!te){te=!0,Pe.diagnosticTracing&&b("mono_download_assets");try{const e=[],t=[],o=(e,t)=>{!Z[e.behavior]&&G(e)&&Pe.expected_instantiated_assets_count++,!H[e.behavior]&&G(e)&&(Pe.expected_downloaded_assets_count++,t.push(se(e)))};for(const t of $)o(t,e);for(const e of z)o(e,t);Pe.allDownloadsQueued.promise_control.resolve(),Promise.all([...e,...t]).then((()=>{Pe.allDownloadsFinished.promise_control.resolve()})).catch((e=>{throw Pe.err("Error in mono_download_assets: "+e),Xe(1,e),e})),await Pe.runtimeModuleLoaded.promise;const n=async e=>{const t=await e;if(t.buffer){if(!Z[t.behavior]){t.buffer&&"object"==typeof t.buffer||Be(!1,"asset buffer must be array-like or buffer-like or promise of these"),"string"!=typeof t.resolvedUrl&&Be(!1,"resolvedUrl must be string");const e=t.resolvedUrl,o=await t.buffer,n=new Uint8Array(o);pe(t),await Ue.beforeOnRuntimeInitialized.promise,Ue.instantiate_asset(t,e,n)}}else J[t.behavior]?("symbols"===t.behavior&&(await Ue.instantiate_symbols_asset(t),pe(t)),J[t.behavior]&&++Pe.actual_downloaded_assets_count):(t.isOptional||Be(!1,"Expected asset to have the downloaded buffer"),!H[t.behavior]&&G(t)&&Pe.expected_downloaded_assets_count--,!Z[t.behavior]&&G(t)&&Pe.expected_instantiated_assets_count--)},r=[],i=[];for(const t of e)r.push(n(t));for(const e of t)i.push(n(e));Promise.all(r).then((()=>{Ce||Ue.coreAssetsInMemory.promise_control.resolve()})).catch((e=>{throw Pe.err("Error in mono_download_assets: "+e),Xe(1,e),e})),Promise.all(i).then((async()=>{Ce||(await Ue.coreAssetsInMemory.promise,Ue.allAssetsInMemory.promise_control.resolve())})).catch((e=>{throw Pe.err("Error in mono_download_assets: "+e),Xe(1,e),e}))}catch(e){throw Pe.err("Error in mono_download_assets: "+e),e}}}let ne=!1;function re(){if(ne)return;ne=!0;const e=Pe.config,t=[];if(e.assets)for(const t of e.assets)"object"!=typeof t&&Be(!1,`asset must be object, it was ${typeof t} : ${t}`),"string"!=typeof t.behavior&&Be(!1,"asset behavior must be known string"),"string"!=typeof t.name&&Be(!1,"asset name must be string"),t.resolvedUrl&&"string"!=typeof t.resolvedUrl&&Be(!1,"asset resolvedUrl could be string"),t.hash&&"string"!=typeof t.hash&&Be(!1,"asset resolvedUrl could be string"),t.pendingDownload&&"object"!=typeof t.pendingDownload&&Be(!1,"asset pendingDownload could be object"),t.isCore?$.push(t):z.push(t),X(t);else if(e.resources){const o=e.resources;o.wasmNative||Be(!1,"resources.wasmNative must be defined"),o.jsModuleNative||Be(!1,"resources.jsModuleNative must be defined"),o.jsModuleRuntime||Be(!1,"resources.jsModuleRuntime must be defined"),K(z,o.wasmNative,"dotnetwasm"),K(t,o.jsModuleNative,"js-module-native"),K(t,o.jsModuleRuntime,"js-module-runtime"),o.jsModuleDiagnostics&&K(t,o.jsModuleDiagnostics,"js-module-diagnostics");const n=(e,t,o)=>{const n=e;n.behavior=t,o?(n.isCore=!0,$.push(n)):z.push(n)};if(o.coreAssembly)for(let e=0;e<o.coreAssembly.length;e++)n(o.coreAssembly[e],"assembly",!0);if(o.assembly)for(let e=0;e<o.assembly.length;e++)n(o.assembly[e],"assembly",!o.coreAssembly);if(0!=e.debugLevel&&Pe.isDebuggingSupported()){if(o.corePdb)for(let e=0;e<o.corePdb.length;e++)n(o.corePdb[e],"pdb",!0);if(o.pdb)for(let e=0;e<o.pdb.length;e++)n(o.pdb[e],"pdb",!o.corePdb)}if(e.loadAllSatelliteResources&&o.satelliteResources)for(const e in o.satelliteResources)for(let t=0;t<o.satelliteResources[e].length;t++){const r=o.satelliteResources[e][t];r.culture=e,n(r,"resource",!o.coreAssembly)}if(o.coreVfs)for(let e=0;e<o.coreVfs.length;e++)n(o.coreVfs[e],"vfs",!0);if(o.vfs)for(let e=0;e<o.vfs.length;e++)n(o.vfs[e],"vfs",!o.coreVfs);const r=O(e);if(r&&o.icu)for(let e=0;e<o.icu.length;e++){const t=o.icu[e];t.name===r&&n(t,"icu",!1)}if(o.wasmSymbols)for(let e=0;e<o.wasmSymbols.length;e++)n(o.wasmSymbols[e],"symbols",!1)}if(e.appsettings)for(let t=0;t<e.appsettings.length;t++){const o=e.appsettings[t],n=he(o);"appsettings.json"!==n&&n!==`appsettings.${e.applicationEnvironment}.json`||z.push({name:o,behavior:"vfs",cache:"no-cache",useCredentials:!0})}e.assets=[...$,...z,...t]}async function ie(e){const t=await se(e);return await t.pendingDownloadInternal.response,t.buffer}async function se(e){try{return await ae(e)}catch(t){if(!Pe.enableDownloadRetry)throw t;if(Ie||Se)throw t;if(e.pendingDownload&&e.pendingDownloadInternal==e.pendingDownload)throw t;if(e.resolvedUrl&&-1!=e.resolvedUrl.indexOf("file://"))throw t;if(t&&404==t.status)throw t;e.pendingDownloadInternal=void 0,await Pe.allDownloadsQueued.promise;try{return Pe.diagnosticTracing&&b(`Retrying download '${e.name}'`),await ae(e)}catch(t){return e.pendingDownloadInternal=void 0,await new Promise((e=>globalThis.setTimeout(e,100))),Pe.diagnosticTracing&&b(`Retrying download (2) '${e.name}' after delay`),await ae(e)}}}async function ae(e){for(;L;)await L.promise;try{++N,N==Pe.maxParallelDownloads&&(Pe.diagnosticTracing&&b("Throttling further parallel downloads"),L=i());const t=await async function(e){if(e.pendingDownload&&(e.pendingDownloadInternal=e.pendingDownload),e.pendingDownloadInternal&&e.pendingDownloadInternal.response)return e.pendingDownloadInternal.response;if(e.buffer){const t=await e.buffer;return e.resolvedUrl||(e.resolvedUrl="undefined://"+e.name),e.pendingDownloadInternal={url:e.resolvedUrl,name:e.name,response:Promise.resolve({ok:!0,arrayBuffer:()=>t,json:()=>JSON.parse(new TextDecoder("utf-8").decode(t)),text:()=>{throw new Error("NotImplementedException")},headers:{get:()=>{}}})},e.pendingDownloadInternal.response}const t=e.loadRemote&&Pe.config.remoteSources?Pe.config.remoteSources:[""];let o;for(let n of t){n=n.trim(),"./"===n&&(n="");const t=le(e,n);e.name===t?Pe.diagnosticTracing&&b(`Attempting to download '${t}'`):Pe.diagnosticTracing&&b(`Attempting to download '${t}' for ${e.name}`);try{e.resolvedUrl=t;const n=fe(e);if(e.pendingDownloadInternal=n,o=await n.response,!o||!o.ok)continue;return o}catch(e){o||(o={ok:!1,url:t,status:0,statusText:""+e});continue}}const n=e.isOptional||e.name.match(/\.pdb$/)&&Pe.config.ignorePdbLoadErrors;if(o||Be(!1,`Response undefined ${e.name}`),!n){const t=new Error(`download '${o.url}' for ${e.name} failed ${o.status} ${o.statusText}`);throw t.status=o.status,t}y(`optional download '${o.url}' for ${e.name} failed ${o.status} ${o.statusText}`)}(e);return t?(J[e.behavior]||(e.buffer=await t.arrayBuffer(),++Pe.actual_downloaded_assets_count),e):e}finally{if(--N,L&&N==Pe.maxParallelDownloads-1){Pe.diagnosticTracing&&b("Resuming more parallel downloads");const e=L;L=void 0,e.promise_control.resolve()}}}function le(e,t){let o;return null==t&&Be(!1,`sourcePrefix must be provided for ${e.name}`),e.resolvedUrl?o=e.resolvedUrl:(o=""===t?"assembly"===e.behavior||"pdb"===e.behavior?e.name:"resource"===e.behavior&&e.culture&&""!==e.culture?`${e.culture}/${e.name}`:e.name:t+e.name,o=ce(Pe.locateFile(o),e.behavior)),o&&"string"==typeof o||Be(!1,"attemptUrl need to be path or url string"),o}function ce(e,t){return Pe.modulesUniqueQuery&&q[t]&&(e+=Pe.modulesUniqueQuery),e}let de=0;const ue=new Set;function fe(e){try{e.resolvedUrl||Be(!1,"Request's resolvedUrl must be set");const t=function(e){let t=e.resolvedUrl;if(Pe.loadBootResource){const o=ge(e);if(o instanceof Promise)return o;"string"==typeof o&&(t=o)}const o={};return e.cache?o.cache=e.cache:Pe.config.disableNoCacheFetch||(o.cache="no-cache"),e.useCredentials?o.credentials="include":!Pe.config.disableIntegrityCheck&&e.hash&&(o.integrity=e.hash),Pe.fetch_like(t,o)}(e),o={name:e.name,url:e.resolvedUrl,response:t};return ue.add(e.name),o.response.then((()=>{"assembly"==e.behavior&&Pe.loadedAssemblies.push(e.name),de++,Pe.onDownloadResourceProgress&&Pe.onDownloadResourceProgress(de,ue.size)})),o}catch(t){const o={ok:!1,url:e.resolvedUrl,status:500,statusText:"ERR29: "+t,arrayBuffer:()=>{throw t},json:()=>{throw t}};return{name:e.name,url:e.resolvedUrl,response:Promise.resolve(o)}}}const me={resource:"assembly",assembly:"assembly",pdb:"pdb",icu:"globalization",vfs:"configuration",manifest:"manifest",dotnetwasm:"dotnetwasm","js-module-dotnet":"dotnetjs","js-module-native":"dotnetjs","js-module-runtime":"dotnetjs","js-module-threads":"dotnetjs"};function ge(e){var t;if(Pe.loadBootResource){const o=null!==(t=e.hash)&&void 0!==t?t:"",n=e.resolvedUrl,r=me[e.behavior];if(r){const t=Pe.loadBootResource(r,e.name,n,o,e.behavior);return"string"==typeof t?I(t):t}}}function pe(e){e.pendingDownloadInternal=null,e.pendingDownload=null,e.buffer=null,e.moduleExports=null}function he(e){let t=e.lastIndexOf("/");return t>=0&&t++,e.substring(t)}async function we(e){e&&await Promise.all((null!=e?e:[]).map((e=>async function(e){try{const t=e.name;if(!e.moduleExports){const o=ce(Pe.locateFile(t),"js-module-library-initializer");Pe.diagnosticTracing&&b(`Attempting to import '${o}' for ${e}`),e.moduleExports=await import(/*! webpackIgnore: true */o)}Pe.libraryInitializers.push({scriptName:t,exports:e.moduleExports})}catch(t){E(`Failed to import library initializer '${e}': ${t}`)}}(e))))}async function be(e,t){if(!Pe.libraryInitializers)return;const o=[];for(let n=0;n<Pe.libraryInitializers.length;n++){const r=Pe.libraryInitializers[n];r.exports[e]&&o.push(ye(r.scriptName,e,(()=>r.exports[e](...t))))}await Promise.all(o)}async function ye(e,t,o){try{await o()}catch(o){throw E(`Failed to invoke '${t}' on library initializer '${e}': ${o}`),Xe(1,o),o}}function ve(e,t){if(e===t)return e;const o={...t};return void 0!==o.assets&&o.assets!==e.assets&&(o.assets=[...e.assets||[],...o.assets||[]]),void 0!==o.resources&&(o.resources=_e(e.resources||{assembly:[],jsModuleNative:[],jsModuleRuntime:[],wasmNative:[]},o.resources)),void 0!==o.environmentVariables&&(o.environmentVariables={...e.environmentVariables||{},...o.environmentVariables||{}}),void 0!==o.runtimeOptions&&o.runtimeOptions!==e.runtimeOptions&&(o.runtimeOptions=[...e.runtimeOptions||[],...o.runtimeOptions||[]]),Object.assign(e,o)}function Ee(e,t){if(e===t)return e;const o={...t};return o.config&&(e.config||(e.config={}),o.config=ve(e.config,o.config)),Object.assign(e,o)}function _e(e,t){if(e===t)return e;const o={...t};return void 0!==o.coreAssembly&&(o.coreAssembly=[...e.coreAssembly||[],...o.coreAssembly||[]]),void 0!==o.assembly&&(o.assembly=[...e.assembly||[],...o.assembly||[]]),void 0!==o.lazyAssembly&&(o.lazyAssembly=[...e.lazyAssembly||[],...o.lazyAssembly||[]]),void 0!==o.corePdb&&(o.corePdb=[...e.corePdb||[],...o.corePdb||[]]),void 0!==o.pdb&&(o.pdb=[...e.pdb||[],...o.pdb||[]]),void 0!==o.jsModuleWorker&&(o.jsModuleWorker=[...e.jsModuleWorker||[],...o.jsModuleWorker||[]]),void 0!==o.jsModuleNative&&(o.jsModuleNative=[...e.jsModuleNative||[],...o.jsModuleNative||[]]),void 0!==o.jsModuleDiagnostics&&(o.jsModuleDiagnostics=[...e.jsModuleDiagnostics||[],...o.jsModuleDiagnostics||[]]),void 0!==o.jsModuleRuntime&&(o.jsModuleRuntime=[...e.jsModuleRuntime||[],...o.jsModuleRuntime||[]]),void 0!==o.wasmSymbols&&(o.wasmSymbols=[...e.wasmSymbols||[],...o.wasmSymbols||[]]),void 0!==o.wasmNative&&(o.wasmNative=[...e.wasmNative||[],...o.wasmNative||[]]),void 0!==o.icu&&(o.icu=[...e.icu||[],...o.icu||[]]),void 0!==o.satelliteResources&&(o.satelliteResources=function(e,t){if(e===t)return e;for(const o in t)e[o]=[...e[o]||[],...t[o]||[]];return e}(e.satelliteResources||{},o.satelliteResources||{})),void 0!==o.modulesAfterConfigLoaded&&(o.modulesAfterConfigLoaded=[...e.modulesAfterConfigLoaded||[],...o.modulesAfterConfigLoaded||[]]),void 0!==o.modulesAfterRuntimeReady&&(o.modulesAfterRuntimeReady=[...e.modulesAfterRuntimeReady||[],...o.modulesAfterRuntimeReady||[]]),void 0!==o.extensions&&(o.extensions={...e.extensions||{},...o.extensions||{}}),void 0!==o.vfs&&(o.vfs=[...e.vfs||[],...o.vfs||[]]),Object.assign(e,o)}function xe(){const e=Pe.config;if(e.environmentVariables=e.environmentVariables||{},e.runtimeOptions=e.runtimeOptions||[],e.resources=e.resources||{assembly:[],jsModuleNative:[],jsModuleWorker:[],jsModuleRuntime:[],wasmNative:[],vfs:[],satelliteResources:{}},e.assets){Pe.diagnosticTracing&&b("config.assets is deprecated, use config.resources instead");for(const t of e.assets){const o={};switch(t.behavior){case"assembly":o.assembly=[t];break;case"pdb":o.pdb=[t];break;case"resource":o.satelliteResources={},o.satelliteResources[t.culture]=[t];break;case"icu":o.icu=[t];break;case"symbols":o.wasmSymbols=[t];break;case"vfs":o.vfs=[t];break;case"dotnetwasm":o.wasmNative=[t];break;case"js-module-threads":o.jsModuleWorker=[t];break;case"js-module-runtime":o.jsModuleRuntime=[t];break;case"js-module-native":o.jsModuleNative=[t];break;case"js-module-diagnostics":o.jsModuleDiagnostics=[t];break;case"js-module-dotnet":break;default:throw new Error(`Unexpected behavior ${t.behavior} of asset ${t.name}`)}_e(e.resources,o)}}e.debugLevel,e.applicationEnvironment||(e.applicationEnvironment="Production"),e.applicationCulture&&(e.environmentVariables.LANG=`${e.applicationCulture}.UTF-8`),Ue.diagnosticTracing=Pe.diagnosticTracing=!!e.diagnosticTracing,Ue.waitForDebugger=e.waitForDebugger,Pe.maxParallelDownloads=e.maxParallelDownloads||Pe.maxParallelDownloads,Pe.enableDownloadRetry=void 0!==e.enableDownloadRetry?e.enableDownloadRetry:Pe.enableDownloadRetry}let je=!1;async function Re(e){var t;if(je)return void await Pe.afterConfigLoaded.promise;let o;try{if(e.configSrc||Pe.config&&0!==Object.keys(Pe.config).length&&(Pe.config.assets||Pe.config.resources)||(e.configSrc="dotnet.boot.js"),o=e.configSrc,je=!0,o&&(Pe.diagnosticTracing&&b("mono_wasm_load_config"),await async function(e){const t=e.configSrc,o=Pe.locateFile(t);let n=null;void 0!==Pe.loadBootResource&&(n=Pe.loadBootResource("manifest",t,o,"","manifest"));let r,i=null;if(n)if("string"==typeof n)n.includes(".json")?(i=await s(I(n)),r=await Ae(i)):r=(await import(I(n))).config;else{const e=await n;"function"==typeof e.json?(i=e,r=await Ae(i)):r=e.config}else o.includes(".json")?(i=await s(ce(o,"manifest")),r=await Ae(i)):r=(await import(ce(o,"manifest"))).config;function s(e){return Pe.fetch_like(e,{method:"GET",credentials:"include",cache:"no-cache"})}Pe.config.applicationEnvironment&&(r.applicationEnvironment=Pe.config.applicationEnvironment),ve(Pe.config,r)}(e)),xe(),await we(null===(t=Pe.config.resources)||void 0===t?void 0:t.modulesAfterConfigLoaded),await be("onRuntimeConfigLoaded",[Pe.config]),e.onConfigLoaded)try{await e.onConfigLoaded(Pe.config,Le),xe()}catch(e){throw _("onConfigLoaded() failed",e),e}xe(),Pe.afterConfigLoaded.promise_control.resolve(Pe.config)}catch(t){const n=`Failed to load config file ${o} ${t} ${null==t?void 0:t.stack}`;throw Pe.config=e.config=Object.assign(Pe.config,{message:n,error:t,isError:!0}),Xe(1,new Error(n)),t}}function Te(){return!!globalThis.navigator&&(Pe.isChromium||Pe.isFirefox)}async function Ae(e){const t=Pe.config,o=await e.json();t.applicationEnvironment||o.applicationEnvironment||(o.applicationEnvironment=e.headers.get("Blazor-Environment")||e.headers.get("DotNet-Environment")||void 0),o.environmentVariables||(o.environmentVariables={});const n=e.headers.get("DOTNET-MODIFIABLE-ASSEMBLIES");n&&(o.environmentVariables.DOTNET_MODIFIABLE_ASSEMBLIES=n);const r=e.headers.get("ASPNETCORE-BROWSER-TOOLS");return r&&(o.environmentVariables.__ASPNETCORE_BROWSER_TOOLS=r),o}"function"!=typeof importScripts||globalThis.onmessage||(globalThis.dotnetSidecar=!0);const Se="object"==typeof process&&"object"==typeof process.versions&&"string"==typeof process.versions.node,De="function"==typeof importScripts,Oe=De&&"undefined"!=typeof dotnetSidecar,Ce=De&&!Oe,ke="object"==typeof window||De&&!Se,Ie=!ke&&!Se;let Ue={},Pe={},Me={},Le={},Ne={},$e=!1;const ze={},We={config:ze},Fe={mono:{},binding:{},internal:Ne,module:We,loaderHelpers:Pe,runtimeHelpers:Ue,diagnosticHelpers:Me,api:Le};function Be(e,t){if(e)return;const o="Assert failed: "+("function"==typeof t?t():t),n=new Error(o);_(o,n),Ue.nativeAbort(n)}function Ve(){return void 0!==Pe.exitCode}function qe(){return Ue.runtimeReady&&!Ve()}function He(){Ve()&&Be(!1,`.NET runtime already exited with ${Pe.exitCode} ${Pe.exitReason}. You can use runtime.runMain() which doesn't exit the runtime.`),Ue.runtimeReady||Be(!1,".NET runtime didn't start yet. Please call dotnet.create() first.")}function Je(){ke&&(globalThis.addEventListener("unhandledrejection",et),globalThis.addEventListener("error",tt))}let Ze,Qe;function Ge(e){Qe&&Qe(e),Xe(e,Pe.exitReason)}function Ke(e){Ze&&Ze(e||Pe.exitReason),Xe(1,e||Pe.exitReason)}function Xe(t,o){var n,r;const i=o&&"object"==typeof o;t=i&&"number"==typeof o.status?o.status:void 0===t?-1:t;const s=i&&"string"==typeof o.message?o.message:""+o;(o=i?o:Ue.ExitStatus?function(e,t){const o=new Ue.ExitStatus(e);return o.message=t,o.toString=()=>t,o}(t,s):new Error("Exit with code "+t+" "+s)).status=t,o.message||(o.message=s);const a=""+(o.stack||(new Error).stack);try{Object.defineProperty(o,"stack",{get:()=>a})}catch(e){}const l=!!o.silent;if(o.silent=!0,Ve())Pe.diagnosticTracing&&b("mono_exit called after exit");else{try{We.onAbort==Ke&&(We.onAbort=Ze),We.onExit==Ge&&(We.onExit=Qe),ke&&(globalThis.removeEventListener("unhandledrejection",et),globalThis.removeEventListener("error",tt)),Ue.runtimeReady?(Ue.jiterpreter_dump_stats&&Ue.jiterpreter_dump_stats(!1),0===t&&(null===(n=Pe.config)||void 0===n?void 0:n.interopCleanupOnExit)&&Ue.forceDisposeProxies(!0,!0),e&&0!==t&&(null===(r=Pe.config)||void 0===r||r.dumpThreadsOnNonZeroExit)):(Pe.diagnosticTracing&&b(`abort_startup, reason: ${o}`),function(e){Pe.allDownloadsQueued.promise_control.reject(e),Pe.allDownloadsFinished.promise_control.reject(e),Pe.afterConfigLoaded.promise_control.reject(e),Pe.wasmCompilePromise.promise_control.reject(e),Pe.runtimeModuleLoaded.promise_control.reject(e),Ue.dotnetReady&&(Ue.dotnetReady.promise_control.reject(e),Ue.afterInstantiateWasm.promise_control.reject(e),Ue.beforePreInit.promise_control.reject(e),Ue.afterPreInit.promise_control.reject(e),Ue.afterPreRun.promise_control.reject(e),Ue.beforeOnRuntimeInitialized.promise_control.reject(e),Ue.afterOnRuntimeInitialized.promise_control.reject(e),Ue.afterPostRun.promise_control.reject(e))}(o))}catch(e){E("mono_exit A failed",e)}try{l||(function(e,t){if(0!==e&&t){const e=Ue.ExitStatus&&t instanceof Ue.ExitStatus?b:_;"string"==typeof t?e(t):(void 0===t.stack&&(t.stack=(new Error).stack+""),t.message?e(Ue.stringify_as_error_with_stack?Ue.stringify_as_error_with_stack(t.message+"\n"+t.stack):t.message+"\n"+t.stack):e(JSON.stringify(t)))}!Ce&&Pe.config&&(Pe.config.logExitCode?Pe.config.forwardConsoleLogsToWS?R("WASM EXIT "+e):v("WASM EXIT "+e):Pe.config.forwardConsoleLogsToWS&&R())}(t,o),function(e){if(ke&&!Ce&&Pe.config&&Pe.config.appendElementOnExit&&document){const t=document.createElement("label");t.id="tests_done",0!==e&&(t.style.background="red"),t.innerHTML=""+e,document.body.appendChild(t)}}(t))}catch(e){E("mono_exit B failed",e)}Pe.exitCode=t,Pe.exitReason||(Pe.exitReason=o),!Ce&&Ue.runtimeReady&&We.runtimeKeepalivePop()}if(Pe.config&&Pe.config.asyncFlushOnExit&&0===t)throw(async()=>{try{await async function(){try{const e=await import(/*! webpackIgnore: true */"process"),t=e=>new Promise(((t,o)=>{e.on("error",o),e.end("","utf8",t)})),o=t(e.stderr),n=t(e.stdout);let r;const i=new Promise((e=>{r=setTimeout((()=>e("timeout")),1e3)}));await Promise.race([Promise.all([n,o]),i]),clearTimeout(r)}catch(e){_(`flushing std* streams failed: ${e}`)}}()}finally{Ye(t,o)}})(),o;Ye(t,o)}function Ye(e,t){if(Ue.runtimeReady&&Ue.nativeExit)try{Ue.nativeExit(e)}catch(e){!Ue.ExitStatus||e instanceof Ue.ExitStatus||E("set_exit_code_and_quit_now failed: "+e.toString())}if(0!==e||!ke)throw Se&&Ne.process?Ne.process.exit(e):Ue.quit&&Ue.quit(e,t),t}function et(e){ot(e,e.reason,"rejection")}function tt(e){ot(e,e.error,"error")}function ot(e,t,o){e.preventDefault();try{t||(t=new Error("Unhandled "+o)),void 0===t.stack&&(t.stack=(new Error).stack),t.stack=t.stack+"",t.silent||(_("Unhandled error:",t),Xe(1,t))}catch(e){}}!function(e){if($e)throw new Error("Loader module already loaded");$e=!0,Ue=e.runtimeHelpers,Pe=e.loaderHelpers,Me=e.diagnosticHelpers,Le=e.api,Ne=e.internal,Object.assign(Le,{INTERNAL:Ne,invokeLibraryInitializers:be}),Object.assign(e.module,{config:ve(ze,{environmentVariables:{}})});const r={mono_wasm_bindings_is_ready:!1,config:e.module.config,diagnosticTracing:!1,nativeAbort:e=>{throw e||new Error("abort")},nativeExit:e=>{throw new Error("exit:"+e)}},l={gitHash:"b16286c2284fecf303dbc12a0bb152476d662e44",config:e.module.config,diagnosticTracing:!1,maxParallelDownloads:16,enableDownloadRetry:!0,_loaded_files:[],loadedFiles:[],loadedAssemblies:[],libraryInitializers:[],workerNextNumber:1,actual_downloaded_assets_count:0,actual_instantiated_assets_count:0,expected_downloaded_assets_count:0,expected_instantiated_assets_count:0,afterConfigLoaded:i(),allDownloadsQueued:i(),allDownloadsFinished:i(),wasmCompilePromise:i(),runtimeModuleLoaded:i(),loadingWorkers:i(),is_exited:Ve,is_runtime_running:qe,assert_runtime_running:He,mono_exit:Xe,createPromiseController:i,getPromiseController:s,assertIsControllablePromise:a,mono_download_assets:oe,resolve_single_asset_path:ee,setup_proxy_console:j,set_thread_prefix:w,installUnhandledErrorHandler:Je,retrieve_asset_download:ie,invokeLibraryInitializers:be,isDebuggingSupported:Te,exceptions:t,simd:n,relaxedSimd:o};Object.assign(Ue,r),Object.assign(Pe,l)}(Fe);let nt,rt,it,st=!1,at=!1;async function lt(e){if(!at){if(at=!0,ke&&Pe.config.forwardConsoleLogsToWS&&void 0!==globalThis.WebSocket&&j("main",globalThis.console,globalThis.location.origin),We||Be(!1,"Null moduleConfig"),Pe.config||Be(!1,"Null moduleConfig.config"),"function"==typeof e){const t=e(Fe.api);if(t.ready)throw new Error("Module.ready couldn't be redefined.");Object.assign(We,t),Ee(We,t)}else{if("object"!=typeof e)throw new Error("Can't use moduleFactory callback of createDotnetRuntime function.");Ee(We,e)}await async function(e){if(Se){const e=await import(/*! webpackIgnore: true */"process"),t=14;if(e.versions.node.split(".")[0]<t)throw new Error(`NodeJS at '${e.execPath}' has too low version '${e.versions.node}', please use at least ${t}. See also https://aka.ms/dotnet-wasm-features`)}const t=/*! webpackIgnore: true */import.meta.url,o=t.indexOf("?");var n;if(o>0&&(Pe.modulesUniqueQuery=t.substring(o)),Pe.scriptUrl=t.replace(/\\/g,"/").replace(/[?#].*/,""),Pe.scriptDirectory=(n=Pe.scriptUrl).slice(0,n.lastIndexOf("/"))+"/",Pe.locateFile=e=>"URL"in globalThis&&globalThis.URL!==C?new URL(e,Pe.scriptDirectory).toString():M(e)?e:Pe.scriptDirectory+e,Pe.fetch_like=k,Pe.out=console.log,Pe.err=console.error,Pe.onDownloadResourceProgress=e.onDownloadResourceProgress,ke&&globalThis.navigator){const e=globalThis.navigator,t=e.userAgentData&&e.userAgentData.brands;t&&t.length>0?Pe.isChromium=t.some((e=>"Google Chrome"===e.brand||"Microsoft Edge"===e.brand||"Chromium"===e.brand)):e.userAgent&&(Pe.isChromium=e.userAgent.includes("Chrome"),Pe.isFirefox=e.userAgent.includes("Firefox"))}Ne.require=Se?await import(/*! webpackIgnore: true */"module").then((e=>e.createRequire(/*! webpackIgnore: true */import.meta.url))):Promise.resolve((()=>{throw new Error("require not supported")})),void 0===globalThis.URL&&(globalThis.URL=C)}(We)}}async function ct(e){return await lt(e),Ze=We.onAbort,Qe=We.onExit,We.onAbort=Ke,We.onExit=Ge,We.ENVIRONMENT_IS_PTHREAD?async function(){(function(){const e=new MessageChannel,t=e.port1,o=e.port2;t.addEventListener("message",(e=>{var n,r;n=JSON.parse(e.data.config),r=JSON.parse(e.data.monoThreadInfo),st?Pe.diagnosticTracing&&b("mono config already received"):(ve(Pe.config,n),Ue.monoThreadInfo=r,xe(),Pe.diagnosticTracing&&b("mono config received"),st=!0,Pe.afterConfigLoaded.promise_control.resolve(Pe.config),ke&&n.forwardConsoleLogsToWS&&void 0!==globalThis.WebSocket&&Pe.setup_proxy_console("worker-idle",console,globalThis.location.origin)),t.close(),o.close()}),{once:!0}),t.start(),self.postMessage({[l]:{monoCmd:"preload",port:o}},[o])})(),await Pe.afterConfigLoaded.promise,function(){const e=Pe.config;e.assets||Be(!1,"config.assets must be defined");for(const t of e.assets)X(t),Q[t.behavior]&&z.push(t)}(),setTimeout((async()=>{try{await oe()}catch(e){Xe(1,e)}}),0);const e=dt(),t=await Promise.all(e);return await ut(t),We}():async function(){var e;await Re(We),re();const t=dt();(async function(){try{const e=ee("dotnetwasm");await se(e),e&&e.pendingDownloadInternal&&e.pendingDownloadInternal.response||Be(!1,"Can't load dotnet.native.wasm");const t=await e.pendingDownloadInternal.response,o=t.headers&&t.headers.get?t.headers.get("Content-Type"):void 0;let n;if("function"==typeof WebAssembly.compileStreaming&&"application/wasm"===o)n=await WebAssembly.compileStreaming(t);else{ke&&"application/wasm"!==o&&E('WebAssembly resource does not have the expected content type "application/wasm", so falling back to slower ArrayBuffer instantiation.');const e=await t.arrayBuffer();Pe.diagnosticTracing&&b("instantiate_wasm_module buffered"),n=Ie?await Promise.resolve(new WebAssembly.Module(e)):await WebAssembly.compile(e)}e.pendingDownloadInternal=null,e.pendingDownload=null,e.buffer=null,e.moduleExports=null,Pe.wasmCompilePromise.promise_control.resolve(n)}catch(e){Pe.wasmCompilePromise.promise_control.reject(e)}})(),setTimeout((async()=>{try{D(),await oe()}catch(e){Xe(1,e)}}),0);const o=await Promise.all(t);return await ut(o),await Ue.dotnetReady.promise,await we(null===(e=Pe.config.resources)||void 0===e?void 0:e.modulesAfterRuntimeReady),await be("onRuntimeReady",[Fe.api]),Le}()}function dt(){const e=ee("js-module-runtime"),t=ee("js-module-native");if(nt&&rt)return[nt,rt,it];"object"==typeof e.moduleExports?nt=e.moduleExports:(Pe.diagnosticTracing&&b(`Attempting to import '${e.resolvedUrl}' for ${e.name}`),nt=import(/*! webpackIgnore: true */e.resolvedUrl)),"object"==typeof t.moduleExports?rt=t.moduleExports:(Pe.diagnosticTracing&&b(`Attempting to import '${t.resolvedUrl}' for ${t.name}`),rt=import(/*! webpackIgnore: true */t.resolvedUrl));const o=Y("js-module-diagnostics");return o&&("object"==typeof o.moduleExports?it=o.moduleExports:(Pe.diagnosticTracing&&b(`Attempting to import '${o.resolvedUrl}' for ${o.name}`),it=import(/*! webpackIgnore: true */o.resolvedUrl))),[nt,rt,it]}async function ut(e){const{initializeExports:t,initializeReplacements:o,configureRuntimeStartup:n,configureEmscriptenStartup:r,configureWorkerStartup:i,setRuntimeGlobals:s,passEmscriptenInternals:a}=e[0],{default:l}=e[1],c=e[2];s(Fe),t(Fe),c&&c.setRuntimeGlobals(Fe),await n(We),Pe.runtimeModuleLoaded.promise_control.resolve(),l((e=>(Object.assign(We,{ready:e.ready,__dotnet_runtime:{initializeReplacements:o,configureEmscriptenStartup:r,configureWorkerStartup:i,passEmscriptenInternals:a}}),We))).catch((e=>{if(e.message&&e.message.toLowerCase().includes("out of memory"))throw new Error(".NET runtime has failed to start, because too much memory was requested. Please decrease the memory by adjusting EmccMaximumHeapSize. See also https://aka.ms/dotnet-wasm-features");throw e}))}const ft=new class{withModuleConfig(e){try{return Ee(We,e),this}catch(e){throw Xe(1,e),e}}withOnConfigLoaded(e){try{return Ee(We,{onConfigLoaded:e}),this}catch(e){throw Xe(1,e),e}}withConsoleForwarding(){try{return ve(ze,{forwardConsoleLogsToWS:!0}),this}catch(e){throw Xe(1,e),e}}withExitOnUnhandledError(){try{return ve(ze,{exitOnUnhandledError:!0}),Je(),this}catch(e){throw Xe(1,e),e}}withAsyncFlushOnExit(){try{return ve(ze,{asyncFlushOnExit:!0}),this}catch(e){throw Xe(1,e),e}}withExitCodeLogging(){try{return ve(ze,{logExitCode:!0}),this}catch(e){throw Xe(1,e),e}}withElementOnExit(){try{return ve(ze,{appendElementOnExit:!0}),this}catch(e){throw Xe(1,e),e}}withInteropCleanupOnExit(){try{return ve(ze,{interopCleanupOnExit:!0}),this}catch(e){throw Xe(1,e),e}}withDumpThreadsOnNonZeroExit(){try{return ve(ze,{dumpThreadsOnNonZeroExit:!0}),this}catch(e){throw Xe(1,e),e}}withWaitingForDebugger(e){try{return ve(ze,{waitForDebugger:e}),this}catch(e){throw Xe(1,e),e}}withInterpreterPgo(e,t){try{return ve(ze,{interpreterPgo:e,interpreterPgoSaveDelay:t}),ze.runtimeOptions?ze.runtimeOptions.push("--interp-pgo-recording"):ze.runtimeOptions=["--interp-pgo-recording"],this}catch(e){throw Xe(1,e),e}}withConfig(e){try{return ve(ze,e),this}catch(e){throw Xe(1,e),e}}withConfigSrc(e){try{return e&&"string"==typeof e||Be(!1,"must be file path or URL"),Ee(We,{configSrc:e}),this}catch(e){throw Xe(1,e),e}}withVirtualWorkingDirectory(e){try{return e&&"string"==typeof e||Be(!1,"must be directory path"),ve(ze,{virtualWorkingDirectory:e}),this}catch(e){throw Xe(1,e),e}}withEnvironmentVariable(e,t){try{const o={};return o[e]=t,ve(ze,{environmentVariables:o}),this}catch(e){throw Xe(1,e),e}}withEnvironmentVariables(e){try{return e&&"object"==typeof e||Be(!1,"must be dictionary object"),ve(ze,{environmentVariables:e}),this}catch(e){throw Xe(1,e),e}}withDiagnosticTracing(e){try{return"boolean"!=typeof e&&Be(!1,"must be boolean"),ve(ze,{diagnosticTracing:e}),this}catch(e){throw Xe(1,e),e}}withDebugging(e){try{return null!=e&&"number"==typeof e||Be(!1,"must be number"),ve(ze,{debugLevel:e}),this}catch(e){throw Xe(1,e),e}}withApplicationArguments(...e){try{return e&&Array.isArray(e)||Be(!1,"must be array of strings"),ve(ze,{applicationArguments:e}),this}catch(e){throw Xe(1,e),e}}withRuntimeOptions(e){try{return e&&Array.isArray(e)||Be(!1,"must be array of strings"),ze.runtimeOptions?ze.runtimeOptions.push(...e):ze.runtimeOptions=e,this}catch(e){throw Xe(1,e),e}}withMainAssembly(e){try{return ve(ze,{mainAssemblyName:e}),this}catch(e){throw Xe(1,e),e}}withApplicationArgumentsFromQuery(){try{if(!globalThis.window)throw new Error("Missing window to the query parameters from");if(void 0===globalThis.URLSearchParams)throw new Error("URLSearchParams is supported");const e=new URLSearchParams(globalThis.window.location.search).getAll("arg");return this.withApplicationArguments(...e)}catch(e){throw Xe(1,e),e}}withApplicationEnvironment(e){try{return ve(ze,{applicationEnvironment:e}),this}catch(e){throw Xe(1,e),e}}withApplicationCulture(e){try{return ve(ze,{applicationCulture:e}),this}catch(e){throw Xe(1,e),e}}withResourceLoader(e){try{return Pe.loadBootResource=e,this}catch(e){throw Xe(1,e),e}}async download(){try{await async function(){lt(We),await Re(We),re(),D(),oe(),await Pe.allDownloadsFinished.promise}()}catch(e){throw Xe(1,e),e}}async create(){try{return this.instance||(this.instance=await async function(){return await ct(We),Fe.api}()),this.instance}catch(e){throw Xe(1,e),e}}async run(){try{return We.config||Be(!1,"Null moduleConfig.config"),this.instance||await this.create(),this.instance.runMainAndExit()}catch(e){throw Xe(1,e),e}}},mt=Xe,gt=ct;Ie||"function"==typeof globalThis.URL||Be(!1,"This browser/engine doesn't support URL API. Please use a modern version. See also https://aka.ms/dotnet-wasm-features"),"function"!=typeof globalThis.BigInt64Array&&Be(!1,"This browser/engine doesn't support BigInt64Array API. Please use a modern version. See also https://aka.ms/dotnet-wasm-features"),ft.withConfig(/*json-start*/{
  "mainAssemblyName": "SpawnDev.ILGPU.Demo",
  "resources": {
    "hash": "sha256-eBUGPW9IiJ9Ev2beTTajq6DLKwJWlK+SfybQk3DmiVs=",
    "jsModuleNative": [
      {
        "name": "dotnet.native.077fzk41e3.js"
      }
    ],
    "jsModuleRuntime": [
      {
        "name": "dotnet.runtime.2zl32tp6ah.js"
      }
    ],
    "wasmNative": [
      {
        "name": "dotnet.native.h5lii313jz.wasm",
        "integrity": "sha256-lUkjBTKTIE454M/SDSpBCDF+CndWLIJXuDL0pbfgBkA=",
        "cache": "force-cache"
      }
    ],
    "icu": [
      {
        "virtualPath": "icudt_CJK.dat",
        "name": "icudt_CJK.tjcz0u77k5.dat",
        "integrity": "sha256-SZLtQnRc0JkwqHab0VUVP7T3uBPSeYzxzDnpxPpUnHk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "icudt_EFIGS.dat",
        "name": "icudt_EFIGS.tptq2av103.dat",
        "integrity": "sha256-8fItetYY8kQ0ww6oxwTLiT3oXlBwHKumbeP2pRF4yTc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "icudt_no_CJK.dat",
        "name": "icudt_no_CJK.lfu7j35m59.dat",
        "integrity": "sha256-L7sV7NEYP37/Qr2FPCePo5cJqRgTXRwGHuwF5Q+0Nfs=",
        "cache": "force-cache"
      }
    ],
    "coreAssembly": [
      {
        "virtualPath": "System.Runtime.InteropServices.JavaScript.wasm",
        "name": "System.Runtime.InteropServices.JavaScript.5bsnbcp9i1.wasm",
        "integrity": "sha256-76Vmm61qTP2ejG2/01oaJ3nLTNbqoq5jy65EtvvS6kY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.CoreLib.wasm",
        "name": "System.Private.CoreLib.btnahqa3oa.wasm",
        "integrity": "sha256-D1BhEjTDY/IsSM3zmtwYu5LEd+LFMxg55snscKYCVkM=",
        "cache": "force-cache"
      }
    ],
    "assembly": [
      {
        "virtualPath": "BouncyCastle.Cryptography.wasm",
        "name": "BouncyCastle.Cryptography.6qnbenkd0k.wasm",
        "integrity": "sha256-IqnVnhA0e0WF9/Ufx8/JIbwvVOGRIsbVT0pJuf86K70=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Concentus.wasm",
        "name": "Concentus.17t2e6w7vi.wasm",
        "integrity": "sha256-s5l6Uv9Aan/iYIpJHfPkUYXHlRf4zqEmOx7I2oa+AkI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "DnsClient.wasm",
        "name": "DnsClient.7sic17ztch.wasm",
        "integrity": "sha256-F3dZ3NYYdk5VvY3HUqpSuhvzGqynmxs+rkei8P2Svho=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Authorization.wasm",
        "name": "Microsoft.AspNetCore.Authorization.1l4onz0xjo.wasm",
        "integrity": "sha256-NVDsBhNtjt4lkD/U8hvDWbrk4zD10WGLJ49QfG9ghJ4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Components.wasm",
        "name": "Microsoft.AspNetCore.Components.nyb7ae3bsc.wasm",
        "integrity": "sha256-sZUvhuzUokaB8IWG+sRnUEaC8XnfNmnd1WYyKMWxoGM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Components.Forms.wasm",
        "name": "Microsoft.AspNetCore.Components.Forms.ij454jqrom.wasm",
        "integrity": "sha256-wkkDru5M2WuwgS+GCBHjkJXPcbaIh92QxI1ILyvjKSo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Components.Web.wasm",
        "name": "Microsoft.AspNetCore.Components.Web.nctswpx09d.wasm",
        "integrity": "sha256-aUgvosJtvwTAuJom5bT8C1ip5gYSh9SXYoz7gMLsixs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Components.WebAssembly.wasm",
        "name": "Microsoft.AspNetCore.Components.WebAssembly.wt1enrrig6.wasm",
        "integrity": "sha256-xPOloKszfUwevGv44AQ0L1EsdyyAYiHiTeDLLarqvG4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Metadata.wasm",
        "name": "Microsoft.AspNetCore.Metadata.9ruf9isj5v.wasm",
        "integrity": "sha256-iwUZbYKQWoTWesKIGtBjyK6/MlhqzBpdLFGLvAatmQU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Configuration.wasm",
        "name": "Microsoft.Extensions.Configuration.orhptnd9xx.wasm",
        "integrity": "sha256-NC8XVrNNxyNhg7tlom6WjSopQZTaiWSbeRookBSYOyM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Configuration.Abstractions.wasm",
        "name": "Microsoft.Extensions.Configuration.Abstractions.b3pij3tebf.wasm",
        "integrity": "sha256-eYEoajjra7bivrxxEwRuon9Cpy6WgklT6Ys9BiILZ5Y=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Configuration.Binder.wasm",
        "name": "Microsoft.Extensions.Configuration.Binder.3ucxiitwm2.wasm",
        "integrity": "sha256-xZawkvTLRWa+BPeYO9fyRVSvrNMjoiiEdj64CPkycHY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Configuration.FileExtensions.wasm",
        "name": "Microsoft.Extensions.Configuration.FileExtensions.5dd7vyvi95.wasm",
        "integrity": "sha256-afYZHLJreDoCM75VFV7bHT07J5Zkxfp5vsdr8Vgstmw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Configuration.Json.wasm",
        "name": "Microsoft.Extensions.Configuration.Json.bv467jqp58.wasm",
        "integrity": "sha256-J9Gi+iAs6qZjI0RyUaVjZKGhtYIkNnWTO+1KIiIVlbc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.DependencyInjection.wasm",
        "name": "Microsoft.Extensions.DependencyInjection.irlczz6hcu.wasm",
        "integrity": "sha256-LgjvUyEGK040AHn5b+4mnmzTKCnP6GMIRQwemaPYKEU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.DependencyInjection.Abstractions.wasm",
        "name": "Microsoft.Extensions.DependencyInjection.Abstractions.fin3467xug.wasm",
        "integrity": "sha256-x6Vmkk+XcLsdDWVSu46CXmXkH+Et1+BWiz/qFw9XXFA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Diagnostics.wasm",
        "name": "Microsoft.Extensions.Diagnostics.96zflqxp3y.wasm",
        "integrity": "sha256-L3NVQdAP7ZrRa1hz0AzugwK6hRFyqD+FC8zd5kk+4Bw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Diagnostics.Abstractions.wasm",
        "name": "Microsoft.Extensions.Diagnostics.Abstractions.nzpokr6gof.wasm",
        "integrity": "sha256-bSYsqFLYGm+HWVegMP4wac7HUtLzYIkawjBZr3fL9SY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.FileProviders.Abstractions.wasm",
        "name": "Microsoft.Extensions.FileProviders.Abstractions.nn19foc92m.wasm",
        "integrity": "sha256-oz8FIwaPR0ArOPDA/VVxRJgUpe0TX8zRWoPnDE2CChc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.FileProviders.Physical.wasm",
        "name": "Microsoft.Extensions.FileProviders.Physical.94nfa9nm1s.wasm",
        "integrity": "sha256-N99PtKUbip+mTXDkrgM7R8vSKLO3ptgImjW6N0z+UV4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.FileSystemGlobbing.wasm",
        "name": "Microsoft.Extensions.FileSystemGlobbing.ky7o0248s6.wasm",
        "integrity": "sha256-TMY4PHVcLvYv10C5Ji01cnaqe03DzxjamHVYvBFvjIw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Logging.wasm",
        "name": "Microsoft.Extensions.Logging.bypfn3k8np.wasm",
        "integrity": "sha256-3SsNpwbnHYzH54P6la4aibD1sewVEBOjWFPya2+okRc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Logging.Abstractions.wasm",
        "name": "Microsoft.Extensions.Logging.Abstractions.j3u8ouhzz6.wasm",
        "integrity": "sha256-oZVl5UoD2ZjGxchUieIBOGouXPMx8KM6Wr/bJwCJlOE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Options.wasm",
        "name": "Microsoft.Extensions.Options.moch3gvzt4.wasm",
        "integrity": "sha256-di22E4jmM/Bs0XCMaI2qOcHAFPN2C/0JExuM/4nQZHg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Options.ConfigurationExtensions.wasm",
        "name": "Microsoft.Extensions.Options.ConfigurationExtensions.wtnkcgjgd7.wasm",
        "integrity": "sha256-XE7YvUnfPGK/WaRvsQKbUsR6SmSvQw35NqXgo2MvXgc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Primitives.wasm",
        "name": "Microsoft.Extensions.Primitives.4elrmnoxfr.wasm",
        "integrity": "sha256-DPdGxWUwWSkPlogm6xTDWrq5DbLzedmuGTh8xn+iJhQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Validation.wasm",
        "name": "Microsoft.Extensions.Validation.sjdijf10qc.wasm",
        "integrity": "sha256-GLTMAMo09z1+eawTOvP3h+E/nl1Z4/zqxvMKhEdDnMk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.JSInterop.wasm",
        "name": "Microsoft.JSInterop.x028qg75kb.wasm",
        "integrity": "sha256-v/mk6V5qkDegjaADYWnFObhoSv+EWBnM/EXvZWvRc4A=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.JSInterop.WebAssembly.wasm",
        "name": "Microsoft.JSInterop.WebAssembly.gpkw9vy0ln.wasm",
        "integrity": "sha256-LCoHxVZxWAeOxp2E4m37JX7jlqP3yMZzEGKk1sdZGB8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "websocket-sharp.wasm",
        "name": "websocket-sharp.hxolgqcoj5.wasm",
        "integrity": "sha256-ixbQzKVBepGWvsSiiv1XPMGu9/6Qiz03/lcIwo3YJt4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SpawnDev.AsyncFileSystem.wasm",
        "name": "SpawnDev.AsyncFileSystem.hj47ljnjjz.wasm",
        "integrity": "sha256-Q1wefr0i+FmH2xcpFfGB21jA7l+Vt4ksbnqbv1TwfOk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SpawnDev.BackgroundServices.wasm",
        "name": "SpawnDev.BackgroundServices.o55nwjvr9o.wasm",
        "integrity": "sha256-JIo07sg6BjmGo8tGfICUCjqpfyRiEA+YYo91XWcnEGY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SpawnDev.BlazorJS.wasm",
        "name": "SpawnDev.BlazorJS.fgz8vz6rap.wasm",
        "integrity": "sha256-f3AOdVnzqetDJGz9xA1YpHO+18YBLjZ7sxa9K97iv1g=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SpawnDev.BlazorJS.Cryptography.wasm",
        "name": "SpawnDev.BlazorJS.Cryptography.aukreqk11v.wasm",
        "integrity": "sha256-vmcYyuKSu4OrMU+9S28msTpbBYvATJepGwAVLu0O2AI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SpawnDev.MultiMedia.wasm",
        "name": "SpawnDev.MultiMedia.3tmk89czsc.wasm",
        "integrity": "sha256-iQNxYM8638/HTpEXqDyMokhzE/YrcLOI0WQ/DTRXDTM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SpawnDev.RTC.wasm",
        "name": "SpawnDev.RTC.jmpw66qj6j.wasm",
        "integrity": "sha256-RbiGY+6arG6IJpAGCxC0l5nNAruxdfYyRzS8KA45YQg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SIPSorcery.wasm",
        "name": "SIPSorcery.81gkhcpdwp.wasm",
        "integrity": "sha256-LL/3D7I210wd29AV1qLvYTX77OoNeemncr3briroakg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SIPSorceryMedia.Abstractions.wasm",
        "name": "SIPSorceryMedia.Abstractions.evfg4tvmuu.wasm",
        "integrity": "sha256-psMWDnBifjyCcYC5pnkGJ4xTqBfUANlbT0BNmhlTU8I=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SpawnDev.UnitTesting.wasm",
        "name": "SpawnDev.UnitTesting.x7qrlz6hlv.wasm",
        "integrity": "sha256-ff6fCTaw2LtrD36mxCivTjuIy4pCDHBdIpWSQ5oDlDI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SpawnDev.UnitTesting.Blazor.wasm",
        "name": "SpawnDev.UnitTesting.Blazor.8vrm73uazg.wasm",
        "integrity": "sha256-bBaXKAzZdFCHiSE4AhWVaoZQeGYeqUEiE9Me/9yd7Ps=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SpawnDev.WebTorrent.wasm",
        "name": "SpawnDev.WebTorrent.vqmgb9zm40.wasm",
        "integrity": "sha256-Jzay4qQQrrFi640R4zUJmwHJuCTGsA/zTc3WX/LvX/U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.CSharp.wasm",
        "name": "Microsoft.CSharp.umoc2dux9t.wasm",
        "integrity": "sha256-SkJyZne8fEWW4jU3twjR6J5cZtOTdRpdQpFXkApfPm0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.VisualBasic.Core.wasm",
        "name": "Microsoft.VisualBasic.Core.gmltau8vp0.wasm",
        "integrity": "sha256-SCswOnDyyZf8hLF3yFLDLodcxsIhIQ38QLwCgF2FPWk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.VisualBasic.wasm",
        "name": "Microsoft.VisualBasic.o6koa33jux.wasm",
        "integrity": "sha256-MKIvJWRhMnILzA0BS2wuOnLqTBSipDcFgC8QBtgBPtc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Win32.Primitives.wasm",
        "name": "Microsoft.Win32.Primitives.52pvzbxwpr.wasm",
        "integrity": "sha256-na/6BrSfvJ1NI/9LVApECtZ5Uk/oZ+IjznPD0JUUCcI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Win32.Registry.wasm",
        "name": "Microsoft.Win32.Registry.7tbewotloe.wasm",
        "integrity": "sha256-S8PNKbP4Nm4QgANoSz1hinXobU5a1yMaw+2wzPmSFfw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.AppContext.wasm",
        "name": "System.AppContext.6b7n5na9iz.wasm",
        "integrity": "sha256-wkV6LyEJaVEJKUZweBaFpg3GKjNE5T8driZngX5yMN0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Buffers.wasm",
        "name": "System.Buffers.ckon5kmglm.wasm",
        "integrity": "sha256-HDFCaeFTx78F21p8HzWydm9n2IXspFM24mghVZ7zG58=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.Concurrent.wasm",
        "name": "System.Collections.Concurrent.kr623jsm4w.wasm",
        "integrity": "sha256-wN0bQwy0pqNRpVlv98yH6yPekiTxqxgtzOsVOzXsSeM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.Immutable.wasm",
        "name": "System.Collections.Immutable.ou0vurziv8.wasm",
        "integrity": "sha256-8A97P1QU5T/locLGgzYaWinnA3Cvv2XkLEPKceMtxXk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.NonGeneric.wasm",
        "name": "System.Collections.NonGeneric.jmc61l3lcr.wasm",
        "integrity": "sha256-lfXx3XYVlsmsRZAJnrfucQ4+eWVnZ3j1XcNkdrr46L0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.Specialized.wasm",
        "name": "System.Collections.Specialized.w0bbvo8gxf.wasm",
        "integrity": "sha256-cJQ0uDt/2ceelgymMGyNBe25kye1N3w31nBUqpYs0lM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.wasm",
        "name": "System.Collections.5f52oqxxto.wasm",
        "integrity": "sha256-8ZdxwjzDvh0OthlvDTbq0B4l8aaklOpBeFDD/CyIxn0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.Annotations.wasm",
        "name": "System.ComponentModel.Annotations.f7cnzq6jny.wasm",
        "integrity": "sha256-dZdxTPaojdnvfB1S2OY95JwxJ6zPV1PW0wPyj+T+8vM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.DataAnnotations.wasm",
        "name": "System.ComponentModel.DataAnnotations.bssmbs6qpe.wasm",
        "integrity": "sha256-xQnOoEuq71v6XtV0d+dGo3InlmOTYe4h319o3MNV/RU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.EventBasedAsync.wasm",
        "name": "System.ComponentModel.EventBasedAsync.yrro7hsms1.wasm",
        "integrity": "sha256-ni1Z0kZfIZYn7OCj4ASVV4rthyLtkzZnAFG+LBxSnZg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.Primitives.wasm",
        "name": "System.ComponentModel.Primitives.bmp8mcbl1f.wasm",
        "integrity": "sha256-s6k3Kj9PuDtybmWVi5O8f/oHOghujcdX/DLJFpbqq6A=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.TypeConverter.wasm",
        "name": "System.ComponentModel.TypeConverter.rjyni1f54b.wasm",
        "integrity": "sha256-Wcu9Si7y9pvUESXiiOReWi7d2QkbQHzlwVmAzH4mIFw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.wasm",
        "name": "System.ComponentModel.x8b6gxey38.wasm",
        "integrity": "sha256-cRL4BGFPK/lbf/1H9LacALYm6McmsCGkUdfRXfq0/Rg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Configuration.wasm",
        "name": "System.Configuration.8fonmeslzv.wasm",
        "integrity": "sha256-nFbW4hdF4HuWHT5kmov1rq/w3gW3o1704XGcT3HqBIs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Console.wasm",
        "name": "System.Console.6fbd45k1o6.wasm",
        "integrity": "sha256-7kx/4+KItpOy1rGgkAEVSS+6GpXcDwpFfV6Pn8/EDq0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Core.wasm",
        "name": "System.Core.2d8wrhg65r.wasm",
        "integrity": "sha256-VlFHw0uYtaxXgkBntlqj2qhkU3bVNOKznNTL28C0n2s=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Data.Common.wasm",
        "name": "System.Data.Common.cp5bttdxwa.wasm",
        "integrity": "sha256-oNJV2Fv1cjxA0SwXCmw99apknnaszlleuJtxs3b/o9I=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Data.DataSetExtensions.wasm",
        "name": "System.Data.DataSetExtensions.t3e4st6hq6.wasm",
        "integrity": "sha256-lw8csoZcpCW4vFHM2eCP3kYkNDiRye7dmQXUM8QLg0U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Data.wasm",
        "name": "System.Data.tsnsqaolai.wasm",
        "integrity": "sha256-gxYQQUb5sYjqpcTG4BDnnjcUHbFUV6jHE1rjKiMCsok=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Contracts.wasm",
        "name": "System.Diagnostics.Contracts.w04ynkslhv.wasm",
        "integrity": "sha256-4I94b8RFJBQcctNzNjQPWylrECvGbKpltVaoYY2U6Tc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Debug.wasm",
        "name": "System.Diagnostics.Debug.lposjtms8n.wasm",
        "integrity": "sha256-GVNOKVriPVNaJ21ckVjUA/SXouyS4BDqu8jR2s0+1jA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.DiagnosticSource.wasm",
        "name": "System.Diagnostics.DiagnosticSource.mr5a9vhofy.wasm",
        "integrity": "sha256-Ti8bEn+ATr2Xjcx6T04dMl/LWRrESE19M96mqkoYqjA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.FileVersionInfo.wasm",
        "name": "System.Diagnostics.FileVersionInfo.kr549addgo.wasm",
        "integrity": "sha256-0OiOVgZa1qDBYwKCgy2shVoqifzk92Eh+8n2Rdv5DQg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Process.wasm",
        "name": "System.Diagnostics.Process.4fo048m2jo.wasm",
        "integrity": "sha256-oP+mtz8IZNNtb1/1a1+adQRXsgZp/ez/CnvJFV0HGaY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.StackTrace.wasm",
        "name": "System.Diagnostics.StackTrace.1ylo7c5c2h.wasm",
        "integrity": "sha256-GewAQ5VGSF0SBCxWX1enyfOb+UqqpvdSVxok2tUdlOs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.TextWriterTraceListener.wasm",
        "name": "System.Diagnostics.TextWriterTraceListener.4ff8kwpt5m.wasm",
        "integrity": "sha256-8BsC+dDmzqXyAUf0Cb4rgOugc4+NVpI0Jn4idjUlvZ4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Tools.wasm",
        "name": "System.Diagnostics.Tools.qbf2989yz5.wasm",
        "integrity": "sha256-FpNgihV63HVYdbMjdilXvlkiYqQhTk5tdX4e8Lc6s9U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.TraceSource.wasm",
        "name": "System.Diagnostics.TraceSource.n3ulmu5axh.wasm",
        "integrity": "sha256-o2kRUubuzLxWcesy2sdN7vpUXrk0HnF2gBkk2U3GHXk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Tracing.wasm",
        "name": "System.Diagnostics.Tracing.1ck7oevdge.wasm",
        "integrity": "sha256-T1364MFN7KvVrzNVXEIfwiIjYDOumovOkfAkoXcnFoM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Drawing.Primitives.wasm",
        "name": "System.Drawing.Primitives.n8tlppp6mf.wasm",
        "integrity": "sha256-R1QKNJu6+p1Fu0WWbzdyJmDyaU4pun/EXnDt6mgNMTM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Drawing.wasm",
        "name": "System.Drawing.5d7z3jqlu2.wasm",
        "integrity": "sha256-Cf9I/9QyTdsXh15XZnu74l+xoCkiNsjcjli6B5xziZk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Dynamic.Runtime.wasm",
        "name": "System.Dynamic.Runtime.qwd8igm9nr.wasm",
        "integrity": "sha256-aiocVeDYoVZ3/+XhLrOhOVb7jYmLwv5OQl7ZBGXCIeo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Formats.Asn1.wasm",
        "name": "System.Formats.Asn1.zbuhuyeon4.wasm",
        "integrity": "sha256-McZYab281c+Ryru0zNdvmp4cfn5ciUfuGgg7TlSoGDQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Formats.Tar.wasm",
        "name": "System.Formats.Tar.ifi7eafa6j.wasm",
        "integrity": "sha256-Dv9YawhTSyd+3lfen1L1Vk7tCZzE6EToiqF6+fZWEio=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Globalization.Calendars.wasm",
        "name": "System.Globalization.Calendars.c8shzrcact.wasm",
        "integrity": "sha256-LDiAd1AhE2topJJXhRsEmYu0XIGEBCbEknh0/JO3WTA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Globalization.Extensions.wasm",
        "name": "System.Globalization.Extensions.k2aq4vrhx5.wasm",
        "integrity": "sha256-hPqehE0EQga8k0iIfjqzpSnmJGgStRQLkEzpv4P/X94=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Globalization.wasm",
        "name": "System.Globalization.3gk5ipfpj1.wasm",
        "integrity": "sha256-PYPHtk5IAG6zLh956APeq1icJHwybE4t7uvlrVvzaAY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Compression.Brotli.wasm",
        "name": "System.IO.Compression.Brotli.7pouum204l.wasm",
        "integrity": "sha256-dRHptRE/EWLoRPWL5ac4xgA5r/pAVr5OIbKkGAU6Rys=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Compression.FileSystem.wasm",
        "name": "System.IO.Compression.FileSystem.g4cow74u7y.wasm",
        "integrity": "sha256-oFCJlYy3zEKTUMP+urds5ezGrzT8c1dKwufHZ9oxYEc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Compression.ZipFile.wasm",
        "name": "System.IO.Compression.ZipFile.ekthguo5cs.wasm",
        "integrity": "sha256-EkgNBN4ptxz5Dx3zY8U/kAnxwXY0w9O0P3KvPKZhEIc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Compression.wasm",
        "name": "System.IO.Compression.45h3o99d8k.wasm",
        "integrity": "sha256-iCWSc38IxXgq/rhrBBl3SFupW4ts1vIt16HC/bOsc/E=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.FileSystem.AccessControl.wasm",
        "name": "System.IO.FileSystem.AccessControl.czvkg344ra.wasm",
        "integrity": "sha256-6BrBmFcVyXI24rFeQh+b7Cm5gfRL7D/lGLmAGbJzAv8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.FileSystem.DriveInfo.wasm",
        "name": "System.IO.FileSystem.DriveInfo.24gi3o0b00.wasm",
        "integrity": "sha256-7tqZ8tDbY+Dt0hdv/0xnwiX6RU0mouEOmEwYvnpObdc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.FileSystem.Primitives.wasm",
        "name": "System.IO.FileSystem.Primitives.7eubnl7idc.wasm",
        "integrity": "sha256-n0n8Jo2fvdNdmAxiQ+YhVCugGH70KwDhyVN5jb/IgzM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.FileSystem.Watcher.wasm",
        "name": "System.IO.FileSystem.Watcher.chcmc3ljgr.wasm",
        "integrity": "sha256-sHDKFJ2VucsjGsPraBsNJt03iT64Nbw3s5G22FdWMwQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.FileSystem.wasm",
        "name": "System.IO.FileSystem.g7w7vpbe7t.wasm",
        "integrity": "sha256-zFFSaxcPJq511KhDEeOibnHfEQB1I61calr1hUZpXNM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.IsolatedStorage.wasm",
        "name": "System.IO.IsolatedStorage.voy292z658.wasm",
        "integrity": "sha256-4ULYXUTKmDvs73IoIzEcfyLbOOS6nM78nZi/l/7oML8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.MemoryMappedFiles.wasm",
        "name": "System.IO.MemoryMappedFiles.l0vs2n50oj.wasm",
        "integrity": "sha256-Bdoril64lT88fN+aHx0Q128nH1Q9mxnrErO9AAlO6oA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Pipelines.wasm",
        "name": "System.IO.Pipelines.dbd2b1cpxa.wasm",
        "integrity": "sha256-F8GXd1fCVsTfC8iuMgNrfo8fSIotcPJGiYsg5NqL8dg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Pipes.AccessControl.wasm",
        "name": "System.IO.Pipes.AccessControl.6v0j5m1sqc.wasm",
        "integrity": "sha256-Itif7g7bvY5VzJK2ZWnkVf1+RzGbEuObWwMtSue6fdU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Pipes.wasm",
        "name": "System.IO.Pipes.fjw3t4g85x.wasm",
        "integrity": "sha256-e5TgkY8iGZ5xla1K7bGeV80PpsZGvq3B3YjwWRbyu3A=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.UnmanagedMemoryStream.wasm",
        "name": "System.IO.UnmanagedMemoryStream.46q9ydp2cq.wasm",
        "integrity": "sha256-RE0+QuF5ElXEfvYfVGxjw1jKIJnnGSeRGNx/k8XrMZo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.wasm",
        "name": "System.IO.67bl8mpb7c.wasm",
        "integrity": "sha256-8ptyKHbVe+9b/z4ccMb3j/pSANC8kJZ9FE8eRj81W34=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.AsyncEnumerable.wasm",
        "name": "System.Linq.AsyncEnumerable.83b0vda9i2.wasm",
        "integrity": "sha256-3BSq62nObcKKGLFvwLwKRrmyR5ZonzzUXf17A0qSgb4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.Expressions.wasm",
        "name": "System.Linq.Expressions.jg89h3ejb5.wasm",
        "integrity": "sha256-E9DKc2zAstleE6Hmt1HYGFKg/l9NhrDfGdgD1iyXNZY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.Parallel.wasm",
        "name": "System.Linq.Parallel.slzb2i9h5h.wasm",
        "integrity": "sha256-ACMedEFm45VOcri/ffyM/gc24BckWAZUUFy+3vYJ7FQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.Queryable.wasm",
        "name": "System.Linq.Queryable.7ff1u2kd0f.wasm",
        "integrity": "sha256-rVWm/YABa4OTltsQLXokaCi6+XM+caR9d/4FM9Ze1Y8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.wasm",
        "name": "System.Linq.ccn6bt0ck9.wasm",
        "integrity": "sha256-VMXpJUa+T1qw6c/kpM+qhsrpHcJw2h1ImlmWcZ3PEt0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Memory.wasm",
        "name": "System.Memory.49x3mu4njc.wasm",
        "integrity": "sha256-6CbjdXMhqZ6E2kSqKNFSyV1Ho8R48nqIO0pIhLBMbxg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Http.Json.wasm",
        "name": "System.Net.Http.Json.tybs4cmgko.wasm",
        "integrity": "sha256-lb+dKc2LXIQk5ZfNVKdNJe1Spmg8Ijjy/eEaAfR2/6U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Http.wasm",
        "name": "System.Net.Http.93nzmkrwp7.wasm",
        "integrity": "sha256-W0oqBrkwTtfqJctUQrcPjrKIWIi4rvBw5UPrZETC1xc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.HttpListener.wasm",
        "name": "System.Net.HttpListener.j7tzqtb1o9.wasm",
        "integrity": "sha256-n1yYXKSTUI8ZZTd++3XYyB3fZKcdbKsPKo2rWyJvx1g=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Mail.wasm",
        "name": "System.Net.Mail.u5wksp0y6j.wasm",
        "integrity": "sha256-0v8c6JtxMIBDVkzH1V4ED8F5XR4YDn8D/lFD7Znn/GE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.NameResolution.wasm",
        "name": "System.Net.NameResolution.oa5cau43sy.wasm",
        "integrity": "sha256-0Cejyi1z5fNVr6VfrUQ1wZbqi74KR52i0vcVVve3cNE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.NetworkInformation.wasm",
        "name": "System.Net.NetworkInformation.ae91mc7wxp.wasm",
        "integrity": "sha256-0tuaDhgGHNFt/8DoSbabHDVBB7rCgYzqUFLE1lf3dyI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Ping.wasm",
        "name": "System.Net.Ping.1iktz2a5en.wasm",
        "integrity": "sha256-CTjTYSRp/DFERX5PxX+16SmTljyHnUIarCWySZLMGxI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Primitives.wasm",
        "name": "System.Net.Primitives.qtmnjuvpei.wasm",
        "integrity": "sha256-Ti60idbv275S3zfguBnBlNt7Ggch11AJA/FLT/4cewI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Quic.wasm",
        "name": "System.Net.Quic.qrd0s3bovd.wasm",
        "integrity": "sha256-SoLDO3cmWEbDMjx+iNfjgxcU81sPIDkkZNPJlmPr+bw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Requests.wasm",
        "name": "System.Net.Requests.cjsz1i8ou5.wasm",
        "integrity": "sha256-yNt884mlsaLvcou/u48tIM5lCK4UurdYxtanQnlhy0k=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Security.wasm",
        "name": "System.Net.Security.2yauw4sh2o.wasm",
        "integrity": "sha256-6kI+8FMGp1MATJIYhoDOnyOX/RinP7+tVTiCaV2YOc0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.ServerSentEvents.wasm",
        "name": "System.Net.ServerSentEvents.nejrthkmh3.wasm",
        "integrity": "sha256-/3sDU3PZ6Jl4m6Mg48kAqIOgpURytSK5XNnJ/IqRygc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.ServicePoint.wasm",
        "name": "System.Net.ServicePoint.x2dmvz79pc.wasm",
        "integrity": "sha256-R3lOU4B2su75K/QB4n4orzoYyN393raI7RK3Ml9m8u4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Sockets.wasm",
        "name": "System.Net.Sockets.gii44nr0jl.wasm",
        "integrity": "sha256-SNmTDnPJp/3+rYZB1e8zyGCOWLJiWVvce2miWF6LMY8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebClient.wasm",
        "name": "System.Net.WebClient.slrlxdowj9.wasm",
        "integrity": "sha256-AOzf+yG46hHEwwsFFawn1p+vN28H8kSCDdk8ZKiisM0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebHeaderCollection.wasm",
        "name": "System.Net.WebHeaderCollection.xbyl1f8xfv.wasm",
        "integrity": "sha256-DZ4So2xb8kMZ4POPINLoEBoMTbrgQsszWJmZbyf+HOs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebProxy.wasm",
        "name": "System.Net.WebProxy.wcmbkewvmm.wasm",
        "integrity": "sha256-8JHnlFHN/Dw1CwQVbW5xHWfiSs38dm9auRN4/LNY0uA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebSockets.Client.wasm",
        "name": "System.Net.WebSockets.Client.00fz63qebl.wasm",
        "integrity": "sha256-UPce7cSCR2LTviZgpGi9AnzsqcPm3S7Re8CObcj0N0s=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebSockets.wasm",
        "name": "System.Net.WebSockets.qzhejoz5z5.wasm",
        "integrity": "sha256-lhU+DmSO2qRnFpXP3iOpSqt2b39oGxpSUSlsMyPdQT8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.wasm",
        "name": "System.Net.4zn4icguuu.wasm",
        "integrity": "sha256-YNTSRuTyCyA0AUDgVZkLdHuXnyRrOlApEEmReFIrHAM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Numerics.Vectors.wasm",
        "name": "System.Numerics.Vectors.vh9eyv65hr.wasm",
        "integrity": "sha256-kwX69acnEG5j+ojUz44LouTv2C1zgfx3HH4oKCdZvTU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Numerics.wasm",
        "name": "System.Numerics.ewxwcrvnfx.wasm",
        "integrity": "sha256-YgTYI3P8x3KCwoYftTEOmzpLAu6rN21PldtNQYyxvR8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ObjectModel.wasm",
        "name": "System.ObjectModel.7c3oa643d8.wasm",
        "integrity": "sha256-50jVND66kDNSUm10WlQJGW7V0ERQ2eHxvcYdX4kQf+Y=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.DataContractSerialization.wasm",
        "name": "System.Private.DataContractSerialization.yjhmavq6w2.wasm",
        "integrity": "sha256-XkivtGIApIQuFXT6SM7pA+LIjbLJxFEqknd2j66FUnY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.Uri.wasm",
        "name": "System.Private.Uri.aaovftgui2.wasm",
        "integrity": "sha256-sg+izHslnAtv7hw2BQf30Iar8CDpSZ7hPg2Yf2i8x4w=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.Xml.Linq.wasm",
        "name": "System.Private.Xml.Linq.6g27ritrnx.wasm",
        "integrity": "sha256-Jhs5V+ysKI5iq/OpWfeF4EYEuboLQA55yZyUjpO0UEc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.Xml.wasm",
        "name": "System.Private.Xml.c86ifkdlwh.wasm",
        "integrity": "sha256-DD9F4GfAGhg3x8nGZY3y6CKTX+BgydO47Q7hvribPlA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.DispatchProxy.wasm",
        "name": "System.Reflection.DispatchProxy.ch4mtgxwzy.wasm",
        "integrity": "sha256-ME2FRwB0f7Z0PT3Ce7Jb50C+XLZIV6RG8jYf4pllnUU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Emit.ILGeneration.wasm",
        "name": "System.Reflection.Emit.ILGeneration.sv7ph6frok.wasm",
        "integrity": "sha256-KC2Q/r1iEG53OQzJS0tyCupEeyyCOBlbJa3/pHFEMb0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Emit.Lightweight.wasm",
        "name": "System.Reflection.Emit.Lightweight.cetjlifibo.wasm",
        "integrity": "sha256-bLdK/Dl/4YfiP9ZYCWpDPO0bZTvFNRrjvzKvdsnk8Eo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Emit.wasm",
        "name": "System.Reflection.Emit.8ufudis1bs.wasm",
        "integrity": "sha256-sGBx7b0keU8yedv5g7qkb0RJ6dLp9+Qf6wW0f2PMZ7I=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Extensions.wasm",
        "name": "System.Reflection.Extensions.lomcreiowd.wasm",
        "integrity": "sha256-K+8kaWp+FF3fL/OaZB4EZ7NE8pujVEhqroSGbsDUf5Y=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Metadata.wasm",
        "name": "System.Reflection.Metadata.zyd7qw6yen.wasm",
        "integrity": "sha256-e/w8sDDD7bk0Hm4QITvuLMu0dDIyBfoYOzVTycdaLJI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Primitives.wasm",
        "name": "System.Reflection.Primitives.z99kurwkj5.wasm",
        "integrity": "sha256-95V08t6Kru93pj/tPmNoDjPkWOCv9vXTEhbVygwovnw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.TypeExtensions.wasm",
        "name": "System.Reflection.TypeExtensions.avb8ys2ybr.wasm",
        "integrity": "sha256-6h9fsPU56NSkjWMnNOdYufo/5/jtKl8jqb8nkmODzeg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.wasm",
        "name": "System.Reflection.03d7jkimai.wasm",
        "integrity": "sha256-/KALwixDztsOTnZGC/+cyiGi+rmzoWjegcaK8nKtI/w=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Resources.Reader.wasm",
        "name": "System.Resources.Reader.zkkp3833zz.wasm",
        "integrity": "sha256-jdikgQZGtSmzoKwmcaUPtK/9olgR54izgaU0XqSpYPQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Resources.ResourceManager.wasm",
        "name": "System.Resources.ResourceManager.asf8ypyhhp.wasm",
        "integrity": "sha256-FjixM9vfFtfaFy7Yuw+53kh18vsDrrrAjIipDhAxTjo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Resources.Writer.wasm",
        "name": "System.Resources.Writer.mt0gzllhlb.wasm",
        "integrity": "sha256-1pAUZ9nkB/6p/frwPTSv3fq4n7wvaluMzNQRffbEsG4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.CompilerServices.Unsafe.wasm",
        "name": "System.Runtime.CompilerServices.Unsafe.713r8kqjh5.wasm",
        "integrity": "sha256-5X+P2fbyQDerkYlRj2sZJSgGkdOr8sl950rBtyv5FjU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.CompilerServices.VisualC.wasm",
        "name": "System.Runtime.CompilerServices.VisualC.iwr88m5y4m.wasm",
        "integrity": "sha256-vtCGb9FHNr2DF2iO8WvlBmSxbeI50Q4RRxVg/WvB/Hw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Extensions.wasm",
        "name": "System.Runtime.Extensions.ht9523ok75.wasm",
        "integrity": "sha256-9W7IkenvgJQYf18CIrp9cYQoIuoeEzTzi+3qrvl7oJk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Handles.wasm",
        "name": "System.Runtime.Handles.0m5g6oo96q.wasm",
        "integrity": "sha256-mJlD6p9hlPCMv/OvbWcuuAVGLMINyiGKFXs7Qo8k580=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.InteropServices.RuntimeInformation.wasm",
        "name": "System.Runtime.InteropServices.RuntimeInformation.qy38p08qqm.wasm",
        "integrity": "sha256-En+zkWhRfeJcZvV905tT4U67e00rSOK45pWfS+muySk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.InteropServices.wasm",
        "name": "System.Runtime.InteropServices.2izr17v8cm.wasm",
        "integrity": "sha256-hgb//XQaM+WNKjjZlVjcFjcfFH9P4fHy9VkhIorA+yo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Intrinsics.wasm",
        "name": "System.Runtime.Intrinsics.kirnyc62k4.wasm",
        "integrity": "sha256-DN0iaWy/LW13evt+xdZmuMRYZqoVrIuR4fbk2rDNLCI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Loader.wasm",
        "name": "System.Runtime.Loader.094vkthw1m.wasm",
        "integrity": "sha256-RF/c+5Mbg6JUBpREHyG3T69LlUaTuWl8ZK7sO92Da2U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Numerics.wasm",
        "name": "System.Runtime.Numerics.jw6uzw5e15.wasm",
        "integrity": "sha256-c9ECS9JUeEl5y05ObbYxzeYMBdACr/kW7EbsQmICtwQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Formatters.wasm",
        "name": "System.Runtime.Serialization.Formatters.8zg0sud5fw.wasm",
        "integrity": "sha256-DMb4FyvQPUKO3dwbycTizSs6z4g3eSL4rjQ4YJptZxQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Json.wasm",
        "name": "System.Runtime.Serialization.Json.15jwnbb4jf.wasm",
        "integrity": "sha256-G6SoUVWBBb+esih7tMm6693aJpKY84CJVVrfaNneC/4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Primitives.wasm",
        "name": "System.Runtime.Serialization.Primitives.jcx37358vq.wasm",
        "integrity": "sha256-k27KS2RzUWZ0KJkF+Fzz0pStgyf8tn+17/fs3jD6V5U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Xml.wasm",
        "name": "System.Runtime.Serialization.Xml.fogot5ogr4.wasm",
        "integrity": "sha256-b1u2290bSYxqIrCH+YeiTa7TsP+oqTkZT1vz0/+m/sc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.wasm",
        "name": "System.Runtime.Serialization.9u7902ze8b.wasm",
        "integrity": "sha256-D3iJWa6z4jHOc+TuSid0G7yRBuIBQ7JXBY576qhP91c=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.wasm",
        "name": "System.Runtime.408iiuj3sh.wasm",
        "integrity": "sha256-BHOy5e9SjSss44UAfhbJxMYG06uuQ3mNLDk5UhUxa0s=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.AccessControl.wasm",
        "name": "System.Security.AccessControl.mh70w4wl0x.wasm",
        "integrity": "sha256-FupP4L8/2tmTb78CiVwNMpH+7G5s0uX6E8S6vVy1HKg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Claims.wasm",
        "name": "System.Security.Claims.k1jmon63ys.wasm",
        "integrity": "sha256-6P3iPDUZmhYY3MKcn/ckW95y2PV5l9A3Hdxc2U6h2MI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.Algorithms.wasm",
        "name": "System.Security.Cryptography.Algorithms.0kt9crods2.wasm",
        "integrity": "sha256-IGqflpF0INKJJvkjEA1jW5Xs9ZSM2b31UZ+8hCMhOXM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.Cng.wasm",
        "name": "System.Security.Cryptography.Cng.x2n63imyyk.wasm",
        "integrity": "sha256-p/p8Mt6ovfX+VUNkicjxj5VCjRFU0VVLQJAx/OGBKMs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.Csp.wasm",
        "name": "System.Security.Cryptography.Csp.o3v2lb9ki9.wasm",
        "integrity": "sha256-DCUEgn+8b12IJ3IpLJSlj2bDRtJ2TzN5wX7NqU/9Vts=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.Encoding.wasm",
        "name": "System.Security.Cryptography.Encoding.ft3a0c4bxy.wasm",
        "integrity": "sha256-LR4nlm4Yj1rVC9tJ1BMmTic+mFT/w1uGNxJ7evH5hXw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.OpenSsl.wasm",
        "name": "System.Security.Cryptography.OpenSsl.v8o9t988gs.wasm",
        "integrity": "sha256-AZyXCAIHr+mbp45M9OV48vk+69GoVq3hS6xUhaMHZSw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.Primitives.wasm",
        "name": "System.Security.Cryptography.Primitives.mair9vq79t.wasm",
        "integrity": "sha256-ooXjWUse2Jnf3g190uYVOL91+NLD8QUxtFpb4jYBNdA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.X509Certificates.wasm",
        "name": "System.Security.Cryptography.X509Certificates.o8e7gdgz0f.wasm",
        "integrity": "sha256-KJjGtOYBD2bxZFNMYm03/50W5NWVsD6OGWla3TerAoo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.wasm",
        "name": "System.Security.Cryptography.vu6jjd3rcp.wasm",
        "integrity": "sha256-d+DcO54rQmk5uQEW2McFp4oxkw89Y/d8ZaggLuCRcso=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Principal.Windows.wasm",
        "name": "System.Security.Principal.Windows.tw83djiuvu.wasm",
        "integrity": "sha256-I7f0ZYoZBmG4r+ppmG0iTLgOJgfbu8UFouIvIML45M8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Principal.wasm",
        "name": "System.Security.Principal.ltqvnuenw6.wasm",
        "integrity": "sha256-dxfa70gSdDb/116QMkNYNge+XCRq8qB7ESIRVIRCM6k=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.SecureString.wasm",
        "name": "System.Security.SecureString.hfgenauhce.wasm",
        "integrity": "sha256-U7YzB+2lioTGYPtAlYWyXrf47fOQuwGKopHB6cviUtY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.wasm",
        "name": "System.Security.1qwehh3rfi.wasm",
        "integrity": "sha256-193XfBUaYAvQ255ieWpKRdlu5c7Iz2b9sl3LzXC/684=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ServiceModel.Web.wasm",
        "name": "System.ServiceModel.Web.xntwv4pgni.wasm",
        "integrity": "sha256-0wJoM+j7yfaJIa+GQ9aPkD6jWIPf5eC7PlmdcHlccck=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ServiceProcess.wasm",
        "name": "System.ServiceProcess.xonmaxk25l.wasm",
        "integrity": "sha256-j7AdfPuqGHiqc504GksG1ZEkDBakaUsQftKXQQRwwNY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Encoding.CodePages.wasm",
        "name": "System.Text.Encoding.CodePages.p4emic6hjb.wasm",
        "integrity": "sha256-92wXalJDnPvZMag6srzCBwlbr9EkqvZ5CVRuSj6i4vk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Encoding.Extensions.wasm",
        "name": "System.Text.Encoding.Extensions.srfwllqxp6.wasm",
        "integrity": "sha256-2KCvdhEk/tluo6WH+hER1pAMJUe+pmFofaIQKKN3bSQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Encoding.wasm",
        "name": "System.Text.Encoding.htvqpzm058.wasm",
        "integrity": "sha256-laj4teBms8B3djWL4nM2k5R3Pnpu1N6wM3tR0k/fp9I=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Encodings.Web.wasm",
        "name": "System.Text.Encodings.Web.z2ybwhkon2.wasm",
        "integrity": "sha256-S8VT8WOWhGAPS+NGgBZFucmVZ0E3i/7owla9Hx9hGhE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Json.wasm",
        "name": "System.Text.Json.ko6861pa2t.wasm",
        "integrity": "sha256-LLIIzmghEI+J0aVhAOyVLXX3y7sC4PcjamGCxiCuAcM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.RegularExpressions.wasm",
        "name": "System.Text.RegularExpressions.hk1fpxirkk.wasm",
        "integrity": "sha256-sXL+4tInDtMp0EJqKiVJQfcBIACxySVz+4jJGSz2RZg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.AccessControl.wasm",
        "name": "System.Threading.AccessControl.xn5weqfl9t.wasm",
        "integrity": "sha256-U3UzSlh+iIX8V71jjCNYPXJ3+E/s2qwpkJwbKrKMBKA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Channels.wasm",
        "name": "System.Threading.Channels.fg41rpqsia.wasm",
        "integrity": "sha256-MZRgJpw9aMWpULpkbxSdRUkP8+tTNpJnc74JZgxTmjw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Overlapped.wasm",
        "name": "System.Threading.Overlapped.s6j4ly2axs.wasm",
        "integrity": "sha256-3Cg7PpkH+Tqm3nZ+NTS2eYuW0DJwCl+N2YhhgH3MqXc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Tasks.Dataflow.wasm",
        "name": "System.Threading.Tasks.Dataflow.vueubhjtht.wasm",
        "integrity": "sha256-SRqr9nQpEX7bWv/3zp+fJtPr38UEwULbezlLjwRY8nA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Tasks.Extensions.wasm",
        "name": "System.Threading.Tasks.Extensions.eofj4l18ur.wasm",
        "integrity": "sha256-Ht4+0mUFYiRCMgXs07XGrKbCeh2NJpUatX7qmnZgynw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Tasks.Parallel.wasm",
        "name": "System.Threading.Tasks.Parallel.tgl500e7gs.wasm",
        "integrity": "sha256-7Zv61gvbNjhFny2Hc0wWrZnctFGmI+lMekxJ2u2zSmM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Tasks.wasm",
        "name": "System.Threading.Tasks.9wlnzyn2u0.wasm",
        "integrity": "sha256-mYnQzOCae0NxNkwIB65wqTHdQ8VzVeYVZJv5D7jjLjk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Thread.wasm",
        "name": "System.Threading.Thread.vbk8bvz5j0.wasm",
        "integrity": "sha256-609zZ/Ppy0tQu17u43Gg7VlMqN3DdOOauYN5WtJEIGM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.ThreadPool.wasm",
        "name": "System.Threading.ThreadPool.w69ao9clrc.wasm",
        "integrity": "sha256-CCmJBCrpzLAzL9vlOzv4KhfXJrSSKUhrcF/4FM3Ga58=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Timer.wasm",
        "name": "System.Threading.Timer.29r6h46k2k.wasm",
        "integrity": "sha256-io/jBXLhvcOtoCXG0ROiwklYUWQm83Va8XEaFpsE+u8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.wasm",
        "name": "System.Threading.67mu17c7mg.wasm",
        "integrity": "sha256-HpdHenzt4ebIy4r+wxb0GIuRzm8uUOZ5W9uDzfYi/w4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Transactions.Local.wasm",
        "name": "System.Transactions.Local.37rz8q5986.wasm",
        "integrity": "sha256-kUt5rvDTjqXh5qHda8sMPonDfPoF5wJh8TOfhi787hc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Transactions.wasm",
        "name": "System.Transactions.onxwenk75g.wasm",
        "integrity": "sha256-ZPx492w1+u4/VlZNEM3DB9BtVOlxS/sJ6SihmLKm9vg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ValueTuple.wasm",
        "name": "System.ValueTuple.vanb4dluvf.wasm",
        "integrity": "sha256-t/IrCEB8mwxkqB7pWXXpVTPR6kp7HVdEVPR5cEbU950=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Web.HttpUtility.wasm",
        "name": "System.Web.HttpUtility.io9kow6kql.wasm",
        "integrity": "sha256-/pmwPJmO5kGgnCH5eRVibscsWU0WHG1cq+MAqzKapsc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Web.wasm",
        "name": "System.Web.yq1k6uyfig.wasm",
        "integrity": "sha256-2iMPF37oE39AXl5COfdp6KAR7kA9saXSARm4kiG8Awc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Windows.wasm",
        "name": "System.Windows.o6l9xldkcc.wasm",
        "integrity": "sha256-gNjwys1kthkod+pKxvb+QajAVPSPLcabtWSXh5BRR9c=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.Linq.wasm",
        "name": "System.Xml.Linq.xw3jlhxq8h.wasm",
        "integrity": "sha256-b7yD3uJK1Lb+e6RTGA8OIoRfUe99qmlOpVdM8JSVvgs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.ReaderWriter.wasm",
        "name": "System.Xml.ReaderWriter.0pp07sftc7.wasm",
        "integrity": "sha256-7Pa627J9wtbahmTHcsR13cPGmHpWQ/hQpGdLy879xWw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.Serialization.wasm",
        "name": "System.Xml.Serialization.99ecxramtu.wasm",
        "integrity": "sha256-0//0yOOnFd7I//l5SZF9XOiBxKsNRkLUInwYfWxnoQM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XDocument.wasm",
        "name": "System.Xml.XDocument.zvr941j0i3.wasm",
        "integrity": "sha256-kbJrk3ZrZpvaoLH+XMXpY3ttsAfMQLOq73c5Pi5KF6M=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XPath.XDocument.wasm",
        "name": "System.Xml.XPath.XDocument.gc07pdn8sw.wasm",
        "integrity": "sha256-gIWHx+gkRXzP2+1tbKNQQBdplqH7ucHZXXANxlShdSg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XPath.wasm",
        "name": "System.Xml.XPath.rjypl7uqj2.wasm",
        "integrity": "sha256-2XMH1Hug3Az8cYeDufYDzCGQz3GvkTsGLYmBV+J/pj4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XmlDocument.wasm",
        "name": "System.Xml.XmlDocument.a9g0rcmdsf.wasm",
        "integrity": "sha256-Tp2QxnBgbuIm/51WckUCrNEn0cDy212GQQX6dXtUJ6I=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XmlSerializer.wasm",
        "name": "System.Xml.XmlSerializer.qf2wfirnar.wasm",
        "integrity": "sha256-qsQE85H24XuWcILBgY/mZdGcHsiIkBgGdJXIKNaqsFc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.wasm",
        "name": "System.Xml.4u5hlbsvt1.wasm",
        "integrity": "sha256-zPiLs8fZH9ZkWC6eZvvm0wJE2tcGgh95r5QLneIo3qw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.wasm",
        "name": "System.q4t9czmx21.wasm",
        "integrity": "sha256-RmwJFtYjoaGqOkFo04nrrC775an2aSqsvm8Z95erCBs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "WindowsBase.wasm",
        "name": "WindowsBase.kdodnftcs0.wasm",
        "integrity": "sha256-qJ+CtLwRm6kGPe4u0A+35RD2uELqkt5tyRa2blkAZpw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "mscorlib.wasm",
        "name": "mscorlib.cqo3np11lp.wasm",
        "integrity": "sha256-9GrgJ0LpB7gHeMkFFNOF56bxRGIkiBU70WgiJIcdhSU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "netstandard.wasm",
        "name": "netstandard.trfv8kyc3h.wasm",
        "integrity": "sha256-Z6YF3KhL0gfv+tWejPc54KFv4sbkeO9zJ0qFSJU6vGk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "ILGPU.Algorithms.wasm",
        "name": "ILGPU.Algorithms.tjvvl4dn09.wasm",
        "integrity": "sha256-eQZbBx212rdmoT/mWmH6szEhjoOVtf9yqZOJxArYjOE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "ILGPU.wasm",
        "name": "ILGPU.2uz8o4r4hq.wasm",
        "integrity": "sha256-lkAvHJ8zYVPcKEcH3gMa5Bzu01pLoOKDuSXOJAx8Pao=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SpawnDev.ILGPU.Demo.Shared.wasm",
        "name": "SpawnDev.ILGPU.Demo.Shared.uc9aj4o1q8.wasm",
        "integrity": "sha256-3pA6Qvde+K16onO1xPIP00vSWMqgi8JSciZbx39sJHw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SpawnDev.ILGPU.wasm",
        "name": "SpawnDev.ILGPU.pb8svs2a1k.wasm",
        "integrity": "sha256-JSgG/YF6bGFtq7DRUY3ZotlHKO3hiDU80U2I5fmFiYM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SpawnDev.ILGPU.P2P.wasm",
        "name": "SpawnDev.ILGPU.P2P.mdretkmm0s.wasm",
        "integrity": "sha256-5pCkTbpG8BfqRv3SthcEW6ew9TwWnr7deYIH26M9+YM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SpawnDev.ILGPU.Demo.wasm",
        "name": "SpawnDev.ILGPU.Demo.3yfjwsl6aa.wasm",
        "integrity": "sha256-e7cPVhfBNK1wEXVlM6FWpcLgmyU+Zx4aY3ZG9smuLck=",
        "cache": "force-cache"
      }
    ],
    "libraryInitializers": [
      {
        "name": "SpawnDev.BlazorJS.lib.module.js"
      }
    ],
    "modulesAfterRuntimeReady": [
      {
        "name": "../SpawnDev.BlazorJS.lib.module.js"
      }
    ]
  },
  "debugLevel": 0,
  "globalizationMode": "sharded",
  "extensions": {
    "blazor": {}
  },
  "runtimeConfig": {
    "runtimeOptions": {
      "configProperties": {
        "Microsoft.AspNetCore.Components.Routing.RegexConstraintSupport": false,
        "System.Diagnostics.Debugger.IsSupported": false,
        "System.Diagnostics.Metrics.Meter.IsSupported": false,
        "System.Diagnostics.Tracing.EventSource.IsSupported": false,
        "System.GC.Server": true,
        "System.Globalization.Invariant": false,
        "System.TimeZoneInfo.Invariant": false,
        "System.Linq.Enumerable.IsSizeOptimized": true,
        "System.Net.Http.EnableActivityPropagation": false,
        "System.Net.Http.WasmEnableStreamingResponse": true,
        "System.Net.SocketsHttpHandler.Http3Support": false,
        "System.Reflection.Metadata.MetadataUpdater.IsSupported": false,
        "System.Resources.UseSystemResourceKeys": true,
        "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization": false,
        "System.Text.Encoding.EnableUnsafeUTF7Encoding": false,
        "System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault": true
      }
    }
  }
}/*json-end*/);export{gt as default,ft as dotnet,mt as exit};
