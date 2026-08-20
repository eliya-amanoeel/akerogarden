// Publishes the *measured* height of each sticky bar as a CSS custom property, so elements
// that stick below a bar can offset by what that bar actually is rather than by a number
// copied off it.
//
// Why this exists. Those offsets used to read a hardcoded `--header-h: 72px`. A constant
// that duplicates a rendered height is correct only until something changes that height —
// a reader raising their browser text size, an extra nav item, a longer CTA label, an admin
// topbar whose title and actions wrap onto a second row. CSS gives one element no way to
// read another's height, so it is measured here and written into the property the stylesheet
// already offsets by.
//
// This is an enhancement, never a dependency: `site.css` declares a `rem` fallback for every
// property below, so a page is correct at a default text size before this script runs and if
// it never runs at all. Loaded only where something actually sticks below a bar — the admin
// host (App.razor) and the public calendar page — not on every public page.
(function () {
    'use strict';

    // [selector of the bar, custom property that rules below it offset by]
    var BARS = [
        ['.site-header', '--header-h'],         // public — .cal-selection sticks below it
        ['.admin-topbar', '--admin-topbar-h']   // admin  — .detail-rail sticks below it
    ];

    var root = document.documentElement;
    var observers = [];

    function publish(el, prop) {
        // getBoundingClientRect() is the border-box height. Both bars carry a 1px bottom
        // border, and anything sticking below them has to clear that border too.
        root.style.setProperty(prop, el.getBoundingClientRect().height + 'px');
    }

    function attach() {
        for (var i = 0; i < observers.length; i++) {
            observers[i].disconnect();
        }
        observers = [];

        // No ResizeObserver: leave the properties unset so the stylesheet's fallback stands.
        if (typeof ResizeObserver !== 'function') {
            return;
        }

        BARS.forEach(function (bar) {
            var el = document.querySelector(bar[0]);
            if (!el) {
                return;   // the other host's bar — expected, not an error
            }
            publish(el, bar[1]);
            var observer = new ResizeObserver(function () { publish(el, bar[1]); });
            observer.observe(el);
            observers.push(observer);
        });
    }

    attach();

    // Blazor's enhanced navigation patches the DOM in place, so the observed bar normally
    // survives a navigation. If it is ever replaced wholesale the observer would be left
    // watching a detached node — re-attaching is idempotent, so just do it on every load.
    window.addEventListener('load', function () {
        if (window.Blazor && typeof window.Blazor.addEventListener === 'function') {
            window.Blazor.addEventListener('enhancedload', attach);
        }
    });
})();
