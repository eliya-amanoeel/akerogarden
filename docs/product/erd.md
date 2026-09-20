# Åkerögården — Entity Relationship Diagram

**Derived from:** requirements.md, Section 8 (Core Data Model)
**Status:** Current — the implemented EF Core / PostgreSQL schema. Keep in sync with §8; if the diagram and the entities drift, reconcile them rather than letting them diverge.

This is the diagram Section 8 of the SRS calls for. Attribute lists below show the **primary key, foreign keys, and the fields that drive relationships or logic** — see requirements.md §8 for the complete field list of each entity.

> **Admin accounts are out of this diagram.** They live in ASP.NET Core Identity's own tables (`AspNetUsers`, etc.) and are treated as infrastructure, not application entities (requirements.md §6.4).

---

## Diagram

```mermaid
erDiagram
    PHYSICALBUILDING ||--o{ BUILDING : "contains lodging units"
    PHYSICALBUILDING |o--o{ FACILITY : "houses (nullable)"
    PHYSICALBUILDING |o--o| CLEANINGZONE : "priced by (nullable)"
    BUILDING ||--o{ ROOM : "contains"
    BUILDING |o--o{ BLOCKEDPERIOD : "scoped to (nullable)"
    ROOM     |o--o{ BLOCKEDPERIOD : "scoped to (nullable)"
    BUILDING ||--o{ BOOKINGBUILDINGASSIGNMENT : "assigned to bookings"

    BOOKING  ||--o{ PARTICIPANT : "has"
    BOOKING  ||--o{ BOOKINGBUILDINGASSIGNMENT : "sleeps in"
    BOOKING  ||--o{ BOOKINGMEALSELECTION : "has"
    BOOKING  ||--o{ BOOKINGCLEANINGSELECTION : "has"
    BOOKING  ||--o{ BOOKINGFACILITYSELECTION : "has"

    MEALOPTION  ||--o{ BOOKINGMEALSELECTION : "chosen as"

    CLEANINGZONE ||--o{ BOOKINGCLEANINGSELECTION : "selected as"
    FACILITY     ||--o{ BOOKINGFACILITYSELECTION : "selected as"

    BOOKING  |o--o{ AUDITLOG : "audited by (nullable)"

    BOOKING {
        guid Id PK
        int SequenceNumber "DB identity, monotonic, no per-year reset"
        string BookingReference "AG-YYYY-NNNN, frozen at creation (FR-92)"
        enum BookingType "StayRequest | GeneralInquiry"
        enum Status "see §2.4"
        string ContactName
        string ContactEmail
        date CheckIn "null for general inquiry"
        date CheckOut "null for general inquiry"
        time CheckInTime "stated arrival, default 14:00 (FR-100)"
        time CheckOutTime "stated departure, default 12:00 (FR-100)"
        int OvernightGuestCount
        int DayGuestCount "inquiry-stage estimate"
        enum MealPlan "FullBoardAll | SelfCateringAll"
        bool ArrivalBreakfast "edge-day meals: stored, not derived (FR-105)"
        bool DepartureDinner "…six flags, arrival + departure"
        bool DayGuestLunch "…three flags, all day guests on the booking"
        int VegetarianCount "dietary preference as a count (§4.6)"
        int VeganCount "dietary preference as a count (§4.6)"
        datetime TermsAcceptedAt "terms accepted at inquiry, null if admin-created (FR-111)"
        bool CreatedByAdmin "admin entered this booking, not the guest (FR-47)"
        guid OnBehalfOfAdminId "which admin, when booked by telephone (FR-112); marks ContactEmail as a staff address"
        datetime TermsAttestedAt "admin attested the terms verbally (FR-112) — never merged into TermsAcceptedAt"
        datetime AllergyConsentAt "art. 9 consent, null when no allergy declared (FR-110)"
        bool MainBuildingCleaningPurchased "completion choice, FR-93"
        bool SleepingCleaningPurchased "completion choice; rows resolve from assigned buildings"
        bool ConferenceCleaningPurchased "completion choice, only if Konferensrum booked"
        date DeleteAfter "computed, FR-91"
    }

    PHYSICALBUILDING {
        guid Id PK
        string Name "Lilla bostadslängan, Huvudbyggnad, …"
        bool IsActive
        int DisplayOrder
    }

    BUILDING {
        guid Id PK
        guid PhysicalBuildingId FK "the physical building it sits in"
        string Name "lodging unit: Blidö, Tjockö, Smedjan, …"
        bool IsAccessible
        bool IsActive
        int DisplayOrder
    }

    ROOM {
        guid Id PK
        guid BuildingId FK
        string Name
        int BedCount
        bool IsActive
        int DisplayOrder
    }

    PARTICIPANT {
        guid Id PK
        guid BookingId FK
        string FullName
        enum ParticipantType "Overnight | DayGuest"
        enum AgeCategory "Adult | Child | Infant"
        date AttendanceDate "day guests only"
        array Allergies "medical only; GDPR art. 9 (§4.6)"
        string AllergyNotes "free text, accompanies OTHER"
    }

    BOOKINGMEALSELECTION {
        guid Id PK
        guid BookingId FK "re-parented from Participant (FR-105)"
        guid MealOptionId FK
        date Date
        enum MealType "Lunch | Dinner"
        string MealOptionSnapshot "history (NFR-15)"
    }

    BOOKINGBUILDINGASSIGNMENT {
        guid Id PK
        guid BookingId FK "cascade-delete"
        guid BuildingId FK "the lodging building the group sleeps in"
        string BuildingNameSnapshot "history (NFR-16)"
        string PhysicalBuildingNameSnapshot "history (NFR-16)"
        datetime AssignedAt
    }

    MEALOPTION {
        guid Id PK
        string Name
        enum MealType "Lunch | Dinner"
        bool IsVegetarian
        bool IsActive
        int DisplayOrder
    }

    FACILITY {
        guid Id PK
        guid PhysicalBuildingId FK "nullable; Konferensrum → Konferensbyggnad"
        string Name
        string ImageUrl "blob name, served via /bilder/{name} (§8.8)"
        decimal AmountExVat
        decimal VatRate
        bool IsActive
        int DisplayOrder
    }

    CLEANINGZONE {
        guid Id PK
        guid PhysicalBuildingId FK "nullable; null for Everything bundle"
        string Name
        decimal AmountExVat
        decimal VatRate
        enum Kind "MainBuilding | SleepingBuilding | Conference | Everything"
        array IncludesZoneIds "Everything bundle; rate-card ref only"
        bool IsActive
        int DisplayOrder
    }

    BOOKINGCLEANINGSELECTION {
        guid Id PK
        guid BookingId FK
        guid CleaningZoneId FK
        string ZoneNameSnapshot
        decimal AmountExVatSnapshot
        decimal VatRateSnapshot
    }

    BOOKINGFACILITYSELECTION {
        guid Id PK
        guid BookingId FK
        guid FacilityId FK
        string FacilityNameSnapshot
        decimal AmountExVatSnapshot
        decimal VatRateSnapshot
    }

    BLOCKEDPERIOD {
        guid Id PK
        enum Scope "WholeProperty | Building | Room"
        guid BuildingId FK "nullable"
        guid RoomId FK "nullable"
        date StartDate
        date EndDate
        string Reason "internal"
    }

    PRICERULE {
        guid Id PK
        string Label
        decimal AmountExVat
        decimal VatRate
        date SeasonStart "nullable"
        date SeasonEnd "nullable"
        bool IsActive
    }

    AUDITLOG {
        guid Id PK
        guid BookingId FK "nullable; cascade-delete; booking-scoped entries"
        datetime Timestamp
        enum ActorType "Admin | Guest | System"
        guid ActorUserId "nullable; Identity user, not FK-constrained"
        string ActorEmailSnapshot "nullable; admin email at write time"
        string ActorNameSnapshot "nullable; admin name at write time"
        enum Action "BookingApproved, BuildingAssignmentChanged, …"
        string EntityType "Booking, CleaningZone, Room, …"
        guid EntityId "affected record"
        string BookingReferenceSnapshot "nullable; the booking's reference (FR-92), not PII"
        string Summary "no guest PII (NFR-37)"
    }
```

