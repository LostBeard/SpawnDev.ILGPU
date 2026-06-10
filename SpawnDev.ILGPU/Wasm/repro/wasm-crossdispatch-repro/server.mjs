// Minimal static server that sends COOP/COEP so SharedArrayBuffer + cross-origin isolation work.
// Run: node server.mjs   then open http://localhost:8787/
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { dirname } from 'node:path';

const __dirname = dirname(fileURLToPath(import.meta.url));
const PORT = 8787;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.wasm': 'application/wasm' };

import { writeFile } from 'node:fs/promises';

createServer(async (req, res) => {
  let path = decodeURIComponent(req.url.split('?')[0]);
  if (path === '/report') {
    const q = Object.fromEntries(new URL(req.url, 'http://x').searchParams);
    await writeFile(join(__dirname, 'stats.json'), JSON.stringify({ ...q, t: new Date().toISOString() }));
    if (q.staleIters && q.staleIters !== '0') console.log(`!!! STALE: ${JSON.stringify(q)}`);
    else console.log(`ok iters=${q.iters} staleIters=${q.staleIters} (${q.secs}s)`);
    res.writeHead(204, { 'Cross-Origin-Resource-Policy': 'same-origin' }); res.end(); return;
  }
  if (path === '/') path = '/index.html';
  try {
    const buf = await readFile(join(__dirname, path));
    res.writeHead(200, {
      'Content-Type': MIME[extname(path)] || 'application/octet-stream',
      // Required for SharedArrayBuffer / crossOriginIsolated:
      'Cross-Origin-Opener-Policy': 'same-origin',
      'Cross-Origin-Embedder-Policy': 'require-corp',
      'Cross-Origin-Resource-Policy': 'same-origin',
      'Cache-Control': 'no-store',
    });
    res.end(buf);
  } catch {
    res.writeHead(404); res.end('not found: ' + path);
  }
}).listen(PORT, () => console.log(`cross-dispatch repro at http://localhost:${PORT}/`));
