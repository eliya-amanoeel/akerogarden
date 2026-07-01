using AkeroGarden.Web.Data.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

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

            // Deleting a booking removes its selection children (retention deletion, Phase 5).
            b.HasMany(x => x.FacilitySelections)
                .WithOne(s => s.Booking!)
                .HasForeignKey(s => s.BookingId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.CleaningSelections)
                .WithOne(s => s.Booking!)
                .HasForeignKey(s => s.BookingId)
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
    }
}
