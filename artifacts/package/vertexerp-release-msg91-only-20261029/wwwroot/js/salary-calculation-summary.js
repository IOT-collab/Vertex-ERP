(() => {
    const summary = document.querySelector('[data-salary-calculation]');
    if (!summary) return;
    const form = summary.closest('form');
    const earnings = ['BasicSalary', 'HouseRentAllowance', 'ConveyanceAllowance', 'SpecialAllowance'];
    const deductions = ['ProvidentFund', 'ProfessionalTax', 'Tds', 'OtherDeductions', 'LeaveDeduction'];
    const money = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' });
    const read = field => {
        const input = form.querySelector(`[name="Values[${field}]"]`);
        return input && input.value !== '' && input.validity.valid ? Number(input.value) : NaN;
    };
    const round = value => Math.round((value + Number.EPSILON) * 100) / 100;
    function update() {
        const days = read('SalaryDays');
        const calendar = Number(summary.dataset.calendarDays);
        const master = earnings.reduce((sum, field) => sum + read(field), 0);
        const actual = days >= 0 && days <= calendar
            ? earnings.reduce((sum, field) => sum + round(read(field) * days / calendar), 0) : NaN;
        const deducted = deductions.reduce((sum, field) => sum + read(field), 0);
        for (const [name, amount] of Object.entries({ master, actual, deductions: deducted, net: actual - deducted })) {
            const target = summary.querySelector(`[data-salary-total="${name}"]`);
            target.textContent = Number.isFinite(amount) ? money.format(amount) : '—';
            target.setAttribute('data-no-localize', '');
        }
    }
    form.addEventListener('input', update);
    update();
})();
