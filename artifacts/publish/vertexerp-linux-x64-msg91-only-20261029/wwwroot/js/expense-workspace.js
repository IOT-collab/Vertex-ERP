(() => {
    const file = document.getElementById('billFile');
    const image = document.getElementById('expenseImagePreview');
    const pdf = document.getElementById('expensePdfPreview');
    const error = document.getElementById('expenseFileError');
    let objectUrl;
    file?.addEventListener('change', () => {
        if (objectUrl) URL.revokeObjectURL(objectUrl);
        image.hidden = true; pdf.hidden = true; error.hidden = true; file.setCustomValidity('');
        const selected = file.files?.[0];
        document.getElementById('expenseFileName').textContent = selected?.name || 'No file selected';
        if (!selected) return;
        if (selected.size > 10 * 1024 * 1024 || !/\.(pdf|jpe?g|png)$/i.test(selected.name)) {
            error.textContent = 'Choose a PDF, JPG or PNG file up to 10 MB.'; error.hidden = false;
            file.setCustomValidity(error.textContent); return;
        }
        objectUrl = URL.createObjectURL(selected);
        if (/\.pdf$/i.test(selected.name)) { pdf.href = objectUrl; pdf.hidden = false; }
        else { image.src = objectUrl; image.hidden = false; }
    });
    document.querySelectorAll('.expense-final-response-form').forEach(form => {
        const input = form.querySelector('[data-response-file]');
        const image = form.querySelector('[data-response-image]');
        const pdf = form.querySelector('[data-response-pdf]');
        const error = form.querySelector('[data-response-error]');
        let url;
        input.addEventListener('change', () => {
            if (url) URL.revokeObjectURL(url);
            image.hidden = true; pdf.hidden = true; error.hidden = true; input.setCustomValidity('');
            const selected = input.files?.[0];
            form.querySelector('[data-response-name]').textContent = selected?.name || 'PDF, JPG or PNG · Maximum 10 MB';
            if (!selected) return;
            if (selected.size > 10 * 1024 * 1024 || !/\.(pdf|jpe?g|png)$/i.test(selected.name)) {
                error.textContent = 'Choose a PDF, JPG or PNG file up to 10 MB.'; error.hidden = false;
                input.setCustomValidity(error.textContent); return;
            }
            url = URL.createObjectURL(selected);
            if (/\.pdf$/i.test(selected.name)) { pdf.href = url; pdf.hidden = false; }
            else { image.src = url; image.hidden = false; }
        });
    });
    const search = document.getElementById('expSearchInput');
    const status = document.getElementById('statusFilter');
    function filter() {
        let shown = 0;
        document.querySelectorAll('[data-expense-status]').forEach(card => {
            card.hidden = !((status.value === 'All' || card.dataset.expenseStatus === status.value)
                && card.textContent.toLowerCase().includes(search.value.trim().toLowerCase()));
            if (!card.hidden) shown++;
        });
        document.getElementById('expenseNoMatches').hidden = shown > 0 || !document.querySelector('[data-expense-status]');
    }
    search?.addEventListener('input', filter); status?.addEventListener('change', filter);
    document.querySelectorAll('.expense-review-form').forEach(form => {
        form.noValidate = true;
        form.addEventListener('submit', event => {
        const note = form.querySelector('[name="note"]');
        note.setCustomValidity(event.submitter?.value === 'Rejected' && !note.value.trim() ? 'Enter a reason for rejection.' : '');
        if (!form.reportValidity()) event.preventDefault();
    });
    });
})();
