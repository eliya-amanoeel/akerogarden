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
public class BookingService(ApplicationDbContext db, BookingEmailService emails, RoomAvailabilityService rooms)
{
    /// <summary>Creates a Pending stay request from the public inquiry form (FR-03).
    /// When <paramref name="createdByAdmin"/> is true the booking is created on the guest's
    /// behalf (FR-47) and no acknowledgement/notification emails are sent.</summary>
    public async Task<Booking> CreateStayRequestAsync(StayRequestInput input, bool createdByAdmin = false)
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
            CreatedByAdmin = createdByAdmin,
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

        if (!createdByAdmin)
        {
            await emails.SendInquiryAcknowledgementAsync(booking);
            await emails.SendAdminNewInquiryAsync(booking);
        }

        return booking;
    }

    /// <summary>Creates a Pending general inquiry from the public inquiry form (FR-04).
    /// When <paramref name="createdByAdmin"/> is true no emails are sent (FR-47).</summary>
    public async Task<Booking> CreateGeneralInquiryAsync(GeneralInquiryInput input, bool createdByAdmin = false)
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
            CreatedByAdmin = createdByAdmin,
            CreatedAt = now,
            UpdatedAt = now,
            DeleteAfter = RetentionCalculator.ComputeDeleteAfter(
                BookingStatus.Pending, submissionDate, checkOut: null, submissionDate),
        };

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        if (!createdByAdmin)
        {
            await emails.SendInquiryAcknowledgementAsync(booking);
            await emails.SendAdminNewInquiryAsync(booking);
        }

        return booking;
    }

    // ── Queries (admin) ───────────────────────────────────────────────────────────

    /// <summary>A booking with its facility/cleaning selections loaded — for the detail page.</summary>
    public async Task<Booking?> GetWithSelectionsAsync(Guid id) =>
        await db.Bookings
            .Include(b => b.FacilitySelections)
            .Include(b => b.CleaningSelections)
            .SingleOrDefaultAsync(b => b.Id == id);

    // ── Completion link (magic link — §2.6, Phase 4) ───────────────────────────────

    /// <summary>
    /// Looks up a booking by its completion token (§2.6). Returns null when no booking holds that
    /// token — never issued, or superseded by a regenerated link (FR-49). The token string is the
    /// sole authentication (FR-54), so callers must treat a null result as "no access".
    /// </summary>
    public async Task<Booking?> GetByCompletionTokenAsync(string token) =>
        string.IsNullOrEmpty(token)
            ? null
            : await db.Bookings.SingleOrDefaultAsync(b => b.CompletionToken == token);

    /// <summary>
    /// Evaluates whether a completion link is usable right now: combines the token lookup with the
    /// pure <see cref="CompletionAccessPolicy"/> (a missing token maps to
    /// <see cref="CompletionAccess.NotFound"/>). The returned booking is null unless access is
    /// evaluated against a real booking — callers must not render booking data unless
    /// access is <see cref="CompletionAccess.Valid"/>.
    /// </summary>
    public async Task<(CompletionAccess Access, Booking? Booking)> EvaluateCompletionAccessAsync(
        string token, DateTime nowUtc)
    {
        var booking = await GetByCompletionTokenAsync(token);
        if (booking is null) return (CompletionAccess.NotFound, null);
        return (CompletionAccessPolicy.Evaluate(booking.Status, booking.CompletionTokenExpiry, nowUtc), booking);
    }

    /// <summary>
    /// Submits the completion form (FR-55/57/59/60/61/62). Re-validates the link server-side (token
    /// found, Approved, unexpired — never trusting the earlier GET), then validates + builds the
    /// overnight participants and their per-day meal preferences with snapshots via the pure
    /// <see cref="CompletionNormalizer"/>, updates org/invoice details, transitions
    /// <c>Approved → DetailsComplete</c> through <see cref="BookingStateMachine"/>, and notifies the
    /// admin (FR-61). The token string is retained — single-use is enforced by the status gate
    /// (once <c>DetailsComplete</c> the link reports AlreadyCompleted, C10), not by erasing it.
    /// Day guests (§4.2) and room assignment (FR-56) are added in later slices.
    /// </summary>
    public async Task<CompletionSubmitResult> SubmitCompletionAsync(
        string token, CompletionFormModel form, DateTime nowUtc)
    {
        var booking = await db.Bookings
            .Include(b => b.Participants)
            .SingleOrDefaultAsync(b => b.CompletionToken == token);

        if (booking is null)
            return CompletionSubmitResult.LinkFailed(CompletionAccess.NotFound);

        var access = CompletionAccessPolicy.Evaluate(booking.Status, booking.CompletionTokenExpiry, nowUtc);
        if (access != CompletionAccess.Valid)
            return CompletionSubmitResult.LinkFailed(access);

        // Belt-and-suspenders against a double submit racing the status gate: a completed booking
        // already holds participants.
        if (booking.Participants.Count > 0)
            return CompletionSubmitResult.LinkFailed(CompletionAccess.AlreadyCompleted);

        if (booking.CheckIn is null || booking.CheckOut is null)
            return CompletionSubmitResult.LinkFailed(CompletionAccess.NotActive);

        var slots = MealsByDayCalculator.Slots(booking.CheckIn.Value, booking.CheckOut.Value);
        var expectedOvernight = booking.OvernightGuestCount ?? 0;

        // Day guests apply only for a catered meal plan (§4.2); for SelfCateringAll they are dropped
        // server-side regardless of what was posted.
        var dayGuestsApplicable = booking.MealPlan is MealPlan.FullBoardAll or MealPlan.Mixed;

        var errors = CompletionNormalizer.Validate(form, slots, expectedOvernight);
        if (dayGuestsApplicable)
            errors = [.. errors, .. CompletionNormalizer.ValidateDayGuests(
                form.DayGuests ?? [], booking.CheckIn.Value, booking.CheckOut.Value)];
        if (errors.Count > 0)
            return CompletionSubmitResult.Invalid(errors);

        // Authoritative active meal list — queried here, not trusted from the page.
        var activeOptions = await db.MealOptions.Where(m => m.IsActive).ToListAsync();
        var optionsById = activeOptions.ToDictionary(o => o.Id);

        // Room assignment is only collected when the booking lets the guest choose (FR-56);
        // the available set is re-derived here, never trusted from the page.
        IReadOnlyDictionary<Guid, RoomInfo>? roomsById = null;
        if (booking.GuestChoosesRooms)
            roomsById = (await rooms.GetAvailableRoomsAsync(booking.Id)).ToDictionary(r => r.Id);

        var participants = CompletionNormalizer.BuildParticipants(form, slots, optionsById, roomsById);
        if (dayGuestsApplicable)
            participants.AddRange(CompletionNormalizer.BuildDayGuests(form.DayGuests ?? [], optionsById));
        foreach (var participant in participants)
        {
            participant.BookingId = booking.Id;
            booking.Participants.Add(participant);
        }

        // Org/invoice (FR-60) — only retained when the booking is invoiced to an organisation.
        booking.IsOrganisation = form.IsOrganisation;
        booking.OrgName = form.IsOrganisation ? form.OrgName?.Trim() : null;
        booking.OrgNumber = form.IsOrganisation ? form.OrgNumber?.Trim() : null;
        booking.InvoiceAddress = form.IsOrganisation ? form.InvoiceAddress?.Trim() : null;

        // One-way transition (§2.5) — routed through the state machine, not set blindly (FR-62).
        BookingStateMachine.EnsureCanTransition(booking.Status, BookingStatus.DetailsComplete);
        booking.Status = BookingStatus.DetailsComplete;
        booking.UpdatedAt = nowUtc; // DetailsComplete is a live state → DeleteAfter stays null (FR-91)
        await db.SaveChangesAsync();

        await emails.SendAdminDetailsCompleteAsync(booking);
        return CompletionSubmitResult.Ok();
    }

    /// <summary>
    /// The admin booking list, filterable by status, type, and check-in date range (FR-36).
    /// Newest first. The date range matches a stay's check-in; general inquiries (no dates)
    /// are excluded when a date range is supplied.
    /// </summary>
    public async Task<List<Booking>> GetBookingsAsync(
        BookingStatus? status = null,
        BookingType? type = null,
        DateOnly? from = null,
        DateOnly? to = null)
    {
        var query = db.Bookings.AsQueryable();

        if (status is not null) query = query.Where(b => b.Status == status);
        if (type is not null) query = query.Where(b => b.BookingType == type);
        if (from is not null) query = query.Where(b => b.CheckIn != null && b.CheckIn >= from);
        if (to is not null) query = query.Where(b => b.CheckIn != null && b.CheckIn <= to);

        return await query.OrderByDescending(b => b.CreatedAt).ToListAsync();
    }

    // ── Status transitions (§2.5 — enforced via BookingStateMachine) ───────────────

    /// <summary>
    /// Approves a Pending stay request (FR-38): generates the completion token + expiry
    /// (§2.6), sends the approval email (FR-52), and reports whether check-in is close enough
    /// that the link is shortened to the 48-hour floor (FR-75).
    /// </summary>
    public async Task<ApproveResult> ApproveAsync(Guid id)
    {
        var booking = await db.Bookings.FindAsync(id);
        if (booking is null) return new ApproveResult(false, "Bokningen kunde inte hittas.", null, false);
        if (booking.BookingType != BookingType.StayRequest)
            return new ApproveResult(false, "Endast bokningsförfrågningar kan godkännas.", null, false);
        if (!BookingStateMachine.CanTransition(booking.Status, BookingStatus.Approved))
            return new ApproveResult(false, "Bokningen kan inte godkännas i sitt nuvarande läge.", null, false);
        if (booking.CheckIn is null)
            return new ApproveResult(false, "Bokningen saknar incheckningsdatum.", null, false);

        var now = DateTime.UtcNow;
        booking.CompletionToken = CompletionTokenPolicy.GenerateToken();
        booking.CompletionTokenExpiry = CompletionTokenPolicy.ComputeExpiry(now, booking.CheckIn.Value);
        booking.Status = BookingStatus.Approved;
        booking.DeleteAfter = null; // live in-progress state (FR-91)
        booking.UpdatedAt = now;
        await db.SaveChangesAsync();

        await emails.SendApprovalAsync(booking);

        // FR-75: warn when check-in is 14 days away or sooner (the link is shortened).
        var daysToCheckIn = booking.CheckIn.Value.DayNumber - DateOnly.FromDateTime(now).DayNumber;
        var shortExpiry = daysToCheckIn <= 14;
        return new ApproveResult(true, null, booking.CompletionTokenExpiry, shortExpiry);
    }

    /// <summary>Denies a Pending stay request with a required reason (FR-50); sends the denial email.</summary>
    public async Task<SaveResult> DenyAsync(Guid id, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return SaveResult.Fail("En avslagsmotivering krävs.");

        var booking = await db.Bookings.FindAsync(id);
        if (booking is null) return SaveResult.Fail("Bokningen kunde inte hittas.");
        if (booking.BookingType != BookingType.StayRequest)
            return SaveResult.Fail("Endast bokningsförfrågningar kan avslås.");
        if (!BookingStateMachine.CanTransition(booking.Status, BookingStatus.Denied))
            return SaveResult.Fail("Bokningen kan inte avslås i sitt nuvarande läge.");

        var now = DateTime.UtcNow;
        booking.DenialReason = reason.Trim();
        booking.Status = BookingStatus.Denied;
        booking.DeleteAfter = RetentionCalculator.ComputeDeleteAfter(
            BookingStatus.Denied, DateOnly.FromDateTime(booking.CreatedAt), booking.CheckOut, DateOnly.FromDateTime(now));
        booking.UpdatedAt = now;
        await db.SaveChangesAsync();

        await emails.SendDenialAsync(booking);
        return SaveResult.Success;
    }

    /// <summary>Closes a Pending general inquiry (FR-80). No guest email — the admin replies outside the system.</summary>
    public async Task<SaveResult> CloseAsync(Guid id)
    {
        var booking = await db.Bookings.FindAsync(id);
        if (booking is null) return SaveResult.Fail("Förfrågan kunde inte hittas.");
        if (booking.BookingType != BookingType.GeneralInquiry)
            return SaveResult.Fail("Endast allmänna förfrågningar kan markeras som besvarade.");
        if (!BookingStateMachine.CanTransition(booking.Status, BookingStatus.Closed))
            return SaveResult.Fail("Förfrågan kan inte stängas i sitt nuvarande läge.");

        var now = DateTime.UtcNow;
        booking.Status = BookingStatus.Closed;
        booking.DeleteAfter = RetentionCalculator.ComputeDeleteAfter(
            BookingStatus.Closed, DateOnly.FromDateTime(booking.CreatedAt), booking.CheckOut, DateOnly.FromDateTime(now));
        booking.UpdatedAt = now;
        await db.SaveChangesAsync();
        return SaveResult.Success;
    }

    /// <summary>
    /// Cancels a booking from Approved/DetailsComplete/Active (FR-76–79). Records the initiator
    /// and optional internal reason, sets the 12-month retention, and sends the cancellation
    /// email with the deposit clause resolved by initiator × timing.
    /// </summary>
    public async Task<SaveResult> CancelAsync(Guid id, CancellationInitiator initiator, string? reason)
    {
        var booking = await db.Bookings.FindAsync(id);
        if (booking is null) return SaveResult.Fail("Bokningen kunde inte hittas.");
        if (!BookingStateMachine.CanTransition(booking.Status, BookingStatus.Cancelled))
            return SaveResult.Fail("Bokningen kan inte avbokas i sitt nuvarande läge.");

        var now = DateTime.UtcNow;
        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAt = now;
        booking.CancellationInitiator = initiator;
        booking.CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        booking.DeleteAfter = RetentionCalculator.ComputeDeleteAfter(
            BookingStatus.Cancelled, DateOnly.FromDateTime(booking.CreatedAt), booking.CheckOut, DateOnly.FromDateTime(now));
        booking.UpdatedAt = now;
        await db.SaveChangesAsync();

        await emails.SendCancellationAsync(booking);
        return SaveResult.Success;
    }

    /// <summary>
    /// Regenerates and resends the completion magic link while the booking is still awaiting
    /// completion (status Approved) — invalidating any previous link (FR-49, §2.6).
    /// </summary>
    public async Task<SaveResult> RegenerateCompletionLinkAsync(Guid id)
    {
        var booking = await db.Bookings.FindAsync(id);
        if (booking is null) return SaveResult.Fail("Bokningen kunde inte hittas.");
        if (booking.Status != BookingStatus.Approved)
            return SaveResult.Fail("Länken kan endast återutfärdas medan bokningen väntar på att slutföras.");
        if (booking.CheckIn is null)
            return SaveResult.Fail("Bokningen saknar incheckningsdatum.");

        var now = DateTime.UtcNow;
        booking.CompletionToken = CompletionTokenPolicy.GenerateToken();
        booking.CompletionTokenExpiry = CompletionTokenPolicy.ComputeExpiry(now, booking.CheckIn.Value);
        booking.UpdatedAt = now;
        await db.SaveChangesAsync();

        await emails.SendApprovalAsync(booking);
        return SaveResult.Success;
    }

    /// <summary>Sets a booking's internal admin notes (FR-39).</summary>
    public async Task SetAdminNotesAsync(Guid id, string? notes)
    {
        var booking = await db.Bookings.FindAsync(id);
        if (booking is null) return;
        booking.AdminNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        booking.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>Updates the deposit/final payment status flags (FR-42). Boolean status only — no amounts (C8).</summary>
    public async Task SetPaymentStatusAsync(
        Guid id, bool depositInvoiced, bool depositPaid, bool finalInvoiced, bool finalPaid)
    {
        var booking = await db.Bookings.FindAsync(id);
        if (booking is null) return;
        booking.DepositInvoiced = depositInvoiced;
        booking.DepositPaid = depositPaid;
        booking.FinalInvoiced = finalInvoiced;
        booking.FinalPaid = finalPaid;
        booking.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>Blocked periods overlapping a date range — for the admin calendar view (FR-37).</summary>
    public async Task<List<BlockedPeriod>> GetBlocksInRangeAsync(DateOnly from, DateOnly to) =>
        await db.BlockedPeriods
            .Where(p => p.StartDate <= to && p.EndDate >= from)
            .OrderBy(p => p.StartDate)
            .ToListAsync();
}

/// <summary>Outcome of an approval: success plus, per FR-75, whether the completion link was
/// shortened because check-in is 14 days away or sooner (so the admin can follow up directly).</summary>
public record ApproveResult(bool Succeeded, string? Error, DateTime? TokenExpiry, bool ShortExpiryWarning);
