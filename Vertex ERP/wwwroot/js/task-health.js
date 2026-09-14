(() => {
    const ring = document.getElementById('taskHealthRing');
    if (!ring) return;
    const state = document.getElementById('taskHealthLiveStatus');
    async function refresh() {
        try {
            const response = await fetch(ring.dataset.url, { cache: 'no-store', signal: AbortSignal.timeout(10000), headers: { Accept: 'application/json' } });
            if (response.status === 401 || response.status === 403 || response.redirected) {
                state.textContent = 'Session ended. Please sign in again.';
                return;
            }
            if (!response.ok) throw new Error('Refresh failed');
            const health = await response.json();
            ring.style.setProperty('--progress', health.progress);
            document.getElementById('taskHealthProgress').textContent = `${health.progress}%`;
            document.getElementById('taskHealthClosed').textContent = health.completedTasks;
            document.getElementById('taskHealthOpen').textContent = health.openTasks;
            document.getElementById('taskHealthOverdue').textContent = health.overdueTasks;
            state.textContent = 'Live · refreshes every 5 seconds';
        } catch { state.textContent = 'Update unavailable. Retrying…'; }
        window.setTimeout(refresh, 5000);
    }
    refresh();
})();
