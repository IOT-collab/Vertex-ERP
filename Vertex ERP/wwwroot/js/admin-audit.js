(() => {
    const container = document.getElementById('adminAuditTimeline');
    if (!container) return;
    const state = document.getElementById('adminAuditStatus');
    async function refresh() {
        try {
            const response = await fetch(container.dataset.url, { cache: 'no-store', headers: { Accept: 'application/json' } });
            if (response.status === 401 || response.status === 403 || response.redirected) {
                container.replaceChildren(); state.textContent = 'Session ended. Please sign in again.'; return;
            }
            if (!response.ok) throw new Error('Unable to refresh');
            const logs = await response.json();
            const items = logs.map(log => {
                const item = document.createElement('div'); item.className = 'alert-item';
                const title = document.createElement('strong'); title.textContent = log.title;
                const detail = document.createElement('p'); detail.className = 'sub-text';
                detail.textContent = `${log.detail} · ${new Date(log.occurredAt).toLocaleString()}`;
                item.append(title, detail); return item;
            });
            if (!items.length) { const empty = document.createElement('p'); empty.textContent = 'No activity recorded yet.'; items.push(empty); }
            container.replaceChildren(...items);
            state.textContent = 'Live · refreshes every 5 seconds';
        } catch { state.textContent = 'Live update unavailable. Retrying…'; }
        window.setTimeout(refresh, 5000);
    }
    refresh();
})();
