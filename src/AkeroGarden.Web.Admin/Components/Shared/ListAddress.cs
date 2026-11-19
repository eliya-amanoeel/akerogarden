using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.JSInterop;

namespace AkeroGarden.Web.Admin.Components.Shared;

/// <summary>
/// The address of a paged admin list (design.md, "Pager"): its page and filters in the query
/// string, so Back from a booking, a reload or a bookmark returns to the same page with the same
/// filters. A list reads them on arrival with <see cref="ReadAsync"/> and, after each change it
/// has already loaded, writes them back with <see cref="ReplaceAsync"/>.
///
/// <para><strong>Never the booking reference.</strong> NFR-05 keeps IDs out of query strings, and a
/// reference search is an ID, so it stays in the page alone and is gone after Back.</para>
///
/// <para>The address is replaced in the browser, not navigated to: with the admin's static router
/// a navigation reloads the page through the server, and the list has already loaded what it
/// shows. The server is therefore never told, which is why <see cref="ReadAsync"/> asks the browser
/// on the live circuit rather than trusting <see cref="NavigationManager"/> there.</para>
/// </summary>
public static class ListAddress
{
    private const string Module = "./_content/AkeroGarden.Web.Core/js/list-address.js";

    /// <summary>The query-string name of the page number, the same on every list.</summary>
    public const string PageKey = "sida";

    /// <summary>
    /// The list's query as the browser holds it. While prerendering that is the request's address,
    /// which <paramref name="nav"/> has; on the live circuit <paramref name="nav"/> can still hold an
    /// address from before a <see cref="ReplaceAsync"/> — Back from a booking then arrived with every
    /// filter gone — so there the browser is asked. Pass the component's
    /// <c>RendererInfo.IsInteractive</c>.
    /// </summary>
    public static async Task<Address> ReadAsync(IJSRuntime js, NavigationManager nav, bool interactive)
    {
        string url;
        if (interactive)
        {
            await using var module = await js.InvokeAsync<IJSObjectReference>("import", Module);
            url = await module.InvokeAsync<string>("current");
        }
        else
        {
            url = nav.Uri;
        }

        var query = QueryHelpers.ParseQuery(new Uri(url).Query);
        return new Address(query.ToDictionary(q => q.Key, q => q.Value.ToString()));
    }

    /// <summary>
    /// Writes <paramref name="query"/> into the address bar in place of the current query. A null
    /// or empty value, and page 1, are left out, so an unfiltered first page is the bare path.
    /// Only from an event handler: there is no browser to write to during prerendering.
    /// </summary>
    public static async Task ReplaceAsync(IJSRuntime js, NavigationManager nav, int page, params (string Key, string? Value)[] query)
    {
        var path = nav.ToAbsoluteUri(nav.Uri).GetLeftPart(UriPartial.Path);
        var pairs = new List<(string Key, string Value)>();
        if (page > 1)
            pairs.Add((PageKey, page.ToString(CultureInfo.InvariantCulture)));
        pairs.AddRange(query.Where(q => !string.IsNullOrEmpty(q.Value)).Select(q => (q.Key, q.Value!)));
        var queryString = string.Join("&", pairs.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

        await using var module = await js.InvokeAsync<IJSObjectReference>("import", Module);
        await module.InvokeVoidAsync("replace", queryString == "" ? path : $"{path}?{queryString}");
    }

    /// <summary>A date as the address carries it.</summary>
    public static string? Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A list's query, read back. Anything missing or malformed is no filter, page 1.</summary>
    public sealed class Address(IReadOnlyDictionary<string, string> values)
    {
        public string? this[string key] => values.TryGetValue(key, out var v) && v != "" ? v : null;

        public int Page => int.TryParse(this[PageKey], NumberStyles.None, CultureInfo.InvariantCulture, out var p) && p > 0 ? p : 1;

        public DateOnly? Date(string key) =>
            DateOnly.TryParseExact(this[key], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

        /// <summary>An enum filter as the select binds it — its name, or "" (Alla) when the address
        /// holds nothing or something that is not one of its values.</summary>
        public string EnumName<TEnum>(string key) where TEnum : struct, Enum =>
            Enum.TryParse<TEnum>(this[key], out var parsed) && Enum.IsDefined(parsed) ? parsed.ToString() : "";
    }
}
