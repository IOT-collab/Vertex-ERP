const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const root = path.join(__dirname, '../../Vertex ERP');
function files(dir, extension) {
    return fs.readdirSync(dir, {withFileTypes:true}).flatMap(e => e.isDirectory() ? files(path.join(dir,e.name),extension) : e.name.endsWith(extension) ? [path.join(dir,e.name)] : []);
}
function withoutVariables(value) {
    return value.replace(/var\(/g, '\u0001(').split('\u0001').map((piece,i) => {
        if (!i) return piece;
        let depth=0;
        for(let j=0;j<piece.length;j++) { if(piece[j]==='(') depth++; if(piece[j]===')' && --depth===0) return piece.slice(j+1); }
        return '';
    }).join('');
}
const failures=[]; let checked=0;
function audit(file, css) {
    assert.ok(css.length, 'Empty stylesheet/view: '+file);
    for(const block of css.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
        if (/overlay|backdrop|data-theme.*dark/i.test(block[1])) continue;
        for(const declaration of block[2].matchAll(/(?<![\w-])background(?:-color|-image)?\s*:\s*([^;{}]+)/g)) {
            checked++;
            const value=withoutVariables(declaration[1]);
            for(const color of value.matchAll(/#([0-9a-f]{8}|[0-9a-f]{6}|[0-9a-f]{4}|[0-9a-f]{3})\b|rgba?\(([^)]+)\)|\bblack\b/gi)) {
                let rgb, alpha=1;
                if(color[1]) {
                    let h=color[1]; if(h.length<=4) h=[...h].map(x=>x+x).join('');
                    rgb=[0,2,4].map(i=>parseInt(h.slice(i,i+2),16)); if(h.length===8) alpha=parseInt(h.slice(6),16)/255;
                } else if(color[2]) { const parts=color[2].split(',').map(Number); if(parts.some(Number.isNaN)) continue; rgb=parts.slice(0,3); alpha=parts[3]??1; }
                else rgb=[0,0,0];
                if(alpha>.55 && Math.max(...rgb)<110 && Math.max(...rgb)-Math.min(...rgb)<85)
                    failures.push(path.relative(root,file)+': '+block[1].trim().slice(-70)+' => '+declaration[1]);
            }
        }
    }
}
for(const file of files(path.join(root,'wwwroot/css'),'.css')) if(!file.endsWith('theme.css')) audit(file,fs.readFileSync(file,'utf8'));
for(const file of files(path.join(root,'Views'),'.cshtml')) {
    const source=fs.readFileSync(file,'utf8'); assert.ok(source.length, 'Empty view: '+file);
    for(const region of source.matchAll(/<style\b[^>]*>([\s\S]*?)<\/style>/gi)) audit(file,region[1]);
    for(const region of source.matchAll(/\bstyle="([^"\n]*)"/gi)) audit(file,'inline {'+region[1]+'}');
}
assert.deepEqual(failures, [], 'Fixed dark surfaces bypass the account theme:\n'+failures.join('\n'));
console.log('Palette coverage passed: '+checked+' background declarations across page CSS and inline views; no unthemed dark surfaces.');
