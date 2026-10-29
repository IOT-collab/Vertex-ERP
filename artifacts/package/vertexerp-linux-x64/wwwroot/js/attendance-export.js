(() => {
    const form = document.getElementById('attendanceExportForm');
    if (!form) return;
    const button = form.querySelector('button[type="submit"]');
    const message = document.getElementById('attendanceExportMessage');
    const original = button.innerHTML;
    let busy = false;
    function reset() { busy = false; button.disabled = false; button.innerHTML = original; }
    // Let the browser process the authenticated attachment response directly.
    // No fetch/blob request: network/CORS restrictions cannot block an AJAX download.
    form.method = 'get';
    form.target = '_self';
    const selected = form.querySelector('input[name="exportPeriod"]:checked');
    if (selected && typeof window.setExportPeriod === 'function') window.setExportPeriod(selected.value);
    form.addEventListener('submit', event => {
        if (busy) { event.preventDefault(); return; }
        const data = new FormData(form);
        const period = data.get('exportPeriod');
        if (!form.reportValidity() || !(period === 'month' ? data.get('exportMonth') : data.get('filterDate'))) {
            event.preventDefault();
            message.hidden = false; message.textContent = 'Select a valid date or month.'; return;
        }
        busy = true;
        message.hidden = false;
        message.textContent = 'Preparing your report. The browser will download the file when it is ready.';
        button.textContent = 'Preparing download...';
        // Do not cancel the native form submission or claim the file has saved yet.
        setTimeout(reset, 15000);
    });
    window.addEventListener('pageshow', reset);
})();
