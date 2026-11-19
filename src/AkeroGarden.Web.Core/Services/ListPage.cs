using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// One page of an admin list that grows (design.md, "Pager"): the rows on it, which page it is
/// (1-based) and how many there are. Drawn by <c>AdminPager</c>; every paged list returns this.
/// </summary>
public record ListPage<T>(IReadOnlyList<T> Items, int Page, int TotalPages);

public static class ListPage
{
    /// <summary>How many rows one page of an admin row list shows (Bokningar, listorna, Blockeringar).</summary>
    public const int RowsPerPage = 25;

    /// <summary>
    /// How many pages <paramref name="count"/> rows make — at least one, so an empty list still has
    /// a page to show its "nothing here" message on — and <paramref name="page"/> kept inside them,
    /// so a filter that leaves fewer pages never strands the reader on an empty one.
    /// </summary>
    public static (int Page, int TotalPages) Clamp(int count, int page, int pageSize)
    {
        var totalPages = Math.Max(1, (count + pageSize - 1) / pageSize);
        return (Math.Clamp(page, 1, totalPages), totalPages);
    }

    /// <summary>
    /// One page of <paramref name="query"/>, counted and cut in the database. The query must already
    /// be in a total order — a tie-breaker on a unique column last — or a row on the boundary between
    /// two pages can show on both or on neither.
    /// </summary>
    public static async Task<ListPage<T>> FromAsync<T>(IOrderedQueryable<T> query, int page, int pageSize = RowsPerPage)
    {
        var (current, totalPages) = Clamp(await query.CountAsync(), page, pageSize);
        var items = await query.Skip((current - 1) * pageSize).Take(pageSize).ToListAsync();
        return new ListPage<T>(items, current, totalPages);
    }
}
