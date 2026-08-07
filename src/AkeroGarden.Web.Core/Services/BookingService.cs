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
/// cleaning choices immediately and resolves the sleeping-building selection from the booking's
/// assigned buildings (§8.12/8.16), re-resolved whenever that assignment changes.
///
/// <para>Status changes (approve/deny/close/cancel) route through
/// <see cref="BookingStateMachine"/>; this service is the only place bookings are mutated.</para>
/// </summary>
public class BookingService(
    ApplicationDbContext db, BookingEmailService emails, CleaningZoneService cleaning,
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
            // The stated times are a request the admin may adjust (§2.8). Re-validated here rather
            // than trusted from the form: a value the dropdown cannot produce falls back to the
            // standard (FR-100).
            CheckInTime = StatedTimePolicy.Normalize(input.CheckInTime, MealOccasionResolver.StandardCheckInTime),
            CheckOutTime = StatedTimePolicy.Normalize(input.CheckOutTime, MealOccasionResolver.StandardCheckOutTime),
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

        // Pre-fill the edge-day meals from the standard schedule (§4.1) so the booking has a
        // coherent meal state from creation — the Matlista and the reminder email never have to
        // read "this group eats nothing" while the completion form is still outstanding. The guest
        // overrides these at completion; from then on the stored flags are the truth.
        MealOccasionResolver.ApplyStandardEdgeDefaults(booking);

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
            .Include(b => b.BuildingAssignments)
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
    /// Submits the completion form (FR-55/57/59/60/61/62/105). Re-validates the link server-side
    /// (token found, Approved, unexpired — never trusting the earlier GET), then validates + builds
    /// the participant roster, the group's meal selections with snapshots, the edge-day meal
    /// occasions and the dietary-preference counts via the pure <see cref="CompletionNormalizer"/>,
    /// updates org/invoice details, transitions <c>Approved → DetailsComplete</c> through
    /// <see cref="BookingStateMachine"/>, and notifies the admin (FR-61). The token string is
    /// retained — single-use is enforced by the status gate (once <c>DetailsComplete</c> the link
    /// reports AlreadyCompleted, C10), not by erasing it.
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

        var expectedOvernight = booking.OvernightGuestCount ?? 0;

        // Day guests apply only for a catered meal plan (§4.2); for SelfCateringAll they are dropped
        // server-side regardless of what was posted.
        var dayGuestsApplicable = InquiryNormalizer.DayGuestsApplicable(booking.MealPlan);

        // Resolve the meal occasions from the *submitted* edge-day choices rather than the stored
        // ones — the guest is choosing them right now, and the form must be validated against what
        // it actually says. The flags are written to the booking below, only once it is valid.
        var occasions = MealOccasionResolver.Resolve(new MealOccasionInput(
            booking.CheckIn.Value, booking.CheckOut.Value, booking.MealPlan ?? MealPlan.SelfCateringAll,
            form.ArrivalBreakfast, form.ArrivalLunch, form.ArrivalDinner,
            form.DepartureBreakfast, form.DepartureLunch, form.DepartureDinner,
            form.DayGuestBreakfast, form.DayGuestLunch, form.DayGuestDinner));

        var errors = CompletionNormalizer.Validate(form, occasions, expectedOvernight);
        if (dayGuestsApplicable)
            errors = [.. errors, .. CompletionNormalizer.ValidateDayGuests(
                form.DayGuests ?? [], booking.CheckIn.Value, booking.CheckOut.Value)];
        if (errors.Count > 0)
            return CompletionSubmitResult.Invalid(errors);

        // Authoritative active meal list — queried here, not trusted from the page.
        var activeOptions = await db.MealOptions.Where(m => m.IsActive).ToListAsync();
        var optionsById = activeOptions.ToDictionary(o => o.Id);

        var participants = CompletionNormalizer.BuildParticipants(form);
        if (dayGuestsApplicable)
            participants.AddRange(CompletionNormalizer.BuildDayGuests(form.DayGuests ?? []));
        foreach (var participant in participants)
        {
            participant.BookingId = booking.Id;
            booking.Participants.Add(participant);
        }

        // The group's one choice per sitting (FR-105), with option-name snapshots (NFR-15). A
        // self-catering booking resolves to no sittings, so this is empty for it.
        // Leave Id unset, as BuildParticipants does. Assigning one here makes EF treat the new row
        // as an existing entity when it is discovered on an already-tracked booking, so it issues an
        // UPDATE that matches nothing rather than an INSERT.
        foreach (var selection in CompletionNormalizer.BuildMealSelections(form, occasions, optionsById))
        {
            selection.BookingId = booking.Id;
            booking.MealSelections.Add(selection);
        }

        // Persist the meal occasions the selections were built against, so the stored flags and the
        // stored rows can never disagree. A self-catering booking keeps them all false — there is no
        // meal to attend (§4.1/§4.2), whatever the form posted.
        var catered = booking.MealPlan == MealPlan.FullBoardAll;
        booking.ArrivalBreakfast = catered && form.ArrivalBreakfast;
        booking.ArrivalLunch = catered && form.ArrivalLunch;
        booking.ArrivalDinner = catered && form.ArrivalDinner;
        booking.DepartureBreakfast = catered && form.DepartureBreakfast;
        booking.DepartureLunch = catered && form.DepartureLunch;
        booking.DepartureDinner = catered && form.DepartureDinner;
        booking.DayGuestBreakfast = dayGuestsApplicable && form.DayGuestBreakfast;
        booking.DayGuestLunch = dayGuestsApplicable && form.DayGuestLunch;
        booking.DayGuestDinner = dayGuestsApplicable && form.DayGuestDinner;

        // Dietary preferences as counts, not per-person flags (§4.6) — validated against the
        // headcount above.
        booking.VegetarianCount = catered ? form.VegetarianCount : 0;
        booking.VeganCount = catered ? form.VeganCount : 0;

        // Org/invoice (FR-60) — only retained when the booking is invoiced to an organisation.
        booking.IsOrganisation = form.IsOrganisation;
        booking.OrgName = form.IsOrganisation ? form.OrgName?.Trim() : null;
        booking.OrgNumber = form.IsOrganisation ? form.OrgNumber?.Trim() : null;
        booking.InvoiceAddress = form.IsOrganisation ? form.InvoiceAddress?.Trim() : null;

        // Cleaning (FR-93, §5.5). Huvudbyggnad always applies (a stay request always has overnight
        // lodging); Konferensbyggnad only when the Konferensrum facility was actually selected —
        // re-checked here server-side, never trusted from the posted form. Both snapshot their zone
        // amount immediately. Sleeping is stored as intent only; its priced selections resolve from
        // the booking's assigned buildings below (FR-93).
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
    /// booking from its <b>assigned buildings</b> (§8.12, FR-93) — one row per distinct occupied
    /// physical building's zone, never per building. A no-op unless the guest opted in
    /// (<see cref="Booking.SleepingCleaningPurchased"/> true); if intent is false, any stale
    /// sleeping-zone selections are removed.
    ///
    /// <para>This used to resolve from the participants' assigned rooms, which meant it could only
    /// run <i>after</i> the guest submitted the completion form — hence the machinery that refreshed
    /// it whenever room assignment changed. Buildings are assigned before approval (FR-103), so the
    /// answer is known earlier and the refresh path is gone: this is called at completion submission
    /// and whenever the assignment itself changes.</para>
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

        var occupiedPhysicalBuildingIds = await db.BookingBuildingAssignments
            .Where(a => a.BookingId == booking.Id)
            .Select(a => a.Building!.PhysicalBuildingId)
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

        // Building assignment is a precondition of approval (FR-38/FR-103/D1) — the approval email
        // names the buildings, so approving without them would send the guest an incomplete answer.
        //
        // D6 exempts bookings with no overnight guests, but that exemption is unreachable here and is
        // deliberately not coded (decision 2026-08-06): general inquiries are already rejected above
        // by the BookingType check, and StayRequestInput.OvernightGuestCount is [Range(1, 1000)], so
        // a stay request always has at least one overnight guest. A `> 0` clause would have been a
        // branch no input could take and no test could honestly cover.
        if (!await db.BookingBuildingAssignments.AnyAsync(a => a.BookingId == booking.Id))
        {
            return new ApproveResult(
                false, "Tilldela minst en byggnad innan bokningen godkänns.", null, false);
        }

        var now = DateTime.UtcNow;
        booking.CompletionToken = CompletionTokenPolicy.GenerateToken();
        booking.CompletionTokenExpiry = CompletionTokenPolicy.ComputeExpiry(now, booking.CheckIn.Value);
        booking.Status = BookingStatus.Approved;
        booking.DeleteAfter = null; // live in-progress state (FR-91)
        booking.UpdatedAt = now;
        await RecordBookingAuditAsync(booking, AuditAction.BookingApproved, "Bokningen godkändes.");
        await db.SaveChangesAsync();

        // The approval email names the assigned buildings (FR-103). The booking was loaded with
        // FindAsync, which does not populate the collection — without this the email would silently
        // omit the lodging rather than fail, which is the worst way for it to be wrong.
        await db.Entry(booking).Collection(b => b.BuildingAssignments).LoadAsync();
        await emails.SendApprovalAsync(booking);
        logger.LogInformation("Booking {BookingId} approved", booking.Id);

        // FR-75: warn when check-in is 14 days away or sooner (the link is shortened).
        var daysToCheckIn = booking.CheckIn.Value.DayNumber - DateOnly.FromDateTime(now).DayNumber;
        var shortExpiry = daysToCheckIn <= 14;
        return new ApproveResult(true, null, booking.CompletionTokenExpiry, shortExpiry);
    }

    /// <summary>
    /// Non-blocking pre-approval checks (Phase 7): does the requested stay overlap a block, or does
    /// the overnight guest count exceed the beds in the buildings actually assigned? The admin keeps
    /// override authority — this only warns, it never blocks approval (FR-103/D2), matching the
    /// existing project pattern (FR-75). Assignment itself <i>is</i> blocking; see
    /// <see cref="ApproveAsync"/>.
    /// </summary>
    public async Task<ApprovalWarning> GetApprovalWarningsAsync(Guid id)
    {
        var booking = await db.Bookings.FindAsync(id);
        if (booking is null || booking.BookingType != BookingType.StayRequest
            || booking.CheckIn is not { } checkIn || booking.CheckOut is not { } checkOut)
            return ApprovalWarning.None;

        var guests = booking.OvernightGuestCount ?? 0;

        // Checkout day is a departure, hence StartDate < checkOut rather than <=.
        var blocks = await db.BlockedPeriods
            .Where(p => p.StartDate < checkOut && p.EndDate >= checkIn)
            .OrderBy(p => p.StartDate)
            .ToListAsync();

        // Beds the assigned buildings really offer over these dates — a room-scoped block nets its
        // beds out (FR-102), so the warning is measured against what is on offer rather than paper
        // capacity. Warning on paper capacity would stay silent in exactly the case it exists for.
        var assignedBuildingIds = await db.BookingBuildingAssignments
            .Where(a => a.BookingId == id)
            .Select(a => a.BuildingId)
            .ToListAsync();

        var assigned = (await LoadBuildingInfosAsync())
            .Where(b => assignedBuildingIds.Contains(b.Id))
            .ToList();
        var availableBeds = BuildingExclusionCalculator.Evaluate(assigned, blocks).Sum(b => b.AvailableBedCount);

        // Stated arrival/departure times that deviate from the standard (FR-101). Non-blocking, and
        // deliberately on the same warning strip as the block/capacity notices: an approved booking
        // would otherwise imply the guest's 08:00 arrival was approved too, and the surcharge
        // conversation would never happen.
        var timeDeviations = new List<string>(2);
        if (StatedTimePolicy.Deviation("Incheckning", booking.CheckInTime,
                MealOccasionResolver.StandardCheckInTime) is { } checkInDeviation)
            timeDeviations.Add(checkInDeviation);
        if (StatedTimePolicy.Deviation("Utcheckning", booking.CheckOutTime,
                MealOccasionResolver.StandardCheckOutTime) is { } checkOutDeviation)
            timeDeviations.Add(checkOutDeviation);

        // With nothing assigned the bed count is zero, so a capacity comparison would warn on every
        // single booking — "2 guests exceed 0 beds" is arithmetic, not information, and a warning
        // that is always present is one the admin learns to skip past. The strip carries the
        // deviation warnings too, so that habit is expensive. Say the true thing instead.
        var nothingAssigned = assignedBuildingIds.Count == 0;

        return new ApprovalWarning(
            CapacityExceeded: !nothingAssigned && guests > availableBeds,
            NoLodgingAssigned: nothingAssigned,
            RequestedGuests: guests,
            AvailableBeds: availableBeds,
            BlockDescriptions: await DescribeBlocksAsync(blocks),
            TimeDeviations: timeDeviations);
    }

    /// <summary>
    /// A booking's assigned lodging with the rooms inside each building (FR-103/D4) — shown
    /// read-only on the completion form so the group can divide itself up, since the system does not
    /// record who sleeps where. Building names come from the assignment <b>snapshots</b> (NFR-16),
    /// so renaming the reference data later does not change what the group was told; the room list
    /// is live, as it is descriptive rather than agreed.
    /// </summary>
    public async Task<IReadOnlyList<AssignedLodging>> GetAssignedLodgingAsync(Guid bookingId)
    {
        var assignments = await db.BookingBuildingAssignments
            .Where(a => a.BookingId == bookingId)
            .OrderBy(a => a.BuildingNameSnapshot)
            .Select(a => new
            {
                a.BuildingId,
                a.BuildingNameSnapshot,
                a.PhysicalBuildingNameSnapshot,
                Rooms = db.Rooms
                    .Where(r => r.BuildingId == a.BuildingId && r.IsActive)
                    .OrderBy(r => r.DisplayOrder)
                    .Select(r => new AssignedLodgingRoom(r.Name, r.BedCount))
                    .ToList(),
            })
            .ToListAsync();

        return assignments
            .Select(a => new AssignedLodging(a.BuildingNameSnapshot, a.PhysicalBuildingNameSnapshot, a.Rooms))
            .ToList();
    }

    /// <summary>Active lodging buildings with their active rooms, projected into the pure
    /// calculator's shape (<see cref="BuildingExclusionCalculator"/>).</summary>
    private async Task<IReadOnlyList<BuildingInfo>> LoadBuildingInfosAsync()
    {
        var buildings = await db.Buildings
            .Where(b => b.IsActive)
            .Select(b => new
            {
                b.Id,
                b.Name,
                PhysicalBuildingName = b.PhysicalBuilding != null ? b.PhysicalBuilding.Name : string.Empty,
                Rooms = db.Rooms.Where(r => r.BuildingId == b.Id && r.IsActive)
                    .Select(r => new AssignableRoom(r.Id, r.BedCount)).ToList(),
            })
            .ToListAsync();

        return buildings
            .Select(b => new BuildingInfo(b.Id, b.Name, b.PhysicalBuildingName, b.Rooms))
            .ToList();
    }

    /// <summary>The buildings the admin may assign for a booking's dates, with the beds each really
    /// offers (FR-102/103). Buildings closed by a whole-property or building-scope block over the
    /// stay are returned as unavailable rather than omitted, so the UI can show <i>why</i>.</summary>
    public async Task<IReadOnlyList<BuildingAvailability>> GetAssignableBuildingsAsync(Guid bookingId)
    {
        var booking = await db.Bookings.FindAsync(bookingId);
        if (booking?.CheckIn is not { } checkIn || booking.CheckOut is not { } checkOut)
            return [];

        var blocks = await db.BlockedPeriods
            .Where(p => p.StartDate < checkOut && p.EndDate >= checkIn)
            .ToListAsync();

        return BuildingExclusionCalculator.Evaluate(await LoadBuildingInfosAsync(), blocks);
    }

    /// <summary>
    /// Sets the lodging buildings a booking's group sleeps in (FR-103, §8.16), replacing any existing
    /// assignment. Editable for the booking's life (D5). Each row snapshots the building and physical
    /// building names at assignment time (NFR-16), so a later rename does not rewrite what was agreed.
    ///
    /// <para>A building closed by a whole-property or building-scope block over the stay is
    /// <b>rejected</b> (FR-102). That protection used to exist only as a side effect of the
    /// completion form's room picker excluding blocked rooms; with the picker gone, nothing else
    /// prevents assigning a closed building.</para>
    ///
    /// <para>Re-resolves the sleeping-cleaning selections, since those follow the assigned buildings
    /// (FR-93).</para>
    /// </summary>
    public async Task<BuildingAssignmentResult> SetBuildingAssignmentsAsync(
        Guid bookingId, IReadOnlyCollection<Guid> buildingIds)
    {
        var booking = await db.Bookings
            .Include(b => b.BuildingAssignments)
            .SingleOrDefaultAsync(b => b.Id == bookingId);
        if (booking is null) return BuildingAssignmentResult.Fail("Bokningen kunde inte hittas.");
        if (booking.BookingType != BookingType.StayRequest)
            return BuildingAssignmentResult.Fail("Endast bokningsförfrågningar kan tilldelas byggnader.");

        var evaluated = (await GetAssignableBuildingsAsync(bookingId)).ToDictionary(b => b.Building.Id);

        var unknown = buildingIds.Where(id => !evaluated.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
            return BuildingAssignmentResult.Fail("En eller flera valda byggnader finns inte eller är inaktiva.");

        var blocked = buildingIds.Where(id => !evaluated[id].IsAvailable).Select(id => evaluated[id].Building.Name).ToList();
        if (blocked.Count > 0)
        {
            return BuildingAssignmentResult.Fail(
                $"Följande byggnader är blockerade under vistelsen och kan inte tilldelas: {string.Join(", ", blocked)}.");
        }

        db.BookingBuildingAssignments.RemoveRange(booking.BuildingAssignments);
        booking.BuildingAssignments.Clear();

        var now = DateTime.UtcNow;
        foreach (var id in buildingIds.Distinct())
        {
            var info = evaluated[id].Building;
            booking.BuildingAssignments.Add(new BookingBuildingAssignment
            {
                BookingId = booking.Id,
                BuildingId = id,
                BuildingNameSnapshot = info.Name,
                PhysicalBuildingNameSnapshot = info.PhysicalBuildingName,
                AssignedAt = now,
            });
        }

        booking.UpdatedAt = now;
        // Building names are reference data, not guest data — safe to record (NFR-37).
        var names = booking.BuildingAssignments.Select(a => a.BuildingNameSnapshot).OrderBy(n => n).ToList();
        await RecordBookingAuditAsync(booking, AuditAction.BuildingAssignmentChanged,
            names.Count == 0
                ? "Byggnadstilldelningen togs bort."
                : $"Byggnadstilldelning uppdaterad: {string.Join(", ", names)}.");
        await db.SaveChangesAsync();

        await ResolveSleepingCleaningSelectionsAsync(booking);
        logger.LogInformation(
            "Booking {BookingId} building assignment set to {Count} building(s)", booking.Id, names.Count);
        return BuildingAssignmentResult.Ok();
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

    /// <summary>Loads a booking with its participants + the group's meal selections (admin
    /// participant view/edit).</summary>
    public async Task<Booking?> GetWithParticipantsAsync(Guid id) =>
        await db.Bookings
            .Include(b => b.Participants)
            .Include(b => b.MealSelections)
            .SingleOrDefaultAsync(b => b.Id == id);

    /// <summary>
    /// Applies the admin's post-submission edits to the <b>participant roster</b> (FR-48): names,
    /// age category and allergies. <b>No status change and no guest reopen</b> (C10).
    ///
    /// <para>Split from the meal and time edits deliberately. Those are booking-level facts shown in
    /// their own sections of the panel, and a single save spanning all three would write values the
    /// admin never opened — a "Spara deltagaruppgifter" button that also rewrote arrival times is
    /// the kind of quiet side effect that is impossible to notice until it is wrong.</para>
    /// </summary>
    public async Task SaveParticipantRosterAsync(Guid bookingId, IReadOnlyList<ParticipantEdit> edits)
    {
        var booking = await db.Bookings
            .Include(b => b.Participants)
            .SingleOrDefaultAsync(b => b.Id == bookingId);
        if (booking is null) return;

        var editsById = edits.ToDictionary(e => e.Id);

        foreach (var participant in booking.Participants)
        {
            if (!editsById.TryGetValue(participant.Id, out var edit)) continue;

            participant.FullName = (edit.FullName ?? string.Empty).Trim();
            participant.AgeCategory = edit.AgeCategory;
            participant.Allergies = edit.Allergies ?? [];
            participant.AllergyNotes = string.IsNullOrWhiteSpace(edit.AllergyNotes)
                ? null
                : edit.AllergyNotes.Trim();
        }

        booking.UpdatedAt = DateTime.UtcNow;
        // Count only — never participant names or allergy values (NFR-37).
        await RecordBookingAuditAsync(booking, AuditAction.ParticipantsEdited,
            $"Deltagaruppgifter uppdaterades ({booking.Participants.Count} deltagare).");
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Applies the admin's edits to the group's meals (FR-48/105): which meals the group takes on
    /// the arrival and departure day, which meals day guests attend, the dish chosen at each
    /// sitting, and the dietary-preference counts.
    ///
    /// <para>Changing an edge-day flag changes which sittings exist, so the stored dishes are
    /// reconciled against them: dropping a meal deletes its dish, because a dish for a meal nobody
    /// attends would keep appearing on the kitchen list. Adding a meal leaves the new sitting
    /// <i>unchosen</i> rather than defaulting it — the Matlista prints it as "Ej vald" with its
    /// headcount so someone decides, instead of the system inventing an agreement.</para>
    /// </summary>
    public async Task SaveGroupMealsAsync(Guid bookingId, GroupMealEdit edit)
    {
        var booking = await db.Bookings
            .Include(b => b.MealSelections)
            .SingleOrDefaultAsync(b => b.Id == bookingId);
        if (booking is null) return;

        var optionsById = (await db.MealOptions.Where(m => m.IsActive).ToListAsync()).ToDictionary(o => o.Id);

        // Only a catered booking has meals at all (§4.1) — a self-catering one keeps every flag and
        // count at zero however the form was posted. Applied before the dishes are reconciled,
        // because these flags decide which meals exist.
        var catered = booking.MealPlan == MealPlan.FullBoardAll;
        booking.ArrivalBreakfast = catered && edit.ArrivalBreakfast;
        booking.ArrivalLunch = catered && edit.ArrivalLunch;
        booking.ArrivalDinner = catered && edit.ArrivalDinner;
        booking.DepartureBreakfast = catered && edit.DepartureBreakfast;
        booking.DepartureLunch = catered && edit.DepartureLunch;
        booking.DepartureDinner = catered && edit.DepartureDinner;
        booking.DayGuestBreakfast = catered && edit.DayGuestBreakfast;
        booking.DayGuestLunch = catered && edit.DayGuestLunch;
        booking.DayGuestDinner = catered && edit.DayGuestDinner;
        booking.VegetarianCount = catered ? Math.Max(0, edit.VegetarianCount) : 0;
        booking.VeganCount = catered ? Math.Max(0, edit.VeganCount) : 0;

        // Reconcile the stored dishes against the meals the booking now has. Working from the
        // resolved meals rather than from the posted list is what lets a dish be set for a meal that
        // had none — the case the admin creates by ticking an edge-day meal.
        var meals = MealOccasionResolver.Resolve(booking).SelectionSlots;
        var chosenBySlot = edit.Meals.ToDictionary(m => new MealSlot(m.Date, m.MealType), m => m.MealOptionId);

        foreach (var slot in meals)
        {
            var existing = booking.MealSelections.FirstOrDefault(m => m.Date == slot.Date && m.MealType == slot.MealType);
            var chosen = chosenBySlot.TryGetValue(slot, out var optionId) ? optionId : existing?.MealOptionId;

            // Not decided yet — a legitimate in-between state for the admin (unlike the guest's
            // submission, which must be complete). Any dish previously agreed for this meal is left
            // alone rather than cleared, so an unfilled dropdown cannot wipe a real choice.
            if (chosen is not { } id || !optionsById.TryGetValue(id, out var option) || option.MealType != slot.MealType)
                continue;

            if (existing is null)
            {
                booking.MealSelections.Add(new BookingMealSelection
                {
                    BookingId = booking.Id,
                    Date = slot.Date,
                    MealType = slot.MealType,
                    MealOptionId = option.Id,
                    MealOptionSnapshot = option.Name,
                });
            }
            else
            {
                existing.MealOptionId = option.Id;
                existing.MealOptionSnapshot = option.Name;
            }
        }

        // A dish for a meal the group no longer takes has to go with the meal — otherwise the
        // Matlista keeps cooking for a sitting nobody attends.
        var validSlots = meals.ToHashSet();
        var orphaned = booking.MealSelections
            .Where(m => !validSlots.Contains(new MealSlot(m.Date, m.MealType)))
            .ToList();
        foreach (var selection in orphaned)
        {
            booking.MealSelections.Remove(selection);
            db.BookingMealSelections.Remove(selection);
        }

        booking.UpdatedAt = DateTime.UtcNow;
        // Dish names are reference data, not guest data — safe to count, but nothing personal (NFR-37).
        await RecordBookingAuditAsync(booking, AuditAction.ParticipantsEdited,
            $"Gruppens måltider uppdaterades ({booking.MealSelections.Count} valda rätter).");
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Applies the admin's adjustment to the guest's stated arrival/departure times (FR-48, §2.8).
    /// An omitted or unusable value leaves the stored one alone rather than resetting it to the
    /// standard — a form that did not post the field must not silently rewrite what the guest asked
    /// for. The times are a request either way: nothing here prices or approves one.
    /// </summary>
    public async Task SaveStatedTimesAsync(Guid bookingId, string? checkInTime, string? checkOutTime)
    {
        var booking = await db.Bookings.FindAsync(bookingId);
        if (booking is null) return;

        if (StatedTimePolicy.TryParse(checkInTime, out var parsedCheckIn)) booking.CheckInTime = parsedCheckIn;
        if (StatedTimePolicy.TryParse(checkOutTime, out var parsedCheckOut)) booking.CheckOutTime = parsedCheckOut;

        booking.UpdatedAt = DateTime.UtcNow;
        // Times are the guest's own stated data, but not identifying — safe to record (NFR-37).
        await RecordBookingAuditAsync(booking, AuditAction.ParticipantsEdited,
            $"Angivna tider uppdaterades (incheckning {StatedTimePolicy.Display(booking.CheckInTime, MealOccasionResolver.StandardCheckInTime)}, "
            + $"utcheckning {StatedTimePolicy.Display(booking.CheckOutTime, MealOccasionResolver.StandardCheckOutTime)}).");
        await db.SaveChangesAsync();
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

/// <summary>One assigned lodging building and the rooms inside it, for the completion form's
/// read-only "Ert boende" panel (FR-103/D4).</summary>
public record AssignedLodging(string Name, string PhysicalBuildingName, IReadOnlyList<AssignedLodgingRoom> Rooms);

/// <summary>A room inside an assigned building — descriptive only, never an assignment target.</summary>
public record AssignedLodgingRoom(string Name, int BedCount);

/// <summary>Outcome of setting a booking's building assignment (FR-103): success, or a Swedish
/// reason — an unknown/inactive building, or one closed by a block over the stay (FR-102).</summary>
public record BuildingAssignmentResult(bool Succeeded, string? Error)
{
    public static BuildingAssignmentResult Ok() => new(true, null);
    public static BuildingAssignmentResult Fail(string error) => new(false, error);
}

/// <summary>Non-blocking pre-approval warnings (Phase 7) shown on the admin booking detail: the stay
/// overlaps one or more blocks, the overnight count exceeds the beds the assigned buildings offer
/// over the dates, and/or the guest stated an arrival or departure time that deviates from the
/// standard (FR-101). None of them block approval — each exists so a conversation is not missed.</summary>
public record ApprovalWarning(
    bool CapacityExceeded, bool NoLodgingAssigned, int RequestedGuests, int AvailableBeds,
    IReadOnlyList<string> BlockDescriptions, IReadOnlyList<string> TimeDeviations)
{
    public static readonly ApprovalWarning None = new(false, false, 0, 0, [], []);
    public bool OverlapsBlock => BlockDescriptions.Count > 0;
    public bool HasTimeDeviation => TimeDeviations.Count > 0;
    public bool Any => CapacityExceeded || NoLodgingAssigned || OverlapsBlock || HasTimeDeviation;
}
