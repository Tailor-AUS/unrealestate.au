(function () {
    function clamp(v, lo, hi) { return Math.max(lo, Math.min(hi, v)); }

    function attach(el) {
        if (!el || el.dataset.baInit === '1') return;
        el.dataset.baInit = '1';

        let dragging = false;

        const setFromClientX = (clientX) => {
            const rect = el.getBoundingClientRect();
            if (rect.width <= 0) return;
            const pct = clamp(((clientX - rect.left) / rect.width) * 100, 0, 100);
            el.style.setProperty('--ba-pos', pct + '%');
        };

        const onPointerDown = (e) => {
            dragging = true;
            try { el.setPointerCapture(e.pointerId); } catch { }
            setFromClientX(e.clientX);
            e.preventDefault();
        };
        const onPointerMove = (e) => {
            if (!dragging) return;
            setFromClientX(e.clientX);
        };
        const onPointerUp = (e) => {
            dragging = false;
            try { el.releasePointerCapture(e.pointerId); } catch { }
        };
        const onKeyDown = (e) => {
            const cur = parseFloat((el.style.getPropertyValue('--ba-pos') || '50%')) || 50;
            let next = cur;
            if (e.key === 'ArrowLeft') next = clamp(cur - 5, 0, 100);
            else if (e.key === 'ArrowRight') next = clamp(cur + 5, 0, 100);
            else if (e.key === 'Home') next = 0;
            else if (e.key === 'End') next = 100;
            else return;
            el.style.setProperty('--ba-pos', next + '%');
            e.preventDefault();
        };

        el.addEventListener('pointerdown', onPointerDown);
        el.addEventListener('pointermove', onPointerMove);
        el.addEventListener('pointerup', onPointerUp);
        el.addEventListener('pointercancel', onPointerUp);
        el.addEventListener('keydown', onKeyDown);
    }

    function attachAll(root) {
        const scope = root && root.querySelectorAll ? root : document;
        scope.querySelectorAll('.ba-slider').forEach(attach);
    }

    window.beforeAfterSlider = {
        init: function (idOrEl) {
            const el = typeof idOrEl === 'string' ? document.getElementById(idOrEl) : idOrEl;
            attach(el);
        },
        initAll: attachAll
    };

    // Auto-attach on initial load and after Blazor enhanced navigation.
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => attachAll(document));
    } else {
        attachAll(document);
    }
    document.addEventListener('enhancedload', () => attachAll(document));
})();
