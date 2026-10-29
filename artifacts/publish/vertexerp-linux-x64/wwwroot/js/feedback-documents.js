(() => {
    document.querySelectorAll('[data-feedback-upload]').forEach(form => {
        const input = form.querySelector('input[type="file"]');
        const list = form.querySelector('[data-file-list]');
        const error = form.querySelector('[data-file-error]');
        const button = form.querySelector('button[type="submit"]');
        const original = button.textContent;
        function validate() {
            input.setCustomValidity(''); error.hidden = true; list.replaceChildren();
            const files = Array.from(input.files || []);
            files.forEach(file => { const item = document.createElement('li'); item.textContent = `${file.name} · ${(file.size / 1024 / 1024).toFixed(2)} MB`; list.appendChild(item); });
            let problem = '';
            if (!files.length) problem = 'Choose at least one PDF or image.';
            else if (files.length > 5) problem = 'Upload up to five files at a time.';
            else if (files.some(file => file.size <= 0 || file.size > 10 * 1024 * 1024 || !/\.(pdf|jpe?g|png)$/i.test(file.name))) problem = 'Each PDF, JPG or PNG must be non-empty and 10 MB or smaller.';
            else if (files.reduce((sum, file) => sum + file.size, 0) > 25 * 1024 * 1024) problem = 'Combined file size must be 25 MB or smaller.';
            if (problem) { input.setCustomValidity(problem); error.textContent = problem; error.hidden = false; }
            return !problem;
        }
        input.addEventListener('change', validate);
        form.addEventListener('submit', event => {
            if (!validate() || !form.reportValidity()) { event.preventDefault(); form.reportValidity(); return; }
            button.disabled = true; button.textContent = 'Uploading...';
        });
        window.addEventListener('pageshow', () => { button.disabled = false; button.textContent = original; });
    });
})();
