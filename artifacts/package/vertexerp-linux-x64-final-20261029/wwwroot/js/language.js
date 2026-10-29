(() => {
    'use strict';
    const hindi = document.documentElement.lang === 'hi';
    const dictionary = new Map(Object.entries(window.VertexHindi || {}).map(([key, value]) => [normalize(key), value]));
    function normalize(value) { return value.replace(/\s+/g, ' ').trim().toLowerCase(); }
    function translate(value) {
        if (!hindi || typeof value !== 'string' || !/[a-z]/i.test(value)) return value;
        const text = value.replace(/\s+/g, ' ').trim();
        const direct = dictionary.get(normalize(text));
        if (direct) return value.replace(value.trim(), direct);
        // UI counters and captions retain their numeric values and separators.
        const count = text.match(/^(.*?)\s*(\([\d,]+\)|[:：*])$/);
        if (count && dictionary.has(normalize(count[1]))) return dictionary.get(normalize(count[1])) + ' ' + count[2];
        const welcome = text.match(/^Welcome\s+(.+)$/);
        if (welcome) return 'स्वागत है ' + welcome[1];
        const date = text.match(/^(\d{1,2}\s+)([A-Za-z]+)(\s+\d{4})$/);
        if (date && dictionary.has(normalize(date[2]))) return date[1] + dictionary.get(normalize(date[2])) + date[3];
        const required = text.match(/^The (.+) field is required\.$/);
        if (required) return translate(required[1]) + ' भरना आवश्यक है।';
        const numeric = text.match(/^The field (.+) must be a number\.$/);
        if (numeric) return translate(numeric[1]) + ' में संख्या भरें।';
        const title = text.match(/^(.*?) - (Vertex[_ ]ERP)$/);
        if (title) return translate(title[1]) + ' - ' + title[2];
        if (text.includes(' · ')) return text.split(' · ').map(translate).join(' · ');
        const locations = text.match(/^(\d+) locations$/);
        if (locations) return locations[1] + ' स्थान';
        const punched = text.match(/^Punched (in|out) at (.+)$/);
        if (punched) return (punched[1] === 'in' ? 'पंच इन का समय: ' : 'पंच आउट का समय: ') + punched[2];
        return value;
    }
    window.VertexLanguage = Object.freeze({ language: hindi ? 'hi' : 'en', locale: hindi ? 'hi-IN' : 'en-IN', t: translate });
    if (!hindi) return;
    const skip = 'script,style,noscript,code,pre,textarea,[contenteditable], [data-no-localize],.notranslate,[translate="no"],.user-meta strong,.detail-profile h2';
    function textNode(node) {
        const parent = node.parentElement;
        if (!parent || parent.closest(skip)) return;
        if (parent.tagName === 'OPTION' && parent.hasAttribute('data-record-option')) return;
        // Options without an explicit value submit their text. Preserve the English
        // contract before translating the visible label.
        if (parent.tagName === 'OPTION' && !parent.hasAttribute('value')) parent.setAttribute('value', parent.value);
        const next = translate(node.nodeValue);
        if (next !== node.nodeValue) node.nodeValue = next;
    }
    function localize(element) {
        if (element.nodeType === 3) { textNode(element); return; }
        if (element.nodeType !== 1 || element.closest(skip)) return;
        const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
        let node;
        while ((node = walker.nextNode())) textNode(node);
        [element, ...element.querySelectorAll('[placeholder],[title],[aria-label],[data-label],input[type="submit"],input[type="button"]')].forEach(item => {
            if (item.closest(skip)) return;
            for (const attr of ['placeholder', 'title', 'aria-label', 'data-label']) {
                if (item.hasAttribute(attr)) item.setAttribute(attr, translate(item.getAttribute(attr)));
            }
            if (item.matches('input[type="submit"],input[type="button"]')) item.value = translate(item.value);
        });
    }
    // Native dialogs are used by several existing modules. Keep their original behavior.
    for (const method of ['alert', 'confirm', 'prompt']) {
        const original = window[method].bind(window);
        window[method] = (message, ...args) => original(translate(message), ...args);
    }
    document.addEventListener('DOMContentLoaded', () => {
        localize(document.documentElement);
        document.title = translate(document.title);
        const observer = new MutationObserver(records => {
            observer.disconnect();
            for (const record of records) {
                if (record.type === 'characterData') textNode(record.target);
                else if (record.type === 'attributes') localize(record.target);
                else record.addedNodes.forEach(localize);
            }
            observer.observe(document.body, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ['placeholder', 'title', 'aria-label', 'data-label'] });
        });
        observer.observe(document.body, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ['placeholder', 'title', 'aria-label', 'data-label'] });
    });
})();
