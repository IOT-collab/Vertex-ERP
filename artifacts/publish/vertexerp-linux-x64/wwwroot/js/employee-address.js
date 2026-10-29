(() => {
    const checkbox = document.getElementById('PermanentAddressSameAsPresent');
    if (!checkbox) return;
    const pairs = ['Address', 'City', 'State', 'PinCode'].map(name => [document.getElementById(name), document.getElementById('Permanent' + name)]);
    const sync = () => pairs.forEach(([present, permanent]) => {
        if (checkbox.checked) permanent.value = present.value;
        permanent.readOnly = checkbox.checked;
    });
    checkbox.addEventListener('change', sync);
    pairs.forEach(([present]) => present.addEventListener('input', sync));
    checkbox.form.addEventListener('submit', sync);
    sync();
})();
