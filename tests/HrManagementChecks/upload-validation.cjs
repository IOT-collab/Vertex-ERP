const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const path = require('node:path');
const view = fs.readFileSync(path.join(__dirname, '../../Vertex ERP/Views/Hr/UploadDocument.cshtml'), 'utf8');
const model = fs.readFileSync(path.join(__dirname, '../../Vertex ERP/Models/EmployeeDocumentViewModels.cs'), 'utf8');
const maximum = Number(model.match(/MaximumFileSize = (\d+) \* 1024 \* 1024/)[1]) * 1024 * 1024;
const script = view.split('<script>')[1].split('})();')[0] + '})();';
const listeners = {};
const message = { textContent: '', classList: { toggle() {} } };
const input = {
    files: [], error: '', reported: false,
    setCustomValidity(error) { this.error = error; },
    reportValidity() { this.reported = true; },
    addEventListener(event, handler) { listeners[event] = handler; },
    form: { addEventListener(event, handler) { listeners[event] = handler; } }
};
vm.runInNewContext(script.replace('@VertexERP.Models.EmployeeDocumentUploadViewModel.MaximumFileSize', String(maximum)), {
    document: { getElementById() { return input; }, querySelector() { return message; } }
});
for (const size of [maximum - 1, maximum, maximum + 1, 20 * 1024 * 1024]) {
    input.files = [{ size }];
    listeners.change();
    assert.equal(Boolean(input.error), size > maximum);
    let blocked = false;
    listeners.submit({ preventDefault() { blocked = true; } });
    assert.equal(blocked, size > maximum);
    if (blocked) assert.match(message.textContent, /3 MB/);
}
input.files = [];
listeners.change();
assert.equal(input.error, '');
const types = [...view.matchAll(/<option>(.*?)<\/option>/g)].map(match => match[1]);
assert.deepEqual(types, ['Aadhaar Card', 'PAN Card', '10th Certificate', '12th Certificate', 'Graduation', 'Post Graduation', 'Employee Photo', 'Previous Company Documents', 'UAN Passbook', 'Other']);
console.log('PASS frontend upload boundaries, submit blocking, reset, and unchanged document types');