> `PRICERULE` has no foreign keys — it is standalone reference data, displayed read-only on the public prices page (FR-82) and on booking records (FR-34). It is shown above without relationship lines for that reason.

---

## Relationships at a glance

| From | To | Cardinality | Via | Notes |
|---|---|---|---|---|
| PhysicalBuilding | Building | 1 → many | `Building.PhysicalBuildingId` | A physical building holds several lodging units (Lilla bostadslängan → Blidö/Ljusterö/Singö/Tjockö); standalone cabins are their own physical building |
| PhysicalBuilding | Facility | 0..1 → many | `Facility.PhysicalBuildingId` | Nullable — set only where a facility sits in a cleaning-relevant building (Konferensrum → Konferensbyggnad) |
| PhysicalBuilding | CleaningZone | 0..1 → 0..1 | `CleaningZone.PhysicalBuildingId` | The building a zone cleans; null for the `Everything` bundle |
| Building | Room | 1 → many | `Room.BuildingId` | A building contains its rooms |
| Booking | Participant | 1 → many | `Participant.BookingId` | General inquiries have zero participants |
| Booking | BookingBuildingAssignment | 1 → many | `BookingBuildingAssignment.BookingId` | Which lodging buildings the group sleeps in; assigned before approval (FR-103), cascade-deleted with the booking |
| Building | BookingBuildingAssignment | 1 → many | `BookingBuildingAssignment.BuildingId` | Live ref; name + physical-building name snapshotted (NFR-16). Blocks hard-delete while assigned (FR-16) |
| Booking | BookingMealSelection | 1 → many | `BookingMealSelection.BookingId` | One row per day × meal type — **the whole group's choice**, not per participant (FR-105) |
| MealOption | BookingMealSelection | 1 → many | `BookingMealSelection.MealOptionId` | Live ref; name also snapshotted |
| Booking | BookingCleaningSelection | 1 → many | `BookingCleaningSelection.BookingId` | One row per purchased cleaning zone; populated at completion (FR-93) |
| CleaningZone | BookingCleaningSelection | 1 → many | `BookingCleaningSelection.CleaningZoneId` | Live ref; name + amount snapshotted (NFR-33) |
| Booking | BookingFacilitySelection | 1 → many | `BookingFacilitySelection.BookingId` | One row per selected facility |
| Facility | BookingFacilitySelection | 1 → many | `BookingFacilitySelection.FacilityId` | Live ref; name + amount snapshotted (NFR-34) |
| Building | BlockedPeriod | 0..1 → many | `BlockedPeriod.BuildingId` | Null for `WholeProperty` scope |
| Room | BlockedPeriod | 0..1 → many | `BlockedPeriod.RoomId` | Set only for `Room` scope |
| CleaningZone | CleaningZone | many ↔ many (self) | `CleaningZone.IncludesZoneIds[]` | A bundle (Hela lägret) covers other zones |
| Booking | AuditLog | 0..1 → many | `AuditLog.BookingId` | Nullable, cascade-delete — set for booking-scoped audit entries; null for reference-data/admin-account entries (which are keyed only by `EntityType`/`EntityId`) |

