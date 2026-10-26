const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const assert = require('node:assert/strict');
const base = path.join(__dirname, '../../Vertex ERP/wwwroot/js');
class Element {
    constructor(tag, attrs = {}, children = []) { this.nodeType = 1; this.tagName = tag.toUpperCase(); this.attrs = attrs; this.children = children; children.forEach(c => c.parentElement = this); }
    get textContent() { return this.children.map(c => c.nodeType === 3 ? c.nodeValue : c.textContent).join(''); }
    get value() { return this.tagName === 'OPTION' ? this.attrs.value ?? this.textContent.replace(/\s+/g, ' ').trim() : this.inputValue; }
    set value(value) { this.inputValue = value; }
    hasAttribute(key) { return key in this.attrs; }
    getAttribute(key) { return this.attrs[key]; }
    setAttribute(key, value) { this.attrs[key] = value; }
    matches(selector) { return selector.includes('input[type=') && this.tagName === 'INPUT' && ['submit','button'].includes(this.attrs.type); }
    closest(selector) {
        for (let p = this; p; p = p.parentElement) {
            if (['SCRIPT','STYLE','NOSCRIPT','CODE','PRE','TEXTAREA'].includes(p.tagName) || p.hasAttribute('data-no-localize') || p.hasAttribute('contenteditable') || p.attrs.translate === 'no') return p;
        }
        return null;
    }
    querySelectorAll() { return this.children.filter(c => c.nodeType === 1).flatMap(c => [c, ...c.querySelectorAll()]); }
}
const text = value => ({ nodeType: 3, nodeValue: value });
function run(language) {
    const heading = new Element('h1', {}, [text('Dashboard')]);
    const name = new Element('strong', { 'data-no-localize': '' }, [text('Project')]);
    const input = new Element('input', { placeholder: 'Enter employee name', type: 'text' }); input.value = 'English data';
    const option = new Element('option', {}, [text('Pending')]);
    const script = new Element('script', {}, [text('Dashboard')]);
    const body = new Element('body', {}, [heading, name, input, option, script]);
    const html = new Element('html', {}, [body]); html.lang = language;
    const events = {};
    let observer;
    const context = {
        window: { alert: value => value, confirm: value => value, prompt: value => value },
        document: { documentElement: html, body, title: 'Dashboard - Vertex ERP', addEventListener: (name, callback) => events[name] = callback,
            createTreeWalker(element) {
                const nodes = [];
                function collect(e) { for (const c of e.children) c.nodeType === 3 ? nodes.push(c) : collect(c); }
                collect(element); let i = 0; return { nextNode: () => nodes[i++] };
            }
        }, NodeFilter: { SHOW_TEXT: 4 }, MutationObserver: class { constructor(callback) { observer = callback; } disconnect() {} observe() {} }
    };
    vm.runInNewContext(fs.readFileSync(path.join(base, 'locales/hi.js'), 'utf8'), context);
    vm.runInNewContext(fs.readFileSync(path.join(base, 'language.js'), 'utf8'), context);
    events.DOMContentLoaded?.();
    return { context, heading, name, input, option, script, observer, body };
}
const hi = run('hi');
assert.equal(hi.heading.textContent, 'डैशबोर्ड');
assert.equal(hi.name.textContent, 'Project', 'Employee-authored data must remain unchanged even when it equals a UI label');
assert.equal(hi.input.value, 'English data');
assert.equal(hi.input.getAttribute('placeholder'), 'कर्मचारी का नाम भरें');
assert.equal(hi.option.textContent, 'लंबित');
assert.equal(hi.option.getAttribute('value'), 'Pending', 'Translation must not alter submitted enum values');
assert.equal(hi.script.textContent, 'Dashboard');
assert.equal(hi.context.document.title, 'डैशबोर्ड - Vertex ERP');
const t = hi.context.window.VertexLanguage.t;
assert.equal(t('My Team (12)'), 'मेरी टीम (12)');
assert.equal(t('The Employee Name field is required.'), 'कर्मचारी का नाम भरना आवश्यक है।');
assert.equal(t('Unknown customer record'), 'Unknown customer record');
assert.equal(hi.context.window.confirm('Are you sure you want to delete this task?'), 'क्या आप यह कार्य हटाना चाहते हैं?');
const dynamic = new Element('p', {}, [text('Employee updated successfully.')]); dynamic.parentElement = hi.body;
hi.observer([{ type: 'childList', addedNodes: [dynamic] }]);
assert.equal(dynamic.textContent, 'कर्मचारी सफलतापूर्वक अपडेट हुआ।');
dynamic.children[0].nodeValue = 'No data available';
hi.observer([{ type: 'characterData', target: dynamic.children[0] }]);
assert.equal(dynamic.textContent, 'कोई जानकारी उपलब्ध नहीं है');
const en = run('en');
assert.equal(en.heading.textContent, 'Dashboard');
assert.equal(en.option.textContent, 'Pending');
assert.equal(en.context.window.VertexLanguage.locale, 'en-IN');
assert.equal(en.context.window.VertexLanguage.t('My Profile'), 'My Profile');
console.log('Language checks passed: Hindi/English, dynamic UI, placeholders, dialogs, required-field messages, original input/option values and protected records.');
