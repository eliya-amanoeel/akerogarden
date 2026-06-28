// Scroll-aware header: adds .scrolled class once page scrolls past the header height.
// Public pages are Static SSR — no Blazor interactivity — so this is plain JS.
(function () {
    var header = document.getElementById('site-header');
    if (!header) return;

    function onScroll() {
        if (window.scrollY > 10) {
            header.classList.add('scrolled');
        } else {
            header.classList.remove('scrolled');
        }
    }

    window.addEventListener('scroll', onScroll, { passive: true });
    onScroll();
})();