---

## Modelling notes

1. **Snapshot pattern.** `BookingMealSelection`, `BookingCleaningSelection`, `BookingFacilitySelection`, and `BookingBuildingAssignment` each keep a *live FK* **and** a *text/amount snapshot* taken at the time of agreement. The FK powers queries; the snapshot preserves what was agreed if the reference data is later edited (NFR-15/16/33/34). Hard-delete of referenced reference data is blocked (FR-16/20/26/30/88). *Cleaning (FR-93) is chosen at completion, not inquiry, and all three kinds snapshot at submission — the `SleepingBuilding` rows resolve from the booking's assigned buildings, which are already known because assignment precedes approval (FR-103).*

2. **No monetary totals — and no payment state at all.** Per C8, no booking total / deposit / balance is stored. `AmountExVat` on `CleaningZone`/`PriceRule` is reference data only. The four deposit/final-payment booleans were removed from `Booking` in 2026-08 (FR-42): invoicing is manual and entirely outside the system (§5.4), so tracking flags here formed an island of state that drove nothing.

3. **Array columns.** `CleaningZone.IncludesZoneIds` is a PostgreSQL `uuid[]` column (supported by EF Core), used only for the Hela lägret bundle self-reference. Facility and cleaning *selections* are full child entities (with snapshots), not arrays.

