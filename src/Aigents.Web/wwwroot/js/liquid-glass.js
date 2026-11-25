/* unrealestate.au — Apple Liquid Glass runtime (auto light/dark + pointer specular + zero-signup buyer shortlist) */
(function () {
    const THEME_KEY = 'ue_glass_theme';
    const SHORTLIST_KEY = 'ue_buyer_shortlist_v1';
    const media = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;
    const shortlistSubscribers = new Map();

    function resolveEffective(mode) {
        if (mode === 'light' || mode === 'dark') return mode;
        return media && media.matches ? 'dark' : 'light';
    }

    function applyTheme(mode) {
        const clean = (mode === 'light' || mode === 'dark') ? mode : 'auto';
        const root = document.documentElement;
        if (clean === 'auto') {
            root.removeAttribute('data-theme');
        } else {
            root.setAttribute('data-theme', clean);
        }
        root.setAttribute('data-theme-mode', clean);

        const effective = resolveEffective(clean);
        let meta = document.querySelector('meta[name="theme-color"][data-dynamic="1"]');
        if (!meta) {
            meta = document.createElement('meta');
            meta.setAttribute('name', 'theme-color');
            meta.setAttribute('data-dynamic', '1');
            document.head.appendChild(meta);
        }
        meta.setAttribute('content', effective === 'dark' ? '#070b13' : '#edf1f7');
        return clean;
    }

    function getThemeMode() {
        try {
            const saved = localStorage.getItem(THEME_KEY);
            return (saved === 'light' || saved === 'dark') ? saved : 'auto';
        } catch {
            return 'auto';
        }
    }

    function setThemeMode(mode) {
        const clean = applyTheme(mode);
        try { localStorage.setItem(THEME_KEY, clean); } catch { }
        window.dispatchEvent(new CustomEvent('ue-theme-changed', { detail: { mode: clean, effective: resolveEffective(clean) } }));
        return clean;
    }

    function cycleThemeMode() {
        const current = getThemeMode();
        const next = current === 'auto' ? 'light' : (current === 'light' ? 'dark' : 'auto');
        return setThemeMode(next);
    }

    function readShortlist() {
        try {
            const raw = localStorage.getItem(SHORTLIST_KEY);
            if (!raw) return [];
            const parsed = JSON.parse(raw);
            return Array.isArray(parsed) ? parsed : [];
        } catch {
            return [];
        }
    }

    function notifyShortlist(items) {
        window.dispatchEvent(new CustomEvent('ue-shortlist-changed', { detail: { count: items.length } }));
        shortlistSubscribers.forEach(ref => {
            try {
                ref.invokeMethodAsync('OnShortlistChanged').catch(() => { });
            } catch { }
        });
    }

    function writeShortlist(items) {
        const trimmed = items.slice(0, 40);
        try {
            localStorage.setItem(SHORTLIST_KEY, JSON.stringify(trimmed));
        } catch { }
        notifyShortlist(trimmed);
    }

    function toggleShortlist(item) {
        if (!item || !item.id) return false;
        const list = readShortlist();
        const idx = list.findIndex(x => x.id === item.id);
        let saved;
        if (idx >= 0) {
            list.splice(idx, 1);
            saved = false;
        } else {
            list.unshift({
                id: String(item.id),
                address: String(item.address || ''),
                suburb: String(item.suburb || ''),
                state: String(item.state || 'QLD'),
                postcode: String(item.postcode || ''),
                priceDisplay: String(item.priceDisplay || 'Contact Agent'),
                askingPrice: item.askingPrice || null,
                bedrooms: item.bedrooms ?? null,
                bathrooms: item.bathrooms ?? null,
                carSpaces: item.carSpaces ?? null,
                propertyType: String(item.propertyType || 'House'),
                savedAt: new Date().toISOString()
            });
            saved = true;
        }
        writeShortlist(list);
        return saved;
    }

    function removeShortlist(id) {
        const list = readShortlist().filter(x => x.id !== String(id));
        writeShortlist(list);
        return list;
    }

    function isShortlisted(id) {
        return readShortlist().some(x => x.id === String(id));
    }

    function subscribeShortlist(id, dotNetRef) {
        if (id && dotNetRef) shortlistSubscribers.set(String(id), dotNetRef);
    }

    function unsubscribeShortlist(id) {
        if (id) shortlistSubscribers.delete(String(id));
    }

    function copyText(text) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            return navigator.clipboard.writeText(text).then(() => true).catch(() => false);
        }
        return Promise.resolve(false);
    }

    // Batched read-then-write pointer specular sheen (zero layout thrashing)
    let rafPending = false;
    let lastX = 0;
    let lastY = 0;
    const reducedMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (!reducedMotion) {
        document.addEventListener('pointermove', (e) => {
            if (e.pointerType === 'touch') return;
            lastX = e.clientX;
            lastY = e.clientY;
            if (rafPending) return;
            rafPending = true;
            requestAnimationFrame(() => {
                rafPending = false;
                const nodes = document.querySelectorAll('.lg-surface, .lg-surface-strong, .lg-refract');
                const updates = [];
                for (let i = 0; i < nodes.length; i++) {
                    const el = nodes[i];
                    const r = el.getBoundingClientRect();
                    if (r.width === 0 || r.height === 0) continue;
                    const gx = ((lastX - r.left) / r.width) * 100;
                    const gy = ((lastY - r.top) / r.height) * 100;
                    if (gx > -25 && gx < 125 && gy > -25 && gy < 125) {
                        updates.push([el, gx.toFixed(1) + '%', gy.toFixed(1) + '%']);
                    }
                }
                for (let i = 0; i < updates.length; i++) {
                    const [el, gx, gy] = updates[i];
                    el.style.setProperty('--gx', gx);
                    el.style.setProperty('--gy', gy);
                }
            });
        }, { passive: true });
    }

    if (media && media.addEventListener) {
        media.addEventListener('change', () => {
            if (getThemeMode() === 'auto') applyTheme('auto');
        });
    }

    applyTheme(getThemeMode());

    window.ueGlass = {
        getThemeMode,
        setThemeMode,
        cycleThemeMode,
        getShortlist: readShortlist,
        toggleShortlist,
        removeShortlist,
        isShortlisted,
        subscribeShortlist,
        unsubscribeShortlist,
        copyText
    };
})();
