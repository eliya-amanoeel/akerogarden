using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Wipes all application data (keeping the ASP.NET Identity admin users) and re-seeds a realistic
/// synthetic dataset for the Phase 8 client demo / UAT on staging. <b>Demo tooling — not a product
/// feature.</b>
///
/// <para><b>Strongly gated.</b> Only runs when configuration <c>DemoSeed:Reset</c> is <c>true</c>.
/// The flag defaults to false and is never set in the production parameter files, so this can never
/// fire in production. It is applied to staging by hand (<c>az webapp config appsettings set</c> +
/// restart) exactly like the surgical-to-staging changes in earlier phases; remove the flag after a
/// run so a later restart does not wipe again.</para>
///
/// <para>All data is synthetic and Swedish — no real PII (NFR-23). Reference data mirrors the
/// actual Åkerögården property layout (Blidö/Ljusterö/Singö/Tjockö in Lilla bostadslängan; Vätö 1/
/// Vätö 2 in Stora bostadslängan; Smedjan and Tallstugan as standalone cabins — 79 beds total);
/// bookings cover every lifecycle state (§2.5) so every admin screen and the public calendar look
/// lived-in. Snapshots are captured faithfully (as a real submission would — NFR-15/16/33/34) and
/// retention dates follow <see cref="RetentionCalculator"/> (FR-91).</para>
/// </summary>
public class DemoDataSeeder(ApplicationDbContext db, ILogger<DemoDataSeeder> logger)
{
    public async Task ResetAndSeedAsync()
    {
        logger.LogWarning(
            "DemoSeed:Reset=true — WIPING all application data (admin users preserved) and re-seeding demo data.");

        await WipeAsync();

        var refData = SeedReferenceData();
        SeedBookingsAndBlocks(refData);

        await db.SaveChangesAsync();

        // BookingReference (FR-92) needs the DB-assigned SequenceNumber, so it's a second small
        // write after the identity values exist — same two-phase pattern as BookingService.
        foreach (var booking in db.ChangeTracker.Entries<Booking>().Select(e => e.Entity))
            booking.BookingReference = BookingReferencePolicy.Format(booking.SequenceNumber, booking.CreatedAt.Year);
        await db.SaveChangesAsync();

        logger.LogWarning(
            "Demo data seeded: {PhysicalBuildings} physical buildings, {Buildings} buildings, {Rooms} rooms, " +
            "{Bookings} bookings, {Blocks} blocks. " +
            "Remove the DemoSeed:Reset app setting so the next restart does not wipe again.",
            refData.PhysicalBuildings.Count, refData.Buildings.Count, refData.Buildings.Sum(b => b.Rooms.Count),
            db.ChangeTracker.Entries<Booking>().Count(),
            db.ChangeTracker.Entries<BlockedPeriod>().Count());
    }

    // ── Wipe ────────────────────────────────────────────────────────────────────
    // Delete referencing rows before referenced rows so the Restrict FKs (Room, MealOption,
    // Facility, CleaningZone, PhysicalBuilding) never block the delete. ExecuteDelete issues a raw
    // bulk DELETE per table. Identity tables (AspNetUsers etc.) are deliberately untouched — the
    // admin survives.
    private async Task WipeAsync()
    {
        await db.MealPreferences.ExecuteDeleteAsync();
        await db.Participants.ExecuteDeleteAsync();
        await db.BookingFacilitySelections.ExecuteDeleteAsync();
        await db.BookingCleaningSelections.ExecuteDeleteAsync();
        await db.Bookings.ExecuteDeleteAsync();
        await db.BlockedPeriods.ExecuteDeleteAsync();
        await db.Rooms.ExecuteDeleteAsync();
        await db.Buildings.ExecuteDeleteAsync();
        await db.MealOptions.ExecuteDeleteAsync();
        await db.PriceRules.ExecuteDeleteAsync();
        await db.CleaningZones.ExecuteDeleteAsync();
        await db.Facilities.ExecuteDeleteAsync();
        await db.PhysicalBuildings.ExecuteDeleteAsync();
    }

    // ── Reference data ────────────────────────────────────────────────────────────
    private sealed record RefData(
        List<PhysicalBuilding> PhysicalBuildings,
        List<Building> Buildings,
        List<Room> Rooms,
        List<MealOption> Lunches,
        List<MealOption> Dinners,
        List<Facility> Facilities,
        List<CleaningZone> Zones);

