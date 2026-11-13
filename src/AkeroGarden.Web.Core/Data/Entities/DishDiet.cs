namespace AkeroGarden.Web.Core.Data.Entities;

/// <summary>
/// What a dish on the menu is, diet-wise (requirements.md §8.7, FR-22) — shown as a label on the
/// public menu (FR-83). One value, not two flags: a vegan dish is labelled "Veganskt" only, never
/// both. Unrelated to the booking's vegetarian/vegan portion counts (§4.6), which say how many
/// guests need an adapted portion, not what a dish is.
/// Persisted as a string for legibility and stability against reordering.
/// </summary>
public enum DishDiet
{
    None,
    Vegetarian,
    Vegan
}
