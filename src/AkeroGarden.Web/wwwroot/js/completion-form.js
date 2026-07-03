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
