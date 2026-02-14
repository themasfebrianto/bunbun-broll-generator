/**
 * Robust Blazor Server reconnection handling for mobile
 * Provides custom UI and auto-reload functionality
 */
(function BlazorReconnectHandler() {
    'use strict';

    // Configuration
    const CONFIG = {
        maxReconnectAttempts: 30,
        retryIntervalMs: 2000,
        autoReloadDelaySec: 5,
        visibilityCheckDelayMs: 5000,
        backoffBaseMs: 1000,
        backoffMaxMs: 30000,
        backoffExponent: 1.5
    };

    // State
    let reconnectAttempts = 0;
    let isReconnecting = false;
    let reconnectTimer = null;

    /**
     * Calculate backoff delay with exponential increase
     */
    function getBackoffDelay(attempt) {
        return Math.min(
            CONFIG.backoffBaseMs * Math.pow(CONFIG.backoffExponent, attempt),
            CONFIG.backoffMaxMs
        );
    }

    /**
     * Show subtle reconnecting indicator
     */
    function showReconnectingUI() {
        let indicator = document.getElementById('reconnect-indicator');
        if (!indicator) {
            indicator = document.createElement('div');
            indicator.id = 'reconnect-indicator';
            indicator.innerHTML = `
                <div style="position:fixed;bottom:16px;left:50%;transform:translateX(-50%);
                            background:hsl(0 0% 10%);color:white;padding:8px 16px;
                            border-radius:8px;font-size:12px;z-index:9999;
                            display:flex;align-items:center;gap:8px;
                            box-shadow:0 4px 12px rgba(0,0,0,0.3);">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" 
                         stroke-width="2" style="animation:spin 1s linear infinite">
                        <path d="M21 12a9 9 0 1 1-9-9c2.52 0 4.85.99 6.57 2.57L21 8"/>
                        <path d="M21 3v5h-5"/>
                    </svg>
                    <span>Reconnecting...</span>
                </div>
                <style>@keyframes spin{to{transform:rotate(360deg)}}</style>
            `;
            document.body.appendChild(indicator);
        }
        indicator.style.display = 'block';
    }

    /**
     * Hide reconnecting indicator
     */
    function hideReconnectingUI() {
        const indicator = document.getElementById('reconnect-indicator');
        if (indicator) indicator.style.display = 'none';
    }

    /**
     * Force page reload
     */
    function forceReload() {
        console.log('Blazor: Forcing page reload...');
        hideReconnectingUI();
        window.location.reload();
    }

    /**
     * Show reload prompt with auto-reload countdown
     */
    function showReloadPrompt() {
        let prompt = document.getElementById('reload-prompt');
        if (!prompt) {
            prompt = document.createElement('div');
            prompt.id = 'reload-prompt';
            prompt.innerHTML = `
                <div style="position:fixed;inset:0;background:rgba(0,0,0,0.8);
                            display:flex;align-items:center;justify-content:center;z-index:9999;">
                    <div style="background:hsl(0 0% 7%);border:1px solid hsl(0 0% 18%);
                                padding:24px;border-radius:12px;text-align:center;max-width:300px;">
                        <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" 
                             stroke-width="1.5" style="margin:0 auto 16px;color:#fbbf24">
                            <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/>
                            <line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/>
                        </svg>
                        <h3 style="color:white;margin:0 0 8px;font-size:16px;font-weight:600;">Connection Lost</h3>
                        <p style="color:#a1a1aa;margin:0 0 16px;font-size:13px;">
                            Auto-reload in <span id="reload-countdown">${CONFIG.autoReloadDelaySec}</span>s
                        </p>
                        <button onclick="window.location.reload()" 
                                style="background:white;color:black;border:none;padding:10px 24px;
                                       border-radius:6px;font-size:14px;font-weight:500;cursor:pointer;">
                            Reload Now
                        </button>
                    </div>
                </div>
            `;
            document.body.appendChild(prompt);
            
            // Auto-reload countdown
            let countdown = CONFIG.autoReloadDelaySec;
            const countdownEl = document.getElementById('reload-countdown');
            const countdownInterval = setInterval(() => {
                countdown--;
                if (countdownEl) countdownEl.textContent = countdown;
                if (countdown <= 0) {
                    clearInterval(countdownInterval);
                    forceReload();
                }
            }, 1000);
        }
    }

    /**
     * Initialize Blazor with custom reconnection handling
     */
    function initBlazor() {
        if (typeof Blazor === 'undefined') {
            console.warn('Blazor not loaded yet, retrying...');
            setTimeout(initBlazor, 100);
            return;
        }

        Blazor.start({
            reconnectionOptions: {
                maxRetries: CONFIG.maxReconnectAttempts,
                retryIntervalMilliseconds: CONFIG.retryIntervalMs
            }
        }).then(() => {
            console.log('Blazor: Connected successfully');
            hideReconnectingUI();
            reconnectAttempts = 0;
        }).catch(err => {
            console.warn('Blazor: Initial connection failed:', err);
            setTimeout(forceReload, 3000);
        });

        // Override default reconnection handler
        Blazor.defaultReconnectionHandler._reconnectionDisplay = {
            show: () => {
                isReconnecting = true;
                showReconnectingUI();
                console.log('Blazor: Connection lost, attempting to reconnect...');
            },
            hide: () => {
                isReconnecting = false;
                hideReconnectingUI();
                reconnectAttempts = 0;
                console.log('Blazor: Reconnected successfully');
            },
            failed: () => {
                console.log('Blazor: Reconnection failed after all attempts');
                hideReconnectingUI();
                showReloadPrompt();
            },
            update: (currentAttempt) => {
                reconnectAttempts = currentAttempt;
                console.log(`Blazor: Reconnection attempt ${currentAttempt}/${CONFIG.maxReconnectAttempts}`);
            }
        };
    }

    /**
     * Handle visibility change (user returns to tab/app)
     */
    function handleVisibilityChange() {
        if (document.visibilityState === 'visible') {
            console.log('Blazor: Tab became visible, checking connection...');
            
            // Clear any pending reconnect timer
            if (reconnectTimer) {
                clearTimeout(reconnectTimer);
                reconnectTimer = null;
            }

            // Check if connection is dead after delay
            reconnectTimer = setTimeout(() => {
                const indicator = document.getElementById('reconnect-indicator');
                if (indicator?.style.display === 'block') {
                    console.log('Blazor: Connection appears dead, forcing reload...');
                    forceReload();
                }
            }, CONFIG.visibilityCheckDelayMs);
        }
    }

    /**
     * Handle online/offline events
     */
    function handleOnline() {
        console.log('Blazor: Network is back online');
        if (!isReconnecting && document.getElementById('reload-prompt')) {
            forceReload();
        }
    }

    function handleOffline() {
        console.log('Blazor: Network went offline');
    }

    /**
     * Handle page show event (for back/forward cache)
     */
    function handlePageShow(event) {
        if (event.persisted) {
            console.log('Blazor: Page restored from bfcache, reloading...');
            forceReload();
        }
    }

    // Register event listeners
    document.addEventListener('visibilitychange', handleVisibilityChange);
    window.addEventListener('online', handleOnline);
    window.addEventListener('offline', handleOffline);
    window.addEventListener('pageshow', handlePageShow);

    // Initialize Blazor when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initBlazor);
    } else {
        initBlazor();
    }
})();
