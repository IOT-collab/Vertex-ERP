const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../Vertex ERP/wwwroot/js/theme.js'), 'utf8');
function run(hour, saved, user = '17', brokenStorage = false) {
    const properties = {};
    const root = { dataset: { themeUser: user }, style: { setProperty: (key, value) => properties[key] = value } };
    const events = {};
    let tick;
    vm.runInNewContext(source, {
        document: { documentElement: root, addEventListener: () => {}, getElementById: () => null },
        window: { addEventListener: (name, handler) => events[name] = handler },
        Date: class { getHours() { return hour; } },
        localStorage: { getItem: key => { assert.equal(key, 'vertex.theme.v1.' + user); if (brokenStorage) throw Error(); return saved; } },
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
transition.events.storage({ key: 'vertex.theme.v1.18', newValue: '{"mode":"light"}' });
assert.equal(transition.root.dataset.theme, 'dark');
transition.events.storage({ key: 'vertex.theme.v1.17', newValue: '{"mode":"light"}' });
assert.equal(transition.root.dataset.theme, 'light');
console.log('Theme checks passed: time boundaries, manual modes, custom validation, account isolation, storage failures and live transitions.');
