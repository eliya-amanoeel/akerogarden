// Progressive enhancement for the /boka inquiry form. The server (InquiryNormalizer) re-applies
// both rules regardless, so this is purely a UX nicety and safe to fail without it.
(function () {
    'use strict';

    // §4.2 — day guests do not apply when the meal plan is self-catering; hide the field.
    var mealPlan = document.getElementById('mealplan');
    var dayGuests = document.getElementById('dayguests-group');
    function toggleDayGuests() {
        if (!mealPlan || !dayGuests) return;
        // Use inline display (not the hidden attribute): .form-group sets display:flex, which
        // would override the browser's [hidden] rule and keep the field visible.
        dayGuests.style.display = mealPlan.value === 'SelfCateringAll' ? 'none' : '';
    }
    if (mealPlan) {
        mealPlan.addEventListener('change', toggleDayGuests);
        toggleDayGuests();
    }

    // §5.5 — selecting a bundle (e.g. Hela lägret) disables the individual zones it covers.
    var bundles = document.querySelectorAll('input[data-covers]');
    function applyBundle(cb) {
        var ids = cb.getAttribute('data-covers').split(',').filter(Boolean);
        ids.forEach(function (id) {
            var target = document.querySelector(
                'input[name="StayModel.SelectedCleaningZoneIds"][value="' + id + '"]');
            if (!target) return;
            target.disabled = cb.checked;
            if (cb.checked) target.checked = false;
        });
    }
    bundles.forEach(function (cb) {
        cb.addEventListener('change', function () { applyBundle(cb); });
        applyBundle(cb);
    });
})();
