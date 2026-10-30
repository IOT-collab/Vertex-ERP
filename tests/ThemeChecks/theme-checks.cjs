const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../Vertex ERP/wwwroot/js/theme.js'), 'utf8');
for (const layout of ['_Layout.cshtml', '_LayoutMaster.cshtml']) {
    const markup = fs.readFileSync(path.join(__dirname, '../../Vertex ERP/Views/Shared', layout), 'utf8');
    assert.ok(markup.includes('~/js/theme.js'), layout + ' must initialize the shared theme');
    assert.ok(markup.includes('~/css/DiwaliTheme.css'), layout + ' must load the shared Diwali styling');
}
function run(hour, saved, user = '17', brokenStorage = false, legacySaved = null) {
    const properties = {};
    const root = { dataset: { themeUser: user }, style: { setProperty: (key, value) => properties[key] = value } };
    const events = {};
    let tick;
    vm.runInNewContext(source, {
        document: { documentElement: root, addEventListener: () => {}, getElementById: () => null },
        window: { addEventListener: (name, handler) => events[name] = handler },
        Date: class { getHours() { return hour; } },
        localStorage: { getItem: key => {
            if (brokenStorage) throw Error();
            if (key === 'vertex.theme.v3.' + user) return saved;
            if (key === 'vertex.theme.v2.' + user) return legacySaved;
            throw Error('Unexpected theme key: ' + key);
        } },
        setInterval: handler => tick = handler
    });
    return { root, properties, events, tick, setHour: value => hour = value };
}
for (const [hour, expected] of [[0, 'dark'], [5, 'dark'], [6, 'light'], [17, 'light'], [18, 'dark'], [23, 'dark']]) {
    assert.equal(run(hour, null).root.dataset.theme, expected);
}
assert.equal(run(12, '{"mode":"dark"}').root.dataset.theme, 'dark');
assert.equal(run(23, '{"mode":"light"}').root.dataset.theme, 'light');
const light = run(12, '{"mode":"light"}');
for (const token of ['--erp-bg-1', '--erp-bg-2', '--erp-bg-3']) {
    assert.equal(light.properties[token], '#f3f6fb', 'Legacy dashboard gradients must use the selected background');
}
assert.equal(light.properties['--erp-text-soft'], '#172033');
assert.equal(light.properties['--erp-glass'], '#ffffff');
assert.equal(light.properties['--erp-text-muted'], '#52627a');
const dark = run(12, '{"mode":"dark"}');
assert.equal(dark.properties['--erp-text-soft'], '#f1f5f9');
assert.equal(dark.properties['--erp-glass'], '#0f172a');
const diwaliDay = run(13, null);
assert.equal(diwaliDay.root.dataset.themePreset, 'diwali');
assert.equal(diwaliDay.root.dataset.diwaliTime, 'day');
assert.equal(diwaliDay.root.dataset.theme, 'light');
assert.equal(diwaliDay.properties['--erp-bg'], '#fff8eb');
assert.equal(diwaliDay.properties['--erp-surface'], '#fffdf7');
assert.equal(diwaliDay.properties['--erp-primary'], '#8e2c1c');
const diwaliNight = run(21, null);
assert.equal(diwaliNight.root.dataset.themePreset, 'diwali');
assert.equal(diwaliNight.root.dataset.diwaliTime, 'night');
assert.equal(diwaliNight.root.dataset.theme, 'dark');
assert.equal(diwaliNight.properties['--erp-bg'], '#080e24');
assert.equal(diwaliNight.properties['--erp-surface'], '#111a38');
assert.equal(diwaliNight.properties['--erp-primary'], '#f4b942');
assert.equal(run(13, null, '17', false, '{"mode":"auto"}').root.dataset.themePreset, 'diwali',
    'Existing accounts start with the requested Diwali day/night theme');
assert.equal(run(13, null, '17', false, '{"mode":"dark"}').root.dataset.themePreset, 'diwali',
    'Old per-account choices do not block the app-wide Diwali default');
assert.equal(run(13, '{"mode":"dark"}').root.dataset.theme, 'dark',
    'A new explicit choice remains respected after the user saves it');
assert.equal(run(12, 'invalid-json').root.dataset.theme, 'light');
assert.equal(run(12, null, '17', true).root.dataset.theme, 'light');
assert.equal(run(12, null, '').root.dataset.theme, undefined);
const custom = run(12, JSON.stringify({ mode: 'custom', background: '#abcdef', surface: '#123456', text: '#ffffff', accent: 'url(bad)' }));
assert.equal(custom.properties['--erp-bg'], '#abcdef');
assert.equal(custom.properties['--erp-primary'], '#2563eb');
const transition = run(17, null);
transition.setHour(18);
transition.tick();
assert.equal(transition.root.dataset.theme, 'dark');
assert.equal(transition.root.dataset.diwaliTime, 'night');
assert.equal(transition.properties['--erp-bg'], '#080e24');
transition.events.storage({ key: 'vertex.theme.v3.18', newValue: '{"mode":"light"}' });
assert.equal(transition.root.dataset.theme, 'dark');
transition.events.storage({ key: 'vertex.theme.v3.17', newValue: '{"mode":"light"}' });
assert.equal(transition.root.dataset.theme, 'light');
console.log('Theme checks passed: time boundaries, manual modes, custom validation, account isolation, storage failures and live transitions.');

assert.equal(light.root.dataset.themeTone, 'light');
assert.equal(dark.root.dataset.themeTone, 'dark');
function relativeLuminance(hex) {
    const c=hex.slice(1).match(/../g).map(x=>parseInt(x,16)/255).map(x=>x<=.04045?x/12.92:((x+.055)/1.055)**2.4);
    return c[0]*.2126+c[1]*.7152+c[2]*.0722;
}
function ratio(a,b) { const x=relativeLuminance(a), y=relativeLuminance(b); return (Math.max(x,y)+.05)/(Math.min(x,y)+.05); }
for (const palette of [light,dark]) for(const role of ['text','text-muted','danger','success','warning'])
    assert.ok(ratio(palette.properties['--erp-'+role],palette.properties['--erp-surface'])>=4.5, role+' text must remain readable on the selected surface');
assert.equal(custom.properties['--erp-danger'], '#ffffff');
for (const palette of [diwaliDay, diwaliNight]) for (const role of ['text', 'text-muted'])
    assert.ok(ratio(palette.properties['--erp-' + role], palette.properties['--erp-surface']) >= 4.5,
        'Diwali ' + palette.root.dataset.diwaliTime + ' ' + role + ' must remain readable on its surface');
console.log('Palette contrast checks passed for light/dark surfaces, status text and custom palettes.');
