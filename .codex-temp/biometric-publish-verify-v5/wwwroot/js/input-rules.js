(() => {
    const digitsOnly = value => value.replace(/[^0-9]/g, '');

    document.querySelectorAll('input').forEach(input => {
        const identity = `${input.name || ''} ${input.id || ''}`;

        if (/(phone|mobile|emergencycontact)/i.test(identity) && !input.disabled) {
            input.type = 'tel';
            input.inputMode = 'numeric';
            input.maxLength = 10;
            input.pattern = '[0-9]{10}';
            input.addEventListener('input', () => { input.value = digitsOnly(input.value).slice(0, 10); });
        }

        if (/(bankaccountnumber|accountnumber)/i.test(identity) && !input.disabled) {
            input.inputMode = 'numeric';
            input.maxLength = 18;
            input.pattern = '[0-9]{8,18}';
            input.addEventListener('input', () => { input.value = digitsOnly(input.value).slice(0, 18); });
        }
    });
})();
