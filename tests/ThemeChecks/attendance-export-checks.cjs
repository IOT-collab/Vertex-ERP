const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const code = fs.readFileSync('Vertex ERP/wwwroot/js/attendance-export.js', 'utf8');
for (const period of ['day', 'month']) {
    let handler, reset;
    const button = { innerHTML: 'Download' }, message = { hidden: true };
    const form = { action: '/Main/ExportAttendance', reportValidity: () => true,
        querySelector: selector => selector.includes('button') ? button : { value: period },
        addEventListener: (_, callback) => handler = callback };
    class Data extends Map { constructor() { super([['exportPeriod', period], ['filterDate', '2026-10-27'], ['exportMonth', '2026-10']]); } }
    vm.runInNewContext(code, {
        document: { getElementById: id => id === 'attendanceExportForm' ? form : message },
        FormData: Data, setTimeout(callback) { reset = callback; },
        window: { setExportPeriod(value) { assert.equal(value, period); }, addEventListener() {} },
        fetch() { throw new Error('Downloads must not use fetch'); }
    });
    let prevented = false;
    handler({ preventDefault() { prevented = true; } });
    assert.equal(prevented, false, 'Native attachment request must proceed');
    assert.equal(form.method, 'get'); assert.equal(form.target, '_self');
    assert.equal(message.hidden, false);
    assert.ok(!message.textContent.includes('downloaded'), 'Do not claim successful save prematurely');
    prevented = false; handler({ preventDefault() { prevented = true; } });
    assert.equal(prevented, true, 'Repeated clicks are prevented while preparing');
    reset();
    assert.equal(button.innerHTML, 'Download');
    form.reportValidity = () => false;
    prevented = false; handler({ preventDefault() { prevented = true; } });
    assert.equal(prevented, true, 'Invalid date must not submit');
}
console.log('PASS day/month native attachment submission, no fetch, repeat-click guard, validation and retry');
