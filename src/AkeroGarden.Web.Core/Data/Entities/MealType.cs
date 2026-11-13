namespace AkeroGarden.Web.Core.Data.Entities;

/// <summary>
/// A sitting's meal: lunch or dinner on a given day (requirements.md §4.1, §8.6). Dishes carry no
/// meal type — any dish on the menu can be chosen at any sitting (§4.5).
/// Persisted as a string for legibility and stability against reordering.
/// </summary>
public enum MealType
{
    Lunch,
    Dinner
}
