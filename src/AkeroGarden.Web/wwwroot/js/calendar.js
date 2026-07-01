// Progressive enhancement for /kalender: turn single-date day links into a two-click date-range
// selection that pre-fills the inquiry form (FR-11). Without JS, each day is a plain link that
// pre-fills only the check-in date — so the page is fully usable either way.
(function () {
    'use strict';

    var start = null;
    var end = null;
    var days = Array.prototype.slice.call(document.querySelectorAll('.cal-day[data-date] a[data-date]'));
    var selectionBar = document.getElementById('cal-selection');
    var rangeLabel = document.getElementById('cal-range');
    var goButton = document.getElementById('cal-go');
    var clearButton = document.getElementById('cal-clear');
    if (!days.length || !selectionBar) return;

    function clearHighlight() {
        days.forEach(function (a) { a.parentElement.classList.remove('cal-selected'); });
    }

    function render() {
        clearHighlight();
        if (!start) { selectionBar.hidden = true; return; }
        days.forEach(function (a) {
            var d = a.getAttribute('data-date');
            if ((end && d >= start && d <= end) || d === start) {
                a.parentElement.classList.add('cal-selected');
            }
        });
        selectionBar.hidden = false;
        rangeLabel.textContent = end ? (start + ' – ' + end) : start;
        goButton.href = end
            ? '/boka?incheckning=' + start + '&utcheckning=' + end
            : '/boka?incheckning=' + start;
    }

    days.forEach(function (a) {
        a.addEventListener('click', function (e) {
            e.preventDefault();
            var d = a.getAttribute('data-date');
            if (!start || end) {          // begin a new selection
                start = d; end = null;
            } else if (d < start) {        // clicked earlier date → restart from it
                start = d;
            } else if (d === start) {      // same day → single-date
                end = null;
            } else {                       // complete the range
                end = d;
            }
            render();
        });
    });

    clearButton.addEventListener('click', function () {
        start = null; end = null; render();
    });
})();
