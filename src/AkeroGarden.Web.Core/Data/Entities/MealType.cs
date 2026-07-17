namespace AkeroGarden.Web.Core.Data.Entities;

/// <summary>
/// Whether a meal option is served at lunch or dinner (requirements.md §4.5, §8.7).
/// Persisted as a string for legibility and stability against reordering.
/// </summary>
public enum MealType
{
    Lunch,
    Dinner
}
