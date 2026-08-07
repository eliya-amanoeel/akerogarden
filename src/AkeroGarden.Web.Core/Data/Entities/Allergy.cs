namespace AkeroGarden.Web.Core.Data.Entities;

/// <summary>
/// A medical dietary constraint for a named participant (requirements.md §4.6). Fixed system
/// values — not admin-managed; adding one requires a code change. A participant may hold several
/// (multi-select), stored on <see cref="Participant.Allergies"/>; <see cref="Other"/> carries free
/// text in <see cref="Participant.AllergyNotes"/> (max 200 chars). Persisted as strings for
/// legibility and stability against reordering.
///
/// <para><b>Allergies only — preferences live elsewhere.</b> This replaced a single
/// <c>DietaryRequirement</c> list that mixed medical constraints with choices, so a nut allergy
/// and vegetarianism were stored identically. They are not alike: an allergy carries a safety
/// consequence and is special-category health data under GDPR art. 9, whereas being vegetarian is
/// an ordinary preference. Preferences are now counts on the booking
/// (<see cref="Booking.VegetarianCount"/> / <see cref="Booking.VeganCount"/>) — the kitchen needs
/// the number of adapted portions, not the names, so this stores strictly less personal data.</para>
/// </summary>
public enum Allergy
{
    /// <summary>No allergies (§4.6 <c>NONE</c>).</summary>
    None,

    /// <summary>Gluten intolerance (§4.6 <c>GLUTEN</c>).</summary>
    Gluten,

    /// <summary>Lactose intolerance (§4.6 <c>LACTOSE</c>).</summary>
    Lactose,

    /// <summary>Nut allergy (§4.6 <c>NUT</c>).</summary>
    Nut,

    /// <summary>Other — free text in <see cref="Participant.AllergyNotes"/> (§4.6 <c>OTHER</c>).</summary>
    Other
}
