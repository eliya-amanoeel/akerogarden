// Keeps a paged admin list's state (page and filters) in the address bar (ListAddress.cs), so
// Back from a booking, a reload or a bookmark comes back to the same page and filters.
//
// The browser's address is the only reliable record of it. The list writes it with replaceState,
// which the server's NavigationManager never hears about, so on the live circuit the list reads it
// back from here rather than from NavigationManager — which still held the address before the
// replace, and so lost the filters on Back (E2E, 2026-11-19).
//
// replaceState, not pushState: paging adds no history entries, so Back leaves the list rather
// than stepping back through its pages. history.state is passed on untouched — it is Blazor's.
export function replace(url) {
    history.replaceState(history.state, '', url);
}

export function current() {
    return location.href;
}
