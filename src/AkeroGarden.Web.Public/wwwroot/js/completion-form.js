// Progressive enhancement for the booking completion form. Presentation only — the server
// re-resolves everything authoritatively (MealOccasionResolver + CompletionNormalizer), so the form
// works with JS disabled: the full meal grid is simply always visible and each sitting is filled
// individually.
(function () {
    "use strict";

    // Edge-day meals (§4.1, FR-105): ticking arrival/departure lunch or dinner reveals that
    // sitting's dropdown. The server re-resolves authoritatively (MealOccasionResolver) and drops a
    // choice for a sitting the group did not take, so without JS the full grid is simply visible
    // and any stray choice is discarded — never silently reassigned to another day.
    function edgeChecked(edge, meal) {
        var box = document.querySelector('input[name="Form.' + edge + meal + '"][data-edge-meal]');
        return !!box && box.checked;
    }

    function syncEdgeSittings() {
        document.querySelectorAll("[data-sitting][data-edge]").forEach(function (cell) {
            var edge = cell.getAttribute("data-edge");
            if (!edge) {
                return;
            }
            var prefix = edge === "arrival" ? "Arrival" : "Departure";
            var visible = edgeChecked(prefix, cell.getAttribute("data-meal"));
            // Toggling the class, not cell.style.display: the initial hidden state is now
            // server-rendered as .meal-cell--hidden, and a class cannot be overridden by
            // clearing an inline style that is no longer there.
            cell.classList.toggle("meal-cell--hidden", !visible);
            if (!visible) {
                var select = cell.querySelector("select");
                if (select) {
                    select.value = "";
                }
            }
        });
    }

    var edgeBoxes = document.querySelectorAll("input[data-edge-meal]");
    if (edgeBoxes.length) {
        syncEdgeSittings();
        edgeBoxes.forEach(function (box) {
            box.addEventListener("change", syncEdgeSittings);
        });
    }

    // Keep the collapsed participant row's allergy label truthful as boxes are ticked. The server
    // renders it too, so without JS it is merely stale until the next postback rather than absent —
    // a safety field must never look the same whether or not something is recorded.
    document.querySelectorAll("[data-allergy-summary]").forEach(function (summary) {
        var index = summary.getAttribute("data-allergy-summary");
        var boxes = document.querySelectorAll('input[name="Form.Overnight[' + index + '].Allergies"]');
        var notes = document.querySelector('textarea[name="Form.Overnight[' + index + '].AllergyNotes"]');
        if (!boxes.length) {
            return;
        }

        function update() {
            var chosen = [];
            boxes.forEach(function (box) {
                if (box.checked) {
                    var label = box.parentElement ? box.parentElement.textContent.trim() : "";
                    if (label) {
                        chosen.push(label);
                    }
                }
            });
            var note = notes && notes.value.trim() !== "" ? notes.value.trim() : "";
            if (chosen.length === 0 && note === "") {
                summary.textContent = "Lägg till allergi";
            } else {
                summary.textContent = chosen.length ? chosen.join(", ") : note;
            }
        }

        boxes.forEach(function (box) { box.addEventListener("change", update); });
        if (notes) {
            notes.addEventListener("input", update);
        }
        update();
    });

    // Show the organisation invoice fields only when "Fakturera en organisation" is checked
    // (mirrors inquiry-form.js). The server ignores these fields unless the box is checked
    // (SubmitCompletionAsync), so a JS-disabled submit with the panel always visible is still correct.
    var isOrg = document.getElementById("is-org");
    var orgFields = document.getElementById("org-invoice-fields");
    if (isOrg && orgFields) {
        function syncOrgFields() {
            orgFields.style.display = isOrg.checked ? "" : "none";
        }
        syncOrgFields();
        isOrg.addEventListener("change", syncOrgFields);
    }

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
