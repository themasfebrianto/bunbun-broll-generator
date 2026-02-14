// Theme Management
// This script runs immediately and defines functions on window for Blazor interop

(function() {
    'use strict';

    /**
     * Initialize theme on page load
     * Runs synchronously before any content to prevent flash
     */
    function initTheme() {
        try {
            var t = localStorage.getItem('theme');
            if (t === 'light') {
                document.documentElement.classList.remove('dark');
                document.documentElement.style.colorScheme = 'light';
            } else {
                document.documentElement.classList.add('dark');
                document.documentElement.style.colorScheme = 'dark';
            }
        } catch (e) {
            // Fallback to dark mode if localStorage fails
            document.documentElement.classList.add('dark');
            document.documentElement.style.colorScheme = 'dark';
        }
    }

    /**
     * Toggle between light and dark themes
     * @returns {boolean} true if dark mode is now active
     */
    function toggleTheme() {
        var isDark = document.documentElement.classList.toggle('dark');
        document.documentElement.style.colorScheme = isDark ? 'dark' : 'light';
        try {
            localStorage.setItem('theme', isDark ? 'dark' : 'light');
        } catch (e) {
            console.warn('Failed to save theme preference:', e);
        }
        return isDark;
    }

    /**
     * Get current theme
     * @returns {string} Current theme name ('dark' or 'light')
     */
    function getTheme() {
        return document.documentElement.classList.contains('dark') ? 'dark' : 'light';
    }

    // Run init immediately
    initTheme();

    // Expose to window for Blazor interop
    window.toggleTheme = toggleTheme;
    window.getTheme = getTheme;
})();
