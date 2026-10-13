const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const path = require('node:path');
const view = fs.readFileSync(path.join(__dirname, '../../Vertex ERP/Views/Main/ForgotPassword.cshtml'), 'utf8');
const script = view.match(/<script type="module">([\s\S]*?)<\/script>/)[1]
    .replace(/const firebaseConfig = .*?;/, 'const firebaseConfig = {apiKey:"test",authDomain:"test",projectId:"test"};')
    .replace(/@Url.Action\("(\w+)"\)/g, '$1')
    .replace(/import\('/g, 'loadModule(\'');
function setup() {
    const elements = new Map();
    const element = id => {
        if (!elements.has(id)) elements.set(id, { hidden: true, value: '123456', dataset: {}, focus() {}, addEventListener(event, handler) { this[event] = handler; } });
        return elements.get(id);
    };
    const state = { calls: [], sendError: null, serverError: null, loadError: false, sends: 0 };
    const authSdk = { getAuth: () => ({}), RecaptchaVerifier: class { async render() {} clear() {} },
        async signInWithPhoneNumber() { state.sends++; if (state.sendError) throw state.sendError; return {}; } };
    const context = { document: { getElementById: element, querySelector: () => ({value: 'csrf'}) },
        window: { setTimeout() {}, clearTimeout() {} }, AbortSignal, Date,
        async loadModule(url) { if (state.loadError) throw new Error('network'); return url.includes('firebase-app') ? {initializeApp: () => ({})} : authSdk; },
        async fetch(url) { state.calls.push(url); const failed = state.serverError && url === 'BeginSmsOtp';
            return {ok: !failed, json: async () => failed ? {ok:false, message:state.serverError} : {ok:true,phoneNumber:'+919999999999',message:'Email accepted'}}; }
    };
    vm.runInNewContext(script, context);
    return {state, element};
}
(async () => {
    for (const [code, expected] of [['auth/billing-not-enabled', /billing is not enabled/], ['auth/operation-not-allowed', /SMS region policy/], ['auth/quota-exceeded', /quota/]]) {
        const {state, element} = setup(); state.sendError = {code};
        await element('smsButton').click();
        assert.match(element('recoveryMessage').textContent, expected);
        assert.equal(element('otpSection').hidden, false);
        assert(state.calls.includes('CancelSmsOtp'));
        assert(!state.calls.includes('MarkSmsSent'));
    }
    {
        const {state, element} = setup();
        await element('smsButton').click();
        state.sendError = {code:'auth/billing-not-enabled'};
        await element('smsButton').click();
        assert.match(element('recoveryMessage').textContent, /billing is not enabled/);
        assert.equal(element('otpEntry').hidden, true);
        assert.equal(state.calls.filter(x => x === 'MarkSmsSent').length, 1);
        assert(state.calls.includes('CancelSmsOtp'));
    }
    {
        const {state, element} = setup(); state.loadError = true;
        await element('smsButton').click();
        assert.match(element('recoveryMessage').textContent, /Firebase could not load/);
        assert.equal(state.calls.length, 0);
        await element('emailButton').click();
        assert(state.calls.includes('BeginEmailOtp'));
    }
    {
        const {state, element} = setup(); state.serverError = 'Please wait 60 seconds.';
        await element('smsButton').click();
        assert.equal(element('recoveryMessage').textContent, state.serverError);
        assert.equal(state.sends, 0);
    }
    console.log('PASS: Firebase billing/provider/quota errors, failed resend, SDK failure with email fallback, and server rate-limit messages. No SMS sent.');
})().catch(error => { console.error(error); process.exitCode = 1; });
