const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const path = require('node:path');
const view = fs.readFileSync(path.join(__dirname, '../../Vertex ERP/Views/Main/FieldAttendanceLive.cshtml'), 'utf8');
const script = view.match(/<script>([\s\S]*?)<\/script>/)[1];
const elements = new Map();
const element = id => {
    if (!elements.has(id)) elements.set(id, { value: '', textContent: '', disabled: false, style: {}, classList: { add() {}, remove() {} }, focus() {} });
    return elements.get(id);
};
let captures = 0, failGps = false, posted;
const context = {
    document: { getElementById: element, querySelector: () => ({ value: 'csrf' }) },
    window: { isSecureContext: true, addEventListener() {} },
    navigator: { geolocation: {
        watchPosition(success, failure) {
            captures++;
            queueMicrotask(() => failGps ? failure({ code: 1 }) : success({ coords: { latitude: 28 + captures / 100, longitude: 77.2, accuracy: 10 }, timestamp: Date.now() }));
            return captures;
        }, clearWatch() {}
    } },
    setTimeout, clearTimeout, Date,
    fetch: async (_, options) => {
        posted = JSON.parse(options.body);
        return { ok: true, json: async () => ({ time: '22 Sep 2026, 12:00 PM IST', siteName: posted.siteName, latitude: posted.latitude, longitude: posted.longitude, accuracyMetres: 10, message: 'Saved' }) };
    }
};
vm.runInNewContext(script, context);
(async () => {
    await element('in').onclick();
    assert.equal(captures, 0, 'Blank site must not request GPS or save');
    element('siteName').value = ' Site A ';
    await element('in').onclick();
    assert.equal(captures, 1);
    failGps = true;
    await element('submit').onclick();
    assert.equal(posted, undefined, 'Denied GPS must not save attendance');
    assert.equal(element('submit').disabled, false, 'GPS failure must allow retry');
    failGps = false;
    await element('submit').onclick();
    assert.equal(posted.latitude, 28.03, 'Submit must capture fresh GPS, not use the earlier preview');
    assert.equal(posted.siteName, 'Site A');
    assert.equal(posted.action, 'Check In');
    assert.equal(element('in').disabled, true);
    assert.equal(element('out').disabled, false);
    assert.match(element('inLocation').textContent, /Site A/);
    element('siteName').value = 'Site B';
    await element('out').onclick();
    await element('submit').onclick();
    assert.equal(posted.action, 'Check Out');
    assert.equal(posted.siteName, 'Site B');
    assert.equal(element('out').disabled, true);
    assert.match(element('outTime').textContent, /IST/);
    console.log('PASS: field attendance browser flow, fresh GPS, permission failure/retry, site capture and check-out');
})().catch(error => { console.error(error); process.exitCode = 1; });
