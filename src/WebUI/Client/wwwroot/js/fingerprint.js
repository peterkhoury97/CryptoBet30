// Device Fingerprinting for Fraud Detection
// Generates a unique fingerprint based on browser/device characteristics

window.deviceFingerprint = (function() {
    function getFingerprint() {
        const components = [];

        // Screen resolution
        components.push(screen.width + 'x' + screen.height);
        components.push(screen.colorDepth);

        // Timezone
        components.push(new Date().getTimezoneOffset());

        // Language
        components.push(navigator.language || navigator.userLanguage);

        // Platform
        components.push(navigator.platform);

        // User agent
        components.push(navigator.userAgent);

        // Hardware concurrency (CPU cores)
        components.push(navigator.hardwareConcurrency || 0);

        // Device memory (GB)
        components.push(navigator.deviceMemory || 0);

        // Touch support
        components.push('ontouchstart' in window || navigator.maxTouchPoints > 0);

        // Canvas fingerprint (more unique)
        try {
            const canvas = document.createElement('canvas');
            const ctx = canvas.getContext('2d');
            const txt = 'CryptoBet30';
            ctx.textBaseline = 'top';
            ctx.font = '14px Arial';
            ctx.textBaseline = 'alphabetic';
            ctx.fillStyle = '#f60';
            ctx.fillRect(125, 1, 62, 20);
            ctx.fillStyle = '#069';
            ctx.fillText(txt, 2, 15);
            ctx.fillStyle = 'rgba(102, 204, 0, 0.7)';
            ctx.fillText(txt, 4, 17);
            components.push(canvas.toDataURL());
        } catch (e) {
            components.push('canvas-error');
        }

        // WebGL vendor/renderer
        try {
            const gl = document.createElement('canvas').getContext('webgl');
            const debugInfo = gl.getExtension('WEBGL_debug_renderer_info');
            components.push(gl.getParameter(debugInfo.UNMASKED_VENDOR_WEBGL));
            components.push(gl.getParameter(debugInfo.UNMASKED_RENDERER_WEBGL));
        } catch (e) {
            components.push('webgl-error');
        }

        // Plugins (deprecated in modern browsers but still useful)
        if (navigator.plugins) {
            const plugins = [];
            for (let i = 0; i < navigator.plugins.length; i++) {
                plugins.push(navigator.plugins[i].name);
            }
            components.push(plugins.join(','));
        }

        // Battery API (if available)
        if (navigator.getBattery) {
            navigator.getBattery().then(function(battery) {
                components.push(battery.level);
                components.push(battery.charging);
            });
        }

        // Join all components and hash
        const fingerprint = components.join('|');
        return hashString(fingerprint);
    }

    function hashString(str) {
        let hash = 0;
        if (str.length === 0) return hash.toString();
        
        for (let i = 0; i < str.length; i++) {
            const char = str.charCodeAt(i);
            hash = ((hash << 5) - hash) + char;
            hash = hash & hash; // Convert to 32bit integer
        }
        
        return Math.abs(hash).toString(16);
    }

    function setFingerprintHeader() {
        const fingerprint = getFingerprint();
        
        // Store in session storage
        sessionStorage.setItem('deviceFingerprint', fingerprint);
        
        return fingerprint;
    }

    function getStoredFingerprint() {
        let fingerprint = sessionStorage.getItem('deviceFingerprint');
        if (!fingerprint) {
            fingerprint = setFingerprintHeader();
        }
        return fingerprint;
    }

    // Auto-attach to all HTTP requests
    function attachToFetch() {
        const originalFetch = window.fetch;
        window.fetch = function(...args) {
            const [url, options = {}] = args;
            
            // Add fingerprint header to all API requests
            if (!options.headers) {
                options.headers = {};
            }
            
            options.headers['X-Device-Fingerprint'] = getStoredFingerprint();
            
            return originalFetch(url, options);
        };
    }

    // Initialize on load
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function() {
            setFingerprintHeader();
            attachToFetch();
        });
    } else {
        setFingerprintHeader();
        attachToFetch();
    }

    return {
        get: getStoredFingerprint,
        refresh: setFingerprintHeader
    };
})();
