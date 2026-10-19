/**
 * Property Visualization & Local Address Helper
 * Self-contained local implementation without third-party map SDK egress.
 */

window.addressAutocomplete = {
    init: async function (inputElement, dotNetRef) {
        if (!inputElement || !dotNetRef) return;
        inputElement.addEventListener("change", () => {
            const raw = (inputElement.value || "").trim();
            dotNetRef.invokeMethodAsync("OnPlaceChanged", {
                fullAddress: raw,
                streetNumber: "",
                route: raw,
                suburb: "",
                state: "",
                postcode: "",
                lat: null,
                lng: null
            }).catch(() => {});
        });
    }
};

window.propertyViz = {
    panorama: null,
    map: null,
    isOrbiting: false,
    animationId: null,
    heading: 0,
    currentMode: null,

    initCinematicView: async function (containerId) {
        const container = document.getElementById(containerId);
        if (!container) return;
        this.currentMode = 'local-preview';
    },

    startOrbit: function () {
        this.isOrbiting = true;
    },

    stopOrbit: function () {
        this.isOrbiting = false;
        if (this.animationId) {
            cancelAnimationFrame(this.animationId);
            this.animationId = null;
        }
    },

    updateRotation: function (heading) {
        this.stopOrbit();
        this.heading = parseFloat(heading) || 0;
    },

    updateTilt: function () {},

    initRipple: function () {
        const canvas = document.getElementById('ripple-canvas');
        if (!canvas) return;

        const ctx = canvas.getContext('2d');
        let ripples = [];
        let mouseX = 0, mouseY = 0;
        let lastRippleTime = 0;

        function resize() {
            canvas.width = window.innerWidth;
            canvas.height = window.innerHeight;
        }
        resize();
        window.addEventListener('resize', resize);

        function easeOutCubic(t) {
            return 1 - Math.pow(1 - t, 3);
        }

        document.addEventListener('mousemove', (e) => {
            mouseX = e.clientX;
            mouseY = e.clientY;

            const now = Date.now();
            if (now - lastRippleTime > 120) {
                ripples.push({
                    x: mouseX,
                    y: mouseY,
                    progress: 0,
                    maxRadius: 150 + Math.random() * 50
                });
                lastRippleTime = now;
            }

            if (ripples.length > 8) ripples.shift();
        });

        function draw() {
            ctx.clearRect(0, 0, canvas.width, canvas.height);

            ripples.forEach((ripple) => {
                ripple.progress += 0.006;
                const eased = easeOutCubic(ripple.progress);
                const radius = ripple.maxRadius * eased;
                const opacity = 0.06 * (1 - eased);

                if (opacity > 0.001) {
                    ctx.beginPath();
                    ctx.arc(ripple.x, ripple.y, radius, 0, Math.PI * 2);
                    ctx.strokeStyle = `rgba(16, 185, 129, ${opacity})`;
                    ctx.lineWidth = 1;
                    ctx.stroke();
                }
            });

            ripples = ripples.filter(r => r.progress < 1);
            requestAnimationFrame(draw);
        }
        draw();
    }
};
