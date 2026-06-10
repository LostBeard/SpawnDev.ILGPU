// Tiny server with COOP/COEP headers for SharedArrayBuffer support
import { createServer } from 'http';
import { readFileSync } from 'fs';
import { extname } from 'path';

const PORT = 8099;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.wasm': 'application/wasm', '.mjs': 'text/javascript' };

createServer((req, res) => {
    const file = req.url === '/' ? '/index.html' : req.url;
    try {
        const data = readFileSync('.' + file);
        const ext = extname(file);
        res.writeHead(200, {
            'Content-Type': MIME[ext] || 'application/octet-stream',
            'Cross-Origin-Opener-Policy': 'same-origin',
            'Cross-Origin-Embedder-Policy': 'require-corp',
        });
        res.end(data);
    } catch {
        res.writeHead(404);
        res.end('Not found');
    }
}).listen(PORT, () => console.log(`Serving at http://localhost:${PORT} (COOP/COEP enabled)`));
