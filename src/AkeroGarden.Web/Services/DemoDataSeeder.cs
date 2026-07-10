using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Services;

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
/// <para>All data is synthetic and Swedish — no real PII (NFR-23). Reference data mirrors an actual
/// small Norrtälje retreat; bookings cover every lifecycle state (§2.5) so every admin screen and the
/// public calendar look lived-in. Snapshots are captured faithfully (as a real submission would —
/// NFR-15/16/33/34) and retention dates follow <see cref="RetentionCalculator"/> (FR-91).</para>
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
            "Demo data seeded: {Buildings} buildings, {Rooms} rooms, {Bookings} bookings, {Blocks} blocks. " +
            "Remove the DemoSeed:Reset app setting so the next restart does not wipe again.",
            refData.Buildings.Count, refData.Buildings.Sum(b => b.Rooms.Count),
            db.ChangeTracker.Entries<Booking>().Count(),
            db.ChangeTracker.Entries<BlockedPeriod>().Count());
    }

    // ── Wipe ────────────────────────────────────────────────────────────────────
    // Delete referencing rows before referenced rows so the Restrict FKs (Room, MealOption,
    // Facility, CleaningZone) never block the delete. ExecuteDelete issues a raw bulk DELETE per
    // table. Identity tables (AspNetUsers etc.) are deliberately untouched — the admin survives.
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
    }

    // ── Reference data ────────────────────────────────────────────────────────────
    private sealed record RefData(
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

        // Buildings + rooms — a plausible ~79-bed rural retreat.
        var huvud = new Building
        {
            Id = Guid.NewGuid(),
            Name = "Huvudbyggnaden",
            Description = "Anläggningens största hus med matsal, samlingssal och de flesta sovrummen.",
            SharedFacilities = "Gemensam matsal, två allrum och fullt utrustat kök i bottenvåningen.",
            IsAccessible = true,
            IsActive = true,
            DisplayOrder = 1,
        };
        var annexet = new Building
        {
            Id = Guid.NewGuid(),
            Name = "Annexet",
            Description = "Fristående hus i lugnt läge med egna sovrum och pentry.",
            SharedFacilities = "Pentry och sällskapsrum på nedre plan.",
            IsAccessible = false,
            IsActive = true,
            DisplayOrder = 2,
        };
        var stugan = new Building
        {
            Id = Guid.NewGuid(),
            Name = "Sjöstugan",
            Description = "Mysig stuga närmast vattnet, passar mindre sällskap.",
            SharedFacilities = "Vedeldad kamin och egen altan mot sjön.",
            IsAccessible = false,
            IsActive = true,
            DisplayOrder = 3,
        };

        var rooms = new List<Room>
        {
            Room(huvud, "Rum 1", 4, "2 våningssängar", 1),
            Room(huvud, "Rum 2", 4, "2 våningssängar", 2),
            Room(huvud, "Rum 3", 2, "2 enkelsängar", 3),
            Room(huvud, "Rum 4", 6, "3 våningssängar", 4),
            Room(huvud, "Rum 5", 6, "3 våningssängar", 5),
            Room(huvud, "Familjerummet", 5, "1 dubbelsäng + 1 våningssäng + 1 enkelsäng", 6),
            Room(annexet, "Annex 1", 4, "2 våningssängar", 1),
            Room(annexet, "Annex 2", 4, "2 våningssängar", 2),
            Room(annexet, "Annex 3", 4, "2 våningssängar", 3),
            Room(annexet, "Annex 4", 2, "2 enkelsängar", 4),
            Room(stugan, "Stora rummet", 8, "4 våningssängar", 1),
            Room(stugan, "Lilla rummet", 4, "2 våningssängar", 2),
        };
        huvud.Rooms = rooms.Where(r => r.BuildingId == huvud.Id).ToList();
        annexet.Rooms = rooms.Where(r => r.BuildingId == annexet.Id).ToList();
        stugan.Rooms = rooms.Where(r => r.BuildingId == stugan.Id).ToList();

        var lunches = new List<MealOption>
        {
            Meal("Köttbullar med potatismos och lingon", MealType.Lunch, false, 1,
                "Klassiska svenska köttbullar med krämigt potatismos, gräddsås och lingonsylt."),
            Meal("Vegetarisk lasagne", MealType.Lunch, true, 2,
                "Lasagne med grönsaker och ost, serveras med sallad."),
            Meal("Fiskgratäng med kokt potatis", MealType.Lunch, false, 3,
                "Gratinerad fisk i gräddsås med dill."),
        };
        var dinners = new List<MealOption>
        {
            Meal("Fläskfilé med rostad potatis", MealType.Dinner, false, 1,
                "Helstekt fläskfilé med rostad potatis och pepparsås."),
            Meal("Halloumibiff med couscous", MealType.Dinner, true, 2,
                "Stekt halloumi på örtcouscous med rostade grönsaker."),
            Meal("Ugnsbakad laxfilé med dillslungad potatis", MealType.Dinner, false, 3,
                "Laxfilé ur ugn med citron, serveras med färskpotatis och dillsås."),
        };

        var facilities = new List<Facility>
        {
            Facility("Bastu", "Vedeldad bastu vid sjön med plats för åtta personer.", 500m, VatService, 1),
            Facility("Konferenssal", "Ljus sal för upp till 40 personer, med projektor och whiteboard.", 1200m, VatService, 2),
            Facility("Kanoter och kajaker", "Fem kanoter och tre kajaker med flytvästar ingår.", 300m, VatService, 3),
            Facility("Lekplats och bollplan", "Lekplats för barn samt gräsplan för fotboll och kubb.", 0m, VatService, 4),
        };

        var kok = Zone("Kök", "Storstädning av köket efter vistelsen.", 800m, VatService, 1);
        var samling = Zone("Samlingssal", "Städning av samlingssal och allrum.", 600m, VatService, 2);
        var sovrum = Zone("Sovrum", "Städning av samtliga sovrum.", 900m, VatService, 3);
        var hela = Zone("Hela lägret", "Fullständig slutstädning av hela anläggningen.", 2000m, VatService, 4);
        hela.IncludesZoneIds = [kok.Id, samling.Id, sovrum.Id];
        var zones = new List<CleaningZone> { kok, samling, sovrum, hela };

        var prices = new List<PriceRule>
        {
            Price("Övernattning vuxen", 450m, VatAccommodation, "per person och natt", null),
            Price("Övernattning barn (4–12 år)", 250m, VatAccommodation, "per person och natt", null),
            Price("Övernattning, sommarsäsong (vuxen)", 495m, VatAccommodation, "per person och natt",
                (new DateOnly(2026, 6, 15), new DateOnly(2026, 8, 15))),
            Price("Helpension (frukost, lunch, middag)", 350m, VatFood, "per person och dag", null),
            Price("Dagbesökare med måltider", 195m, VatFood, "per person och dag", null),
            Price("Konferenspaket", 595m, VatService, "per person och dag", null),
        };

        db.Buildings.AddRange(huvud, annexet, stugan);
        db.MealOptions.AddRange(lunches);
        db.MealOptions.AddRange(dinners);
        db.Facilities.AddRange(facilities);
        db.CleaningZones.AddRange(zones);
        db.PriceRules.AddRange(prices);

        return new RefData(
            [huvud, annexet, stugan], rooms, lunches, dinners, facilities, zones);

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
        Facility Facility(string name, string desc, decimal amount, decimal vat, int order) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = desc,
            AmountExVat = amount,
            VatRate = vat,
            IsActive = true,
            DisplayOrder = order,
        };
        CleaningZone Zone(string name, string desc, decimal amount, decimal vat, int order) => new()
        {
            Id = Guid.NewGuid(),
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

        // 1. Pending — a fresh stay request awaiting an admin decision, with selections.
        var pending = StayRequest(
            "Karin Lundgren", "karin.lundgren@example.com", "070-123 45 67",
            today.AddDays(45), today.AddDays(48), overnight: 18, dayGuests: 0,
            MealPlan.FullBoardAll, createdAt: nowUtc.AddDays(-2));
        pending.Status = BookingStatus.Pending;
        pending.Message = "Vi är en syförening som vill boka en helg med helpension. Går det bra med bastun?";
        pending.IsOrganisation = true;
        pending.OrgName = "Roslagens syförening";
        pending.OrgNumber = "802000-1234";
        pending.DeleteAfter = RetentionCalculator.ComputeDeleteAfter(
            BookingStatus.Pending, today.AddDays(-2), pending.CheckOut, today);
        AddFacility(pending, r.Facilities[0]); // Bastu
        AddZone(pending, r.Zones[3]);          // Hela lägret
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
        AddFacility(approved, r.Facilities[1]); // Konferenssal
        db.Bookings.Add(approved);

        // 3. DetailsComplete — guest submitted the completion form (participants + meals + rooms).
        var details = StayRequest(
            "Anna Sjöberg", "anna.sjoberg@example.com", "076-555 11 22",
            today.AddDays(25), today.AddDays(28), overnight: 4, dayGuests: 1,
            MealPlan.FullBoardAll, createdAt: nowUtc.AddDays(-9));
        details.Status = BookingStatus.DetailsComplete;
        details.GuestChoosesRooms = true;
        details.DeleteAfter = null;
        AddFacility(details, r.Facilities[0]);
        AddZone(details, r.Zones[0]);
        var detailsRoom = r.Rooms.First(x => x.Name == "Rum 3"); // 2 beds
        AddOvernight(details, "Anna Sjöberg", AgeCategory.Adult, detailsRoom, [DietaryRequirement.LactoseFree], null, r);
        AddOvernight(details, "Erik Sjöberg", AgeCategory.Adult, detailsRoom, [], null, r);
        var detailsRoom2 = r.Rooms.First(x => x.Name == "Familjerummet");
        AddOvernight(details, "Maja Sjöberg", AgeCategory.Child, detailsRoom2, [DietaryRequirement.NutAllergy], null, r);
        AddOvernight(details, "Olle Sjöberg", AgeCategory.Child, detailsRoom2, [], null, r);
        AddDayGuest(details, "Britt Sjöberg", AgeCategory.Adult, details.CheckIn!.Value.AddDays(1),
            attendsBreakfast: false, [DietaryRequirement.Vegetarian], null, r);
        db.Bookings.Add(details);

        // 4. Active — a group currently on site (check-in past, check-out ahead).
        var active = StayRequest(
            "Peter Nyström", "peter.nystrom@example.com", "070-888 99 00",
            today.AddDays(-1), today.AddDays(2), overnight: 3, dayGuests: 0,
            MealPlan.FullBoardAll, createdAt: nowUtc.AddDays(-20));
        active.Status = BookingStatus.Active;
        active.GuestChoosesRooms = false;
        active.DepositInvoiced = true;
        active.DepositPaid = true;
        active.DeleteAfter = null;
        var activeRoom = r.Rooms.First(x => x.Name == "Annex 1");
        AddOvernight(active, "Peter Nyström", AgeCategory.Adult, activeRoom, [], null, r);
        AddOvernight(active, "Lena Nyström", AgeCategory.Adult, activeRoom, [DietaryRequirement.GlutenFree], null, r);
        AddOvernight(active, "Sven Åkesson", AgeCategory.Adult, activeRoom,
            [DietaryRequirement.Other], "Undviker fläskkött.", r);
        db.Bookings.Add(active);

        // 5. Completed — a finished stay (retention = check-out + 2 years).
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
        var compRoom = r.Rooms.First(x => x.Name == "Stora rummet");
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
                BuildingId = r.Buildings.First(b => b.Name == "Annexet").Id,
                StartDate = today.AddDays(90),
                EndDate = today.AddDays(95),
                Reason = "Målning av Annexet.",
                CreatedAt = nowUtc,
            },
            new BlockedPeriod
            {
                Id = Guid.NewGuid(),
                Scope = BlockScope.Room,
                BuildingId = r.Buildings.First(b => b.Name == "Huvudbyggnaden").Id,
                RoomId = r.Rooms.First(x => x.Name == "Rum 4").Id,
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
