//! Licensed to the .NET Foundation under one or more agreements.
//! The .NET Foundation licenses this file to you under the MIT license.

var e=!1;const t=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,4,1,96,0,0,3,2,1,0,10,8,1,6,0,6,64,25,11,11])),o=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,5,1,96,0,1,123,3,2,1,0,10,15,1,13,0,65,1,253,15,65,2,253,15,253,128,2,11])),n=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,5,1,96,0,1,123,3,2,1,0,10,10,1,8,0,65,0,253,15,253,98,11])),r=Symbol.for("wasm promise_control");function i(e,t){let o=null;const n=new Promise((function(n,r){o={isDone:!1,promise:null,resolve:t=>{o.isDone||(o.isDone=!0,n(t),e&&e())},reject:e=>{o.isDone||(o.isDone=!0,r(e),t&&t())}}}));o.promise=n;const i=n;return i[r]=o,{promise:i,promise_control:o}}function s(e){return e[r]}function a(e){e&&function(e){return void 0!==e[r]}(e)||Be(!1,"Promise is not controllable")}const l="__mono_message__",c=["debug","log","trace","warn","info","error"],d="MONO_WASM: ";let u,f,m,g,p,h;function w(e){g=e}function b(e){if(Pe.diagnosticTracing){const t="function"==typeof e?e():e;console.debug(d+t)}}function y(e,...t){console.info(d+e,...t)}function v(e,...t){console.info(e,...t)}function E(e,...t){console.warn(d+e,...t)}function _(e,...t){if(t&&t.length>0&&t[0]&&"object"==typeof t[0]){if(t[0].silent)return;if(t[0].toString)return void console.error(d+e,t[0].toString())}console.error(d+e,...t)}function x(e,t,o){return function(...n){try{let r=n[0];if(void 0===r)r="undefined";else if(null===r)r="null";else if("function"==typeof r)r=r.toString();else if("string"!=typeof r)try{r=JSON.stringify(r)}catch(e){r=r.toString()}t(o?JSON.stringify({method:e,payload:r,arguments:n.slice(1)}):[e+r,...n.slice(1)])}catch(e){m.error(`proxyConsole failed: ${e}`)}}}function j(e,t,o){f=t,g=e,m={...t};const n=`${o}/console`.replace("https://","wss://").replace("http://","ws://");u=new WebSocket(n),u.addEventListener("error",A),u.addEventListener("close",S),function(){for(const e of c)f[e]=x(`console.${e}`,T,!0)}()}function R(e){let t=30;const o=()=>{u?0==u.bufferedAmount||0==t?(e&&v(e),function(){for(const e of c)f[e]=x(`console.${e}`,m.log,!1)}(),u.removeEventListener("error",A),u.removeEventListener("close",S),u.close(1e3,e),u=void 0):(t--,globalThis.setTimeout(o,100)):e&&m&&m.log(e)};o()}function T(e){u&&u.readyState===WebSocket.OPEN?u.send(e):m.log(e)}function A(e){m.error(`[${g}] proxy console websocket error: ${e}`,e)}function S(e){m.debug(`[${g}] proxy console websocket closed: ${e}`,e)}function D(){Pe.preferredIcuAsset=O(Pe.config);let e="invariant"==Pe.config.globalizationMode;if(!e)if(Pe.preferredIcuAsset)Pe.diagnosticTracing&&b("ICU data archive(s) available, disabling invariant mode");else{if("custom"===Pe.config.globalizationMode||"all"===Pe.config.globalizationMode||"sharded"===Pe.config.globalizationMode){const e="invariant globalization mode is inactive and no ICU data archives are available";throw _(`ERROR: ${e}`),new Error(e)}Pe.diagnosticTracing&&b("ICU data archive(s) not available, using invariant globalization mode"),e=!0,Pe.preferredIcuAsset=null}const t="DOTNET_SYSTEM_GLOBALIZATION_INVARIANT",o=Pe.config.environmentVariables;if(void 0===o[t]&&e&&(o[t]="1"),void 0===o.TZ)try{const e=Intl.DateTimeFormat().resolvedOptions().timeZone||null;e&&(o.TZ=e)}catch(e){y("failed to detect timezone, will fallback to UTC")}}function O(e){var t;if((null===(t=e.resources)||void 0===t?void 0:t.icu)&&"invariant"!=e.globalizationMode){const t=e.applicationCulture||(ke?globalThis.navigator&&globalThis.navigator.languages&&globalThis.navigator.languages[0]:Intl.DateTimeFormat().resolvedOptions().locale),o=e.resources.icu;let n=null;if("custom"===e.globalizationMode){if(o.length>=1)return o[0].name}else t&&"all"!==e.globalizationMode?"sharded"===e.globalizationMode&&(n=function(e){const t=e.split("-")[0];return"en"===t||["fr","fr-FR","it","it-IT","de","de-DE","es","es-ES"].includes(e)?"icudt_EFIGS.dat":["zh","ko","ja"].includes(t)?"icudt_CJK.dat":"icudt_no_CJK.dat"}(t)):n="icudt.dat";if(n)for(let e=0;e<o.length;e++){const t=o[e];if(t.virtualPath===n)return t.name}}return e.globalizationMode="invariant",null}(new Date).valueOf();const C=class{constructor(e){this.url=e}toString(){return this.url}};async function k(e,t){try{const o="function"==typeof globalThis.fetch;if(Se){const n=e.startsWith("file://");if(!n&&o)return globalThis.fetch(e,t||{credentials:"same-origin"});p||(h=Ne.require("url"),p=Ne.require("fs")),n&&(e=h.fileURLToPath(e));const r=await p.promises.readFile(e);return{ok:!0,headers:{length:0,get:()=>null},url:e,arrayBuffer:()=>r,json:()=>JSON.parse(r),text:()=>{throw new Error("NotImplementedException")}}}if(o)return globalThis.fetch(e,t||{credentials:"same-origin"});if("function"==typeof read)return{ok:!0,url:e,headers:{length:0,get:()=>null},arrayBuffer:()=>new Uint8Array(read(e,"binary")),json:()=>JSON.parse(read(e,"utf8")),text:()=>read(e,"utf8")}}catch(t){return{ok:!1,url:e,status:500,headers:{length:0,get:()=>null},statusText:"ERR28: "+t,arrayBuffer:()=>{throw t},json:()=>{throw t},text:()=>{throw t}}}throw new Error("No fetch implementation available")}function I(e){return"string"!=typeof e&&Be(!1,"url must be a string"),!M(e)&&0!==e.indexOf("./")&&0!==e.indexOf("../")&&globalThis.URL&&globalThis.document&&globalThis.document.baseURI&&(e=new URL(e,globalThis.document.baseURI).toString()),e}const U=/^[a-zA-Z][a-zA-Z\d+\-.]*?:\/\//,P=/[a-zA-Z]:[\\/]/;function M(e){return Se||Ie?e.startsWith("/")||e.startsWith("\\")||-1!==e.indexOf("///")||P.test(e):U.test(e)}let L,N=0;const $=[],z=[],W=new Map,F={"js-module-threads":!0,"js-module-runtime":!0,"js-module-dotnet":!0,"js-module-native":!0,"js-module-diagnostics":!0},B={...F,"js-module-library-initializer":!0},V={...F,dotnetwasm:!0,heap:!0,manifest:!0},q={...B,manifest:!0},H={...B,dotnetwasm:!0},J={dotnetwasm:!0,symbols:!0},Z={...B,dotnetwasm:!0,symbols:!0},Q={symbols:!0};function G(e){return!("icu"==e.behavior&&e.name!=Pe.preferredIcuAsset)}function K(e,t,o){null!=t||(t=[]),Be(1==t.length,`Expect to have one ${o} asset in resources`);const n=t[0];return n.behavior=o,X(n),e.push(n),n}function X(e){V[e.behavior]&&W.set(e.behavior,e)}function Y(e){Be(V[e],`Unknown single asset behavior ${e}`);const t=W.get(e);if(t&&!t.resolvedUrl)if(t.resolvedUrl=Pe.locateFile(t.name),F[t.behavior]){const e=ge(t);e?("string"!=typeof e&&Be(!1,"loadBootResource response for 'dotnetjs' type should be a URL string"),t.resolvedUrl=e):t.resolvedUrl=ce(t.resolvedUrl,t.behavior)}else if("dotnetwasm"!==t.behavior)throw new Error(`Unknown single asset behavior ${e}`);return t}function ee(e){const t=Y(e);return Be(t,`Single asset for ${e} not found`),t}let te=!1;async function oe(){if(!te){te=!0,Pe.diagnosticTracing&&b("mono_download_assets");try{const e=[],t=[],o=(e,t)=>{!Z[e.behavior]&&G(e)&&Pe.expected_instantiated_assets_count++,!H[e.behavior]&&G(e)&&(Pe.expected_downloaded_assets_count++,t.push(se(e)))};for(const t of $)o(t,e);for(const e of z)o(e,t);Pe.allDownloadsQueued.promise_control.resolve(),Promise.all([...e,...t]).then((()=>{Pe.allDownloadsFinished.promise_control.resolve()})).catch((e=>{throw Pe.err("Error in mono_download_assets: "+e),Xe(1,e),e})),await Pe.runtimeModuleLoaded.promise;const n=async e=>{const t=await e;if(t.buffer){if(!Z[t.behavior]){t.buffer&&"object"==typeof t.buffer||Be(!1,"asset buffer must be array-like or buffer-like or promise of these"),"string"!=typeof t.resolvedUrl&&Be(!1,"resolvedUrl must be string");const e=t.resolvedUrl,o=await t.buffer,n=new Uint8Array(o);pe(t),await Ue.beforeOnRuntimeInitialized.promise,Ue.instantiate_asset(t,e,n)}}else J[t.behavior]?("symbols"===t.behavior&&(await Ue.instantiate_symbols_asset(t),pe(t)),J[t.behavior]&&++Pe.actual_downloaded_assets_count):(t.isOptional||Be(!1,"Expected asset to have the downloaded buffer"),!H[t.behavior]&&G(t)&&Pe.expected_downloaded_assets_count--,!Z[t.behavior]&&G(t)&&Pe.expected_instantiated_assets_count--)},r=[],i=[];for(const t of e)r.push(n(t));for(const e of t)i.push(n(e));Promise.all(r).then((()=>{Ce||Ue.coreAssetsInMemory.promise_control.resolve()})).catch((e=>{throw Pe.err("Error in mono_download_assets: "+e),Xe(1,e),e})),Promise.all(i).then((async()=>{Ce||(await Ue.coreAssetsInMemory.promise,Ue.allAssetsInMemory.promise_control.resolve())})).catch((e=>{throw Pe.err("Error in mono_download_assets: "+e),Xe(1,e),e}))}catch(e){throw Pe.err("Error in mono_download_assets: "+e),e}}}let ne=!1;function re(){if(ne)return;ne=!0;const e=Pe.config,t=[];if(e.assets)for(const t of e.assets)"object"!=typeof t&&Be(!1,`asset must be object, it was ${typeof t} : ${t}`),"string"!=typeof t.behavior&&Be(!1,"asset behavior must be known string"),"string"!=typeof t.name&&Be(!1,"asset name must be string"),t.resolvedUrl&&"string"!=typeof t.resolvedUrl&&Be(!1,"asset resolvedUrl could be string"),t.hash&&"string"!=typeof t.hash&&Be(!1,"asset resolvedUrl could be string"),t.pendingDownload&&"object"!=typeof t.pendingDownload&&Be(!1,"asset pendingDownload could be object"),t.isCore?$.push(t):z.push(t),X(t);else if(e.resources){const o=e.resources;o.wasmNative||Be(!1,"resources.wasmNative must be defined"),o.jsModuleNative||Be(!1,"resources.jsModuleNative must be defined"),o.jsModuleRuntime||Be(!1,"resources.jsModuleRuntime must be defined"),K(z,o.wasmNative,"dotnetwasm"),K(t,o.jsModuleNative,"js-module-native"),K(t,o.jsModuleRuntime,"js-module-runtime"),o.jsModuleDiagnostics&&K(t,o.jsModuleDiagnostics,"js-module-diagnostics");const n=(e,t,o)=>{const n=e;n.behavior=t,o?(n.isCore=!0,$.push(n)):z.push(n)};if(o.coreAssembly)for(let e=0;e<o.coreAssembly.length;e++)n(o.coreAssembly[e],"assembly",!0);if(o.assembly)for(let e=0;e<o.assembly.length;e++)n(o.assembly[e],"assembly",!o.coreAssembly);if(0!=e.debugLevel&&Pe.isDebuggingSupported()){if(o.corePdb)for(let e=0;e<o.corePdb.length;e++)n(o.corePdb[e],"pdb",!0);if(o.pdb)for(let e=0;e<o.pdb.length;e++)n(o.pdb[e],"pdb",!o.corePdb)}if(e.loadAllSatelliteResources&&o.satelliteResources)for(const e in o.satelliteResources)for(let t=0;t<o.satelliteResources[e].length;t++){const r=o.satelliteResources[e][t];r.culture=e,n(r,"resource",!o.coreAssembly)}if(o.coreVfs)for(let e=0;e<o.coreVfs.length;e++)n(o.coreVfs[e],"vfs",!0);if(o.vfs)for(let e=0;e<o.vfs.length;e++)n(o.vfs[e],"vfs",!o.coreVfs);const r=O(e);if(r&&o.icu)for(let e=0;e<o.icu.length;e++){const t=o.icu[e];t.name===r&&n(t,"icu",!1)}if(o.wasmSymbols)for(let e=0;e<o.wasmSymbols.length;e++)n(o.wasmSymbols[e],"symbols",!1)}if(e.appsettings)for(let t=0;t<e.appsettings.length;t++){const o=e.appsettings[t],n=he(o);"appsettings.json"!==n&&n!==`appsettings.${e.applicationEnvironment}.json`||z.push({name:o,behavior:"vfs",cache:"no-cache",useCredentials:!0})}e.assets=[...$,...z,...t]}async function ie(e){const t=await se(e);return await t.pendingDownloadInternal.response,t.buffer}async function se(e){try{return await ae(e)}catch(t){if(!Pe.enableDownloadRetry)throw t;if(Ie||Se)throw t;if(e.pendingDownload&&e.pendingDownloadInternal==e.pendingDownload)throw t;if(e.resolvedUrl&&-1!=e.resolvedUrl.indexOf("file://"))throw t;if(t&&404==t.status)throw t;e.pendingDownloadInternal=void 0,await Pe.allDownloadsQueued.promise;try{return Pe.diagnosticTracing&&b(`Retrying download '${e.name}'`),await ae(e)}catch(t){return e.pendingDownloadInternal=void 0,await new Promise((e=>globalThis.setTimeout(e,100))),Pe.diagnosticTracing&&b(`Retrying download (2) '${e.name}' after delay`),await ae(e)}}}async function ae(e){for(;L;)await L.promise;try{++N,N==Pe.maxParallelDownloads&&(Pe.diagnosticTracing&&b("Throttling further parallel downloads"),L=i());const t=await async function(e){if(e.pendingDownload&&(e.pendingDownloadInternal=e.pendingDownload),e.pendingDownloadInternal&&e.pendingDownloadInternal.response)return e.pendingDownloadInternal.response;if(e.buffer){const t=await e.buffer;return e.resolvedUrl||(e.resolvedUrl="undefined://"+e.name),e.pendingDownloadInternal={url:e.resolvedUrl,name:e.name,response:Promise.resolve({ok:!0,arrayBuffer:()=>t,json:()=>JSON.parse(new TextDecoder("utf-8").decode(t)),text:()=>{throw new Error("NotImplementedException")},headers:{get:()=>{}}})},e.pendingDownloadInternal.response}const t=e.loadRemote&&Pe.config.remoteSources?Pe.config.remoteSources:[""];let o;for(let n of t){n=n.trim(),"./"===n&&(n="");const t=le(e,n);e.name===t?Pe.diagnosticTracing&&b(`Attempting to download '${t}'`):Pe.diagnosticTracing&&b(`Attempting to download '${t}' for ${e.name}`);try{e.resolvedUrl=t;const n=fe(e);if(e.pendingDownloadInternal=n,o=await n.response,!o||!o.ok)continue;return o}catch(e){o||(o={ok:!1,url:t,status:0,statusText:""+e});continue}}const n=e.isOptional||e.name.match(/\.pdb$/)&&Pe.config.ignorePdbLoadErrors;if(o||Be(!1,`Response undefined ${e.name}`),!n){const t=new Error(`download '${o.url}' for ${e.name} failed ${o.status} ${o.statusText}`);throw t.status=o.status,t}y(`optional download '${o.url}' for ${e.name} failed ${o.status} ${o.statusText}`)}(e);return t?(J[e.behavior]||(e.buffer=await t.arrayBuffer(),++Pe.actual_downloaded_assets_count),e):e}finally{if(--N,L&&N==Pe.maxParallelDownloads-1){Pe.diagnosticTracing&&b("Resuming more parallel downloads");const e=L;L=void 0,e.promise_control.resolve()}}}function le(e,t){let o;return null==t&&Be(!1,`sourcePrefix must be provided for ${e.name}`),e.resolvedUrl?o=e.resolvedUrl:(o=""===t?"assembly"===e.behavior||"pdb"===e.behavior?e.name:"resource"===e.behavior&&e.culture&&""!==e.culture?`${e.culture}/${e.name}`:e.name:t+e.name,o=ce(Pe.locateFile(o),e.behavior)),o&&"string"==typeof o||Be(!1,"attemptUrl need to be path or url string"),o}function ce(e,t){return Pe.modulesUniqueQuery&&q[t]&&(e+=Pe.modulesUniqueQuery),e}let de=0;const ue=new Set;function fe(e){try{e.resolvedUrl||Be(!1,"Request's resolvedUrl must be set");const t=function(e){let t=e.resolvedUrl;if(Pe.loadBootResource){const o=ge(e);if(o instanceof Promise)return o;"string"==typeof o&&(t=o)}const o={};return e.cache?o.cache=e.cache:Pe.config.disableNoCacheFetch||(o.cache="no-cache"),e.useCredentials?o.credentials="include":!Pe.config.disableIntegrityCheck&&e.hash&&(o.integrity=e.hash),Pe.fetch_like(t,o)}(e),o={name:e.name,url:e.resolvedUrl,response:t};return ue.add(e.name),o.response.then((()=>{"assembly"==e.behavior&&Pe.loadedAssemblies.push(e.name),de++,Pe.onDownloadResourceProgress&&Pe.onDownloadResourceProgress(de,ue.size)})),o}catch(t){const o={ok:!1,url:e.resolvedUrl,status:500,statusText:"ERR29: "+t,arrayBuffer:()=>{throw t},json:()=>{throw t}};return{name:e.name,url:e.resolvedUrl,response:Promise.resolve(o)}}}const me={resource:"assembly",assembly:"assembly",pdb:"pdb",icu:"globalization",vfs:"configuration",manifest:"manifest",dotnetwasm:"dotnetwasm","js-module-dotnet":"dotnetjs","js-module-native":"dotnetjs","js-module-runtime":"dotnetjs","js-module-threads":"dotnetjs"};function ge(e){var t;if(Pe.loadBootResource){const o=null!==(t=e.hash)&&void 0!==t?t:"",n=e.resolvedUrl,r=me[e.behavior];if(r){const t=Pe.loadBootResource(r,e.name,n,o,e.behavior);return"string"==typeof t?I(t):t}}}function pe(e){e.pendingDownloadInternal=null,e.pendingDownload=null,e.buffer=null,e.moduleExports=null}function he(e){let t=e.lastIndexOf("/");return t>=0&&t++,e.substring(t)}async function we(e){e&&await Promise.all((null!=e?e:[]).map((e=>async function(e){try{const t=e.name;if(!e.moduleExports){const o=ce(Pe.locateFile(t),"js-module-library-initializer");Pe.diagnosticTracing&&b(`Attempting to import '${o}' for ${e}`),e.moduleExports=await import(/*! webpackIgnore: true */o)}Pe.libraryInitializers.push({scriptName:t,exports:e.moduleExports})}catch(t){E(`Failed to import library initializer '${e}': ${t}`)}}(e))))}async function be(e,t){if(!Pe.libraryInitializers)return;const o=[];for(let n=0;n<Pe.libraryInitializers.length;n++){const r=Pe.libraryInitializers[n];r.exports[e]&&o.push(ye(r.scriptName,e,(()=>r.exports[e](...t))))}await Promise.all(o)}async function ye(e,t,o){try{await o()}catch(o){throw E(`Failed to invoke '${t}' on library initializer '${e}': ${o}`),Xe(1,o),o}}function ve(e,t){if(e===t)return e;const o={...t};return void 0!==o.assets&&o.assets!==e.assets&&(o.assets=[...e.assets||[],...o.assets||[]]),void 0!==o.resources&&(o.resources=_e(e.resources||{assembly:[],jsModuleNative:[],jsModuleRuntime:[],wasmNative:[]},o.resources)),void 0!==o.environmentVariables&&(o.environmentVariables={...e.environmentVariables||{},...o.environmentVariables||{}}),void 0!==o.runtimeOptions&&o.runtimeOptions!==e.runtimeOptions&&(o.runtimeOptions=[...e.runtimeOptions||[],...o.runtimeOptions||[]]),Object.assign(e,o)}function Ee(e,t){if(e===t)return e;const o={...t};return o.config&&(e.config||(e.config={}),o.config=ve(e.config,o.config)),Object.assign(e,o)}function _e(e,t){if(e===t)return e;const o={...t};return void 0!==o.coreAssembly&&(o.coreAssembly=[...e.coreAssembly||[],...o.coreAssembly||[]]),void 0!==o.assembly&&(o.assembly=[...e.assembly||[],...o.assembly||[]]),void 0!==o.lazyAssembly&&(o.lazyAssembly=[...e.lazyAssembly||[],...o.lazyAssembly||[]]),void 0!==o.corePdb&&(o.corePdb=[...e.corePdb||[],...o.corePdb||[]]),void 0!==o.pdb&&(o.pdb=[...e.pdb||[],...o.pdb||[]]),void 0!==o.jsModuleWorker&&(o.jsModuleWorker=[...e.jsModuleWorker||[],...o.jsModuleWorker||[]]),void 0!==o.jsModuleNative&&(o.jsModuleNative=[...e.jsModuleNative||[],...o.jsModuleNative||[]]),void 0!==o.jsModuleDiagnostics&&(o.jsModuleDiagnostics=[...e.jsModuleDiagnostics||[],...o.jsModuleDiagnostics||[]]),void 0!==o.jsModuleRuntime&&(o.jsModuleRuntime=[...e.jsModuleRuntime||[],...o.jsModuleRuntime||[]]),void 0!==o.wasmSymbols&&(o.wasmSymbols=[...e.wasmSymbols||[],...o.wasmSymbols||[]]),void 0!==o.wasmNative&&(o.wasmNative=[...e.wasmNative||[],...o.wasmNative||[]]),void 0!==o.icu&&(o.icu=[...e.icu||[],...o.icu||[]]),void 0!==o.satelliteResources&&(o.satelliteResources=function(e,t){if(e===t)return e;for(const o in t)e[o]=[...e[o]||[],...t[o]||[]];return e}(e.satelliteResources||{},o.satelliteResources||{})),void 0!==o.modulesAfterConfigLoaded&&(o.modulesAfterConfigLoaded=[...e.modulesAfterConfigLoaded||[],...o.modulesAfterConfigLoaded||[]]),void 0!==o.modulesAfterRuntimeReady&&(o.modulesAfterRuntimeReady=[...e.modulesAfterRuntimeReady||[],...o.modulesAfterRuntimeReady||[]]),void 0!==o.extensions&&(o.extensions={...e.extensions||{},...o.extensions||{}}),void 0!==o.vfs&&(o.vfs=[...e.vfs||[],...o.vfs||[]]),Object.assign(e,o)}function xe(){const e=Pe.config;if(e.environmentVariables=e.environmentVariables||{},e.runtimeOptions=e.runtimeOptions||[],e.resources=e.resources||{assembly:[],jsModuleNative:[],jsModuleWorker:[],jsModuleRuntime:[],wasmNative:[],vfs:[],satelliteResources:{}},e.assets){Pe.diagnosticTracing&&b("config.assets is deprecated, use config.resources instead");for(const t of e.assets){const o={};switch(t.behavior){case"assembly":o.assembly=[t];break;case"pdb":o.pdb=[t];break;case"resource":o.satelliteResources={},o.satelliteResources[t.culture]=[t];break;case"icu":o.icu=[t];break;case"symbols":o.wasmSymbols=[t];break;case"vfs":o.vfs=[t];break;case"dotnetwasm":o.wasmNative=[t];break;case"js-module-threads":o.jsModuleWorker=[t];break;case"js-module-runtime":o.jsModuleRuntime=[t];break;case"js-module-native":o.jsModuleNative=[t];break;case"js-module-diagnostics":o.jsModuleDiagnostics=[t];break;case"js-module-dotnet":break;default:throw new Error(`Unexpected behavior ${t.behavior} of asset ${t.name}`)}_e(e.resources,o)}}e.debugLevel,e.applicationEnvironment||(e.applicationEnvironment="Production"),e.applicationCulture&&(e.environmentVariables.LANG=`${e.applicationCulture}.UTF-8`),Ue.diagnosticTracing=Pe.diagnosticTracing=!!e.diagnosticTracing,Ue.waitForDebugger=e.waitForDebugger,Pe.maxParallelDownloads=e.maxParallelDownloads||Pe.maxParallelDownloads,Pe.enableDownloadRetry=void 0!==e.enableDownloadRetry?e.enableDownloadRetry:Pe.enableDownloadRetry}let je=!1;async function Re(e){var t;if(je)return void await Pe.afterConfigLoaded.promise;let o;try{if(e.configSrc||Pe.config&&0!==Object.keys(Pe.config).length&&(Pe.config.assets||Pe.config.resources)||(e.configSrc="dotnet.boot.js"),o=e.configSrc,je=!0,o&&(Pe.diagnosticTracing&&b("mono_wasm_load_config"),await async function(e){const t=e.configSrc,o=Pe.locateFile(t);let n=null;void 0!==Pe.loadBootResource&&(n=Pe.loadBootResource("manifest",t,o,"","manifest"));let r,i=null;if(n)if("string"==typeof n)n.includes(".json")?(i=await s(I(n)),r=await Ae(i)):r=(await import(I(n))).config;else{const e=await n;"function"==typeof e.json?(i=e,r=await Ae(i)):r=e.config}else o.includes(".json")?(i=await s(ce(o,"manifest")),r=await Ae(i)):r=(await import(ce(o,"manifest"))).config;function s(e){return Pe.fetch_like(e,{method:"GET",credentials:"include",cache:"no-cache"})}Pe.config.applicationEnvironment&&(r.applicationEnvironment=Pe.config.applicationEnvironment),ve(Pe.config,r)}(e)),xe(),await we(null===(t=Pe.config.resources)||void 0===t?void 0:t.modulesAfterConfigLoaded),await be("onRuntimeConfigLoaded",[Pe.config]),e.onConfigLoaded)try{await e.onConfigLoaded(Pe.config,Le),xe()}catch(e){throw _("onConfigLoaded() failed",e),e}xe(),Pe.afterConfigLoaded.promise_control.resolve(Pe.config)}catch(t){const n=`Failed to load config file ${o} ${t} ${null==t?void 0:t.stack}`;throw Pe.config=e.config=Object.assign(Pe.config,{message:n,error:t,isError:!0}),Xe(1,new Error(n)),t}}function Te(){return!!globalThis.navigator&&(Pe.isChromium||Pe.isFirefox)}async function Ae(e){const t=Pe.config,o=await e.json();t.applicationEnvironment||o.applicationEnvironment||(o.applicationEnvironment=e.headers.get("Blazor-Environment")||e.headers.get("DotNet-Environment")||void 0),o.environmentVariables||(o.environmentVariables={});const n=e.headers.get("DOTNET-MODIFIABLE-ASSEMBLIES");n&&(o.environmentVariables.DOTNET_MODIFIABLE_ASSEMBLIES=n);const r=e.headers.get("ASPNETCORE-BROWSER-TOOLS");return r&&(o.environmentVariables.__ASPNETCORE_BROWSER_TOOLS=r),o}"function"!=typeof importScripts||globalThis.onmessage||(globalThis.dotnetSidecar=!0);const Se="object"==typeof process&&"object"==typeof process.versions&&"string"==typeof process.versions.node,De="function"==typeof importScripts,Oe=De&&"undefined"!=typeof dotnetSidecar,Ce=De&&!Oe,ke="object"==typeof window||De&&!Se,Ie=!ke&&!Se;let Ue={},Pe={},Me={},Le={},Ne={},$e=!1;const ze={},We={config:ze},Fe={mono:{},binding:{},internal:Ne,module:We,loaderHelpers:Pe,runtimeHelpers:Ue,diagnosticHelpers:Me,api:Le};function Be(e,t){if(e)return;const o="Assert failed: "+("function"==typeof t?t():t),n=new Error(o);_(o,n),Ue.nativeAbort(n)}function Ve(){return void 0!==Pe.exitCode}function qe(){return Ue.runtimeReady&&!Ve()}function He(){Ve()&&Be(!1,`.NET runtime already exited with ${Pe.exitCode} ${Pe.exitReason}. You can use runtime.runMain() which doesn't exit the runtime.`),Ue.runtimeReady||Be(!1,".NET runtime didn't start yet. Please call dotnet.create() first.")}function Je(){ke&&(globalThis.addEventListener("unhandledrejection",et),globalThis.addEventListener("error",tt))}let Ze,Qe;function Ge(e){Qe&&Qe(e),Xe(e,Pe.exitReason)}function Ke(e){Ze&&Ze(e||Pe.exitReason),Xe(1,e||Pe.exitReason)}function Xe(t,o){var n,r;const i=o&&"object"==typeof o;t=i&&"number"==typeof o.status?o.status:void 0===t?-1:t;const s=i&&"string"==typeof o.message?o.message:""+o;(o=i?o:Ue.ExitStatus?function(e,t){const o=new Ue.ExitStatus(e);return o.message=t,o.toString=()=>t,o}(t,s):new Error("Exit with code "+t+" "+s)).status=t,o.message||(o.message=s);const a=""+(o.stack||(new Error).stack);try{Object.defineProperty(o,"stack",{get:()=>a})}catch(e){}const l=!!o.silent;if(o.silent=!0,Ve())Pe.diagnosticTracing&&b("mono_exit called after exit");else{try{We.onAbort==Ke&&(We.onAbort=Ze),We.onExit==Ge&&(We.onExit=Qe),ke&&(globalThis.removeEventListener("unhandledrejection",et),globalThis.removeEventListener("error",tt)),Ue.runtimeReady?(Ue.jiterpreter_dump_stats&&Ue.jiterpreter_dump_stats(!1),0===t&&(null===(n=Pe.config)||void 0===n?void 0:n.interopCleanupOnExit)&&Ue.forceDisposeProxies(!0,!0),e&&0!==t&&(null===(r=Pe.config)||void 0===r||r.dumpThreadsOnNonZeroExit)):(Pe.diagnosticTracing&&b(`abort_startup, reason: ${o}`),function(e){Pe.allDownloadsQueued.promise_control.reject(e),Pe.allDownloadsFinished.promise_control.reject(e),Pe.afterConfigLoaded.promise_control.reject(e),Pe.wasmCompilePromise.promise_control.reject(e),Pe.runtimeModuleLoaded.promise_control.reject(e),Ue.dotnetReady&&(Ue.dotnetReady.promise_control.reject(e),Ue.afterInstantiateWasm.promise_control.reject(e),Ue.beforePreInit.promise_control.reject(e),Ue.afterPreInit.promise_control.reject(e),Ue.afterPreRun.promise_control.reject(e),Ue.beforeOnRuntimeInitialized.promise_control.reject(e),Ue.afterOnRuntimeInitialized.promise_control.reject(e),Ue.afterPostRun.promise_control.reject(e))}(o))}catch(e){E("mono_exit A failed",e)}try{l||(function(e,t){if(0!==e&&t){const e=Ue.ExitStatus&&t instanceof Ue.ExitStatus?b:_;"string"==typeof t?e(t):(void 0===t.stack&&(t.stack=(new Error).stack+""),t.message?e(Ue.stringify_as_error_with_stack?Ue.stringify_as_error_with_stack(t.message+"\n"+t.stack):t.message+"\n"+t.stack):e(JSON.stringify(t)))}!Ce&&Pe.config&&(Pe.config.logExitCode?Pe.config.forwardConsoleLogsToWS?R("WASM EXIT "+e):v("WASM EXIT "+e):Pe.config.forwardConsoleLogsToWS&&R())}(t,o),function(e){if(ke&&!Ce&&Pe.config&&Pe.config.appendElementOnExit&&document){const t=document.createElement("label");t.id="tests_done",0!==e&&(t.style.background="red"),t.innerHTML=""+e,document.body.appendChild(t)}}(t))}catch(e){E("mono_exit B failed",e)}Pe.exitCode=t,Pe.exitReason||(Pe.exitReason=o),!Ce&&Ue.runtimeReady&&We.runtimeKeepalivePop()}if(Pe.config&&Pe.config.asyncFlushOnExit&&0===t)throw(async()=>{try{await async function(){try{const e=await import(/*! webpackIgnore: true */"process"),t=e=>new Promise(((t,o)=>{e.on("error",o),e.end("","utf8",t)})),o=t(e.stderr),n=t(e.stdout);let r;const i=new Promise((e=>{r=setTimeout((()=>e("timeout")),1e3)}));await Promise.race([Promise.all([n,o]),i]),clearTimeout(r)}catch(e){_(`flushing std* streams failed: ${e}`)}}()}finally{Ye(t,o)}})(),o;Ye(t,o)}function Ye(e,t){if(Ue.runtimeReady&&Ue.nativeExit)try{Ue.nativeExit(e)}catch(e){!Ue.ExitStatus||e instanceof Ue.ExitStatus||E("set_exit_code_and_quit_now failed: "+e.toString())}if(0!==e||!ke)throw Se&&Ne.process?Ne.process.exit(e):Ue.quit&&Ue.quit(e,t),t}function et(e){ot(e,e.reason,"rejection")}function tt(e){ot(e,e.error,"error")}function ot(e,t,o){e.preventDefault();try{t||(t=new Error("Unhandled "+o)),void 0===t.stack&&(t.stack=(new Error).stack),t.stack=t.stack+"",t.silent||(_("Unhandled error:",t),Xe(1,t))}catch(e){}}!function(e){if($e)throw new Error("Loader module already loaded");$e=!0,Ue=e.runtimeHelpers,Pe=e.loaderHelpers,Me=e.diagnosticHelpers,Le=e.api,Ne=e.internal,Object.assign(Le,{INTERNAL:Ne,invokeLibraryInitializers:be}),Object.assign(e.module,{config:ve(ze,{environmentVariables:{}})});const r={mono_wasm_bindings_is_ready:!1,config:e.module.config,diagnosticTracing:!1,nativeAbort:e=>{throw e||new Error("abort")},nativeExit:e=>{throw new Error("exit:"+e)}},l={gitHash:"47fb725acf5d7094af51aebbb5b7e5c44a3b2a77",config:e.module.config,diagnosticTracing:!1,maxParallelDownloads:16,enableDownloadRetry:!0,_loaded_files:[],loadedFiles:[],loadedAssemblies:[],libraryInitializers:[],workerNextNumber:1,actual_downloaded_assets_count:0,actual_instantiated_assets_count:0,expected_downloaded_assets_count:0,expected_instantiated_assets_count:0,afterConfigLoaded:i(),allDownloadsQueued:i(),allDownloadsFinished:i(),wasmCompilePromise:i(),runtimeModuleLoaded:i(),loadingWorkers:i(),is_exited:Ve,is_runtime_running:qe,assert_runtime_running:He,mono_exit:Xe,createPromiseController:i,getPromiseController:s,assertIsControllablePromise:a,mono_download_assets:oe,resolve_single_asset_path:ee,setup_proxy_console:j,set_thread_prefix:w,installUnhandledErrorHandler:Je,retrieve_asset_download:ie,invokeLibraryInitializers:be,isDebuggingSupported:Te,exceptions:t,simd:n,relaxedSimd:o};Object.assign(Ue,r),Object.assign(Pe,l)}(Fe);let nt,rt,it,st=!1,at=!1;async function lt(e){if(!at){if(at=!0,ke&&Pe.config.forwardConsoleLogsToWS&&void 0!==globalThis.WebSocket&&j("main",globalThis.console,globalThis.location.origin),We||Be(!1,"Null moduleConfig"),Pe.config||Be(!1,"Null moduleConfig.config"),"function"==typeof e){const t=e(Fe.api);if(t.ready)throw new Error("Module.ready couldn't be redefined.");Object.assign(We,t),Ee(We,t)}else{if("object"!=typeof e)throw new Error("Can't use moduleFactory callback of createDotnetRuntime function.");Ee(We,e)}await async function(e){if(Se){const e=await import(/*! webpackIgnore: true */"process"),t=14;if(e.versions.node.split(".")[0]<t)throw new Error(`NodeJS at '${e.execPath}' has too low version '${e.versions.node}', please use at least ${t}. See also https://aka.ms/dotnet-wasm-features`)}const t=/*! webpackIgnore: true */import.meta.url,o=t.indexOf("?");var n;if(o>0&&(Pe.modulesUniqueQuery=t.substring(o)),Pe.scriptUrl=t.replace(/\\/g,"/").replace(/[?#].*/,""),Pe.scriptDirectory=(n=Pe.scriptUrl).slice(0,n.lastIndexOf("/"))+"/",Pe.locateFile=e=>"URL"in globalThis&&globalThis.URL!==C?new URL(e,Pe.scriptDirectory).toString():M(e)?e:Pe.scriptDirectory+e,Pe.fetch_like=k,Pe.out=console.log,Pe.err=console.error,Pe.onDownloadResourceProgress=e.onDownloadResourceProgress,ke&&globalThis.navigator){const e=globalThis.navigator,t=e.userAgentData&&e.userAgentData.brands;t&&t.length>0?Pe.isChromium=t.some((e=>"Google Chrome"===e.brand||"Microsoft Edge"===e.brand||"Chromium"===e.brand)):e.userAgent&&(Pe.isChromium=e.userAgent.includes("Chrome"),Pe.isFirefox=e.userAgent.includes("Firefox"))}Ne.require=Se?await import(/*! webpackIgnore: true */"module").then((e=>e.createRequire(/*! webpackIgnore: true */import.meta.url))):Promise.resolve((()=>{throw new Error("require not supported")})),void 0===globalThis.URL&&(globalThis.URL=C)}(We)}}async function ct(e){return await lt(e),Ze=We.onAbort,Qe=We.onExit,We.onAbort=Ke,We.onExit=Ge,We.ENVIRONMENT_IS_PTHREAD?async function(){(function(){const e=new MessageChannel,t=e.port1,o=e.port2;t.addEventListener("message",(e=>{var n,r;n=JSON.parse(e.data.config),r=JSON.parse(e.data.monoThreadInfo),st?Pe.diagnosticTracing&&b("mono config already received"):(ve(Pe.config,n),Ue.monoThreadInfo=r,xe(),Pe.diagnosticTracing&&b("mono config received"),st=!0,Pe.afterConfigLoaded.promise_control.resolve(Pe.config),ke&&n.forwardConsoleLogsToWS&&void 0!==globalThis.WebSocket&&Pe.setup_proxy_console("worker-idle",console,globalThis.location.origin)),t.close(),o.close()}),{once:!0}),t.start(),self.postMessage({[l]:{monoCmd:"preload",port:o}},[o])})(),await Pe.afterConfigLoaded.promise,function(){const e=Pe.config;e.assets||Be(!1,"config.assets must be defined");for(const t of e.assets)X(t),Q[t.behavior]&&z.push(t)}(),setTimeout((async()=>{try{await oe()}catch(e){Xe(1,e)}}),0);const e=dt(),t=await Promise.all(e);return await ut(t),We}():async function(){var e;await Re(We),re();const t=dt();(async function(){try{const e=ee("dotnetwasm");await se(e),e&&e.pendingDownloadInternal&&e.pendingDownloadInternal.response||Be(!1,"Can't load dotnet.native.wasm");const t=await e.pendingDownloadInternal.response,o=t.headers&&t.headers.get?t.headers.get("Content-Type"):void 0;let n;if("function"==typeof WebAssembly.compileStreaming&&"application/wasm"===o)n=await WebAssembly.compileStreaming(t);else{ke&&"application/wasm"!==o&&E('WebAssembly resource does not have the expected content type "application/wasm", so falling back to slower ArrayBuffer instantiation.');const e=await t.arrayBuffer();Pe.diagnosticTracing&&b("instantiate_wasm_module buffered"),n=Ie?await Promise.resolve(new WebAssembly.Module(e)):await WebAssembly.compile(e)}e.pendingDownloadInternal=null,e.pendingDownload=null,e.buffer=null,e.moduleExports=null,Pe.wasmCompilePromise.promise_control.resolve(n)}catch(e){Pe.wasmCompilePromise.promise_control.reject(e)}})(),setTimeout((async()=>{try{D(),await oe()}catch(e){Xe(1,e)}}),0);const o=await Promise.all(t);return await ut(o),await Ue.dotnetReady.promise,await we(null===(e=Pe.config.resources)||void 0===e?void 0:e.modulesAfterRuntimeReady),await be("onRuntimeReady",[Fe.api]),Le}()}function dt(){const e=ee("js-module-runtime"),t=ee("js-module-native");if(nt&&rt)return[nt,rt,it];"object"==typeof e.moduleExports?nt=e.moduleExports:(Pe.diagnosticTracing&&b(`Attempting to import '${e.resolvedUrl}' for ${e.name}`),nt=import(/*! webpackIgnore: true */e.resolvedUrl)),"object"==typeof t.moduleExports?rt=t.moduleExports:(Pe.diagnosticTracing&&b(`Attempting to import '${t.resolvedUrl}' for ${t.name}`),rt=import(/*! webpackIgnore: true */t.resolvedUrl));const o=Y("js-module-diagnostics");return o&&("object"==typeof o.moduleExports?it=o.moduleExports:(Pe.diagnosticTracing&&b(`Attempting to import '${o.resolvedUrl}' for ${o.name}`),it=import(/*! webpackIgnore: true */o.resolvedUrl))),[nt,rt,it]}async function ut(e){const{initializeExports:t,initializeReplacements:o,configureRuntimeStartup:n,configureEmscriptenStartup:r,configureWorkerStartup:i,setRuntimeGlobals:s,passEmscriptenInternals:a}=e[0],{default:l}=e[1],c=e[2];s(Fe),t(Fe),c&&c.setRuntimeGlobals(Fe),await n(We),Pe.runtimeModuleLoaded.promise_control.resolve(),l((e=>(Object.assign(We,{ready:e.ready,__dotnet_runtime:{initializeReplacements:o,configureEmscriptenStartup:r,configureWorkerStartup:i,passEmscriptenInternals:a}}),We))).catch((e=>{if(e.message&&e.message.toLowerCase().includes("out of memory"))throw new Error(".NET runtime has failed to start, because too much memory was requested. Please decrease the memory by adjusting EmccMaximumHeapSize. See also https://aka.ms/dotnet-wasm-features");throw e}))}const ft=new class{withModuleConfig(e){try{return Ee(We,e),this}catch(e){throw Xe(1,e),e}}withOnConfigLoaded(e){try{return Ee(We,{onConfigLoaded:e}),this}catch(e){throw Xe(1,e),e}}withConsoleForwarding(){try{return ve(ze,{forwardConsoleLogsToWS:!0}),this}catch(e){throw Xe(1,e),e}}withExitOnUnhandledError(){try{return ve(ze,{exitOnUnhandledError:!0}),Je(),this}catch(e){throw Xe(1,e),e}}withAsyncFlushOnExit(){try{return ve(ze,{asyncFlushOnExit:!0}),this}catch(e){throw Xe(1,e),e}}withExitCodeLogging(){try{return ve(ze,{logExitCode:!0}),this}catch(e){throw Xe(1,e),e}}withElementOnExit(){try{return ve(ze,{appendElementOnExit:!0}),this}catch(e){throw Xe(1,e),e}}withInteropCleanupOnExit(){try{return ve(ze,{interopCleanupOnExit:!0}),this}catch(e){throw Xe(1,e),e}}withDumpThreadsOnNonZeroExit(){try{return ve(ze,{dumpThreadsOnNonZeroExit:!0}),this}catch(e){throw Xe(1,e),e}}withWaitingForDebugger(e){try{return ve(ze,{waitForDebugger:e}),this}catch(e){throw Xe(1,e),e}}withInterpreterPgo(e,t){try{return ve(ze,{interpreterPgo:e,interpreterPgoSaveDelay:t}),ze.runtimeOptions?ze.runtimeOptions.push("--interp-pgo-recording"):ze.runtimeOptions=["--interp-pgo-recording"],this}catch(e){throw Xe(1,e),e}}withConfig(e){try{return ve(ze,e),this}catch(e){throw Xe(1,e),e}}withConfigSrc(e){try{return e&&"string"==typeof e||Be(!1,"must be file path or URL"),Ee(We,{configSrc:e}),this}catch(e){throw Xe(1,e),e}}withVirtualWorkingDirectory(e){try{return e&&"string"==typeof e||Be(!1,"must be directory path"),ve(ze,{virtualWorkingDirectory:e}),this}catch(e){throw Xe(1,e),e}}withEnvironmentVariable(e,t){try{const o={};return o[e]=t,ve(ze,{environmentVariables:o}),this}catch(e){throw Xe(1,e),e}}withEnvironmentVariables(e){try{return e&&"object"==typeof e||Be(!1,"must be dictionary object"),ve(ze,{environmentVariables:e}),this}catch(e){throw Xe(1,e),e}}withDiagnosticTracing(e){try{return"boolean"!=typeof e&&Be(!1,"must be boolean"),ve(ze,{diagnosticTracing:e}),this}catch(e){throw Xe(1,e),e}}withDebugging(e){try{return null!=e&&"number"==typeof e||Be(!1,"must be number"),ve(ze,{debugLevel:e}),this}catch(e){throw Xe(1,e),e}}withApplicationArguments(...e){try{return e&&Array.isArray(e)||Be(!1,"must be array of strings"),ve(ze,{applicationArguments:e}),this}catch(e){throw Xe(1,e),e}}withRuntimeOptions(e){try{return e&&Array.isArray(e)||Be(!1,"must be array of strings"),ze.runtimeOptions?ze.runtimeOptions.push(...e):ze.runtimeOptions=e,this}catch(e){throw Xe(1,e),e}}withMainAssembly(e){try{return ve(ze,{mainAssemblyName:e}),this}catch(e){throw Xe(1,e),e}}withApplicationArgumentsFromQuery(){try{if(!globalThis.window)throw new Error("Missing window to the query parameters from");if(void 0===globalThis.URLSearchParams)throw new Error("URLSearchParams is supported");const e=new URLSearchParams(globalThis.window.location.search).getAll("arg");return this.withApplicationArguments(...e)}catch(e){throw Xe(1,e),e}}withApplicationEnvironment(e){try{return ve(ze,{applicationEnvironment:e}),this}catch(e){throw Xe(1,e),e}}withApplicationCulture(e){try{return ve(ze,{applicationCulture:e}),this}catch(e){throw Xe(1,e),e}}withResourceLoader(e){try{return Pe.loadBootResource=e,this}catch(e){throw Xe(1,e),e}}async download(){try{await async function(){lt(We),await Re(We),re(),D(),oe(),await Pe.allDownloadsFinished.promise}()}catch(e){throw Xe(1,e),e}}async create(){try{return this.instance||(this.instance=await async function(){return await ct(We),Fe.api}()),this.instance}catch(e){throw Xe(1,e),e}}async run(){try{return We.config||Be(!1,"Null moduleConfig.config"),this.instance||await this.create(),this.instance.runMainAndExit()}catch(e){throw Xe(1,e),e}}},mt=Xe,gt=ct;Ie||"function"==typeof globalThis.URL||Be(!1,"This browser/engine doesn't support URL API. Please use a modern version. See also https://aka.ms/dotnet-wasm-features"),"function"!=typeof globalThis.BigInt64Array&&Be(!1,"This browser/engine doesn't support BigInt64Array API. Please use a modern version. See also https://aka.ms/dotnet-wasm-features"),ft.withConfig(/*json-start*/{
  "mainAssemblyName": "SpawnDev.ILGPU.Demo",
  "resources": {
    "hash": "sha256-Zd++ZJJYCgWGHFr5/kB6oIVsuJGNIrvWeWZnPGSejHc=",
    "jsModuleNative": [
      {
        "name": "dotnet.native.0hjibq2mk8.js"
      }
    ],
    "jsModuleRuntime": [
      {
        "name": "dotnet.runtime.f4b1oiwlzh.js"
      }
    ],
    "wasmNative": [
      {
        "name": "dotnet.native.njktda3v94.wasm",
        "integrity": "sha256-vW1DR6gpTv692HU0J15KxoiXjCBYsK0GMHDtFyibyYw=",
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
        "name": "System.Runtime.InteropServices.JavaScript.btc6lsm44s.wasm",
        "integrity": "sha256-ofRu5+p/zT+jZWbsX12wpjr4CJGdWlUMHhs+Dr4cecs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.CoreLib.wasm",
        "name": "System.Private.CoreLib.j3li44xrdq.wasm",
        "integrity": "sha256-Tx915/D+yChp2Q4xJWEsw6dCQUmiwEsa8oCgUlbvDGw=",
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
        "virtualPath": "SIPSorcery.wasm",
        "name": "SIPSorcery.m5kq9o4ffh.wasm",
        "integrity": "sha256-dg7SBJrEy1Gvco49pz/U812kc5nuyLX26niK2yK/qTQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "websocket-sharp.wasm",
        "name": "websocket-sharp.hxolgqcoj5.wasm",
        "integrity": "sha256-ixbQzKVBepGWvsSiiv1XPMGu9/6Qiz03/lcIwo3YJt4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SIPSorceryMedia.Abstractions.wasm",
        "name": "SIPSorceryMedia.Abstractions.kzwxkc9wsr.wasm",
        "integrity": "sha256-QPkv2qT21i0GTw4Lpaw9TeW/FXf4O0W5QRev5KGKfAk=",
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
        "name": "SpawnDev.BlazorJS.bwl84aywgy.wasm",
        "integrity": "sha256-JdOYDdBCRJ3y8c3TYmMZqVtuhU0/opAQAyCtuapAxsU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SpawnDev.BlazorJS.Cryptography.wasm",
        "name": "SpawnDev.BlazorJS.Cryptography.lwompt3uqu.wasm",
        "integrity": "sha256-lSSAwC8HgKI5Bjn1kQZWdfSfzRoOYNIsDJCW6g3coJk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SpawnDev.UnitTesting.wasm",
        "name": "SpawnDev.UnitTesting.blqi46nuqa.wasm",
        "integrity": "sha256-4VxUDQWh7TXUn1Fx/jMOa6VUR5n20F9BTxKGXRqGzcI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SpawnDev.UnitTesting.Blazor.wasm",
        "name": "SpawnDev.UnitTesting.Blazor.c6ukihzobm.wasm",
        "integrity": "sha256-PMQWPd2JxiCd2fSN9g50rl+BWhz9GXAl2TLWGEKN+wI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SpawnDev.WebTorrent.wasm",
        "name": "SpawnDev.WebTorrent.ixoaarfc8i.wasm",
        "integrity": "sha256-tiLUCh9yOI1vUi/nbQQv5I3LPcs3za8CtMJJPaHsL0E=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.CSharp.wasm",
        "name": "Microsoft.CSharp.l9wgqk3fqt.wasm",
        "integrity": "sha256-WSE8McvfRahgGo/2y4i0/MYIw+vMHiXvPGHNnzVT0qg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.VisualBasic.Core.wasm",
        "name": "Microsoft.VisualBasic.Core.kmnk2mhqm4.wasm",
        "integrity": "sha256-ZGGb/rzMLq6hJp2ldr7CS1RnNCujtg9/xX9h0/CqQjQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.VisualBasic.wasm",
        "name": "Microsoft.VisualBasic.2fddz6tcet.wasm",
        "integrity": "sha256-0s9ThAOT1sf6YwvIbpQ8/LlTVCALStBThImePx53Fvw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Win32.Primitives.wasm",
        "name": "Microsoft.Win32.Primitives.f8hmzzgjtw.wasm",
        "integrity": "sha256-QdCNNtcODIiMbqPdJ9xc3ciMotzBSyj7RcYCBZNsZuY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Win32.Registry.wasm",
        "name": "Microsoft.Win32.Registry.q7ygiwmrdj.wasm",
        "integrity": "sha256-NgOD8HOqiqkI2xr2kLBKFKDVBi32RyoWwWpVywyNsjM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.AppContext.wasm",
        "name": "System.AppContext.n6abmzyy2p.wasm",
        "integrity": "sha256-sRW1CuOSMPHZ9tQYY0qRcO6LkDiImBs+lIkUNU/YcKM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Buffers.wasm",
        "name": "System.Buffers.1xv6sma00j.wasm",
        "integrity": "sha256-68TgaYN4+/2HUzOBtejZacY6Qu0SdL9ftmyuv2n2p9U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.Concurrent.wasm",
        "name": "System.Collections.Concurrent.xy8790zfaf.wasm",
        "integrity": "sha256-KcKWH4U+qpALEkxqe9WnGjuMe13bIB0F3mUX7yt8plU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.Immutable.wasm",
        "name": "System.Collections.Immutable.daz1k7v6ky.wasm",
        "integrity": "sha256-GxcPGZFQBjLNXXIsnfcmOpZXZhOgrct7kb19ymvnzB4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.NonGeneric.wasm",
        "name": "System.Collections.NonGeneric.fq8dj2rr70.wasm",
        "integrity": "sha256-iecb5FiUCZy6jgFpA8Uu+E+9WYmkK49zhy/VuURismo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.Specialized.wasm",
        "name": "System.Collections.Specialized.h7eo99fk0o.wasm",
        "integrity": "sha256-E0WZVCamwM/9iqzeVvKVo/zix07m2X9OIM0FiSlCQ7E=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.wasm",
        "name": "System.Collections.pi9c3mcp25.wasm",
        "integrity": "sha256-z0lBFHr1kD/AsiriCBEjGwhR9yi8jjxKN9eynd3ZEuQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.Annotations.wasm",
        "name": "System.ComponentModel.Annotations.7pb70gk2h1.wasm",
        "integrity": "sha256-+zYOdOswqtFcnvIhhiQnnSWXnUsSbw1zyqUuVtF/dC0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.DataAnnotations.wasm",
        "name": "System.ComponentModel.DataAnnotations.dgh4jc194o.wasm",
        "integrity": "sha256-o9tC+AEq0OjhF5lB7LxD+niaeC4T2AWUo3439V7XWmM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.EventBasedAsync.wasm",
        "name": "System.ComponentModel.EventBasedAsync.isn1o0d4oa.wasm",
        "integrity": "sha256-shZ1A65ZavIj3PkH+LRSVD2rx/nRCp2S1Qa69SQ9nTc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.Primitives.wasm",
        "name": "System.ComponentModel.Primitives.h2f96dwkqx.wasm",
        "integrity": "sha256-ic4XAIie4iR2OXia2R5shvtzFqqsLkXIRk32En0r9K8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.TypeConverter.wasm",
        "name": "System.ComponentModel.TypeConverter.t3vlx2mrmh.wasm",
        "integrity": "sha256-R0bHRnLjhkLdrYpr0kDCYV7YDYVrDbr2RZEEcANJFqU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.wasm",
        "name": "System.ComponentModel.h86hwi1eqn.wasm",
        "integrity": "sha256-L45j82nOLuXllsXm1SifhXf/FhgJL/tJl+FS1/NQBDE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Configuration.wasm",
        "name": "System.Configuration.wcm08lc7ka.wasm",
        "integrity": "sha256-0JJOokykbfjy8ssC7z4PM0J0mGPu4V5wnoIlmsbnEgY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Console.wasm",
        "name": "System.Console.b7qkzhtod4.wasm",
        "integrity": "sha256-WYcTHr7e1+LN4G0ohfNnrPVooEjW0e2YzeAEpZiDV8k=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Core.wasm",
        "name": "System.Core.ioyv3k3ryq.wasm",
        "integrity": "sha256-UmTg0W4SzTpuX+Hq/kw/Vum7yejM3i+1Maxy17s5d/Q=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Data.Common.wasm",
        "name": "System.Data.Common.iw6yp4yiws.wasm",
        "integrity": "sha256-jr8rthvvWS/c90fkB5HIXggDPew39Vykdmg1bj3+Vhg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Data.DataSetExtensions.wasm",
        "name": "System.Data.DataSetExtensions.gt33nptjvi.wasm",
        "integrity": "sha256-7L5bOSbT4eLKXYIcp3/RgDOmGwv/gxAAWViFfWuwBsI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Data.wasm",
        "name": "System.Data.jgebmgf2md.wasm",
        "integrity": "sha256-DVyBOzvehuWh88+ScCoirNSCtho74Xa8mUwU5sp06G4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Contracts.wasm",
        "name": "System.Diagnostics.Contracts.3gxszpux1f.wasm",
        "integrity": "sha256-U7+mA8jLtSgfDwYLY6oX+K/ECrofXjrty1z9CHBEMDA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Debug.wasm",
        "name": "System.Diagnostics.Debug.tifuoz2gak.wasm",
        "integrity": "sha256-67FCj1WXNGq+XiXo6Wd/eoCKoQ5OcGarMjgjHk4vn/8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.DiagnosticSource.wasm",
        "name": "System.Diagnostics.DiagnosticSource.12jh3kg4pr.wasm",
        "integrity": "sha256-uS0dO0wDMEhIDZy08XGvnmzBGwPsf7mTB+BPz+oAhKg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.FileVersionInfo.wasm",
        "name": "System.Diagnostics.FileVersionInfo.zm0met1jpa.wasm",
        "integrity": "sha256-u96JCOj4W85vw4N9Gs4COKKqnbcPXEpDeEmm+/f2en0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Process.wasm",
        "name": "System.Diagnostics.Process.qo0gqezltx.wasm",
        "integrity": "sha256-ein2qA1jZ5AFM7qnHVMCErJub3jdfHDlkB1JSGwHYSU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.StackTrace.wasm",
        "name": "System.Diagnostics.StackTrace.kn7d1osvxx.wasm",
        "integrity": "sha256-ABdJ+B2b9Ja4JzLlFQF3Hq+HjYLLDE1D4S7769YDXg4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.TextWriterTraceListener.wasm",
        "name": "System.Diagnostics.TextWriterTraceListener.zxaxfgqbjo.wasm",
        "integrity": "sha256-p8RpAzQCHqc2yy5SN654H/npJf1TlGi3ufYjZ2j1+lA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Tools.wasm",
        "name": "System.Diagnostics.Tools.p9awaoj88k.wasm",
        "integrity": "sha256-AyqBfX5fg4maMWFxH9bfwnlqkQsSWZQ4rkBvxEn5mh8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.TraceSource.wasm",
        "name": "System.Diagnostics.TraceSource.3l0095gwcr.wasm",
        "integrity": "sha256-9988we6qLAtoljTblx9kYukX4fmn1w+LNI1wmFkIyhw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Tracing.wasm",
        "name": "System.Diagnostics.Tracing.7ndf74kqat.wasm",
        "integrity": "sha256-06lGcvs6a1sfx9MYi46DfqbGPqM7QlKxvVfaPSLpNnM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Drawing.Primitives.wasm",
        "name": "System.Drawing.Primitives.lze6yv3ah8.wasm",
        "integrity": "sha256-YX+io/CUK9d6PVj9yL4X/RBee1ZzUseogB0qBY4g5lg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Drawing.wasm",
        "name": "System.Drawing.yveu20rr0o.wasm",
        "integrity": "sha256-IqfPb1SbK5z8r79w7VhbK1GaoLahck4prNGZEfh87zE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Dynamic.Runtime.wasm",
        "name": "System.Dynamic.Runtime.7zug7p4aiv.wasm",
        "integrity": "sha256-LdQNONteum3ywUlGptztUBQz18eY69lWb4oqK2Ux4J8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Formats.Asn1.wasm",
        "name": "System.Formats.Asn1.0fhvv0d4ht.wasm",
        "integrity": "sha256-FEcteIDXUNr3E1OhjruXWCnhzVYjoVKLbrgILYFubTI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Formats.Tar.wasm",
        "name": "System.Formats.Tar.k3wn8ar228.wasm",
        "integrity": "sha256-QKJ9AHmytXEDHhQ6Cdjioe5NhfI2VD9424nPU3NCkWA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Globalization.Calendars.wasm",
        "name": "System.Globalization.Calendars.twn7nvvzj0.wasm",
        "integrity": "sha256-zXcVHVQZ40xZOMeoc0AAVNcx1Lei2spyIozijxDc5AA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Globalization.Extensions.wasm",
        "name": "System.Globalization.Extensions.aqz8v30zrh.wasm",
        "integrity": "sha256-4pFCt9iLFvsckAZrcXD5QxwL5+jzT3SkXVqmTQNQwO8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Globalization.wasm",
        "name": "System.Globalization.q4vcd8ksy1.wasm",
        "integrity": "sha256-mrWtV+aVjroLHoyyZgfYThSzzIcRSTB6RFjkRrWO+UU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Compression.Brotli.wasm",
        "name": "System.IO.Compression.Brotli.mdh3gtv71n.wasm",
        "integrity": "sha256-us5kYveD1P5qCjyTMwZn85K6iVBtvurwMP/X/1q1XW4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Compression.FileSystem.wasm",
        "name": "System.IO.Compression.FileSystem.t7448dnoym.wasm",
        "integrity": "sha256-Wbq0lOWnop8OOaexbxPT73b0tpUf5aSrMhbAd0Hzv/o=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Compression.ZipFile.wasm",
        "name": "System.IO.Compression.ZipFile.ifztmx601s.wasm",
        "integrity": "sha256-nrLz2Fcrg+ko3ANVCSUAA5d7rkaxWxnIc7bsHFhKXUk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Compression.wasm",
        "name": "System.IO.Compression.9zgg9t392y.wasm",
        "integrity": "sha256-9Wbu0xz2XRR6dCRky4FXDXQRKn8oa5hFe39DYm+wRTc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.FileSystem.AccessControl.wasm",
        "name": "System.IO.FileSystem.AccessControl.zgaha8ei31.wasm",
        "integrity": "sha256-veZz2sCmQRr3TMhxvn8pYUUKmBdFEtZ0AMHh90tCFtE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.FileSystem.DriveInfo.wasm",
        "name": "System.IO.FileSystem.DriveInfo.fio2m93mod.wasm",
        "integrity": "sha256-ac3Xs4CMVZbeMMJaiV36PG3m/ud326leVdNvh1sXltI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.FileSystem.Primitives.wasm",
        "name": "System.IO.FileSystem.Primitives.i1mry81p89.wasm",
        "integrity": "sha256-VnqveQcXUfUl8RAIhxZgnF6wwiPIGN3hzj3Xqsycwxg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.FileSystem.Watcher.wasm",
        "name": "System.IO.FileSystem.Watcher.7oua4vo2oa.wasm",
        "integrity": "sha256-xxmO/FEzxaV0en5a4lR6NJo2SHcZMwoh5q9Vn3tOH5k=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.FileSystem.wasm",
        "name": "System.IO.FileSystem.so6hryehmr.wasm",
        "integrity": "sha256-HB9RAw992yU1ik10TtvCJKKjRv0BMcrYI2RhN4hPlxY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.IsolatedStorage.wasm",
        "name": "System.IO.IsolatedStorage.nduhew0dla.wasm",
        "integrity": "sha256-C0LuLRprOL1xLdT5jExyM1o1NYbKfR/tTPswDTJwW7M=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.MemoryMappedFiles.wasm",
        "name": "System.IO.MemoryMappedFiles.vp2s4ogjr6.wasm",
        "integrity": "sha256-w6BMUNjxd5pKNIzqUgVjJP+JD4ARTdFcGp19CWWuXfE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Pipelines.wasm",
        "name": "System.IO.Pipelines.61uvv0lda1.wasm",
        "integrity": "sha256-NkdLtMdRhWCEw3EPO05CkBgDAdrukMDWd2E3Q9YPkx4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Pipes.AccessControl.wasm",
        "name": "System.IO.Pipes.AccessControl.8cuinz03ch.wasm",
        "integrity": "sha256-GA1qnOwUFXYif8rqTZKH3dgh4qOFX+a9II/LVrWCVRQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Pipes.wasm",
        "name": "System.IO.Pipes.hctonldsoe.wasm",
        "integrity": "sha256-b5oemEgbQj2+bhfPU6BGeu0UJ/i6gbKorO8uy4zqUWE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.UnmanagedMemoryStream.wasm",
        "name": "System.IO.UnmanagedMemoryStream.58j0bcxbvp.wasm",
        "integrity": "sha256-Vcxqa2Q1a/MLKlbhLkE0+4lb0uGewS/xP5qtUms72+g=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.wasm",
        "name": "System.IO.y4c3d0ze0j.wasm",
        "integrity": "sha256-Mj2S/WZidwZsx+JXyfPeMN1TWWnO2i48YpwfKlOpX00=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.AsyncEnumerable.wasm",
        "name": "System.Linq.AsyncEnumerable.21ckadzeke.wasm",
        "integrity": "sha256-Gji0Y0m8sx68nun0BmqtIOak7Efwwef5jIjk65vA1dE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.Expressions.wasm",
        "name": "System.Linq.Expressions.9d6hn7sk0j.wasm",
        "integrity": "sha256-gxJ92wyiuTi8VKjZ7DjORw7CuocoLlTMFvJvl5ap8Kg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.Parallel.wasm",
        "name": "System.Linq.Parallel.yzmmt3f7a3.wasm",
        "integrity": "sha256-Egl9FI9ttw2OXQ0N4mPqj4Ms1W93cgZoU6V2POOIXQo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.Queryable.wasm",
        "name": "System.Linq.Queryable.x6q4nrlo2r.wasm",
        "integrity": "sha256-mXAA38V+jLwH4awDxHEygGxtaq3J96P/MimiiICQwwk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.wasm",
        "name": "System.Linq.j3smz65i7k.wasm",
        "integrity": "sha256-QR38RJ/PctD+gYiwKqrMr9dUT4gBQzCS6AGmxvFyioE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Memory.wasm",
        "name": "System.Memory.983oy63p2u.wasm",
        "integrity": "sha256-py8sNq6BbiSkyW2KNXosWUJ2ZcBr2Kk9mWvSjaUknR0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Http.Json.wasm",
        "name": "System.Net.Http.Json.3u1l2hlpkp.wasm",
        "integrity": "sha256-i7IPwJNm4jg6NiENqWmUzHZJbSepRSmZBNubo0H9P3g=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Http.wasm",
        "name": "System.Net.Http.qlqjvnsqyk.wasm",
        "integrity": "sha256-boj3adQj1uNv0euZCIiczOdgxX76mdF8vbiNuvvk0mU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.HttpListener.wasm",
        "name": "System.Net.HttpListener.10mxpjm8qw.wasm",
        "integrity": "sha256-X801OSd3Im/H3N++w0DFEbSMXqM7tTVqHBCqTTlnToI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Mail.wasm",
        "name": "System.Net.Mail.otsawsf7ff.wasm",
        "integrity": "sha256-bHA2CTLSAYthHl6VHj6NggEdJ8766MwdFR+We1A429Q=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.NameResolution.wasm",
        "name": "System.Net.NameResolution.3wkoxykbtn.wasm",
        "integrity": "sha256-wwhMMXCrTrdAljYH6A6QDnycfhMbk0XHv3oB68ZX7S4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.NetworkInformation.wasm",
        "name": "System.Net.NetworkInformation.uqly0fkd39.wasm",
        "integrity": "sha256-lr41Frdsg+sR9jiF2xbBjRbJc1QIxS+nNqUN6xj3RZM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Ping.wasm",
        "name": "System.Net.Ping.b7i8pktbn2.wasm",
        "integrity": "sha256-2djzYJlthNqsbcZdIBpBgNskTooAwFpgvar3stTJB+I=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Primitives.wasm",
        "name": "System.Net.Primitives.3xbl6w8fc4.wasm",
        "integrity": "sha256-aUoYBAcRpIujVcyIRKmSRZ7yy55hcBgPp6Jn3+CbbVE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Quic.wasm",
        "name": "System.Net.Quic.rkpf6rdawt.wasm",
        "integrity": "sha256-xW41h8qwmO2qbadUAb1D5++V+dDYA5nAa5QITCLZtzQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Requests.wasm",
        "name": "System.Net.Requests.w0qjsd9ax2.wasm",
        "integrity": "sha256-gPVKsduolBr7Ucg7TP49Jf9tv8C2hysTNVRacJOv26g=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Security.wasm",
        "name": "System.Net.Security.dsowaqbexd.wasm",
        "integrity": "sha256-fWc+RYzPvggkzT6oECuOx/D5U+FbRugCyzPwHHCGBN4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.ServerSentEvents.wasm",
        "name": "System.Net.ServerSentEvents.i41oq4vo1f.wasm",
        "integrity": "sha256-svUrQs0iOtIsyL1DzXrwijzNMTZNjSY8IVwFf7xUydE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.ServicePoint.wasm",
        "name": "System.Net.ServicePoint.8dv5m1dg60.wasm",
        "integrity": "sha256-DHhArsOgDa5HDAz5uktzHVt6vV7kqSsad2HbvBhzmqY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Sockets.wasm",
        "name": "System.Net.Sockets.t68gtqh7bl.wasm",
        "integrity": "sha256-i73F/FtdtAjUNrbPpesjNiZTqhYYBUojf+2fBsy7c+E=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebClient.wasm",
        "name": "System.Net.WebClient.prk7d2ug5q.wasm",
        "integrity": "sha256-5bms5g7SBp96rH91ugaXOGdApxmHKGvuP8h9URNph3A=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebHeaderCollection.wasm",
        "name": "System.Net.WebHeaderCollection.44kuj25p9b.wasm",
        "integrity": "sha256-rHMJPduu9pbY/32ljasAzKoO9H/dBKB/VbL/1IZaCxU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebProxy.wasm",
        "name": "System.Net.WebProxy.exbmlmbu5q.wasm",
        "integrity": "sha256-Hp19scpsRuHqN6BrMleaQYS16nD2unEoB2+FCoS1Yfw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebSockets.Client.wasm",
        "name": "System.Net.WebSockets.Client.ngaltobszp.wasm",
        "integrity": "sha256-N3WhP7DzK/ZxJ4yj3gWTp5FDy+Wx0RQhxruGKNaysBc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebSockets.wasm",
        "name": "System.Net.WebSockets.i3pq128fj5.wasm",
        "integrity": "sha256-jipXWKfL+vNHZ+VlvTjLAbNgBZchI8RWVz8PZoY84io=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.wasm",
        "name": "System.Net.rnj7nvsh2d.wasm",
        "integrity": "sha256-uU31N6903kC21JI7H9fpFFdh6FSTP69WbfTQqi3qLd4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Numerics.Vectors.wasm",
        "name": "System.Numerics.Vectors.rx0s5eafzc.wasm",
        "integrity": "sha256-QY+XCKTo8ND18VUlcG1/jksM5KtTOLR6pzjhuZslMns=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Numerics.wasm",
        "name": "System.Numerics.rvrqvvgil4.wasm",
        "integrity": "sha256-p+rNkt5uVLpKwPmgeKUn0arpquLeC42c9seQtKRBQNk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ObjectModel.wasm",
        "name": "System.ObjectModel.3157zgypge.wasm",
        "integrity": "sha256-yUdDfXVXa6CP26bf65t+5kkrrHZeLm0K1u3Jus7ueGU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.DataContractSerialization.wasm",
        "name": "System.Private.DataContractSerialization.ez50zcwqu7.wasm",
        "integrity": "sha256-tq5gicV1/KeKSniy76TipyeTuSGzdFmXMcTK+m1daz8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.Uri.wasm",
        "name": "System.Private.Uri.3vdd2q52ug.wasm",
        "integrity": "sha256-kb64xWPvvC/RjfLfNV/5TGfdZ2EFBOzfzeuNSnYl4+I=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.Xml.Linq.wasm",
        "name": "System.Private.Xml.Linq.qx293bw0nv.wasm",
        "integrity": "sha256-4pkiCDxnVJzk+7RHC+DddAm8usipxcNqgjhuzAyu530=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.Xml.wasm",
        "name": "System.Private.Xml.5z70o5zva4.wasm",
        "integrity": "sha256-n5uQsE51/PbfhE+UE203oMOqtqqT0LUbppEGjuepYW0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.DispatchProxy.wasm",
        "name": "System.Reflection.DispatchProxy.gg8jdyj3dc.wasm",
        "integrity": "sha256-kJZ6982mSeRVJCNb06NQ23HoWnTEmvO0NxrvOXOJJpg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Emit.ILGeneration.wasm",
        "name": "System.Reflection.Emit.ILGeneration.u8axice94p.wasm",
        "integrity": "sha256-HtRfKZ6iNZxx2piyanfk4LmhRX1YBCx37ponvpBh0Eo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Emit.Lightweight.wasm",
        "name": "System.Reflection.Emit.Lightweight.1me3hn6uqk.wasm",
        "integrity": "sha256-ubm6+9/dLow9wWPxmitV1cnNdOOn3VKPRAA289GtM/A=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Emit.wasm",
        "name": "System.Reflection.Emit.teu4e69yfo.wasm",
        "integrity": "sha256-9ShVCP6FOb9pNkQYr3uD+xQM70jsRCRJyT3p1lzK+sk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Extensions.wasm",
        "name": "System.Reflection.Extensions.tfn1llsz8n.wasm",
        "integrity": "sha256-6VX3NwxR+O06/Kc8xH4coN1pBOZHJSBeqiNXXpQZFlU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Metadata.wasm",
        "name": "System.Reflection.Metadata.hcrad5y2m0.wasm",
        "integrity": "sha256-D1bD5UCNAJTSZnFeSiLqN5rKRIQsBwZbm6/HK97ojv4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Primitives.wasm",
        "name": "System.Reflection.Primitives.tu6bwlqdmh.wasm",
        "integrity": "sha256-i5T5KkSptp/H5k15G4xo7bYzmFro/BIcv84JAJHZR38=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.TypeExtensions.wasm",
        "name": "System.Reflection.TypeExtensions.8d6wziiigr.wasm",
        "integrity": "sha256-xOwcyLqqK2vAkmuDS20yQx2b06VxHy/z9c3Rd/aYuZE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.wasm",
        "name": "System.Reflection.dh306hpixy.wasm",
        "integrity": "sha256-XzSPE7vGd+/TAOxM905b5osEVeD0sySpe2iOjx2N9qg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Resources.Reader.wasm",
        "name": "System.Resources.Reader.vlwhuwehja.wasm",
        "integrity": "sha256-Uz2ScLSv9booa8nlFGoCdgfpTAX9SQU1NYqKL6BP6sI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Resources.ResourceManager.wasm",
        "name": "System.Resources.ResourceManager.rhye091lm5.wasm",
        "integrity": "sha256-H7ruwl1CUR0YEy8+sFhj2o9FhPMxwZMS/zwyRn3OpIg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Resources.Writer.wasm",
        "name": "System.Resources.Writer.wc79hpkg07.wasm",
        "integrity": "sha256-gH2ujhHpecbNaz9JfZKjYKa4vf1Pav0JPkq5A4feuWY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.CompilerServices.Unsafe.wasm",
        "name": "System.Runtime.CompilerServices.Unsafe.4heh5foz8q.wasm",
        "integrity": "sha256-eJvXb+34tWrxJk/sBO+3b304cVPSyspcSsKsyBNwEWI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.CompilerServices.VisualC.wasm",
        "name": "System.Runtime.CompilerServices.VisualC.5yzdk58bfs.wasm",
        "integrity": "sha256-wwD8YYGBj7WUKlMH5t5zaZlGcAaMncngwl3dmPuB5N8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Extensions.wasm",
        "name": "System.Runtime.Extensions.r88ud4v9ne.wasm",
        "integrity": "sha256-O3Jv7BWyKatxL5Btzr3sFp2GlY7xYfPteb31ErQfEoI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Handles.wasm",
        "name": "System.Runtime.Handles.s5geff2cnc.wasm",
        "integrity": "sha256-sItFWWI7kFjaqly9CRp/m6S0gudfBdnnvqnptvyqrhk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.InteropServices.RuntimeInformation.wasm",
        "name": "System.Runtime.InteropServices.RuntimeInformation.wf0mhc6lz8.wasm",
        "integrity": "sha256-RFP0POyiP/mdtEVe6EDozxJh+wUS9mst7h6Qx2iCGpA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.InteropServices.wasm",
        "name": "System.Runtime.InteropServices.otep1kpfuj.wasm",
        "integrity": "sha256-TNi4zfnCu/siZwJikNEtOEDf1AfKHnSQfTktEtj1Xhw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Intrinsics.wasm",
        "name": "System.Runtime.Intrinsics.lf48puditi.wasm",
        "integrity": "sha256-cQkvKDJXrDEt7xXKMW/ikQO3ZvEcN+uTKwKKLM9umXM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Loader.wasm",
        "name": "System.Runtime.Loader.9fyz8vui1v.wasm",
        "integrity": "sha256-Bd1xga1GgV0SQjeqPh1hS7LZ1sanXsOa3pk0vi+zJj0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Numerics.wasm",
        "name": "System.Runtime.Numerics.a1ljia6hzl.wasm",
        "integrity": "sha256-wrRmb4+DolnraisjK+0TlFjSN6nFqVKK9vOW4MX/C10=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Formatters.wasm",
        "name": "System.Runtime.Serialization.Formatters.ptm4qsaphp.wasm",
        "integrity": "sha256-c4mIQkpWi22538veGns9Vd+Wua44z9lsKjp0edAfweQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Json.wasm",
        "name": "System.Runtime.Serialization.Json.mjtwe7v2dy.wasm",
        "integrity": "sha256-khfd1A9z0/UfxT4QZii0WSfbcDnFRu99a+TwAbWtb6Q=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Primitives.wasm",
        "name": "System.Runtime.Serialization.Primitives.8m9kji25rl.wasm",
        "integrity": "sha256-sEi50nLjCHQHv3JNTKpKaWGFFj8GfZ2NmmhfR8oQOO0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Xml.wasm",
        "name": "System.Runtime.Serialization.Xml.a02htu603n.wasm",
        "integrity": "sha256-lgbUh1Z/HJWgM9LHS+juqhYDdvIewSJMVJ6qarzPjRY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.wasm",
        "name": "System.Runtime.Serialization.nh7kpsvl4q.wasm",
        "integrity": "sha256-6zsDkLawZXQwfH8hc4UhUOhQU9YivrQH8aFznDN+7TQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.wasm",
        "name": "System.Runtime.c8po3rm6da.wasm",
        "integrity": "sha256-vKRjEybppCl6nyVTDA9DLejFpH0f2O4hgvgcQiR+W2g=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.AccessControl.wasm",
        "name": "System.Security.AccessControl.ua2hvci7um.wasm",
        "integrity": "sha256-GssAULrRtqOMhRDOvoldLkDq8TOBplmdDSsoy+lLAtk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Claims.wasm",
        "name": "System.Security.Claims.2fqh7dt6oy.wasm",
        "integrity": "sha256-AlVbTpbImF6/fYkJfjEoRXaZXIUTrBzYP6Y6j/gbajo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.Algorithms.wasm",
        "name": "System.Security.Cryptography.Algorithms.atzgi4e2fq.wasm",
        "integrity": "sha256-Tlt6WzWa/SsZi07gqY/ZdR3LQfFrZhnTRWpaYpi36sM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.Cng.wasm",
        "name": "System.Security.Cryptography.Cng.i2dmbmhe5n.wasm",
        "integrity": "sha256-qn78MkWNWGpCxoT0jmLxOXedpctUGODDYf2PSTIp6Lg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.Csp.wasm",
        "name": "System.Security.Cryptography.Csp.ly5ynpbsoc.wasm",
        "integrity": "sha256-rX1cDAn4ejx3pagDKQJ3QoF2CeGHy7tuUoJgbUx8aL0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.Encoding.wasm",
        "name": "System.Security.Cryptography.Encoding.gzaxy5frii.wasm",
        "integrity": "sha256-3PsQmOoEo1Yks0N9utuMQ3ERZGONqw9jQv+P979UyKY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.OpenSsl.wasm",
        "name": "System.Security.Cryptography.OpenSsl.sumn0lpnc1.wasm",
        "integrity": "sha256-jGg/quFJzXSPH2zn+1J6ZhR1t/U+mDiWKRg7pRGU59U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.Primitives.wasm",
        "name": "System.Security.Cryptography.Primitives.9udf8s9dzo.wasm",
        "integrity": "sha256-0bsFyp7o4b9kQmCIcXGEGnWlHUmKop7w/1caLfLAjt8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.X509Certificates.wasm",
        "name": "System.Security.Cryptography.X509Certificates.rmoti88thr.wasm",
        "integrity": "sha256-81OqfIG/W45sBSODvD+sEtqsjLZogmrIo0LboiVPq0s=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.wasm",
        "name": "System.Security.Cryptography.ljynckjiyv.wasm",
        "integrity": "sha256-oRrmbtJGfPEE48e0Ot5LaebdVRfWGmTNrkREQwrVwCc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Principal.Windows.wasm",
        "name": "System.Security.Principal.Windows.n4h2eo2fdx.wasm",
        "integrity": "sha256-N9H8aEGT5Npul8OwNvW57ecI6qJjRSPIaKVS9MQswlI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Principal.wasm",
        "name": "System.Security.Principal.gm1mq3aag1.wasm",
        "integrity": "sha256-SOiceuuFN2bYYe3RAbPGBf6iT8QGgCs96M1u4IAqPGE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.SecureString.wasm",
        "name": "System.Security.SecureString.q1gu5bqoon.wasm",
        "integrity": "sha256-QoJTWLLIlGX0jDshr1iVdXu0KslfK6Kh7exda2LDbGw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.wasm",
        "name": "System.Security.mqpvajhq9e.wasm",
        "integrity": "sha256-Dpr06GP2NVZq7VBrlyf3cgLnmbAkG2Wbp3wNLIfxtYY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ServiceModel.Web.wasm",
        "name": "System.ServiceModel.Web.mlf9p7vomy.wasm",
        "integrity": "sha256-OtwYo4Z0pWoITXQnIHfx2EhNb4DUZ2YLz9I1seUPp8Y=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ServiceProcess.wasm",
        "name": "System.ServiceProcess.r1o2f5q6yl.wasm",
        "integrity": "sha256-wVaVxrCKHAiTQqIaHFURc4UnSIvvpbuPwMuxHNuaw4o=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Encoding.CodePages.wasm",
        "name": "System.Text.Encoding.CodePages.59m4ox2qmf.wasm",
        "integrity": "sha256-V9oToO+jWCbrB8Qm/BtaGMMWjAze+N3NcmMpqmVTeog=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Encoding.Extensions.wasm",
        "name": "System.Text.Encoding.Extensions.o5dguyae24.wasm",
        "integrity": "sha256-nGwmZyO9MEZ4q/LhTNjDaZylYpaqsIJCZFlORevnxp8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Encoding.wasm",
        "name": "System.Text.Encoding.3cg6eqwr34.wasm",
        "integrity": "sha256-M86FKPnH/hYcjA9BLe4mdvmBAejBITQxVPGzTbGYN+0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Encodings.Web.wasm",
        "name": "System.Text.Encodings.Web.5k08zvgr5y.wasm",
        "integrity": "sha256-K6wAbTWtLWSEzgxBmSa0PFJ7pgfbA6TOA2Kqy0RioeY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Json.wasm",
        "name": "System.Text.Json.dlpseev2cb.wasm",
        "integrity": "sha256-b1nI/h+y24awPDB+EFxtEs9ABc24kDuxT4VanjmkdbQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.RegularExpressions.wasm",
        "name": "System.Text.RegularExpressions.fhrmxwzd65.wasm",
        "integrity": "sha256-XRpLznZc/nmlf8Y8L/WOzZ8E6aOa6P1nBWkK9twNlCI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.AccessControl.wasm",
        "name": "System.Threading.AccessControl.l9hhyns4md.wasm",
        "integrity": "sha256-V7CumsBCCfayNcjOljx+pHQftq+Q1RV2v0YXAbG9BPE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Channels.wasm",
        "name": "System.Threading.Channels.cixub4fvpi.wasm",
        "integrity": "sha256-JDC3Uuyy/HdmvBNdeOJh12bY032vib96+5Lo55ob420=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Overlapped.wasm",
        "name": "System.Threading.Overlapped.ai3gdgwy4a.wasm",
        "integrity": "sha256-Pp0KDODCFAOz2V1B6Fno1FezY7gyDJuY2hzmBdVOkBM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Tasks.Dataflow.wasm",
        "name": "System.Threading.Tasks.Dataflow.wpuat8ifgp.wasm",
        "integrity": "sha256-BPf8IgmyBJIKeA6DUTe8ttYkLY/icAtm7UhK2ouTjnQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Tasks.Extensions.wasm",
        "name": "System.Threading.Tasks.Extensions.99xv6d8k7h.wasm",
        "integrity": "sha256-46vUWx47jcaQEap6vXaotAIiNI16riyOSUY0WVH3z+0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Tasks.Parallel.wasm",
        "name": "System.Threading.Tasks.Parallel.ui8wl4iwgg.wasm",
        "integrity": "sha256-JtiyPOPaKBU3SXcpbXAzfH3Ze3ciaZpJ762+8ktw/lc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Tasks.wasm",
        "name": "System.Threading.Tasks.x3lvm8xw0a.wasm",
        "integrity": "sha256-nYKgjI52rM1m0ViTlXSUxGwlogkCehlXlJI15mjx9Do=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Thread.wasm",
        "name": "System.Threading.Thread.1hchzbw4f1.wasm",
        "integrity": "sha256-m5d/MIjlv3GAGkbLI3iCCTgo+bXBSWGxFvuJF3R4QXA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.ThreadPool.wasm",
        "name": "System.Threading.ThreadPool.3erzuvgv6e.wasm",
        "integrity": "sha256-a+88mOWAdtEVA9UGG3ZEBgMoUch2XD2TSW93Hgg7jpY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Timer.wasm",
        "name": "System.Threading.Timer.pw7u5ao13z.wasm",
        "integrity": "sha256-dy9p3hHHabKjY4Av7utj7dqfHXki7SMEU/5MXp2qiqw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.wasm",
        "name": "System.Threading.s5cik749sj.wasm",
        "integrity": "sha256-EJ4s7/1lPt5JiBv0vpmRubA6BpT78VFy5Q1eMeLReuw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Transactions.Local.wasm",
        "name": "System.Transactions.Local.j88iamrwcu.wasm",
        "integrity": "sha256-Mww6bslF711oljPYctDaBZP3NPy3w0MpSHGY148EgSc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Transactions.wasm",
        "name": "System.Transactions.ofqh9q35m9.wasm",
        "integrity": "sha256-FGG21MGuWcoQ081Puc642jqVKHazG6MEZYsncrXsqL8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ValueTuple.wasm",
        "name": "System.ValueTuple.5pb1qjkobl.wasm",
        "integrity": "sha256-h0gYEViCM0eSnj9zf0yhV6ptt5ofIzyM9hKBw6K/4ks=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Web.HttpUtility.wasm",
        "name": "System.Web.HttpUtility.uap0pmnr39.wasm",
        "integrity": "sha256-FoGNcrgVQs50CkljTsqRw1S0acdPxSEDOJXEkHm1zaE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Web.wasm",
        "name": "System.Web.m2rkccydv1.wasm",
        "integrity": "sha256-8p0x7YMZU3vGvWb/CAKdCIQ31nuvEhkNJc/tkA7Zp4Q=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Windows.wasm",
        "name": "System.Windows.wd26h7y74m.wasm",
        "integrity": "sha256-bKF9QivP11+GZja3z4uNDyy3WL4TTUy4hlm4TlTpUgM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.Linq.wasm",
        "name": "System.Xml.Linq.ertx8s608b.wasm",
        "integrity": "sha256-FpNihCLKIMzEewGN7yeqcj82h5CdfaeJXDudxPiJEh8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.ReaderWriter.wasm",
        "name": "System.Xml.ReaderWriter.faceg4ij54.wasm",
        "integrity": "sha256-SUquzJK5y1u6qSiMi+zsPtbmYms7RotWZtd+5GH0vaI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.Serialization.wasm",
        "name": "System.Xml.Serialization.yrzwr5555w.wasm",
        "integrity": "sha256-4sMnasnQaMz3Yu7EAKxUjKxHlPgQ526QWMpInkItviQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XDocument.wasm",
        "name": "System.Xml.XDocument.glg03abz6m.wasm",
        "integrity": "sha256-/PBxd6iPu4q29TLqFcWsIwVMLLW2HsGYrf08KkZ8cwQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XPath.XDocument.wasm",
        "name": "System.Xml.XPath.XDocument.eeqmj78v18.wasm",
        "integrity": "sha256-Jt50OkWqc0Rn8xPKRj9sYEncgVfc3T3OxrFoxxi+nC0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XPath.wasm",
        "name": "System.Xml.XPath.nigr3pdphr.wasm",
        "integrity": "sha256-X4MvW0Pa1xxqzVSVsPhf5Asgif6Hv0y0uTTToKjiD0E=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XmlDocument.wasm",
        "name": "System.Xml.XmlDocument.4dvov313j7.wasm",
        "integrity": "sha256-yO+k9Htzbj0yIfcZh0YaDyOR5krlBYsKIJcDrqPfUZ0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XmlSerializer.wasm",
        "name": "System.Xml.XmlSerializer.bogmmoou52.wasm",
        "integrity": "sha256-Fcxq6Uco3OOkifyHBo3zZ9TvT+ZNbFnxcV0FSe7HRGc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.wasm",
        "name": "System.Xml.0j8sqtm01g.wasm",
        "integrity": "sha256-1JdAs3HwZbWGgmGzl3MGTJAa6nhulR8y3axCUtDbmX8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.wasm",
        "name": "System.jdbqe5ui30.wasm",
        "integrity": "sha256-6eFJQTq09B3/035aAtXUn2G6oYrE3LUUcYCNcS1sRlM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "WindowsBase.wasm",
        "name": "WindowsBase.lts5b5v1e3.wasm",
        "integrity": "sha256-14+RXB0TSvzoezj5d1/ydAUOIUvOtt50hH27E4aX7GM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "mscorlib.wasm",
        "name": "mscorlib.22svw5locb.wasm",
        "integrity": "sha256-Nry1+fNADEy9nf9k6z+J9ev19ix1Kh/hkBRdmxrc+BA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "netstandard.wasm",
        "name": "netstandard.mt3on52d87.wasm",
        "integrity": "sha256-psswpZswqO6xnCtxPQLjPxm9aQziZYPWbiBewKB3pOE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "ILGPU.Algorithms.wasm",
        "name": "ILGPU.Algorithms.g16xpz99pk.wasm",
        "integrity": "sha256-1JHt5gN+Aohf1ToH4U5COqu2zwfuQR93+Bh0QdcIu9o=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "ILGPU.wasm",
        "name": "ILGPU.1uzig6t4ik.wasm",
        "integrity": "sha256-F8KohwJ75vvC69wfaL4lMTZ3qjxwUZNTik+zi1lPlcE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SpawnDev.ILGPU.Demo.Shared.wasm",
        "name": "SpawnDev.ILGPU.Demo.Shared.nh44g9x8jm.wasm",
        "integrity": "sha256-RSwVfd/iygGucO+WlsUe1sZ8v36LIuYYYSHf8uE6drc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SpawnDev.ILGPU.wasm",
        "name": "SpawnDev.ILGPU.8rwcuconos.wasm",
        "integrity": "sha256-1P5SWnWMIdVYMTiGrJOlOi+JnU7iHN7vC6P9bERGXBc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SpawnDev.ILGPU.P2P.wasm",
        "name": "SpawnDev.ILGPU.P2P.g24jwpprqz.wasm",
        "integrity": "sha256-WA8L8AYartVH5M6mfsZhvc01VmgkHyjAQ4/qsXkaNlo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SpawnDev.ILGPU.Demo.wasm",
        "name": "SpawnDev.ILGPU.Demo.c74pbxugc8.wasm",
        "integrity": "sha256-eC9BGkQ60duqgRMCtwVHz+9fsvYxq2IkOHcn5q5lGGI=",
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
