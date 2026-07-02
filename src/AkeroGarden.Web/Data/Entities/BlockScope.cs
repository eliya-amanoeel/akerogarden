namespace AkeroGarden.Web.Data.Entities;

/// <summary>
/// The scope of a <see cref="BlockedPeriod"/> (requirements.md §3.3, §8.10). A block
/// removes availability for a date range at one of three levels. Room- and building-scoped
/// blocks reduce a date's effective capacity on the public calendar (FR-10) and exclude the
/// affected rooms from selection (FR-56); a <see cref="WholeProperty"/> block marks the date
/// fully unavailable.
/// Persisted as a string for legibility and stability against reordering.
/// </summary>
public enum BlockScope
{
    WholeProperty,
    Building,
    Room
}
