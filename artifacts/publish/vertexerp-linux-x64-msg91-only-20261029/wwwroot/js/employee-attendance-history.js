(() => {
    const content = document.getElementById('employee-attendance-content');
    const status = document.getElementById('attendance-refresh-status');
    if (!content) return;
    let loading = false;
    const t = text => window.VertexLanguage?.t(text) || text;
    async function refresh() {
        if (loading || document.hidden) return;
        loading = true;
        try {
            const url = new URL(window.location.href);
            url.searchParams.set('refresh', 'true');
            const response = await fetch(url, { cache: 'no-store', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!response.ok || response.redirected) throw new Error('Session or refresh unavailable');
            const page = new DOMParser().parseFromString(await response.text(), 'text/html');
            if (!page.querySelector('.attendance-history')) throw new Error('Unexpected response');
            content.replaceChildren(...page.body.childNodes);
            status.textContent = t('Last updated') + ': ' + new Date().toLocaleTimeString(window.VertexLanguage?.locale || 'en-IN');
        } catch { status.textContent = t('Live update unavailable. Retrying…'); }
        finally { loading = false; }
    }
    setInterval(refresh, 15000);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) refresh(); });
})();
