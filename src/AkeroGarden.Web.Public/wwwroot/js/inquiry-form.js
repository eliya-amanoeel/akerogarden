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

    // Organisation fields appear only when "for an organisation" is ticked (progressive
    // disclosure). Without JS the panel stays visible, so the fields remain reachable.
    var isOrg = document.getElementById('s-isorg');
    var orgFields = document.getElementById('org-fields');
    function toggleOrg() {
        if (!isOrg || !orgFields) return;
        orgFields.style.display = isOrg.checked ? '' : 'none';
    }
    if (isOrg) {
        isOrg.addEventListener('change', toggleOrg);
        toggleOrg();
    }

    // ── Client-side validation (Phase 7 A2d) ─────────────────────────────────────────────────────
    // Eliminates the *invalid-submit* postback: the whole-page round-trip that only re-renders to
    // tell the guest a field is wrong and scrolls them away from it. Rules and Swedish messages
    // mirror the server DataAnnotations exactly; the server (DataAnnotationsValidator +
    // IValidatableObject) re-validates every post, so this is a pure UX layer and safe to fail
    // without (JS off ⇒ the server messages appear as before). The valid submit still posts — that
    // is the real action (create booking + emails), not an unnecessary round-trip.

    var EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    // Mirrors OptionalPhoneAttribute: allowed shape + 6–15 actual digits.
    var PHONE_SHAPE = /^\+?[\d\s\-().\/]+$/;

    function isBlank(v) { return v == null || v.trim() === ''; }
    function digits(v) { return (v.match(/\d/g) || []).length; }
    function phoneOk(v) { return isBlank(v) || (PHONE_SHAPE.test(v) && digits(v) >= 6 && digits(v) <= 15); }
    function emailsMatch(a, b) { return (a || '').trim().toLowerCase() === (b || '').trim().toLowerCase(); }

    // A field descriptor: the model property name (its rendered `name` is `<prefix>.<prop>`), the
    // element type, and an ordered list of rules returning a message or null. Cross-field rules read
    // sibling values via the `vals` accessor.
    function field(prop, rules) { return { prop: prop, rules: rules }; }

    var stayFields = [
        field('ContactName', [function (v) { return isBlank(v) ? 'Namn krävs.' : null; }]),
        field('ContactEmail', [
            function (v) { return isBlank(v) ? 'E-postadress krävs.' : null; },
            function (v) { return !isBlank(v) && !EMAIL.test(v.trim()) ? 'Ange en giltig e-postadress.' : null; }
        ]),
        field('ConfirmEmail', [
            function (v) { return isBlank(v) ? 'Bekräfta din e-postadress.' : null; },
            function (v, vals) { return !isBlank(v) && !emailsMatch(v, vals('ContactEmail')) ? 'E-postadresserna stämmer inte överens.' : null; }
        ]),
        field('ContactPhone', [function (v) { return !phoneOk(v) ? 'Ange ett giltigt telefonnummer.' : null; }]),
        field('CheckIn', [function (v) { return isBlank(v) ? 'Incheckningsdatum krävs.' : null; }]),
        field('CheckOut', [
            function (v) { return isBlank(v) ? 'Utcheckningsdatum krävs.' : null; },
            function (v, vals) {
                var ci = vals('CheckIn');
                return !isBlank(v) && !isBlank(ci) && v <= ci ? 'Utcheckningsdatumet måste vara efter incheckningsdatumet.' : null;
            }
        ]),
        field('OvernightGuestCount', [
            function (v) { return isBlank(v) ? 'Antal gäster krävs.' : null; },
            function (v) { return !isBlank(v) && Number(v) < 1 ? 'Ange minst en gäst.' : null; }
        ]),
        field('MealPlan', [function (v) { return isBlank(v) ? 'Välj en måltidsplan.' : null; }]),
        field('DayGuestCount', [function (v) { return !isBlank(v) && Number(v) < 0 ? 'Antal dagsgäster kan inte vara negativt.' : null; }]),
        field('OrgName', [function (v, vals) { return vals('IsOrganisation') && isBlank(v) ? 'Organisationsnamn krävs när bokningen görs för en organisation.' : null; }]),
        // Mirrors StayRequestInput.AcceptTerms — field name and message must both match the server's,
        // or this fires the wrong text (or, on a rename, silently stops firing at all).
        field('AcceptTerms', [function (v) { return v !== 'true' ? 'Du måste godkänna integritetspolicyn och bokningsvillkoren för att skicka förfrågan.' : null; }])
    ];

    var generalFields = [
        field('ContactName', [function (v) { return isBlank(v) ? 'Namn krävs.' : null; }]),
        field('ContactEmail', [
            function (v) { return isBlank(v) ? 'E-postadress krävs.' : null; },
            function (v) { return !isBlank(v) && !EMAIL.test(v.trim()) ? 'Ange en giltig e-postadress.' : null; }
        ]),
        field('ConfirmEmail', [
            function (v) { return isBlank(v) ? 'Bekräfta din e-postadress.' : null; },
            function (v, vals) { return !isBlank(v) && !emailsMatch(v, vals('ContactEmail')) ? 'E-postadresserna stämmer inte överens.' : null; }
        ]),
        field('ContactPhone', [function (v) { return !phoneOk(v) ? 'Ange ett giltigt telefonnummer.' : null; }]),
        field('Message', [function (v) { return isBlank(v) ? 'Meddelande krävs.' : null; }]),
        field('AcceptPrivacyPolicy', [function (v) { return v !== 'true' ? 'Du måste godkänna integritetspolicyn för att skicka förfrågan.' : null; }])
    ];

    function enhanceForm(prefix, fields) {
        // Locate the form via any of its rendered inputs (name = "<prefix>.<prop>").
        var anchor = document.querySelector('[name^="' + prefix + '."]');
        var form = anchor && anchor.closest('form');
        if (!form) return;
        // Take ownership of validation messaging so native constraints (e.g. number min) don't show
        // a second, unstyled bubble. Without JS the server + native min still guard.
        form.noValidate = true;

        function el(prop) { return form.querySelector('[name="' + prefix + '.' + prop + '"]'); }
        function rawValue(prop) {
            var e = el(prop);
            if (!e) return '';
            if (e.type === 'checkbox') return e.checked ? 'true' : 'false';
            return e.value;
        }
        function vals(prop) {
            if (prop === 'IsOrganisation') { return isOrg ? isOrg.checked : false; }
            return rawValue(prop);
        }

        function messageHost(e) {
            // The .form-group (or checkbox label's parent) that holds the input + its message div.
            return e.closest('.form-group') || e.parentElement;
        }
        function setError(f, msg) {
            var e = el(f.prop);
            if (!e) return;
            var host = messageHost(e);
            var div = host.querySelector('.validation-message');
            if (msg) {
                e.classList.add('invalid'); e.classList.remove('valid');
                e.setAttribute('aria-invalid', 'true');
                if (!div) {
                    div = document.createElement('div');
                    div.className = 'validation-message';
                    host.appendChild(div);
                }
                div.textContent = msg;
            } else {
                e.classList.remove('invalid'); e.classList.add('valid');
                e.removeAttribute('aria-invalid');
                if (div) div.textContent = '';
            }
        }
        function evaluate(f) {
            var v = rawValue(f.prop);
            for (var i = 0; i < f.rules.length; i++) {
                var msg = f.rules[i](v, vals);
                if (msg) return msg;
            }
            return null;
        }
        function validateField(f) { var msg = evaluate(f); setError(f, msg); return !msg; }

        // Live feedback: validate a field once the guest leaves it, and clear it as they fix it.
        // Confirm-email also re-checks when the primary email changes.
        fields.forEach(function (f) {
            var e = el(f.prop);
            if (!e) return;
            e.addEventListener('blur', function () { validateField(f); });
            e.addEventListener('input', function () {
                if (e.classList.contains('invalid')) validateField(f);
                if (f.prop === 'ContactEmail') {
                    var cf = fields.filter(function (x) { return x.prop === 'ConfirmEmail'; })[0];
                    var ce = cf && el('ConfirmEmail');
                    if (cf && ce && ce.classList.contains('invalid')) validateField(cf);
                }
            });
            e.addEventListener('change', function () { validateField(f); });
        });

        form.addEventListener('submit', function (ev) {
            // Track the failing *field*, not its element. Tracking the element meant that a field
            // whose input could not be found left firstInvalid null, so preventDefault never ran and
            // the form posted for real — the browser then reloaded the page and landed at the top,
            // which looks like "the page scrolled up" rather than like a broken validation gate. That
            // is exactly what a stale field name in the list above produces, and it is silent.
            var firstInvalid = null;
            fields.forEach(function (f) {
                if (!validateField(f) && !firstInvalid) firstInvalid = f;
            });
            if (!firstInvalid) return;

            ev.preventDefault();
            var target = el(firstInvalid.prop);
            if (!target) return;

            // preventScroll: focus() scrolls the element into view on its own, and it does so
            // without honouring the 'nearest' below — the two calls fought and focus() won.
            target.focus({ preventScroll: true });
            // 'nearest', not 'center': the acknowledgement checkbox is the last field and sits
            // directly above the submit button, so the guest is already looking at it when they
            // click. Centring a field that is already on screen scrolls the page anyway, pulling the
            // view up and away from the button. 'nearest' is a no-op when the field is fully visible
            // and still brings a far-off field (e.g. the name at the top) into view.
            target.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
        });
    }

    enhanceForm('StayModel', stayFields);
    enhanceForm('GeneralModel', generalFields);
})();
