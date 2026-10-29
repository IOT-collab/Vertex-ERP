(() => {
    const versions = new WeakMap();
    const summaries = () => document.querySelectorAll('[data-facility-quota]');
    const kind = form => form?.elements.namedItem('leaveType')?.value;
    async function refresh(node) {
        const form = node.closest('form');
        const date = form?.elements.namedItem('fromDate')?.value || node.dataset.date;
        if (!date) return;
        const version = (versions.get(node) || 0) + 1;
        versions.set(node, version);
        const url = new URL(node.dataset.quotaUrl, location.origin);
        url.searchParams.set('date', date);
        if (form?.elements.namedItem('id')) url.searchParams.set('excludingId', form.elements.namedItem('id').value);
        const content = node.querySelector('[data-quota-content]');
        try {
            const response = await fetch(url, {cache: 'no-store', headers: {'Accept': 'application/json'}});
            if (!response.ok) throw new Error('quota');
            const data = await response.json();
            if (versions.get(node) !== version) return;
            content.replaceChildren();
            const month = document.createElement('small'); month.textContent = data.month; content.append(month);
            for (const [label, quota] of [['Half Day', data.halfDay], ['Short Leave', data.shortLeave]]) {
                const row = document.createElement('div'); row.className = 'facility-quota-row';
                row.dataset.quotaFull = String(quota.available === 0);
                const name = document.createElement('strong'); name.textContent = label === 'Short Leave' ? 'Short Leave (max 2 hours each)' : label;
                const values = document.createElement('span');
                values.textContent = `Limit ${quota.limit} | Used ${quota.used} | Pending ${quota.pending} | Available ${quota.available}`;
                row.append(name, values); content.append(row);
            }
            const selected = kind(form);
            const quota = selected === 'Half Day' ? data.halfDay : selected === 'Short Leave' ? data.shortLeave : null;
            const button = form?.querySelector('button[type="submit"]');
            if (button) button.disabled = !!quota && quota.available === 0;
            const error = form?.querySelector('[data-facility-error]');
            if (error) error.textContent = quota?.available === 0 ? 'Monthly quota is full, including pending requests. Select another month or review your requests.' : '';
        } catch {
            if (versions.get(node) !== version) return;
            content.textContent = 'Quota could not be loaded. Your quota will be checked when you submit.';
            const button = form?.querySelector('button[type="submit"]'); if (button) button.disabled = false;
        }
    }
    function sync(form) {
        const type = kind(form);
        const facility = type === 'Half Day' || type === 'Short Leave';
        const from = form.elements.namedItem('fromDate'), to = form.elements.namedItem('toDate');
        if (facility && from && to) to.value = from.value;
        if (to && to.type !== 'hidden') to.readOnly = facility;
        const session = form.elements.namedItem('halfDaySession');
        if (session) { session.required = type === 'Half Day'; session.closest('label').hidden = type !== 'Half Day'; }
        const start = form.elements.namedItem('shortLeaveFrom'), end = form.elements.namedItem('shortLeaveTo');
        for (const input of [start, end]) if (input) { input.required = type === 'Short Leave'; input.closest('label').hidden = type !== 'Short Leave'; }
        if (end) {
            const minutes = value => value ? Number(value.split(':')[0]) * 60 + Number(value.split(':')[1]) : NaN;
            const duration = minutes(end.value) - minutes(start?.value);
            end.setCustomValidity(type === 'Short Leave' && start?.value && end.value && (duration <= 0 || duration > 120) ? 'Choose a later end time, up to 2 hours after the start.' : '');
        }
    }
    window.openExtraLeave = id => {
        const dialog = document.getElementById(id); if (!dialog) return;
        dialog.showModal(); dialog.querySelectorAll('[data-facility-quota]').forEach(refresh);
    };
    document.querySelectorAll('[data-facility-form]').forEach(form => {
        sync(form);
        form.addEventListener('input', event => {
            sync(form);
            if (['fromDate','leaveType'].includes(event.target.name)) form.querySelectorAll('[data-facility-quota]').forEach(refresh);
        });
        form.addEventListener('submit', () => sync(form));
    });
    summaries().forEach(refresh);
})();
