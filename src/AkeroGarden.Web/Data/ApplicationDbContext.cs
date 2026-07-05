using AkeroGarden.Web.Data.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AkeroGarden.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    // Application entities are added incrementally per phase (implementation-plan.md).
    // Phase 1 slice 1: reference data — Building, Room.
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Room> Rooms => Set<Room>();

    // Phase 1 slice 2: reference data — MealOption.
    public DbSet<MealOption> MealOptions => Set<MealOption>();

    // Phase 1 slice 3: reference data — PriceRule.
    public DbSet<PriceRule> PriceRules => Set<PriceRule>();

    // Phase 1 slice 4: reference data — CleaningZone.
    public DbSet<CleaningZone> CleaningZones => Set<CleaningZone>();

    // Phase 1 slice 5: reference data — Facility.
    public DbSet<Facility> Facilities => Set<Facility>();

    // Phase 3 slice 1: booking lifecycle — Booking + selections, BlockedPeriod.
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingFacilitySelection> BookingFacilitySelections => Set<BookingFacilitySelection>();
    public DbSet<BookingCleaningSelection> BookingCleaningSelections => Set<BookingCleaningSelection>();
    public DbSet<BlockedPeriod> BlockedPeriods => Set<BlockedPeriod>();

    // Phase 4 slice 1: completion flow — Participant + MealPreference.
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<MealPreference> MealPreferences => Set<MealPreference>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Building>(b =>
        {
            b.HasMany(x => x.Rooms)
                .WithOne(r => r.Building!)
                .HasForeignKey(r => r.BuildingId)
                // Restrict: a building with rooms cannot be hard-deleted at the DB level.
                // The app-level guard (BuildingService) blocks it first with a Swedish message;
                // this is defence in depth, consistent with the no-hard-delete-of-referenced rule.
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MealOption>(m =>
        {
            // Persist enums as strings: legible in the DB and stable against reordering.
            m.Property(x => x.MealType)
                .HasConversion<string>()
                .HasMaxLength(20);
        });

        builder.Entity<PriceRule>(p =>
        {
            // Reference figures only — no totals are computed from them (C8).
            p.Property(x => x.AmountExVat).HasPrecision(18, 2);
            p.Property(x => x.VatRate).HasPrecision(5, 4); // fraction, e.g. 0.1200
        });

        builder.Entity<CleaningZone>(c =>
        {
            c.Property(x => x.AmountExVat).HasPrecision(18, 2);
            c.Property(x => x.VatRate).HasPrecision(5, 4);
            // IncludesZoneIds maps to a native PostgreSQL uuid[] column (Npgsql).
        });

        builder.Entity<Facility>(f =>
        {
            // Reference figures only, mirroring CleaningZone/PriceRule — no totals (C8).
            f.Property(x => x.AmountExVat).HasPrecision(18, 2);
            f.Property(x => x.VatRate).HasPrecision(5, 4); // fraction, e.g. 0.2500
        });

        // ── Phase 3: booking lifecycle ──────────────────────────────────────────
        builder.Entity<Booking>(b =>
        {
            // Enums persisted as strings: legible in the DB and stable against reordering.
            b.Property(x => x.BookingType).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.MealPlan).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.CancellationInitiator).HasConversion<string>().HasMaxLength(30);

            // Filter the booking list by status and type without scanning (FR-36).
            b.HasIndex(x => x.Status);
            b.HasIndex(x => x.BookingType);
            // The retention job (Phase 5) scans due records by DeleteAfter (FR-69/91).
            b.HasIndex(x => x.DeleteAfter);
            // The 14-day reminder job (Phase 5) scans upcoming stays by CheckIn (FR-63).
            b.HasIndex(x => x.CheckIn);

            // Deleting a booking removes its selection children (retention deletion, Phase 5).
            b.HasMany(x => x.FacilitySelections)
                .WithOne(s => s.Booking!)
                .HasForeignKey(s => s.BookingId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.CleaningSelections)
                .WithOne(s => s.Booking!)
                .HasForeignKey(s => s.BookingId)
                .OnDelete(DeleteBehavior.Cascade);
            // Participants are children of the booking; retention deletion (Phase 5) cascades.
            b.HasMany(x => x.Participants)
                .WithOne(p => p.Booking!)
                .HasForeignKey(p => p.BookingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<BookingFacilitySelection>(s =>
        {
            s.Property(x => x.AmountExVatSnapshot).HasPrecision(18, 2);
            s.Property(x => x.VatRateSnapshot).HasPrecision(5, 4);
            s.Property(x => x.FacilityNameSnapshot).HasMaxLength(200);

            // Live FK to Facility. Restrict at the DB level so a facility referenced by a
            // booking selection cannot be hard-deleted (FR-30) — defence in depth behind the
            // app-level guard (FacilityService.CanHardDelete).
            s.HasOne(x => x.Facility!)
                .WithMany()
                .HasForeignKey(x => x.FacilityId)
                .OnDelete(DeleteBehavior.Restrict);
            s.HasIndex(x => x.FacilityId);
        });

        builder.Entity<BookingCleaningSelection>(s =>
        {
            s.Property(x => x.AmountExVatSnapshot).HasPrecision(18, 2);
            s.Property(x => x.VatRateSnapshot).HasPrecision(5, 4);
            s.Property(x => x.ZoneNameSnapshot).HasMaxLength(200);

            // Live FK to CleaningZone; Restrict so a referenced zone cannot be hard-deleted
            // (FR-88) — defence in depth behind CleaningZoneService.CanHardDelete.
            s.HasOne(x => x.CleaningZone!)
                .WithMany()
                .HasForeignKey(x => x.CleaningZoneId)
                .OnDelete(DeleteBehavior.Restrict);
            s.HasIndex(x => x.CleaningZoneId);
        });

        builder.Entity<BlockedPeriod>(p =>
        {
            p.Property(x => x.Scope).HasConversion<string>().HasMaxLength(20);
            // BuildingId/RoomId are logical references validated in the service layer, not
            // enforced FKs (see BlockedPeriod remarks) — no relationship configured on purpose.
            // The public calendar and admin views query blocks by date range (FR-10/37).
            p.HasIndex(x => new { x.StartDate, x.EndDate });
        });

        // ── Phase 4: completion flow ────────────────────────────────────────────
        // Multi-select dietary requirements persist as a comma-joined string of enum names —
        // legible, and never queried in a WHERE, so no join table or PG array is warranted.
        var dietaryConverter = new ValueConverter<List<DietaryRequirement>, string>(
            v => string.Join(',', v.Select(d => d.ToString())),
            v => string.IsNullOrEmpty(v)
                ? new List<DietaryRequirement>()
                : v.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(Enum.Parse<DietaryRequirement>)
                    .ToList());
        // EF needs a value comparer for a mutable reference-typed property to track edits correctly.
        var dietaryComparer = new ValueComparer<List<DietaryRequirement>>(
            (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
            v => v.Aggregate(0, (acc, d) => HashCode.Combine(acc, d.GetHashCode())),
            v => v.ToList());

        builder.Entity<Participant>(p =>
        {
            // Enums persisted as strings: legible in the DB and stable against reordering.
            p.Property(x => x.ParticipantType).HasConversion<string>().HasMaxLength(20);
            p.Property(x => x.AgeCategory).HasConversion<string>().HasMaxLength(20);
            p.Property(x => x.FullName).HasMaxLength(200);
            p.Property(x => x.RoomAssignmentSnapshot).HasMaxLength(300);
            p.Property(x => x.DietaryNotes).HasMaxLength(200);

            p.Property(x => x.DietaryRequirements)
                .HasConversion(dietaryConverter)
                .Metadata.SetValueComparer(dietaryComparer);

            // Live FK → Room; Restrict so a room referenced by a participant cannot be
            // hard-deleted (FR-20) — defence in depth behind RoomService.CanHardDelete.
            p.HasOne(x => x.Room)
                .WithMany()
                .HasForeignKey(x => x.RoomId)
                .OnDelete(DeleteBehavior.Restrict);

            // Query participants by booking (completion + admin views) and by room (conflict
            // detection / exclusion / the FR-20 guard count).
            p.HasIndex(x => x.BookingId);
            p.HasIndex(x => x.RoomId);
        });

        builder.Entity<MealPreference>(m =>
        {
            m.Property(x => x.MealType).HasConversion<string>().HasMaxLength(20);
            m.Property(x => x.MealOptionSnapshot).HasMaxLength(200);

            // Meal preferences cascade with their participant (retention deletion, Phase 5).
            m.HasOne(x => x.Participant!)
                .WithMany(x => x.MealPreferences)
                .HasForeignKey(x => x.ParticipantId)
                .OnDelete(DeleteBehavior.Cascade);

            // Live FK → MealOption; Restrict so a referenced option cannot be hard-deleted
            // (FR-26) — defence in depth behind MealOptionService.CanHardDelete.
            m.HasOne(x => x.MealOption!)
                .WithMany()
                .HasForeignKey(x => x.MealOptionId)
                .OnDelete(DeleteBehavior.Restrict);
            m.HasIndex(x => x.MealOptionId);
        });
    }
}
