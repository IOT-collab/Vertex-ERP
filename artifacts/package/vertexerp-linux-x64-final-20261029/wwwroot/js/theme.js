(() => {
    'use strict';
    const root = document.documentElement;
    const user = root.dataset.themeUser;
    if (!user) return;
    const key = 'vertex.theme.v1.' + user;
    const defaults = { mode: 'auto', background: '#f3f6fb', surface: '#ffffff', text: '#172033', accent: '#2563eb' };
    const modes = ['auto', 'light', 'dark', 'custom'];
    const colors = ['background', 'surface', 'text', 'accent'];
    const normalize = value => {
        const result = { ...defaults };
        if (value && modes.includes(value.mode)) result.mode = value.mode;
        colors.forEach(name => { if (value && /^#[0-9a-f]{6}$/i.test(value[name])) result[name] = value[name]; });
        return result;
    };
    let preference;
    try { preference = normalize(JSON.parse(localStorage.getItem(key))); }
    catch { preference = { ...defaults }; }
    const luminance = hex => {
        const rgb = hex.match(/[0-9a-f]{2}/ig).map(x => parseInt(x, 16) / 255)
            .map(x => x <= .04045 ? x / 12.92 : ((x + .055) / 1.055) ** 2.4);
        return rgb[0] * .2126 + rgb[1] * .7152 + rgb[2] * .0722;
    };
    const contrast = (a, b) => (Math.max(luminance(a), luminance(b)) + .05) / (Math.min(luminance(a), luminance(b)) + .05);
    const apply = () => {
        const hour = new Date().getHours();
        const mode = preference.mode === 'auto' ? (hour >= 6 && hour < 18 ? 'light' : 'dark') : preference.mode;
        const palette = mode === 'custom' ? preference : mode === 'light' ? defaults :
            { background: '#07111f', surface: '#0f172a', text: '#f1f5f9', accent: '#60a5fa' };
        const dark = luminance(palette.surface) < .18;
        const muted = mode === 'custom' ? palette.text : dark ? '#a8b3cf' : '#52627a';
        root.dataset.theme = mode;
        root.dataset.themeTone = dark ? 'dark' : 'light';
        root.style.colorScheme = dark ? 'dark' : 'light';
        const values = {
            'bg': palette.background, 'bg-1': palette.background, 'bg-2': palette.background, 'bg-3': palette.background,
            'surface': palette.surface, 'surface-soft': mode === 'custom' ? palette.surface : dark ? '#162238' : '#f1f5f9',
            'text': palette.text, 'text-soft': palette.text, 'text-muted': muted, 'primary': palette.accent, 'primary-hover': palette.accent,
            'border': dark ? '#29374e' : '#dce4ef',
            'success': mode === 'custom' ? palette.text : dark ? '#86efac' : '#15803d',
            'danger': mode === 'custom' ? palette.text : dark ? '#fda4af' : '#b91c1c',
            'warning': mode === 'custom' ? palette.text : dark ? '#fcd34d' : '#92400e',
            'glass': palette.surface, 'glass-strong': palette.surface, 'glass-border': dark ? '#29374e' : '#dce4ef',
            'shadow': dark ? '0 8px 28px rgba(0,0,0,.18)' : '0 4px 20px rgba(15,23,42,.045)',
            'shadow-soft': dark ? '0 8px 28px rgba(0,0,0,.18)' : '0 4px 20px rgba(15,23,42,.045)'
        };
        Object.entries(values).forEach(([name, value]) => root.style.setProperty('--erp-' + name, value));
        root.style.setProperty('--theme-accent-text', luminance(palette.accent) > .179 ? '#000000' : '#ffffff');
        root.style.setProperty('--app-bg', palette.background);
        root.style.setProperty('--app-surface', palette.surface);
        root.style.setProperty('--app-text', palette.text);
        root.style.setProperty('--app-text-soft', palette.text);
        root.style.setProperty('--app-muted', muted);
        root.style.setProperty('--app-border', values.border);
    };
    apply();
    // Re-evaluate at the time boundary, after sleep, and when returning to a tab.
    setInterval(apply, 15000);
    document.addEventListener('visibilitychange', apply);
    window.addEventListener('focus', apply);
    window.addEventListener('storage', event => {
        if (event.key !== key && event.key !== null) return;
        try { preference = normalize(JSON.parse(event.newValue)); } catch { preference = { ...defaults }; }
        apply();
        fillForm();
    });
    function fillForm() {
        const form = document.getElementById('theme-form');
        if (!form) return;
        form.elements.mode.value = preference.mode;
        colors.forEach(name => { form.elements[name].value = preference[name]; });
        document.getElementById('theme-custom').disabled = preference.mode !== 'custom';
    }
    document.addEventListener('DOMContentLoaded', () => {
        const form = document.getElementById('theme-form');
        if (!form) return;
        const status = document.getElementById('theme-status');
        fillForm();
        form.addEventListener('input', () => {
            preference = normalize({ ...preference, ...Object.fromEntries(new FormData(form)) });
            apply();
            document.getElementById('theme-custom').disabled = preference.mode !== 'custom';
            status.textContent = 'Preview — save to keep this theme.';
        });
        form.addEventListener('submit', event => {
            event.preventDefault();
            if (preference.mode === 'custom' &&
                Math.min(contrast(preference.text, preference.background), contrast(preference.text, preference.surface)) < 4.5) {
                status.textContent = 'Please choose text with stronger contrast against both background and panels so your workspace stays readable.';
                return;
            }
            try {
                localStorage.setItem(key, JSON.stringify(preference));
                status.textContent = 'Theme saved for your account in this browser.';
            } catch { status.textContent = 'Browser storage is unavailable. The preview works, but your theme could not be saved.'; }
        });
        document.getElementById('theme-reset').addEventListener('click', () => {
            preference = { ...defaults };
            apply();
            fillForm();
            status.textContent = 'Auto preview — select Save theme to keep it.';
        });
    });
})();
