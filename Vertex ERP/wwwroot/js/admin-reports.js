(() => {
    const type = document.getElementById('reportType');
    const status = document.getElementById('status');
    const fields = document.querySelectorAll('[data-filter]');
    function update(initial) {
        const t = type.value;
        const department = t.startsWith('department-');
        const attendance = t.includes('attendance') || t === 'punch-report';
        const person = ['employee-list', 'employee-designation'].includes(t);
        const task = ['employee-tasks', 'manager-projects'].includes(t);
        const project = t === 'project-details';
        const leave = t === 'leave-report';
        const manager = t === 'manager-report';
        const summary = t === 'attendance-summary';
        const show = new Set(t ? ['department'] : []);
        if (person || task || attendance || leave) show.add('employee');
        if (person || task || attendance || leave || project || manager) show.add('manager');
        if (project) show.add('project');
        if (t === 'daily-attendance') show.add('date');
        else if (t === 'monthly-attendance') show.add('month');
        else if (attendance || task || project || leave || manager) show.add('range');
        let values = person ? ['Active', 'Inactive'] : project ? ['Planning', 'Active', 'On Hold', 'Completed', 'Cancelled'] : leave ? ['Pending', 'Approved', 'Rejected'] : task ? ['To Do', 'In Progress', 'Completed'] : attendance && !summary ? ['Present', 'Absent', 'Late', 'Weekly Off', 'Leave'] : [];
        if (values.length) show.add('status');
        fields.forEach(field => {
            const visible = show.has(field.dataset.filter);
            field.hidden = !visible;
            field.querySelectorAll('input,select').forEach(input => { input.disabled = !visible; if (!initial && !visible && input.tagName === 'SELECT') input.value = ''; });
        });
        status.replaceChildren(new Option('All statuses', ''));
        values.forEach(value => status.add(new Option(value, value)));
        if (initial) status.value = status.dataset.selected || '';
        document.getElementById('reportHelp').textContent = project ? 'Projects overlapping the selected dates. Save projects in Project Management → Project Creation.' : manager ? 'Team size is current. Projects and pending leaves overlap the period; tasks are filtered by due date.' : task ? 'Tasks with a due date in this period.' : leave ? 'Leave requests overlapping this period.' : attendance ? 'Choose a day, month or range. Future days and days before joining are not marked absent.' : 'Select filters, then preview. Downloads use the filters shown in the preview.';
    }
    type.addEventListener('change', () => update(false));
    update(true);
    document.getElementById('reportsForm').addEventListener('submit', event => {
        const from = document.getElementById('fromDate'), to = document.getElementById('toDate');
        to.setCustomValidity('');
        if (!from.disabled && from.value && to.value && (to.value < from.value || (Date.parse(to.value) - Date.parse(from.value)) / 86400000 > 365)) {
            event.preventDefault(); to.setCustomValidity('Choose an ordered date range of 366 days or less.'); to.reportValidity();
        }
    });
})();
