// Sends each form on the page at most once (NFR-41). Loaded by the two public pages that post —
// /boka and /slutfor — and nowhere in admin, whose forms run on the interactive circuit and never
// reload: a form locked there would stay locked.
//
// The redirect after a successful post (Post/Redirect/Get) is what stops Back and Reload re-sending
// a successful submission; this covers the one gap it leaves, a second click while the first post is
// still on its way (1–2.5 s in production), which would otherwise create the booking twice. Without
// JS the redirect still holds and only that window reopens.
(function () {
    'use strict';

    function submitButtons(form) {
        return form.querySelectorAll('button[type="submit"]');
    }

    // On the document, not the form, so it runs after the page's own handlers: inquiry-form.js
    // validates on the form and cancels an invalid submit, and a submit already cancelled was never
    // sent — locking the form then would leave the guest unable to send it once corrected.
    document.addEventListener('submit', function (ev) {
        if (ev.defaultPrevented) return;
        var form = ev.target;

        if (form.dataset.submitting === 'true') {
            ev.preventDefault();
            return;
        }
        form.dataset.submitting = 'true';

        // Greyed out on the next tick, not now: a button disabled during the submit event is left out
        // of the posted data, and the completion form tells its submit buttons apart by their
        // name/value (Action — complete, add or remove a day guest). The flag above already blocks a
        // second submit in the meantime, so nothing depends on the timing.
        setTimeout(function () {
            submitButtons(form).forEach(function (b) { b.disabled = true; });
        }, 0);
    });

    // Going Back to this page can restore it from the browser's page cache exactly as it was left —
    // locked. Unlock it, or the form the guest returned to could not be sent at all.
    window.addEventListener('pageshow', function (ev) {
        if (!ev.persisted) return;
        document.querySelectorAll('form[data-submitting]').forEach(function (form) {
            delete form.dataset.submitting;
            submitButtons(form).forEach(function (b) { b.disabled = false; });
        });
    });
})();
