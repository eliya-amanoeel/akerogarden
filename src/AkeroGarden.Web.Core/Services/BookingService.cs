using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Creates and transitions bookings. Inquiry creation applies the server-authoritative
/// normalisation of <see cref="InquiryNormalizer"/>, captures a price snapshot on each facility
/// selection (NFR-34; cleaning is not selected at inquiry — it moves to the completion form,
/// FR-93), computes the initial retention date (FR-91), and fires the acknowledgement + admin
/// notification emails (FR-05/06) — whose delivery is best-effort and never fails the booking
/// (NFR-13). Completion submission (FR-93, §5.5) snapshots the Huvudbyggnad/Konferensbyggnad
/// cleaning choices immediately and resolves the sleeping-building selection from room assignment
/// (§8.12), refreshed whenever room assignment is set or changed.
///
/// <para>Status changes (approve/deny/close/cancel) route through
/// <see cref="BookingStateMachine"/>; this service is the only place bookings are mutated.</para>
/// </summary>
public class BookingService(
    ApplicationDbContext db, BookingEmailService emails, RoomAvailabilityService rooms, CleaningZoneService cleaning,
    IAuditWriter audit, ILogger<BookingService> logger)
{
    /// <summary>Queues a booking-scoped audit entry (§8.15) on the current unit of work — persists
    /// with the caller's next <c>SaveChangesAsync</c>. <paramref name="summary"/> must carry no
    /// guest personal data (NFR-37). Actor defaults to the ambient admin; pass an explicit actor for
    /// guest (completion) or system (background) actions.</summary>
    private Task RecordBookingAuditAsync(Booking booking, AuditAction action, string summary, AuditActor? actor = null)
        => audit.RecordAsync(action, nameof(Booking), booking.Id, summary, booking.Id, booking.BookingReference, actor);

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

        var facilityIds = InquiryNormalizer.NormalizeFacilitySelection(input.SelectedFacilityIds, activeFacilities);

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

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        await AssignBookingReferenceAsync(booking);

        if (createdByAdmin)
        {
            // FR-95: manual admin creation is audited; a public inquiry submission is not.
            await RecordBookingAuditAsync(booking, AuditAction.BookingCreatedByAdmin,
                "Bokningsförfrågan skapades av administratör.");
            await db.SaveChangesAsync();
        }
        else
        {
            await emails.SendInquiryAcknowledgementAsync(booking);
            await emails.SendAdminNewInquiryAsync(booking);
        }

        logger.LogInformation("Stay request {BookingId} created (admin: {CreatedByAdmin})", booking.Id, createdByAdmin);
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
        await AssignBookingReferenceAsync(booking);

        if (createdByAdmin)
        {
            // FR-95: manual admin creation is audited; a public inquiry submission is not.
            await RecordBookingAuditAsync(booking, AuditAction.BookingCreatedByAdmin,
                "Allmän förfrågan skapades av administratör.");
            await db.SaveChangesAsync();
        }
        else
        {
            await emails.SendInquiryAcknowledgementAsync(booking);
            await emails.SendAdminNewInquiryAsync(booking);
        }

        logger.LogInformation("General inquiry {BookingId} created (admin: {CreatedByAdmin})", booking.Id, createdByAdmin);
        return booking;
    }

    /// <summary>
    /// Sets <see cref="Booking.BookingReference"/> from the DB-assigned <see cref="Booking.SequenceNumber"/>
    /// (FR-92) — must run after the initial insert, since the identity value is only known once the row
    /// exists. A second, small write; format is <c>AG-YYYY-NNNN</c> with the creation year and the
    /// sequence zero-padded to 4 digits.
    /// </summary>
    private async Task AssignBookingReferenceAsync(Booking booking)
    {
        booking.BookingReference = BookingReferencePolicy.Format(booking.SequenceNumber, booking.CreatedAt.Year);
        await db.SaveChangesAsync();
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
            participants.AddRange(CompletionNormalizer.BuildDayGuests(
                form.DayGuests ?? [], optionsById, booking.CheckIn.Value, booking.CheckOut.Value));
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

        // Cleaning (FR-93, §5.5). Huvudbyggnad always applies (a stay request always has overnight
        // lodging); Konferensbyggnad only when the Konferensrum facility was actually selected —
        // re-checked here server-side, never trusted from the posted form. Both snapshot their zone
        // amount immediately. Sleeping is stored as intent only; its priced selections are resolved
        // from room assignment below, once this save has persisted the participants' RoomId.
        booking.MainBuildingCleaningPurchased = form.MainBuildingCleaning;
        if (form.MainBuildingCleaning)
            await AddCleaningSelectionAsync(booking, CleaningZoneKind.MainBuilding);

        var conferenceApplicable = await cleaning.IsConferenceCleaningApplicableAsync(booking.Id);
        booking.ConferenceCleaningPurchased = conferenceApplicable ? form.ConferenceCleaning : null;
        if (conferenceApplicable && form.ConferenceCleaning)
            await AddCleaningSelectionAsync(booking, CleaningZoneKind.Conference);

        booking.SleepingCleaningPurchased = form.SleepingCleaning;

        // One-way transition (§2.5) — routed through the state machine, not set blindly (FR-62).
        BookingStateMachine.EnsureCanTransition(booking.Status, BookingStatus.DetailsComplete);
        booking.Status = BookingStatus.DetailsComplete;
        booking.UpdatedAt = nowUtc; // DetailsComplete is a live state → DeleteAfter stays null (FR-91)
        // Actor is the guest (magic link, FR-54) — no participant names in the summary (NFR-37).
        await RecordBookingAuditAsync(booking, AuditAction.CompletionSubmitted,
            "Bokningsuppgifter skickades in via slutförandelänken.", AuditActor.Guest);
        await db.SaveChangesAsync();

        // Resolves after the save above, since it depends on participants' RoomId being persisted.
        await ResolveSleepingCleaningSelectionsAsync(booking);

        await emails.SendAdminDetailsCompleteAsync(booking);
        logger.LogInformation("Booking {BookingId} completion submitted", booking.Id);
        return CompletionSubmitResult.Ok();
    }

    /// <summary>Snapshots the active zone of a given Kind onto the booking's cleaning selections
    /// (§8.12). A no-op if no active zone of that Kind is configured (defensive — should not
    /// happen with correctly maintained reference data).</summary>
    private async Task AddCleaningSelectionAsync(Booking booking, CleaningZoneKind kind)
    {
        var zone = await cleaning.GetActiveByKindAsync(kind);
        if (zone is null) return;

        db.BookingCleaningSelections.Add(new BookingCleaningSelection
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            CleaningZoneId = zone.Id,
            ZoneNameSnapshot = zone.Name,
            AmountExVatSnapshot = zone.AmountExVat,
            VatRateSnapshot = zone.VatRate,
        });
    }

    /// <summary>
    /// Resolves and snapshots the <see cref="CleaningZoneKind.SleepingBuilding"/> selections for a
    /// booking from its currently assigned rooms (§8.12, FR-93) — one row per distinct occupied
    /// physical building's zone, never per room, refreshed whenever room assignment is set or
    /// changed. A no-op unless the guest opted in (<see cref="Booking.SleepingCleaningPurchased"/>
    /// true); if intent is false, any stale sleeping-zone selections are removed. Requires
    /// participants' RoomId to already be persisted — call after a <c>SaveChangesAsync</c> that
    /// wrote the room assignment.
    /// </summary>
    private async Task ResolveSleepingCleaningSelectionsAsync(Booking booking)
    {
        var existingSleeping = await db.BookingCleaningSelections
            .Where(s => s.BookingId == booking.Id)
            .Join(db.CleaningZones, s => s.CleaningZoneId, z => z.Id, (s, z) => new { Selection = s, z.Kind })
            .Where(x => x.Kind == CleaningZoneKind.SleepingBuilding)
            .Select(x => x.Selection)
            .ToListAsync();

        if (booking.SleepingCleaningPurchased != true)
        {
            if (existingSleeping.Count > 0)
            {
                db.BookingCleaningSelections.RemoveRange(existingSleeping);
                await db.SaveChangesAsync();
            }
            return;
        }

        var occupiedPhysicalBuildingIds = await db.Participants
            .Where(p => p.BookingId == booking.Id && p.RoomId != null)
            .Select(p => p.Room!.Building!.PhysicalBuildingId)
            .Distinct()
            .ToListAsync();

        var sleepingZones = await cleaning.GetActiveListByKindAsync(CleaningZoneKind.SleepingBuilding);
        var targetZoneIds = CleaningZoneResolver.ResolveSleepingZoneIds(occupiedPhysicalBuildingIds, sleepingZones);
        var existingZoneIds = existingSleeping.Select(s => s.CleaningZoneId).ToHashSet();

        foreach (var stale in existingSleeping.Where(s => !targetZoneIds.Contains(s.CleaningZoneId)))
            db.BookingCleaningSelections.Remove(stale);

        var zonesById = sleepingZones.ToDictionary(z => z.Id);
        foreach (var newZoneId in targetZoneIds.Except(existingZoneIds))
        {
            var z = zonesById[newZoneId];
            db.BookingCleaningSelections.Add(new BookingCleaningSelection
            {
                Id = Guid.NewGuid(),
                BookingId = booking.Id,
                CleaningZoneId = z.Id,
                ZoneNameSnapshot = z.Name,
                AmountExVatSnapshot = z.AmountExVat,
                VatRateSnapshot = z.VatRate,
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The admin booking list, filterable by status, type, check-in date range, and searchable by
    /// booking reference (FR-36/FR-92). Newest first. The date range matches a stay's check-in;
    /// general inquiries (no dates) are excluded when a date range is supplied.
    /// </summary>
    public async Task<List<Booking>> GetBookingsAsync(
        BookingStatus? status = null,
        BookingType? type = null,
        DateOnly? from = null,
        DateOnly? to = null,
        string? reference = null)
    {
        var query = db.Bookings.AsQueryable();

        if (status is not null) query = query.Where(b => b.Status == status);
        if (type is not null) query = query.Where(b => b.BookingType == type);
        if (from is not null) query = query.Where(b => b.CheckIn != null && b.CheckIn >= from);
        if (to is not null) query = query.Where(b => b.CheckIn != null && b.CheckIn <= to);
        if (!string.IsNullOrWhiteSpace(reference))
            query = query.Where(b => EF.Functions.ILike(b.BookingReference, $"%{reference.Trim()}%"));

        return await query.OrderByDescending(b => b.CreatedAt).ToListAsync();
    }

    /// <summary>Count of bookings still awaiting a first admin response (both stay requests and
    /// general inquiries) — drives the admin nav badge and the "Väntar på svar" dashboard queue.</summary>
    public async Task<int> GetPendingCountAsync() =>
        await db.Bookings.CountAsync(b => b.Status == BookingStatus.Pending);

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
        await RecordBookingAuditAsync(booking, AuditAction.BookingApproved, "Bokningen godkändes.");
        await db.SaveChangesAsync();

        await emails.SendApprovalAsync(booking);
        logger.LogInformation("Booking {BookingId} approved", booking.Id);

        // FR-75: warn when check-in is 14 days away or sooner (the link is shortened).
        var daysToCheckIn = booking.CheckIn.Value.DayNumber - DateOnly.FromDateTime(now).DayNumber;
        var shortExpiry = daysToCheckIn <= 14;
        return new ApproveResult(true, null, booking.CompletionTokenExpiry, shortExpiry);
    }

    /// <summary>
    /// Non-blocking pre-approval checks (Phase 7): does the requested stay overlap a block, or does
    /// the overnight guest count exceed the beds actually available for those dates? The admin keeps
    /// override authority — this only warns, it never blocks approval — but it stops a booking being
    /// approved onto fully-blocked dates that would leave the guest with an empty room picker.
    /// </summary>
    public async Task<ApprovalWarning> GetApprovalWarningsAsync(Guid id)
    {
        var booking = await db.Bookings.FindAsync(id);
        if (booking is null || booking.BookingType != BookingType.StayRequest
            || booking.CheckIn is not { } checkIn || booking.CheckOut is not { } checkOut)
            return ApprovalWarning.None;

        var guests = booking.OvernightGuestCount ?? 0;
        // Beds actually available for the dates — blocks and other bookings' occupancy already excluded.
        var availableBeds = (await rooms.GetAvailableRoomsAsync(id)).Sum(r => r.BedCount);

        // The same overlap predicate RoomAvailabilityService uses, so the block warning matches what
        // actually reduces the room picker (checkout day is a departure, hence StartDate < checkOut).
        var blocks = await db.BlockedPeriods
            .Where(p => p.StartDate < checkOut && p.EndDate >= checkIn)
            .OrderBy(p => p.StartDate)
            .ToListAsync();

        return new ApprovalWarning(
            CapacityExceeded: guests > availableBeds,
            RequestedGuests: guests,
            AvailableBeds: availableBeds,
            BlockDescriptions: await DescribeBlocksAsync(blocks));
    }

    /// <summary>Swedish, human-readable descriptions of the overlapping blocks (scope + name + dates).</summary>
    private async Task<IReadOnlyList<string>> DescribeBlocksAsync(List<BlockedPeriod> blocks)
    {
        if (blocks.Count == 0) return [];

        var buildingIds = blocks.Where(b => b.BuildingId is not null).Select(b => b.BuildingId!.Value).Distinct().ToList();
        var roomIds = blocks.Where(b => b.RoomId is not null).Select(b => b.RoomId!.Value).Distinct().ToList();
        var buildingNames = await db.Buildings.Where(b => buildingIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name);
        var roomNames = await db.Rooms.Where(r => roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name);

        static string NameOr(IReadOnlyDictionary<Guid, string> names, Guid? id, string fallback)
            => id is { } g && names.TryGetValue(g, out var n) ? n : fallback;

        return blocks.Select(b =>
        {
            var period = $"{b.StartDate:yyyy-MM-dd}–{b.EndDate:yyyy-MM-dd}";
            return b.Scope switch
            {
                BlockScope.WholeProperty => $"Hela anläggningen ({period})",
                BlockScope.Building => $"Byggnaden {NameOr(buildingNames, b.BuildingId, "okänd")} ({period})",
                BlockScope.Room => $"Rummet {NameOr(roomNames, b.RoomId, "okänt")} ({period})",
                _ => $"Blockering ({period})",
            };
        }).ToList();
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
        // No denial reason in the summary — it is admin free-text that could name the guest (NFR-37).
        await RecordBookingAuditAsync(booking, AuditAction.BookingDenied, "Bokningen avslogs.");
        await db.SaveChangesAsync();

        await emails.SendDenialAsync(booking);
        logger.LogInformation("Booking {BookingId} denied", booking.Id);
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
        await RecordBookingAuditAsync(booking, AuditAction.InquiryClosed, "Förfrågan markerades som besvarad.");
        await db.SaveChangesAsync();
        logger.LogInformation("Inquiry {BookingId} closed", booking.Id);
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
        // The initiator is an enum, not PII; the free-text reason is deliberately omitted (NFR-37).
        var initiatorLabel = initiator == CancellationInitiator.GuestInitiated ? "gästinitierad" : "anläggningsinitierad";
        await RecordBookingAuditAsync(booking, AuditAction.BookingCancelled, $"Bokningen avbokades ({initiatorLabel}).");
        await db.SaveChangesAsync();

        await emails.SendCancellationAsync(booking);
        logger.LogInformation("Booking {BookingId} cancelled by {Initiator}", booking.Id, initiator);
        return SaveResult.Success;
    }

    /// <summary>
    /// Admin marks an arrived guest: <c>DetailsComplete → Active</c> (§2.5, "markera anländ").
    /// A live in-progress state, so <c>DeleteAfter</c> stays null (FR-91). No email.
    /// </summary>
    public async Task<SaveResult> MarkArrivedAsync(Guid id)
    {
        var booking = await db.Bookings.FindAsync(id);
        if (booking is null) return SaveResult.Fail("Bokningen kunde inte hittas.");
        if (!BookingStateMachine.CanTransition(booking.Status, BookingStatus.Active))
            return SaveResult.Fail("Bokningen kan inte markeras som anländ i sitt nuvarande läge.");

        booking.Status = BookingStatus.Active;
        booking.DeleteAfter = null; // live in-progress state (FR-91)
        booking.UpdatedAt = DateTime.UtcNow;
        await RecordBookingAuditAsync(booking, AuditAction.BookingMarkedArrived, "Bokningen markerades som anländ.");
        await db.SaveChangesAsync();
        logger.LogInformation("Booking {BookingId} marked arrived", booking.Id);
        return SaveResult.Success;
    }

    /// <summary>
    /// Admin marks a finished stay: <c>Active → Completed</c> (§2.5, "markera avslutad"). Sets the
    /// 2-year retention date from check-out (FR-91) — the last retention-relevant transition to do
    /// so — and sends the fixed thank-you email (FR-65).
    /// </summary>
    public async Task<SaveResult> MarkCompletedAsync(Guid id)
    {
        var booking = await db.Bookings.FindAsync(id);
        if (booking is null) return SaveResult.Fail("Bokningen kunde inte hittas.");
        if (!BookingStateMachine.CanTransition(booking.Status, BookingStatus.Completed))
            return SaveResult.Fail("Bokningen kan inte markeras som avslutad i sitt nuvarande läge.");
        if (booking.CheckOut is null)
            return SaveResult.Fail("Bokningen saknar utcheckningsdatum.");

        var now = DateTime.UtcNow;
        booking.Status = BookingStatus.Completed;
        booking.DeleteAfter = RetentionCalculator.ComputeDeleteAfter(
            BookingStatus.Completed, DateOnly.FromDateTime(booking.CreatedAt), booking.CheckOut, DateOnly.FromDateTime(now));
        booking.UpdatedAt = now;
        await RecordBookingAuditAsync(booking, AuditAction.BookingMarkedCompleted, "Bokningen markerades som avslutad.");
        await db.SaveChangesAsync();

        await emails.SendThankYouAsync(booking);
        logger.LogInformation("Booking {BookingId} marked completed", booking.Id);
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

        RegenerateCompletionToken(booking);
        await RecordBookingAuditAsync(booking, AuditAction.CompletionLinkRegenerated,
            "Slutförandelänk återutfärdades och skickades till gästen.");
        await db.SaveChangesAsync();

        await emails.SendApprovalAsync(booking);
        logger.LogInformation("Booking {BookingId} completion link regenerated", booking.Id);
        return SaveResult.Success;
    }

    /// <summary>
    /// Generates a fresh completion token + expiry on the booking (§2.6), invalidating any previous
    /// link, without saving or sending mail — the shared core used both by
    /// <see cref="RegenerateCompletionLinkAsync"/> (admin resend, approval email) and by the 14-day
    /// reminder job's still-<c>Approved</c> nudge (FR-63). Caller persists and sends the appropriate mail.
    /// </summary>
    public void RegenerateCompletionToken(Booking booking)
    {
        if (booking.CheckIn is null)
            throw new InvalidOperationException("Slutförandelänk kräver ett incheckningsdatum.");

        var now = DateTime.UtcNow;
        booking.CompletionToken = CompletionTokenPolicy.GenerateToken();
        booking.CompletionTokenExpiry = CompletionTokenPolicy.ComputeExpiry(now, booking.CheckIn.Value);
        booking.UpdatedAt = now;
    }

    /// <summary>Sets a booking's internal admin notes (FR-39).</summary>
    // ── Participant data (admin, post-submission — FR-48/56) ───────────────────────

    /// <summary>Loads a booking with its participants + meal preferences (admin participant view/edit).</summary>
    public async Task<Booking?> GetWithParticipantsAsync(Guid id) =>
        await db.Bookings
            .Include(b => b.Participants).ThenInclude(p => p.MealPreferences)
            .SingleOrDefaultAsync(b => b.Id == id);

    /// <summary>Toggles whether the guest assigns rooms on the completion form (FR-56); the admin
    /// sets this before the guest completes. Booking-wide, not per-participant.</summary>
    public async Task SetGuestChoosesRoomsAsync(Guid id, bool value)
    {
        var booking = await db.Bookings.FindAsync(id);
        if (booking is null) return;
        booking.GuestChoosesRooms = value;
        booking.UpdatedAt = DateTime.UtcNow;
        await RecordBookingAuditAsync(booking, AuditAction.GuestChoosesRoomsChanged,
            $"Inställning för gästens rumsval ändrades till {(value ? "på" : "av")}.");
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Applies the admin's post-submission edits to a booking's participants (FR-48): names, age,
    /// dietary, day-guest breakfast, overnight room assignment (re-snapshotting name + building,
    /// NFR-16), and corrections to existing meal choices. <b>No status change and no guest
    /// reopen</b> — this is the only post-submission edit path (C10). The admin may assign any active
    /// room (overrides allowed); conflicts are flagged separately.
    /// </summary>
    public async Task SaveParticipantEditsAsync(Guid bookingId, IReadOnlyList<ParticipantEdit> edits)
    {
        var booking = await db.Bookings
            .Include(b => b.Participants).ThenInclude(p => p.MealPreferences)
            .SingleOrDefaultAsync(b => b.Id == bookingId);
        if (booking is null) return;

        var roomsById = (await rooms.GetAllActiveRoomsAsync()).ToDictionary(r => r.Id);
        var optionsById = (await db.MealOptions.Where(m => m.IsActive).ToListAsync()).ToDictionary(o => o.Id);
        var editsById = edits.ToDictionary(e => e.Id);

        foreach (var participant in booking.Participants)
        {
            if (!editsById.TryGetValue(participant.Id, out var edit)) continue;

            participant.FullName = (edit.FullName ?? string.Empty).Trim();
            participant.AgeCategory = edit.AgeCategory;
            participant.DietaryRequirements = edit.Dietary ?? [];
            participant.DietaryNotes = string.IsNullOrWhiteSpace(edit.DietaryNotes) ? null : edit.DietaryNotes.Trim();

            if (participant.ParticipantType == ParticipantType.DayGuest)
            {
                participant.AttendsBreakfast = edit.AttendsBreakfast;
            }
            else if (edit.RoomId is { } roomId && roomsById.TryGetValue(roomId, out var room))
            {
                participant.RoomId = roomId;
                participant.RoomAssignmentSnapshot = $"{room.Name} — {room.BuildingName}";
            }
            else if (edit.RoomId is null)
            {
                participant.RoomId = null;
                participant.RoomAssignmentSnapshot = null;
            }

            // Correct existing meal choices only (right type + active) — the set of days is fixed.
            foreach (var mealEdit in edit.Meals)
            {
                var meal = participant.MealPreferences.FirstOrDefault(m => m.Id == mealEdit.Id);
                if (meal is null) continue;
                if (!optionsById.TryGetValue(mealEdit.MealOptionId, out var option)) continue;
                if (option.MealType != meal.MealType) continue;
                meal.MealOptionId = option.Id;
                meal.MealOptionSnapshot = option.Name;
            }
        }

        booking.UpdatedAt = DateTime.UtcNow;
        // Count only — never participant names/dietary values (NFR-37).
        await RecordBookingAuditAsync(booking, AuditAction.ParticipantsEdited,
            $"Deltagaruppgifter uppdaterades ({booking.Participants.Count} deltagare).");
        await db.SaveChangesAsync();

        // Room assignment may have just been set/changed above — refresh the sleeping-building
        // cleaning selections to match (§8.12, FR-93). Needs the save above: it reads persisted
        // RoomId values via ResolveSleepingCleaningSelectionsAsync's own query.
        await ResolveSleepingCleaningSelectionsAsync(booking);
    }

    public async Task SetAdminNotesAsync(Guid id, string? notes)
    {
        var booking = await db.Bookings.FindAsync(id);
        if (booking is null) return;
        booking.AdminNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        booking.UpdatedAt = DateTime.UtcNow;
        // Note content is not recorded — it is internal free-text that may reference the guest (NFR-37).
        await RecordBookingAuditAsync(booking, AuditAction.AdminNotesEdited, "Interna anteckningar uppdaterades.");
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
        // Boolean status flags only — no amounts (C8) and no guest data (NFR-37).
        static string Flag(bool v) => v ? "ja" : "nej";
        await RecordBookingAuditAsync(booking, AuditAction.PaymentStatusChanged,
            $"Betalningsstatus uppdaterad — handpenning fakturerad: {Flag(depositInvoiced)}, betald: {Flag(depositPaid)}; "
            + $"slutbetalning fakturerad: {Flag(finalInvoiced)}, betald: {Flag(finalPaid)}.");
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

/// <summary>Non-blocking pre-approval warnings (Phase 7) shown on the admin booking detail: the stay
/// overlaps one or more blocks, and/or the overnight count exceeds the beds available for the dates.</summary>
public record ApprovalWarning(
    bool CapacityExceeded, int RequestedGuests, int AvailableBeds, IReadOnlyList<string> BlockDescriptions)
{
    public static readonly ApprovalWarning None = new(false, 0, 0, []);
    public bool OverlapsBlock => BlockDescriptions.Count > 0;
    public bool Any => CapacityExceeded || OverlapsBlock;
}
