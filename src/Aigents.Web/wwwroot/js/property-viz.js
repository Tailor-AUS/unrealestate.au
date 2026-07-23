/**
 * Property Visualization & 3D Drone Orbit Logic
 */

window.addressAutocomplete = {
    waitForGoogleMaps: async function () {
        if (window.google?.maps?.importLibrary) {
            return true;
        }

        const loaded = await (window.googleMapsReady ?? Promise.resolve(false));
        return loaded === true && !!window.google?.maps?.importLibrary;
    },

    init: async function (inputElement, dotNetRef) {
        if (!inputElement?.isConnected || !await this.waitForGoogleMaps()) {
            console.warn("Google Maps is unavailable; address autocomplete remains a plain text field.");
            return false;
        }

        // Modern dynamic library loading
        const { Autocomplete } = await google.maps.importLibrary("places");

        const autocomplete = new Autocomplete(inputElement, {
            componentRestrictions: { country: "au" },
            fields: ["address_components", "geometry", "formatted_address"],
            types: ["address"]
        });

        autocomplete.addListener("place_changed", () => {
            const place = autocomplete.getPlace();
            if (!place.geometry || !place.geometry.location) {
                return;
            }

            const result = {
                fullAddress: place.formatted_address,
                streetNumber: "",
                route: "",
                suburb: "",
                state: "",
                postcode: "",
                lat: place.geometry.location.lat(),
                lng: place.geometry.location.lng()
            };

            place.address_components.forEach(component => {
                const types = component.types;
                if (types.includes("street_number")) result.streetNumber = component.long_name;
                if (types.includes("route")) result.route = component.long_name;
                if (types.includes("locality")) result.suburb = component.long_name;
                if (types.includes("administrative_area_level_1")) result.state = component.short_name;
                if (types.includes("postal_code")) result.postcode = component.long_name;
            });

            dotNetRef.invokeMethodAsync("OnPlaceChanged", result);
        });

        return true;
    }
};

window.propertyViz = {
    panorama: null,
    map: null,
    isOrbiting: false,
    animationId: null,
    heading: 0,
    currentMode: null, // 'streetview' or 'satellite'

    initCinematicView: async function (containerId, lat, lng) {
        if (!await window.addressAutocomplete.waitForGoogleMaps()) {
            console.warn("Google Maps is unavailable; cinematic property view was skipped.");
            return false;
        }

        const { StreetViewService, StreetViewPanorama, StreetViewStatus } = await google.maps.importLibrary("streetView");
        const { Map } = await google.maps.importLibrary("maps");
        const container = document.getElementById(containerId);

        // 1. Try to find a Street View Panorama first
        const svService = new StreetViewService();

        svService.getPanorama({ location: { lat: lat, lng: lng }, radius: 50 }, (data, status) => {
            if (status === StreetViewStatus.OK) {
                // --- STREET VIEW MODE ---
                this.currentMode = 'streetview';
                console.log("Initializing Cinematic Street View");

                this.panorama = new StreetViewPanorama(container, {
                    pano: data.location.pano,
                    pov: { heading: 0, pitch: 0 },
                    zoom: 0,
                    disableDefaultUI: true,
                    showRoadLabels: false,
                    clickToGo: false,
                    addressControl: false,
                    fullscreenControl: false,
                    linksControl: false,
                    panControl: false,
                    enableCloseButton: false
                });

                this.startOrbit();

            } else {
                // --- SATELLITE MAP FALLBACK ---
                this.currentMode = 'satellite';
                console.log("Street View not found. Fallback to Satellite Orbit.");

                this.map = new Map(container, {
                    center: { lat: lat, lng: lng },
                    zoom: 19,
                    mapId: "DEMO_MAP_ID", // Try vector if possible, else standard
                    mapTypeId: 'satellite',
                    tilt: 45, // Try 45 map if available
                    disableDefaultUI: true,
                    gestureHandling: 'none'
                });

                this.heading = 0;
                this.startOrbit();
            }
        });
    },

    startOrbit: function () {
        if (this.isOrbiting) return;
        this.isOrbiting = true;

        const animate = () => {
            if (!this.isOrbiting) return;

            if (this.currentMode === 'streetview' && this.panorama) {
                // Rotate Street View Camera
                this.heading = (this.heading + 0.08) % 360; // Slower, smoother rotation
                this.panorama.setPov({
                    heading: this.heading,
                    pitch: 5 // Slight upward tilt
                });
            } else if (this.currentMode === 'satellite' && this.map) {
                // Rotate Satellite Map
                this.heading = (this.heading + 0.15) % 360;
                this.map.moveCamera({
                    heading: this.heading,
                    tilt: 45
                });
            }

            this.animationId = requestAnimationFrame(animate);
        };

        animate();
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
        this.heading = parseFloat(heading);

        if (this.currentMode === 'streetview' && this.panorama) {
            this.panorama.setPov({ heading: this.heading, pitch: 5 });
        } else if (this.currentMode === 'satellite' && this.map) {
            this.map.moveCamera({ heading: this.heading });
        }
    },

    updateTilt: function (tilt) {
        // Only applicable for satellite/vector map really, or pitch for SV
    },

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
