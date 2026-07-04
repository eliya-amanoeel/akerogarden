// Progressive enhancement for the booking completion form (Phase 4). Presentation only — the
// server re-derives everything authoritatively (CompletionNormalizer), so the form works with
// JS disabled: the meal sections are simply always visible and each day is filled individually.
(function () {
    "use strict";

    // Show a participant's meal section only when they are marked full-board (§4.1).
    function syncMeals(checkbox) {
        var i = checkbox.getAttribute("data-fullboard-participant");
        var section = document.querySelector('.meals[data-meals-participant="' + i + '"]');
        if (section) {
            section.style.display = checkbox.checked ? "" : "none";
        }
    }

    document.querySelectorAll("input[data-fullboard-participant]").forEach(function (checkbox) {
        syncMeals(checkbox);
        checkbox.addEventListener("change", function () {
            syncMeals(checkbox);
        });
    });

    // Day guests: show only the meals actually served on the chosen attendance date (§4.1) — no
    // lunch/dinner on the departure day, no breakfast/lunch on the arrival day. Data lives on the
    // date <option>s; the server also renders + drops authoritatively.
    function applyDayGuestMeals(dateSelect) {
        var i = dateSelect.getAttribute("data-dg-date-participant");
        var opt = dateSelect.options[dateSelect.selectedIndex];
        var served = {
            breakfast: opt && opt.getAttribute("data-breakfast") === "1",
            lunch: opt && opt.getAttribute("data-lunch") === "1",
            dinner: opt && opt.getAttribute("data-dinner") === "1"
        };
        ["breakfast", "lunch", "dinner"].forEach(function (meal) {
            var el = document.querySelector('[data-dg-meal="' + meal + '"][data-dg-participant="' + i + '"]');
            if (el) {
                el.style.display = served[meal] ? "" : "none";
            }
        });
    }

    document.querySelectorAll("select[data-dg-date-participant]").forEach(function (dateSelect) {
        applyDayGuestMeals(dateSelect);
        dateSelect.addEventListener("change", function () {
            applyDayGuestMeals(dateSelect);
        });
    });

    // "Same for all days" (FR-59): copy one choice into every matching per-day select for that
    // participant + meal type.
    document.querySelectorAll("select[data-sameall-participant]").forEach(function (source) {
        source.addEventListener("change", function () {
            var i = source.getAttribute("data-sameall-participant");
            var meal = source.getAttribute("data-sameall-meal");
            var value = source.value;
            if (!value) {
                return;
            }
            var selector = 'select[data-participant="' + i + '"][data-meal="' + meal + '"]';
            document.querySelectorAll(selector).forEach(function (target) {
                target.value = value;
            });
        });
    });
})();
