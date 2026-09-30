(() => {
    document.querySelectorAll('[data-member-form]').forEach(form => {
        const project = form.querySelector('[data-project-select]');
        const member = form.querySelector('[data-member-select]');
        const update = () => {
            [...member.options].forEach(option => {
                if (!option.value) return;
                const allowed = (option.dataset.projects || '').split(',').includes(project.value);
                option.hidden = !allowed; option.disabled = !allowed;
                if (!allowed && option.selected) member.value = '';
            });
        };
        project.addEventListener('change', update); update();
    });
    document.querySelectorAll('[data-delete-form]').forEach(form => form.addEventListener('submit', event => {
        if (!window.confirm('Delete this record? This action cannot be undone.')) event.preventDefault();
    }));
})();
