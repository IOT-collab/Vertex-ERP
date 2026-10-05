(() => {
    const dialog = document.getElementById('dashboardCalendar');
    const trigger = document.getElementById('dashboardCalendarTrigger');
    if (!dialog || !trigger) return;
    const holidays = JSON.parse(document.getElementById('dc-holidays').textContent);
    const [year, month, day] = dialog.dataset.today.split('-').map(Number);
    const today = new Date(year, month - 1, day);
    let current = new Date(year, month - 1, 1);
    let previousOverflow;
    const key = date => `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
    const label = new Intl.DateTimeFormat('en-IN', { dateStyle: 'full' });
    function render() {
        document.getElementById('dc-month').textContent = current.toLocaleDateString('en-IN', { month: 'long', year: 'numeric' });
        const grid = document.getElementById('dc-grid');
        grid.replaceChildren();
        const days = new Date(current.getFullYear(), current.getMonth() + 1, 0).getDate();
        const cells = Math.ceil((current.getDay() + days) / 7) * 7;
        for (let index = 0; index < cells; index++) {
            const date = new Date(current.getFullYear(), current.getMonth(), index - current.getDay() + 1);
            const dateKey = key(date);
            const holiday = holidays.find(h => h.start <= dateKey && h.end >= dateKey);
            const cell = document.createElement('button');
            cell.type = 'button';
            cell.className = 'dc-day';
            cell.classList.toggle('dc-outside', date.getMonth() !== current.getMonth());
            cell.classList.toggle('dc-holiday', !!holiday);
            if (dateKey === key(today)) { cell.classList.add('dc-current'); cell.setAttribute('aria-current', 'date'); }
            cell.setAttribute('aria-label', label.format(date) + (holiday ? `, ${holiday.name}` : ''));
            const number = document.createElement('span');
            number.textContent = date.getDate();
            cell.append(number);
            if (holiday) { const name = document.createElement('small'); name.textContent = holiday.name; cell.append(name); }
            cell.addEventListener('click', () => {
                grid.querySelectorAll('.dc-selected').forEach(item => { item.classList.remove('dc-selected'); item.removeAttribute('aria-pressed'); });
                cell.classList.add('dc-selected');
                cell.setAttribute('aria-pressed', 'true');
                document.getElementById('dc-description').textContent = label.format(date) + (holiday ? ` · ${holiday.name}` : ' · No company holiday listed');
            });
            grid.append(cell);
        }
        document.getElementById('dc-description').textContent = current.getFullYear() === 2026
            ? 'Select a date to see its details.'
            : 'Company holiday schedule is available for 2026 only.';
    }
    function open() {
        if (dialog.open) return;
        current = new Date(year, month - 1, 1);
        render();
        previousOverflow = document.body.style.overflow;
        document.body.style.overflow = 'hidden';
        dialog.showModal();
        document.getElementById('dc-close').focus();
    }
    trigger.addEventListener('click', open);
    trigger.addEventListener('keydown', event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); open(); } });
    document.getElementById('dc-close').addEventListener('click', () => dialog.close());
    dialog.addEventListener('close', () => { document.body.style.overflow = previousOverflow; trigger.focus({ preventScroll: true }); });
    document.getElementById('dc-prev').addEventListener('click', () => { current.setMonth(current.getMonth() - 1); render(); });
    document.getElementById('dc-next').addEventListener('click', () => { current.setMonth(current.getMonth() + 1); render(); });
    document.getElementById('dc-today').addEventListener('click', () => { current = new Date(year, month - 1, 1); render(); });
})();
