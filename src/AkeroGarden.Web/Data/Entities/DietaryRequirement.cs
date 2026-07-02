namespace AkeroGarden.Web.Data.Entities;

/// <summary>
/// A dietary requirement for a participant (requirements.md §4.6). Fixed system values — not
/// admin-managed; adding a new type requires a code change. A participant may hold several
/// (multi-select), stored on <see cref="Participant.DietaryRequirements"/>; <see cref="Other"/>
/// carries free text in <see cref="Participant.DietaryNotes"/> (max 200 chars). Persisted as
/// strings for legibility and stability against reordering.
/// </summary>
public enum DietaryRequirement
{
    /// <summary>No dietary requirements (§4.6 <c>NONE</c>).</summary>
    None,

    /// <summary>Vegetarian (§4.6 <c>VEGETARIAN</c>).</summary>
    Vegetarian,

    /// <summary>Vegan (§4.6 <c>VEGAN</c>).</summary>
    Vegan,

    /// <summary>Gluten intolerance (§4.6 <c>GLUTEN_FREE</c>).</summary>
    GlutenFree,

    /// <summary>Lactose intolerance (§4.6 <c>LACTOSE_FREE</c>).</summary>
    LactoseFree,

    /// <summary>Nut allergy (§4.6 <c>NUT_ALLERGY</c>).</summary>
    NutAllergy,

    /// <summary>Other — free text in <see cref="Participant.DietaryNotes"/> (§4.6 <c>OTHER</c>).</summary>
    Other
}