    private RefData SeedReferenceData()
    {
        // Accommodation VAT 12 %, food 12 %, other services 25 % (placeholder rates — confirmed
        // with the accountant at go-live, Phase 9; fine for the demo).
        const decimal VatAccommodation = 0.12m;
        const decimal VatFood = 0.12m;
        const decimal VatService = 0.25m;

        // ── Physical buildings (§8.14) — the level cleaning is priced at ──────────────
        var huvudPb = PhysicalBuilding(
            "Huvudbyggnad", "Matsal, samlingsytor och kök — anläggningens gemensamma hjärta.", 1);
        var lillaPb = PhysicalBuilding(
            "Lilla bostadslängan", "Boendelänga med fyra sovsektioner: Blidö, Ljusterö, Singö, Tjockö.", 2);
        var storaPb = PhysicalBuilding(
            "Stora bostadslängan", "Boendelänga med två sovsektioner: Vätö 1 och Vätö 2.", 3);
        var smedjanPb = PhysicalBuilding("Smedjan", "Fristående mindre stuga.", 4);
        var tallstuganPb = PhysicalBuilding("Tallstugan", "Fristående enrumsstuga.", 5);
        var konferensPb = PhysicalBuilding("Konferensbyggnad", "Byggnaden som rymmer konferensrummet.", 6);
        var physicalBuildings = new List<PhysicalBuilding>
            { huvudPb, lillaPb, storaPb, smedjanPb, tallstuganPb, konferensPb };

        // ── Buildings + rooms — the real ~79-bed Åkerögården layout ───────────────────
        var blido = Building(lillaPb, "Blidö", "4 rum, delar allrum, 1 dusch- och städrum, 2 toaletter.",
            "Delar allrum, 1 dusch- och städrum, 2 toaletter.", isAccessible: false, 1);
        var ljustero = Building(lillaPb, "Ljusterö", "4 rum, delar allrum, 1 dusch- och städrum, 1 toalett.",
            "Delar allrum, 1 dusch- och städrum, 1 toalett.", isAccessible: true, 2);
        var singo = Building(lillaPb, "Singö", "4 rum, delar allrum, 1 dusch, 2 toaletter.",
            "Delar allrum, 1 dusch, 2 toaletter.", isAccessible: false, 3);
        var tjocko = Building(lillaPb, "Tjockö", "4 rum, delar allrum, 1 dusch- och städrum, 2 toaletter.",
            "Delar allrum, 1 dusch- och städrum, 2 toaletter.", isAccessible: false, 4);
        var vato1 = Building(storaPb, "Vätö 1", "5 rum, delar allrum, 2 dusch- och städrum, 2 toaletter.",
            "Delar allrum, 2 dusch- och städrum, 2 toaletter.", isAccessible: true, 5);
        var vato2 = Building(storaPb, "Vätö 2", "5 rum, delar allrum, 2 dusch- och städrum, 2 toaletter.",
            "Delar allrum, 2 dusch- och städrum, 2 toaletter.", isAccessible: true, 6);
        var smedjanB = Building(smedjanPb, "Smedjan", "Liten stuga med eget rum.",
            "Eget dusch- och städrum.", isAccessible: false, 7);
        var tallstuganB = Building(tallstuganPb, "Tallstugan", "1 rum med dusch och toalett.",
            "Eget dusch och toalett.", isAccessible: false, 8);
        var buildings = new List<Building> { blido, ljustero, singo, tjocko, vato1, vato2, smedjanB, tallstuganB };

        var rooms = new List<Room>
        {
            Room(blido, "Rum 1", 4, "2 våningssängar", 1),
            Room(blido, "Rum 2", 2, "2 enkelsängar", 2),
            Room(blido, "Rum 3", 3, "3 enkelsängar", 3),
            Room(blido, "Rum 4", 3, "3 enkelsängar", 4),

            Room(ljustero, "Rum 1", 3, "3 enkelsängar", 1),
            Room(ljustero, "Rum 2", 6, "3 våningssängar", 2),
            Room(ljustero, "Rum 3", 4, "2 våningssängar", 3),
            Room(ljustero, "Rum 4", 4, "2 våningssängar", 4),

            Room(singo, "Rum 1", 3, "3 enkelsängar", 1),
            Room(singo, "Rum 2", 5, "2 våningssängar & 1 enkelsäng", 2),
            Room(singo, "Rum 3", 4, "1 våningssäng & 2 enkelsängar", 3),
            Room(singo, "Rum 4", 4, "1 våningssäng & 2 enkelsängar", 4),

            Room(tjocko, "Rum 1", 3, "3 enkelsängar", 1),
            Room(tjocko, "Rum 2", 2, "2 enkelsängar", 2),
            Room(tjocko, "Rum 3", 3, "3 enkelsängar", 3),
            Room(tjocko, "Rum 4", 2, "2 enkelsängar", 4),

            Room(vato1, "Rum 1", 2, "2 enkelsängar", 1),
            Room(vato1, "Rum 2", 2, "2 enkelsängar", 2),
            Room(vato1, "Rum 3", 1, "1 enkelsäng", 3),
            Room(vato1, "Rum 4", 1, "1 enkelsäng", 4),
            Room(vato1, "Rum 5", 2, "2 enkelsängar", 5),

            Room(vato2, "Rum 6", 2, "2 enkelsängar", 1),
            Room(vato2, "Rum 7", 1, "1 enkelsäng", 2),
            Room(vato2, "Rum 8", 1, "1 enkelsäng", 3),
            Room(vato2, "Rum 9", 2, "2 enkelsängar", 4),
            Room(vato2, "Rum 10", 2, "2 enkelsängar", 5),

            Room(smedjanB, "Rum 1", 4, "2 våningssängar", 1),

            Room(tallstuganB, "Rum 1", 4, "2 våningssängar & 2 enkelsängar", 1),
        };
        foreach (var b in buildings)
            b.Rooms = rooms.Where(r => r.BuildingId == b.Id).ToList();

        // Real Åkerögården menu (client-provided). "Vegetarisk meny" is a single standing
        // alternative offered alongside every lunch/dinner, so it's seeded once per meal type.
        var lunches = new List<MealOption>
        {
            Meal("Panerad fisk med dansk remouladsås", MealType.Lunch, false, 1,
                "Panerad fisk med dansk remouladsås, kokta grönsaker och kokt potatis."),
            Meal("Ugnsstekt kycklingklubba", MealType.Lunch, false, 2,
                "Ugnsstekt kycklingklubba med tzatziki och bulgur."),
            Meal("Spaghetti bolognese", MealType.Lunch, false, 3,
                "Spaghetti bolognese."),
            Meal("Köttbullar med potatismos och gräddsås", MealType.Lunch, false, 4,
                "Köttbullar med potatismos och gräddsås."),
            Meal("Wokade grönsaker med nudlar", MealType.Lunch, true, 5,
                "Vegetarisk meny: wokade grönsaker med nudlar."),
        };
        var dinners = new List<MealOption>
        {
            Meal("Grillad kycklingfilé med champinjonsås", MealType.Dinner, false, 1,
                "Grillad kycklingfilé med champinjonsås, stekta grönsaker och klyftpotatis."),
            Meal("Hamburgare med pommes", MealType.Dinner, false, 2,
                "Hamburgare med pommes."),
            Meal("Korv med bröd eller potatismos", MealType.Dinner, false, 3,
                "Korv med bröd eller potatismos."),
            Meal("Pannkakor med sylt och grädde", MealType.Dinner, false, 4,
                "Pannkakor med sylt och grädde."),
            Meal("Wokade grönsaker med nudlar", MealType.Dinner, true, 5,
                "Vegetarisk meny: wokade grönsaker med nudlar."),
        };

        // Facilities — the client's actual "Lokaler" price list. Only Konferensrum carries a
        // PhysicalBuildingId — it's the only facility that puts a building's cleaning in play
        // (§8.8, §5.5). Husvagn/Tält are per-pitch fees, also billed under Lokaler.
        var facilities = new List<Facility>
        {
            Facility(null, "Kapellet", "Litet kapell för vigslar och andakt, plats för ca 30 personer.", 1000m, VatService, 1),
            Facility(konferensPb, "Konferensrum", "Ljust konferensrum för upp till 40 personer, med projektor och whiteboard.", 1000m, VatService, 2),
            Facility(null, "Utflykt - gården", "I utflykt ingår gården, lilla matsalen och toalett.", 2000m, VatService, 3),
            Facility(null, "Husvagn", "Husvagnsplats på gården.", 300m, VatService, 4),
            Facility(null, "Tält", "Tältplats på gården.", 250m, VatService, 5),
        };

        // Cleaning zones (§5.5) — one per physical building, priced per building never per room,
        // plus the Hela lägret bundle covering the four 2 000 kr zones (4 × 2 000 = 8 000).
        var huvudZone = Zone(huvudPb, CleaningZoneKind.MainBuilding, "Huvudbyggnad",
            "Städning av matsal, kök och gemensamma ytor i huvudbyggnaden.", 2000m, VatService, 1);
        var lillaZone = Zone(lillaPb, CleaningZoneKind.SleepingBuilding, "Lilla bostadslängan",
            "Städning av hela Lilla bostadslängan (Blidö, Ljusterö, Singö, Tjockö).", 2000m, VatService, 2);
        var storaZone = Zone(storaPb, CleaningZoneKind.SleepingBuilding, "Stora bostadslängan",
            "Städning av hela Stora bostadslängan (Vätö 1, Vätö 2).", 2000m, VatService, 3);
        var smedjanZone = Zone(smedjanPb, CleaningZoneKind.SleepingBuilding, "Smedjan",
            "Städning av Smedjan.", 500m, VatService, 4);
        var tallstuganZone = Zone(tallstuganPb, CleaningZoneKind.SleepingBuilding, "Tallstugan",
            "Städning av Tallstugan.", 500m, VatService, 5);
        var konferensZone = Zone(konferensPb, CleaningZoneKind.Conference, "Konferensbyggnad",
            "Städning av konferensbyggnaden.", 2000m, VatService, 6);
        var helaZone = Zone(null, CleaningZoneKind.Everything, "Hela lägret",
            "Fullständig slutstädning av hela anläggningen — rate-card-referens, inte ett gästval.", 8000m, VatService, 7);
        helaZone.IncludesZoneIds = [huvudZone.Id, lillaZone.Id, storaZone.Id, konferensZone.Id];
        var zones = new List<CleaningZone>
            { huvudZone, lillaZone, storaZone, smedjanZone, tallstuganZone, konferensZone, helaZone };

        // Real Åkerögården rate card (client-provided). No age-tiered discount beyond the
        // system's own Infant-free rule (0–3 år) — 4 år and over all bill at the standard rate.
        var prices = new List<PriceRule>
        {
            Price("Lägerlogi med mat (helpension)", 550m, VatAccommodation, "per person och dygn", null),
            Price("Lägerlogi utan mat", 250m, VatAccommodation, "per person och dygn", null),
            Price("Stuga självhushåll (Smedjan/Tallstugan)", 1500m, VatAccommodation, "per dygn", null),
            Price("Stuga självhushåll (Smedjan/Tallstugan), hel vecka", 7000m, VatAccommodation, "per vecka", null),
            Price("Köksanvändning vid logi utan mat", 100m, VatService, "per person och dag", null),
        };

        db.PhysicalBuildings.AddRange(physicalBuildings);
        db.Buildings.AddRange(buildings);
        db.MealOptions.AddRange(lunches);
        db.MealOptions.AddRange(dinners);
        db.Facilities.AddRange(facilities);
        db.CleaningZones.AddRange(zones);
        db.PriceRules.AddRange(prices);

        return new RefData(physicalBuildings, buildings, rooms, lunches, dinners, facilities, zones);

        PhysicalBuilding PhysicalBuilding(string name, string desc, int order) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = desc,
            IsActive = true,
            DisplayOrder = order,
        };
        Building Building(PhysicalBuilding pb, string name, string desc, string shared, bool isAccessible, int order) => new()
        {
            Id = Guid.NewGuid(),
            PhysicalBuildingId = pb.Id,
            Name = name,
            Description = desc,
            SharedFacilities = shared,
            IsAccessible = isAccessible,
            IsActive = true,
            DisplayOrder = order,
        };
        Room Room(Building b, string name, int beds, string config, int order) => new()
        {
            Id = Guid.NewGuid(),
            BuildingId = b.Id,
            Name = name,
            BedCount = beds,
            BedConfiguration = config,
            IsActive = true,
            DisplayOrder = order,
        };
        MealOption Meal(string name, MealType type, bool veg, int order, string desc) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = desc,
            MealType = type,
            IsVegetarian = veg,
            IsActive = true,
            DisplayOrder = order,
        };
        Facility Facility(PhysicalBuilding? pb, string name, string desc, decimal amount, decimal vat, int order) => new()
        {
            Id = Guid.NewGuid(),
            PhysicalBuildingId = pb?.Id,
            Name = name,
            Description = desc,
            AmountExVat = amount,
            VatRate = vat,
            IsActive = true,
            DisplayOrder = order,
        };
        CleaningZone Zone(PhysicalBuilding? pb, CleaningZoneKind kind, string name, string desc, decimal amount, decimal vat, int order) => new()
        {
            Id = Guid.NewGuid(),
            PhysicalBuildingId = pb?.Id,
            Kind = kind,
            Name = name,
            Description = desc,
            AmountExVat = amount,
            VatRate = vat,
            IsActive = true,
            DisplayOrder = order,
        };
        PriceRule Price(string label, decimal amount, decimal vat, string unit, (DateOnly, DateOnly)? season) => new()
        {
            Id = Guid.NewGuid(),
            Label = label,
            AmountExVat = amount,
            VatRate = vat,
            Unit = unit,
            SeasonStart = season?.Item1,
            SeasonEnd = season?.Item2,
            IsActive = true,
        };
    }

    // ── Bookings + blocks ─────────────────────────────────────────────────────────
    private void SeedBookingsAndBlocks(RefData r)
    {
        var nowUtc = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(nowUtc);

        var utflykt = r.Facilities.First(f => f.Name == "Utflykt - gården");
        var konferensrum = r.Facilities.First(f => f.Name == "Konferensrum");

        var huvudZone = r.Zones.First(z => z.Name == "Huvudbyggnad");
        var lillaZone = r.Zones.First(z => z.Name == "Lilla bostadslängan");

        var tjocko = r.Buildings.First(b => b.Name == "Tjockö");
        var singo = r.Buildings.First(b => b.Name == "Singö");
        var ljustero = r.Buildings.First(b => b.Name == "Ljusterö");
        var blido = r.Buildings.First(b => b.Name == "Blidö");

        // 1. Pending — a fresh stay request awaiting an admin decision, with selections.
        // Cleaning is not chosen at this stage under the new model (FR-93) — it is a
        // completion-form choice, and this booking has not reached completion yet.
        var pending = StayRequest(
            "Karin Lundgren", "karin.lundgren@example.com", "070-123 45 67",
            today.AddDays(45), today.AddDays(48), overnight: 18, dayGuests: 0,
            MealPlan.FullBoardAll, createdAt: nowUtc.AddDays(-2));
        pending.Status = BookingStatus.Pending;
        pending.Message = "Vi är en syförening som vill boka en helg med helpension. Går det bra med en utflykt till gården?";
        pending.IsOrganisation = true;
        pending.OrgName = "Roslagens syförening";
        pending.OrgNumber = "802000-1234";
        pending.DeleteAfter = RetentionCalculator.ComputeDeleteAfter(
            BookingStatus.Pending, today.AddDays(-2), pending.CheckOut, today);
        AddFacility(pending, utflykt);
        db.Bookings.Add(pending);

        // 2. Approved — token issued, guest yet to complete details (valid magic link).
        var approved = StayRequest(
            "Johan Bergström", "johan.bergstrom@example.com", "073-222 33 44",
            today.AddDays(30), today.AddDays(33), overnight: 24, dayGuests: 6,
            MealPlan.FullBoardAll, createdAt: nowUtc.AddDays(-6));
        approved.Status = BookingStatus.Approved;
        approved.GuestChoosesRooms = true;
        approved.CompletionToken = CompletionTokenPolicy.GenerateToken();
        approved.CompletionTokenExpiry = CompletionTokenPolicy.ComputeExpiry(nowUtc, approved.CheckIn!.Value);
        approved.Message = "Konfirmationsläger för församlingen.";
        approved.AdminNotes = "Ringde och bekräftade antal – återkommer med matval via länken.";
        approved.DeleteAfter = null; // live state
        AddFacility(approved, konferensrum);
        db.Bookings.Add(approved);

        // 3. DetailsComplete — guest submitted the completion form (participants + meals + rooms +
        // cleaning, FR-93). Occupies rooms in both Tjockö and Singö — both map to the same Lilla
        // bostadslängan zone, so this demonstrates the "one row per distinct occupied zone, never
        // per room" rule (§8.12): two occupied lodging units, one cleaning charge.
        var details = StayRequest(
            "Anna Sjöberg", "anna.sjoberg@example.com", "076-555 11 22",
            today.AddDays(25), today.AddDays(28), overnight: 4, dayGuests: 1,
            MealPlan.FullBoardAll, createdAt: nowUtc.AddDays(-9));
        details.Status = BookingStatus.DetailsComplete;
        details.GuestChoosesRooms = true;
        details.DeleteAfter = null;
        details.MainBuildingCleaningPurchased = true;
        details.SleepingCleaningPurchased = true;
        AddFacility(details, r.Facilities.First(f => f.Name == "Kapellet"));
        AddZone(details, huvudZone);
        AddZone(details, lillaZone);
        var detailsRoom = r.Rooms.First(x => x.BuildingId == tjocko.Id && x.Name == "Rum 2"); // 2 beds
        AddOvernight(details, "Anna Sjöberg", AgeCategory.Adult, detailsRoom, [DietaryRequirement.LactoseFree], null, r);
        AddOvernight(details, "Erik Sjöberg", AgeCategory.Adult, detailsRoom, [], null, r);
        var detailsRoom2 = r.Rooms.First(x => x.BuildingId == singo.Id && x.Name == "Rum 2"); // 5 beds
        AddOvernight(details, "Maja Sjöberg", AgeCategory.Child, detailsRoom2, [DietaryRequirement.NutAllergy], null, r);
        AddOvernight(details, "Olle Sjöberg", AgeCategory.Child, detailsRoom2, [], null, r);
        AddDayGuest(details, "Britt Sjöberg", AgeCategory.Adult, details.CheckIn!.Value.AddDays(1),
            attendsBreakfast: false, [DietaryRequirement.Vegetarian], null, r);
        db.Bookings.Add(details);

        // 4. Active — a group currently on site (check-in past, check-out ahead). Completed
        // details but chose the default self-clean for both areas (§5.5) — demonstrates the
        // "guest cleans themselves" default path explicitly rather than leaving it ambiguous.
        var active = StayRequest(
            "Peter Nyström", "peter.nystrom@example.com", "070-888 99 00",
            today.AddDays(-1), today.AddDays(2), overnight: 3, dayGuests: 0,
            MealPlan.FullBoardAll, createdAt: nowUtc.AddDays(-20));
        active.Status = BookingStatus.Active;
        active.GuestChoosesRooms = false;
        active.DepositInvoiced = true;
        active.DepositPaid = true;
        active.DeleteAfter = null;
        active.MainBuildingCleaningPurchased = false;
        active.SleepingCleaningPurchased = false;
        var activeRoom = r.Rooms.First(x => x.BuildingId == ljustero.Id && x.Name == "Rum 3"); // 4 beds
        AddOvernight(active, "Peter Nyström", AgeCategory.Adult, activeRoom, [], null, r);
        AddOvernight(active, "Lena Nyström", AgeCategory.Adult, activeRoom, [DietaryRequirement.GlutenFree], null, r);
        AddOvernight(active, "Sven Åkesson", AgeCategory.Adult, activeRoom,
            [DietaryRequirement.Other], "Undviker fläskkött.", r);
        db.Bookings.Add(active);

        // 5. Completed — a finished stay (retention = check-out + 2 years). Purchased both
        // Huvudbyggnad and sleeping-building cleaning.
        var completed = StayRequest(
            "Margareta Holm", "margareta.holm@example.com", "073-111 22 33",
            today.AddDays(-30), today.AddDays(-27), overnight: 5, dayGuests: 2,
            MealPlan.FullBoardAll, createdAt: nowUtc.AddDays(-60));
        completed.Status = BookingStatus.Completed;
        completed.GuestChoosesRooms = true;
        completed.DepositInvoiced = true;
        completed.DepositPaid = true;
        completed.FinalInvoiced = true;
        completed.FinalPaid = true;
        completed.DeleteAfter = RetentionCalculator.ComputeDeleteAfter(
            BookingStatus.Completed, today.AddDays(-60), completed.CheckOut, today.AddDays(-27));
        completed.MainBuildingCleaningPurchased = true;
        completed.SleepingCleaningPurchased = true;
        AddZone(completed, huvudZone);
        AddZone(completed, lillaZone);
        var compRoom = r.Rooms.First(x => x.BuildingId == ljustero.Id && x.Name == "Rum 2"); // 6 beds
        AddOvernight(completed, "Margareta Holm", AgeCategory.Adult, compRoom, [], null, r);
        AddOvernight(completed, "Gustav Holm", AgeCategory.Adult, compRoom, [], null, r);
        AddOvernight(completed, "Elin Holm", AgeCategory.Adult, compRoom, [DietaryRequirement.Vegan], null, r);
        AddOvernight(completed, "Nils Holm", AgeCategory.Child, compRoom, [], null, r);
        AddOvernight(completed, "Alva Holm", AgeCategory.Child, compRoom, [], null, r);
        AddDayGuest(completed, "Kerstin Ek", AgeCategory.Adult, completed.CheckIn!.Value.AddDays(1),
            attendsBreakfast: true, [], null, r);
        AddDayGuest(completed, "Bengt Ek", AgeCategory.Adult, completed.CheckIn!.Value.AddDays(1),
            attendsBreakfast: true, [DietaryRequirement.GlutenFree], null, r);
        db.Bookings.Add(completed);

        // 6. Denied — declined with a required reason (retention = denial date + 12 months).
        var denied = StayRequest(
            "Fredrik Ohlsson", "fredrik.ohlsson@example.com", "070-333 44 55",
            today.AddDays(20), today.AddDays(22), overnight: 60, dayGuests: 0,
            MealPlan.SelfCateringAll, createdAt: nowUtc.AddDays(-10));
        denied.Status = BookingStatus.Denied;
        denied.DenialReason = "Tyvärr är anläggningen redan bokad de aktuella datumen. Välkommen åter!";
        denied.DeleteAfter = RetentionCalculator.ComputeDeleteAfter(
            BookingStatus.Denied, today.AddDays(-10), denied.CheckOut, today.AddDays(-8));
        db.Bookings.Add(denied);

        // 7. Cancelled — a guest-initiated cancellation (retention = cancellation date + 12 months).
        var cancelled = StayRequest(
            "Sara Lind", "sara.lind@example.com", "076-777 88 99",
            today.AddDays(14), today.AddDays(16), overnight: 12, dayGuests: 0,
            MealPlan.FullBoardAll, createdAt: nowUtc.AddDays(-15));
        cancelled.Status = BookingStatus.Cancelled;
        cancelled.CancelledAt = nowUtc.AddDays(-3);
        cancelled.CancellationInitiator = CancellationInitiator.GuestInitiated;
        cancelled.CancellationReason = "Gruppen fick förhinder och avbokar.";
        cancelled.DepositInvoiced = true;
        cancelled.DeleteAfter = RetentionCalculator.ComputeDeleteAfter(
            BookingStatus.Cancelled, today.AddDays(-15), cancelled.CheckOut, today.AddDays(-3));
        db.Bookings.Add(cancelled);

        // 8. Closed — a general inquiry that was answered and closed (retention = submission + 12 months).
        var closed = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = $"AG-SEED-{Guid.NewGuid():N}"[..20],
            BookingType = BookingType.GeneralInquiry,
            Status = BookingStatus.Closed,
            ContactName = "Ingrid Persson",
            ContactEmail = "ingrid.persson@example.com",
            ContactPhone = "070-444 55 66",
            Message = "Hej! Har ni möjlighet att ta emot dagsbesök för en pensionärsförening i höst?",
            AdminNotes = "Svarade via e-post – hänvisade till bokningsformuläret.",
            CreatedAt = nowUtc.AddDays(-20),
            UpdatedAt = nowUtc.AddDays(-18),
            DeleteAfter = RetentionCalculator.ComputeDeleteAfter(
                BookingStatus.Closed, today.AddDays(-20), null, today.AddDays(-18)),
        };
        db.Bookings.Add(closed);

        // Note: Husvagn and Tält are seeded but not used by any demo booking above — kept
        // available for manual admin testing of facility selection on the inquiry form.

        // Blocks — make the public calendar and admin block views non-empty.
        db.BlockedPeriods.AddRange(
            new BlockedPeriod
            {
                Id = Guid.NewGuid(),
                Scope = BlockScope.WholeProperty,
                StartDate = today.AddDays(60),
                EndDate = today.AddDays(62),
                Reason = "Internt underhåll – anläggningen stängd.",
                CreatedAt = nowUtc,
            },
            new BlockedPeriod
            {
                Id = Guid.NewGuid(),
                Scope = BlockScope.Building,
                BuildingId = ljustero.Id,
                StartDate = today.AddDays(90),
                EndDate = today.AddDays(95),
                Reason = "Målning av Ljusterö.",
                CreatedAt = nowUtc,
            },
            new BlockedPeriod
            {
                Id = Guid.NewGuid(),
                Scope = BlockScope.Room,
                BuildingId = blido.Id,
                RoomId = r.Rooms.First(x => x.BuildingId == blido.Id && x.Name == "Rum 4").Id,
                StartDate = today.AddDays(10),
                EndDate = today.AddDays(20),
                Reason = "Renovering – trasigt fönster.",
                CreatedAt = nowUtc,
            });

        Booking StayRequest(
            string name, string email, string? phone, DateOnly checkIn, DateOnly checkOut,
            int overnight, int dayGuests, MealPlan plan, DateTime createdAt) => new()
            {
                Id = Guid.NewGuid(),
                // Placeholder — all 8 demo bookings insert in one batch below, so each needs a distinct
                // value up front to satisfy the unique index; overwritten with the real AG-YYYY-NNNN
                // form (from the DB-assigned SequenceNumber) after that first save.
                BookingReference = $"AG-SEED-{Guid.NewGuid():N}"[..20],
                BookingType = BookingType.StayRequest,
                ContactName = name,
                ContactEmail = email,
                ContactPhone = phone,
                CheckIn = checkIn,
                CheckOut = checkOut,
                OvernightGuestCount = overnight,
                DayGuestCount = dayGuests,
                MealPlan = plan,
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
            };
    }

    private static void AddFacility(Booking b, Facility f) =>
        b.FacilitySelections.Add(new BookingFacilitySelection
        {
            Id = Guid.NewGuid(),
            FacilityId = f.Id,
            FacilityNameSnapshot = f.Name,
            AmountExVatSnapshot = f.AmountExVat,
            VatRateSnapshot = f.VatRate,
        });

    private static void AddZone(Booking b, CleaningZone z) =>
        b.CleaningSelections.Add(new BookingCleaningSelection
        {
            Id = Guid.NewGuid(),
            CleaningZoneId = z.Id,
            ZoneNameSnapshot = z.Name,
            AmountExVatSnapshot = z.AmountExVat,
            VatRateSnapshot = z.VatRate,
        });

    // Overnight participant with full-board meals following the §4.1 meals-by-day rule and a room
    // assignment snapshot (as a real completion submission would capture — NFR-15/16).
    private void AddOvernight(
        Booking b, string name, AgeCategory age, Room room,
        List<DietaryRequirement> dietary, string? dietaryNotes, RefData r)
    {
        var p = new Participant
        {
            Id = Guid.NewGuid(),
            FullName = name,
            ParticipantType = ParticipantType.Overnight,
            AgeCategory = age,
            RoomId = room.Id,
            RoomAssignmentSnapshot = $"{room.Name} ({r.Buildings.First(x => x.Id == room.BuildingId).Name})",
            IsFullBoard = true,
            AttendsBreakfast = true,
            DietaryRequirements = dietary,
            DietaryNotes = dietaryNotes,
        };

        foreach (var slot in MealsByDayCalculator.Slots(b.CheckIn!.Value, b.CheckOut!.Value))
        {
            var option = PickMeal(slot.MealType, dietary, r);
            p.MealPreferences.Add(new MealPreference
            {
                Id = Guid.NewGuid(),
                Date = slot.Date,
                MealType = slot.MealType,
                MealOptionId = option.Id,
                MealOptionSnapshot = option.Name,
            });
        }

        b.Participants.Add(p);
    }

    private void AddDayGuest(
        Booking b, string name, AgeCategory age, DateOnly attendanceDate,
        bool attendsBreakfast, List<DietaryRequirement> dietary, string? dietaryNotes, RefData r)
    {
        var p = new Participant
        {
            Id = Guid.NewGuid(),
            FullName = name,
            ParticipantType = ParticipantType.DayGuest,
            AgeCategory = age,
            AttendanceDate = attendanceDate,
            IsFullBoard = false,
            AttendsBreakfast = attendsBreakfast,
            DietaryRequirements = dietary,
            DietaryNotes = dietaryNotes,
        };

        // A day guest on a middle day attends lunch and dinner.
        var served = MealsByDayCalculator.ServedOn(attendanceDate, b.CheckIn!.Value, b.CheckOut!.Value);
        if (served.Lunch)
            AddDayMeal(p, attendanceDate, MealType.Lunch, dietary, r);
        if (served.Dinner)
            AddDayMeal(p, attendanceDate, MealType.Dinner, dietary, r);

        b.Participants.Add(p);
    }

    private void AddDayMeal(Participant p, DateOnly date, MealType type, List<DietaryRequirement> dietary, RefData r)
    {
        var option = PickMeal(type, dietary, r);
        p.MealPreferences.Add(new MealPreference
        {
            Id = Guid.NewGuid(),
            Date = date,
            MealType = type,
            MealOptionId = option.Id,
            MealOptionSnapshot = option.Name,
        });
    }

    // Pick a plausible option: vegetarian/vegan participants get the vegetarian option.
    private static MealOption PickMeal(MealType type, List<DietaryRequirement> dietary, RefData r)
    {
        var options = type == MealType.Lunch ? r.Lunches : r.Dinners;
        var wantsVeg = dietary.Contains(DietaryRequirement.Vegetarian)
            || dietary.Contains(DietaryRequirement.Vegan);
        return wantsVeg
            ? options.First(o => o.IsVegetarian)
            : options[0];
    }
}
