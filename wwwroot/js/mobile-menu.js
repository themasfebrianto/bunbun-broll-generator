// Mobile menu functionality - loaded globally
(function() {
    let hamburgerButton = null;
    let mobileMenu = null;
    let mobileMenuBackdrop = null;
    let hamburgerIcon = null;
    let mobileMenuLinks = null;
    let isMenuOpen = false;
    let initialized = false;

    function toggleMenu() {
        isMenuOpen = !isMenuOpen;

        if (isMenuOpen) {
            // Show menu
            mobileMenu.classList.remove('hidden');
            // Update icon to X
            hamburgerIcon.innerHTML = `
                <line x1="18" y1="6" x2="6" y2="18"></line>
                <line x1="6" y1="6" x2="18" y2="18"></line>
            `;
        } else {
            // Hide menu
            mobileMenu.classList.add('hidden');
            // Update icon to hamburger
            hamburgerIcon.innerHTML = `
                <line class="hamburger-line" x1="3" y1="12" x2="21" y2="12"></line>
                <line class="hamburger-line" x1="3" y1="6" x2="21" y2="6"></line>
                <line class="hamburger-line" x1="3" y1="18" x2="21" y2="18"></line>
            `;
        }
    }

    function closeMenu() {
        if (isMenuOpen) {
            isMenuOpen = false;
            mobileMenu.classList.add('hidden');
            // Reset icon to hamburger
            hamburgerIcon.innerHTML = `
                <line class="hamburger-line" x1="3" y1="12" x2="21" y2="12"></line>
                <line class="hamburger-line" x1="3" y1="6" x2="21" y2="6"></line>
                <line class="hamburger-line" x1="3" y1="18" x2="21" y2="18"></line>
            `;
        }
    }

    function init() {
        if (initialized) return;

        hamburgerButton = document.getElementById('hamburger-button');
        mobileMenu = document.getElementById('mobile-menu');
        mobileMenuBackdrop = document.getElementById('mobile-menu-backdrop');
        hamburgerIcon = document.getElementById('hamburger-icon');
        mobileMenuLinks = document.querySelectorAll('.mobile-menu-link');

        if (hamburgerButton) {
            // Remove any existing listeners
            hamburgerButton.removeEventListener('click', toggleMenu);
            // Add new listener
            hamburgerButton.addEventListener('click', function(e) {
                e.preventDefault();
                e.stopPropagation();
                console.log('Hamburger clicked!', new Date().toISOString());
                toggleMenu();
            });
        }

        if (mobileMenuBackdrop) {
            mobileMenuBackdrop.removeEventListener('click', closeMenu);
            mobileMenuBackdrop.addEventListener('click', closeMenu);
        }

        // Close menu when clicking on links
        mobileMenuLinks.forEach(function(link) {
            link.removeEventListener('click', closeMenu);
            link.addEventListener('click', closeMenu);
        });

        initialized = true;
        console.log('Mobile menu initialized at:', new Date().toISOString());
    }

    // Initialize on DOM ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    // Re-initialize after Blazor updates (with delay for DOM to settle)
    window.addEventListener('blazor:updated', function() {
        setTimeout(init, 100);
    });

    // Expose cleanup function
    window.bunbunMobileMenuCleanup = function() {
        if (hamburgerButton) {
            hamburgerButton.removeEventListener('click', toggleMenu);
        }
        if (mobileMenuBackdrop) {
            mobileMenuBackdrop.removeEventListener('click', closeMenu);
        }
        mobileMenuLinks.forEach(function(link) {
            link.removeEventListener('click', closeMenu);
        });
        initialized = false;
        console.log('Mobile menu cleaned up');
    };

    // Also re-init periodically to catch any DOM changes
    setInterval(function() {
        if (!initialized || !document.getElementById('hamburger-button')) {
            init();
        }
    }, 2000);

})();
