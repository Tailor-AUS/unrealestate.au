window.syndicationMap = {
    mapInstance: null,

    init: function (containerId, dotnetRef) {
        const container = document.getElementById(containerId);
        if (!container) return;

        container.innerHTML = '';
        const regions = ["Australia", "China", "USA", "UK"];
        const wrap = document.createElement('div');
        wrap.style.cssText = 'display:flex;flex-wrap:wrap;gap:8px;padding:16px;';
        regions.forEach(name => {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'custom-region-marker';
            btn.style.cssText = 'padding:8px 12px;border-radius:999px;border:1px solid #4f46e5;background:#eef2ff;color:#1e1b4b;cursor:pointer;font-weight:600;';
            btn.textContent = name;
            btn.onclick = () => {
                if (dotnetRef) dotnetRef.invokeMethodAsync('OnRegionClick', name).catch(() => {});
            };
            wrap.appendChild(btn);
        });
        container.appendChild(wrap);
    }
};
