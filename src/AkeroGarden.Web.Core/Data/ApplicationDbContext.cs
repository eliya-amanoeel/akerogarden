using AkeroGarden.Web.Core.Data.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AkeroGarden.Web.Core.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    // Entities were introduced per phase during the build; that is history, not a live rule — the
    // build is over and every schema change is now an incremental migration on the frozen baseline
    // (see CLAUDE.md). The phase record lives in docs/archive/implementation-plan.md.
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
    public DbSet<BookingBuildingAssignment> BookingBuildingAssignments => Set<BookingBuildingAssignment>();
    public DbSet<BlockedPeriod> BlockedPeriods => Set<BlockedPeriod>();

    // Phase 4 slice 1: completion flow — Participant. Meal selection re-parented from Participant
    // to Booking in the 2026-08 demo-feedback work (§8.6, FR-105).
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<BookingMealSelection> BookingMealSelections => Set<BookingMealSelection>();

    // Phase 8: cleaning rework — PhysicalBuilding (§8.14).
    public DbSet<PhysicalBuilding> PhysicalBuildings => Set<PhysicalBuilding>();

    // Phase 11: audit trail — AuditLog (§8.15, §6.9).
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Job history — one row per nightly job run (§8.17, FR-114).
    public DbSet<JobRun> JobRuns => Set<JobRun>();

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

            // Required FK → PhysicalBuilding (§8.14, FR-13). Restrict so a physical building with
            // lodging units cannot be hard-deleted — defence in depth behind
            // PhysicalBuildingService.CanHardDelete.
            b.HasOne(x => x.PhysicalBuilding!)
                .WithMany()
                .HasForeignKey(x => x.PhysicalBuildingId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.PhysicalBuildingId);
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

            // Enums persisted as strings: legible in the DB and stable against reordering.
            c.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);

            // Nullable FK → PhysicalBuilding (§8.11) — null only for the Everything bundle
            // (validated in CleaningZoneService, not a DB constraint). Restrict so a physical
            // building referenced by a zone cannot be hard-deleted — defence in depth behind
            // PhysicalBuildingService.CanHardDelete.
            c.HasOne(x => x.PhysicalBuilding)
                .WithMany()
                .HasForeignKey(x => x.PhysicalBuildingId)
                .OnDelete(DeleteBehavior.Restrict);
            c.HasIndex(x => x.PhysicalBuildingId);
        });

        builder.Entity<Facility>(f =>
        {
            // Reference figures only, mirroring CleaningZone/PriceRule — no totals (C8).
            f.Property(x => x.AmountExVat).HasPrecision(18, 2);
            f.Property(x => x.VatRate).HasPrecision(5, 4); // fraction, e.g. 0.2500

            // Nullable FK → PhysicalBuilding (§8.8) — set only for facilities that put a
            // physical building's cleaning in play (Konferensrum → Konferensbyggnad). Restrict so
            // a referenced physical building cannot be hard-deleted — defence in depth behind
            // PhysicalBuildingService.CanHardDelete.
            f.HasOne(x => x.PhysicalBuilding)
                .WithMany()
                .HasForeignKey(x => x.PhysicalBuildingId)
                .OnDelete(DeleteBehavior.Restrict);
            f.HasIndex(x => x.PhysicalBuildingId);
        });

        // ── Phase 3: booking lifecycle ──────────────────────────────────────────
        builder.Entity<Booking>(b =>
        {
            // Enums persisted as strings: legible in the DB and stable against reordering.
            b.Property(x => x.BookingType).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.MealPlan).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.CancellationInitiator).HasConversion<string>().HasMaxLength(30);

            // The one amount on a booking (C8 as amended, §5.6) — same precision as the reference
            // amounts, though only whole kronor are accepted (DepositPolicy.ValidatePrice).
            b.Property(x => x.EstimatedPriceInclVat).HasPrecision(18, 2);

            // Global monotonic sequence backing BookingReference (FR-92) — DB-generated so it is
            // race-free under concurrent inserts; the Guid Id stays the primary key.
            b.Property(x => x.SequenceNumber).ValueGeneratedOnAdd().UseIdentityAlwaysColumn();
            b.Property(x => x.BookingReference).HasMaxLength(20);
            b.HasIndex(x => x.BookingReference).IsUnique();

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

        builder.Entity<BookingBuildingAssignment>(a =>
        {
            a.Property(x => x.BuildingNameSnapshot).HasMaxLength(200);
            a.Property(x => x.PhysicalBuildingNameSnapshot).HasMaxLength(200);

            // Assignments follow the booking out under GDPR retention (FR-67/68/89/90).
            a.HasOne(x => x.Booking!)
                .WithMany(b => b.BuildingAssignments)
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            // Live FK to Building; Restrict so a building assigned to a booking cannot be
            // hard-deleted (FR-16) — defence in depth behind BuildingService.CanHardDelete.
            a.HasOne(x => x.Building!)
                .WithMany()
                .HasForeignKey(x => x.BuildingId)
                .OnDelete(DeleteBehavior.Restrict);

            // One row per building per booking — re-assigning the same building is not a new row.
            a.HasIndex(x => new { x.BookingId, x.BuildingId }).IsUnique();
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
        // Multi-select allergies persist as a comma-joined string of enum names — legible, and
        // never queried in a WHERE, so no join table or PG array is warranted.
        var allergyConverter = new ValueConverter<List<Allergy>, string>(
            v => string.Join(',', v.Select(d => d.ToString())),
            v => string.IsNullOrEmpty(v)
                ? new List<Allergy>()
                : v.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(Enum.Parse<Allergy>)
                    .ToList());
        // EF needs a value comparer for a mutable reference-typed property to track edits correctly.
        var allergyComparer = new ValueComparer<List<Allergy>>(
            (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
            v => v.Aggregate(0, (acc, d) => HashCode.Combine(acc, d.GetHashCode())),
            v => v.ToList());

        builder.Entity<Participant>(p =>
        {
            // Enums persisted as strings: legible in the DB and stable against reordering.
            p.Property(x => x.ParticipantType).HasConversion<string>().HasMaxLength(20);
            p.Property(x => x.AgeCategory).HasConversion<string>().HasMaxLength(20);
            p.Property(x => x.FullName).HasMaxLength(200);
            p.Property(x => x.AllergyNotes).HasMaxLength(200);

            p.Property(x => x.Allergies)
                .HasConversion(allergyConverter)
                .Metadata.SetValueComparer(allergyComparer);

            // No room relationship: participants are no longer assigned rooms (FR-56 tombstoned).
            // The booking's lodging is recorded at building level instead — see
            // BookingBuildingAssignment (§8.16, FR-103).
            p.HasIndex(x => x.BookingId);
        });

        builder.Entity<BookingMealSelection>(m =>
        {
            m.Property(x => x.MealType).HasConversion<string>().HasMaxLength(20);
            m.Property(x => x.MealOptionSnapshot).HasMaxLength(200);

            // Meal selections cascade with their booking (retention deletion, Phase 5).
            m.HasOne(x => x.Booking!)
                .WithMany(x => x.MealSelections)
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            // One choice per sitting for the whole group (FR-105) — enforced in the database, not
            // only in the normalizer, so a duplicate row cannot exist however it was written.
            m.HasIndex(x => new { x.BookingId, x.Date, x.MealType }).IsUnique();

            // Live FK → MealOption; Restrict so a referenced option cannot be hard-deleted
            // (FR-26) — defence in depth behind MealOptionService.CanHardDelete.
            m.HasOne(x => x.MealOption!)
                .WithMany()
                .HasForeignKey(x => x.MealOptionId)
                .OnDelete(DeleteBehavior.Restrict);
            m.HasIndex(x => x.MealOptionId);
        });

        // ── Phase 11: audit trail (§8.15, §6.9) ─────────────────────────────────
        builder.Entity<AuditLog>(a =>
        {
            // Enums persisted as strings: legible in the DB and stable against reordering.
            a.Property(x => x.ActorType).HasConversion<string>().HasMaxLength(20);
            a.Property(x => x.Action).HasConversion<string>().HasMaxLength(50);
            a.Property(x => x.EntityType).HasMaxLength(50);
            a.Property(x => x.ActorEmailSnapshot).HasMaxLength(256);
            a.Property(x => x.ActorNameSnapshot).HasMaxLength(200);
            a.Property(x => x.BookingReferenceSnapshot).HasMaxLength(20);
            a.Property(x => x.Summary).HasMaxLength(500);

            // Booking-scoped entries cascade-delete with the booking (FR-98) so the trail honours
            // the booking's retention; non-booking entries (BookingId null) are purged on age by
            // the retention job. Optional FK — reference-data/admin entries carry no BookingId.
            a.HasOne(x => x.Booking)
                .WithMany()
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            // The future admin view (FR-97) lists per booking and filters/sorts by time; the
            // retention job scans non-booking rows by age.
            a.HasIndex(x => x.BookingId);
            a.HasIndex(x => x.Timestamp);

            // What a nightly run did (FR-114). SET NULL, not cascade: deleting a run after six
            // months must not take audit entries with it — the trail keeps its own retention (FR-98).
            a.HasOne(x => x.JobRun)
                .WithMany()
                .HasForeignKey(x => x.JobRunId)
                .OnDelete(DeleteBehavior.SetNull);
            a.HasIndex(x => x.JobRunId);
        });

        // ── Job history (§8.17, FR-114) ─────────────────────────────────────────
        builder.Entity<JobRun>(r =>
        {
            r.Property(x => x.JobName).HasMaxLength(100);
            r.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(20);
            r.Property(x => x.FailureReason).HasMaxLength(500);
            // Listed newest first, and purged by age by the retention job.
            r.HasIndex(x => x.StartedAt);
        });
    }
}