4. **Facility & cleaning selections are symmetric.** Both `BookingFacilitySelection` and `BookingCleaningSelection` are child entities with a live FK plus name/amount snapshots, so an agreed facility or cleaning price survives later edits to the reference record (NFR-33/34, resolved consistently with C6). This was previously an asymmetry (facilities were a bare ID array) — aligned in v0.32.

5. **PhysicalBuilding is the cleaning/blocking level.** Cleaning is priced per physical building, so `CleaningZone` links to `PhysicalBuilding` by **FK** (not a `uuid[]` of building/facility IDs) — sleeping-cleaning resolves by the structural join assigned `Building.PhysicalBuildingId` → `CleaningZone.PhysicalBuildingId` with referential integrity, and `Facility.PhysicalBuildingId` drives conference cleaning. The lodging `Building` (Blidö, …) is a *unit inside* a physical building; the level was promoted to a first-class entity (rather than a `CleaningZone`↔`Building` join table) because a concrete future use exists — blocking a whole physical building (e.g. all of Stora bostadslängan) in one action, a `BlockedPeriod.PhysicalBuilding` scope, deferred until the client confirms it is a recurring need (requirements.md §10).

6. **`Room` is capacity data, not an assignment target.** Participants used to carry a `RoomId`; that link was removed in 2026-08 along with per-participant room assignment (FR-56). Bookings now attach to whole lodging **buildings** (`BookingBuildingAssignment`, §8.16) and the group divides itself among the rooms. `Room` remains because bed counts drive the approval-time capacity warning (FR-103) and the admin's building overview, and because `BlockedPeriod` can still be scoped to a single room — it simply no longer appears in any booking's data. One consequence worth stating: with no participant pointing at a room, the old hard-delete guard for rooms would count zero for ever and silently pass, so FR-20 was repointed at `BlockedPeriod` references, which are logical rather than foreign keys and therefore the real orphan risk.

7. **`Participant` is a roster.** It carries who is present, their age category, and their allergies — nothing else. Meal choice, catering plan, room, and breakfast attendance were all removed: each was a booking-level decision that had been stored per person, and each could disagree with the booking it was copied from. Dietary *preferences* became counts on `Booking` for the same reason, which also stores strictly less personal data.

8. **AuditLog is accountability, not preservation.** The snapshot pattern (note 1) answers *"what was agreed?"*; `AuditLog` (§8.15) answers *"who changed this, and when?"* — a distinct concern, so it is a separate append-only entity, not more snapshot columns. It is **semi-polymorphic**: `EntityType`/`EntityId` identify the affected record of any kind, while an optional hard FK `BookingId` (ON DELETE CASCADE) is set for booking-scoped entries so they are purged with the booking (FR-98) and listable per booking. It stores **no guest personal data** (NFR-37) — booking-change entries carry the action + `BookingReferenceSnapshot` only, never guest field values or art. 9 dietary data. Actor is `Admin` / `Guest` (magic-link completion) / `System` (background jobs), so every change is attributable. Admin identity is stored as a live `ActorUserId` plus an `ActorEmailSnapshot` (not FK-constrained — Identity tables are infrastructure, §6.4 — and admins can be removed, FR-74).
