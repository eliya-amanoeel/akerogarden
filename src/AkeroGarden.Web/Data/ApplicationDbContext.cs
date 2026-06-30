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
    }
}
