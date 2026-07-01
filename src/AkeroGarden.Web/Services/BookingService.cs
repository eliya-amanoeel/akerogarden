using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Services;

/// <summary>
/// Creates and (in later slices) transitions bookings. Inquiry creation applies the
/// server-authoritative normalisation of <see cref="InquiryNormalizer"/>, captures price
/// snapshots on the facility/cleaning selections (NFR-33/34), computes the initial retention
/// date (FR-91), and fires the acknowledgement + admin notification emails (FR-05/06) — whose
/// delivery is best-effort and never fails the booking (NFR-13).
///
/// <para>Status changes (approve/deny/close/cancel) are added in slice 4 and will route through
/// <see cref="BookingStateMachine"/>; this service is the only place bookings are mutated.</para>
/// </summary>
public class BookingService(ApplicationDbContext db, BookingEmailService emails)
{
    /// <summary>Creates a Pending stay request from the public inquiry form (FR-03).</summary>
    public async Task<Booking> CreateStayRequestAsync(StayRequestInput input)
    {
        if (input.MealPlan is null)
            throw new ArgumentException("Måltidsplan krävs för en bokningsförfrågan.", nameof(input));

        var now = DateTime.UtcNow;
        var submissionDate = DateOnly.FromDateTime(now);

        var activeFacilities = await db.Facilities.Where(f => f.IsActive).ToListAsync();
        var activeZones = await db.CleaningZones.Where(z => z.IsActive).ToListAsync();

        var facilityIds = InquiryNormalizer.NormalizeFacilitySelection(input.SelectedFacilityIds, activeFacilities);
        var zoneIds = InquiryNormalizer.NormalizeCleaningSelection(input.SelectedCleaningZoneIds, activeZones);

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingType = BookingType.StayRequest,
            Status = BookingStatus.Pending,
            ContactName = input.ContactName.Trim(),
            ContactEmail = input.ContactEmail.Trim(),
            ContactPhone = string.IsNullOrWhiteSpace(input.ContactPhone) ? null : input.ContactPhone.Trim(),
            CheckIn = input.CheckIn,
            CheckOut = input.CheckOut,
            OvernightGuestCount = input.OvernightGuestCount,
            // SelfCateringAll has no catered meals, so day guests do not apply (§4.2) — enforced
            // server-side regardless of what the form submitted.
            DayGuestCount = InquiryNormalizer.NormalizeDayGuestCount(input.MealPlan.Value, input.DayGuestCount),
            MealPlan = input.MealPlan,
            IsOrganisation = input.IsOrganisation,
            OrgName = input.IsOrganisation ? input.OrgName?.Trim() : null,
            OrgNumber = input.IsOrganisation ? input.OrgNumber?.Trim() : null,
            InvoiceAddress = input.IsOrganisation ? input.InvoiceAddress?.Trim() : null,
            Message = string.IsNullOrWhiteSpace(input.Message) ? null : input.Message.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            DeleteAfter = RetentionCalculator.ComputeDeleteAfter(
                BookingStatus.Pending, submissionDate, input.CheckOut, submissionDate),
        };

        // Snapshot the agreed name + price so later edits to the reference data don't change
        // what the guest selected (NFR-33/34).
        var facilitiesById = activeFacilities.ToDictionary(f => f.Id);
        foreach (var id in facilityIds)
        {
            var f = facilitiesById[id];
            booking.FacilitySelections.Add(new BookingFacilitySelection
            {
                Id = Guid.NewGuid(),
                FacilityId = f.Id,
                FacilityNameSnapshot = f.Name,
                AmountExVatSnapshot = f.AmountExVat,
                VatRateSnapshot = f.VatRate,
            });
        }

        var zonesById = activeZones.ToDictionary(z => z.Id);
        foreach (var id in zoneIds)
        {
            var z = zonesById[id];
            booking.CleaningSelections.Add(new BookingCleaningSelection
            {
                Id = Guid.NewGuid(),
                CleaningZoneId = z.Id,
                ZoneNameSnapshot = z.Name,
                AmountExVatSnapshot = z.AmountExVat,
                VatRateSnapshot = z.VatRate,
            });
        }

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        await emails.SendInquiryAcknowledgementAsync(booking);
        await emails.SendAdminNewInquiryAsync(booking);

        return booking;
    }

    /// <summary>Creates a Pending general inquiry from the public inquiry form (FR-04).</summary>
    public async Task<Booking> CreateGeneralInquiryAsync(GeneralInquiryInput input)
    {
        var now = DateTime.UtcNow;
        var submissionDate = DateOnly.FromDateTime(now);

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingType = BookingType.GeneralInquiry,
            Status = BookingStatus.Pending,
            ContactName = input.ContactName.Trim(),
            ContactEmail = input.ContactEmail.Trim(),
            ContactPhone = string.IsNullOrWhiteSpace(input.ContactPhone) ? null : input.ContactPhone.Trim(),
            Message = input.Message.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            DeleteAfter = RetentionCalculator.ComputeDeleteAfter(
                BookingStatus.Pending, submissionDate, checkOut: null, submissionDate),
        };

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        await emails.SendInquiryAcknowledgementAsync(booking);
        await emails.SendAdminNewInquiryAsync(booking);

        return booking;
    }
}
