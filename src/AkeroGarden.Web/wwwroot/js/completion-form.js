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

    // Group defaults (FR-59, scaled to the whole group): cascade one choice to every participant.
    // "Helpension för alla" toggles every participant's full-board box and re-syncs their meal
    // visibility; each per-day standard select copies into that same slot (day + meal) for everyone.
    // Show the per-day group-default meal pickers only when "Helpension för alla" is checked —
    // mirrors syncMeals's per-participant gating so a self-catering/half-board group isn't shown
    // meal dropdowns it can't use.
    function syncGroupDefaultMeals(checkbox) {
        document.querySelectorAll("[data-groupdefault-days]").forEach(function (el) {
            el.style.display = checkbox.checked ? "" : "none";
        });
    }

    var gdFullBoard = document.querySelector("[data-groupdefault-fullboard]");
    if (gdFullBoard) {
        syncGroupDefaultMeals(gdFullBoard);
        gdFullBoard.addEventListener("change", function () {
            syncGroupDefaultMeals(gdFullBoard);
            document.querySelectorAll("input[data-fullboard-participant]").forEach(function (cb) {
                cb.checked = gdFullBoard.checked;
                cb.dispatchEvent(new Event("change", { bubbles: true }));
            });
        });
    }

    document.querySelectorAll("select[data-groupdefault-slot]").forEach(function (source) {
        source.addEventListener("change", function () {
            var slot = source.getAttribute("data-groupdefault-slot");
            if (!source.value) {
                return;
            }
            document.querySelectorAll('select[data-participant][data-slot="' + slot + '"]').forEach(function (target) {
                target.value = source.value;
            });
        });
    });

    // Live progress: how many overnight participants have a name filled in (presentation only;
    // hidden without JS so it never shows a misleading "0 av N").
    var progress = document.querySelector("[data-progress]");
    if (progress) {
        var names = document.querySelectorAll("input[data-progress-name]");
        var bar = progress.querySelector("[data-progress-bar]");
        var text = progress.querySelector("[data-progress-text]");
        function updateProgress() {
            var total = names.length, filled = 0;
            names.forEach(function (n) { if (n.value.trim() !== "") filled++; });
            if (bar) bar.style.width = (total ? Math.round(filled / total * 100) : 0) + "%";
            if (text) text.textContent = filled + " av " + total + " ifyllda";
        }
        names.forEach(function (n) { n.addEventListener("input", updateProgress); });
        progress.hidden = false;
        updateProgress();
    }
})();
