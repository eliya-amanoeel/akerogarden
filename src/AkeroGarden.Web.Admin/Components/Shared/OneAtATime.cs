namespace AkeroGarden.Web.Admin.Components.Shared;

/// <summary>
/// Runs a page's loads one after another. An interactive page shares one scoped
/// <c>ApplicationDbContext</c> across everything its circuit does, and EF Core throws when a second
/// query starts on it before the first has finished — an unhandled exception that ends the circuit
/// and leaves the page frozen on "Laddar…".
///
/// <para>The paged lists (design.md, "Pager") load on every page click and filter change, and a
/// second click before the first load has finished — two page numbers in quick succession, a filter
/// changed while a page loads — would start its query beside the first. Queued here, it waits. (A
/// change made before the list has read its address is a different problem, and is prevented by the
/// filters being inert until then, not by this.)</para>
/// </summary>
public sealed class OneAtATime
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task RunAsync(Func<Task> work)
    {
        await _gate.WaitAsync();
        try
        {
            await work();
        }
        finally
        {
            _gate.Release();
        }
    }
}
