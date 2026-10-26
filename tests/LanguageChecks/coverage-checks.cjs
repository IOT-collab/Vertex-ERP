// Audit static UI phrases across every Razor screen. Names, sample IDs, hardware
// menu paths and the contents of generated employment letters are business data.
const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm'), assert = require('node:assert/strict');
const base = path.join(__dirname, '../../Vertex ERP');
const context = { window: {} };
vm.runInNewContext(fs.readFileSync(path.join(base, 'wwwroot/js/locales/hi.js'), 'utf8'), context);
const keys = new Set(Object.keys(context.window.VertexHindi).map(x => x.toLowerCase()));
const literals = new Set(['Vertex Automation System Pvt. Ltd.', 'Vertex Power Controls Pvt. Ltd.', 'VERTEX GROUP', 'Vertex ERP', 'AK', 'Arjun Kumar', 'Sector 62, Noida', 'Noida Office K40 Pro', 'NOIDA-HO', 'Menu → Comm. → Cloud Server Setting', 'ADMS', 'HTTPS', 'ABCDE1234F', 'MS Teams', 'Zoom', 'Google Meet', 'DD/MM/YYYY', 'Tarun Sharma', 'Sanjeev Kumar', 'PS', 'Priya Sharma', 'RM', 'Rohit Mehta', "Arjun Kumar's route", 'EMP-1024 · Field Sales', 'Sector 18, Noida', 'Sector 16, Noida', 'e.g. VAS0180', 'PRJ-2026-01', 'ASPNETCORE_ENVIRONMENT', 'so the compiler defers property checks to runtime IEnumerable']);
const missing = new Map(); let screens = 0;
function check(text, file) {
    text = text.replace(/\s+/g, ' ').trim().replaceAll('&amp;', '&');
    if (!/^[A-Za-z]/.test(text) || /[{}@=;]/.test(text) || /\.Count\(|\.ToString\(|\.IsActive\)|\.IsOnline\)|\.MappingCount\)/.test(text) || /^https?:/.test(text)) return;
    if (keys.has(text.toLowerCase()) || literals.has(text)) return;
    if (file.endsWith(path.join('Language', 'Index.cshtml')) && ['English', 'Use the app in English.'].includes(text)) return; // Native language names stay recognizable.
    missing.set(text, path.relative(base, file));
}
function walk(folder) {
    for (const entry of fs.readdirSync(folder, { withFileTypes: true })) {
        const file = path.join(folder, entry.name);
        if (entry.isDirectory()) { walk(file); continue; }
        if (!file.endsWith('.cshtml') || entry.name === 'GeneratedEmployeeDocument.cshtml') continue;
        screens++;
        const source = fs.readFileSync(file, 'utf8').replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, '').replace(/<style\b[^>]*>[\s\S]*?<\/style>/gi, '').replace(/<!--[^]*?-->/g, '');
        for (const match of source.matchAll(/>([^<>]+)</g)) check(match[1], file);
        for (const match of source.matchAll(/(?:placeholder|title|aria-label)="([^"]+)"/g)) check(match[1], file);
    }
}
walk(path.join(base, 'Views'));
assert.equal(missing.size, 0, 'Missing Hindi UI resources:\n' + [...missing].map(([text, file]) => `${file}: ${text}`).join('\n'));
console.log(`Static Hindi UI coverage passed across ${screens} Razor screens and partials (${keys.size} resources).`);
