window.globeViz = {
    globe: null,

    init: function (containerId, dotnetRef) {
        const container = document.getElementById(containerId);
        if (!container) return;

        const keyMarkets = [
            { name: "Australia (National)", listings: 3, markets: "National coverage" },
            { name: "Canberra", listings: 2, markets: "ACT" },
            { name: "Perth", listings: 2, markets: "WA" },
            { name: "Melbourne", listings: 3, markets: "VIC" },
            { name: "Brisbane (HQ)", listings: 4, markets: "QLD" }
        ];

        container.innerHTML = '';
        const list = document.createElement('div');
        list.style.cssText = 'display:grid;gap:8px;padding:16px;';
        keyMarkets.forEach(p => {
            const item = document.createElement('button');
            item.type = 'button';
            item.className = 'globe-marker';
            item.style.cssText = 'text-align:left;padding:10px 12px;border-radius:8px;border:1px solid rgba(16,185,129,0.4);background:rgba(15,23,42,0.85);color:#fff;cursor:pointer;';
            item.textContent = `${p.name} · ${p.markets}`;
            item.onclick = () => {
                if (dotnetRef) dotnetRef.invokeMethodAsync('OnRegionClick', p.name).catch(() => {});
            };
            list.appendChild(item);
        });
        container.appendChild(list);
    }
};
