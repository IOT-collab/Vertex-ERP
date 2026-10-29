(() => {
    const bells = [...document.querySelectorAll('[data-notification-bell]')];
    if (!bells.length) return;
    let busy = false;
    async function refresh() {
        if (busy || document.hidden) return;
        busy = true;
        try {
            const response = await fetch('/Notifications/Count', { cache: 'no-store', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) return;
            const data = await response.json();
            if (!Number.isInteger(data.count) || data.count < 0) return;
            for (const bell of bells) {
                const badge = bell.querySelector('[data-notification-count]');
                badge.textContent = data.count > 99 ? '99+' : String(data.count);
                badge.hidden = data.count === 0;
                bell.setAttribute('aria-label', `Notifications, ${data.count} unread`);
                bell.title = `${data.count} unread notifications`;
            }
        } catch { /* Keep navigation available during temporary connection failures. */ }
        finally { busy = false; }
    }
    refresh();
    window.setInterval(refresh, 30000);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) refresh(); });
    window.addEventListener('pageshow', refresh);
})();
