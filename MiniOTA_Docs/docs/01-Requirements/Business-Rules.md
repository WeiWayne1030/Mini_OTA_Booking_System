# Business Rules — Mini OTA Booking System

- **Version:** 0.2
- **Related specification:** [SRS.md](SRS.md)

This document defines the domain rules that must remain true regardless of API or infrastructure implementation.

## 1. Date and stay rules

### BR-001 — Stay-date range

`CheckInDate` is included and `CheckOutDate` is excluded.

Example: check-in `2026-11-10`, check-out `2026-11-13` consumes inventory for:
- 2026-11-10
- 2026-11-11
- 2026-11-12

It does not consume inventory for 2026-11-13.

### BR-002 — Valid date range

`CheckOutDate` must be later than `CheckInDate`.

The first implementation should reject past or otherwise unsupported dates according to the API's documented validation policy.

### BR-003 — Valid room count

`RoomCount` must be a positive integer.

## 2. Daily inventory rules

For each room type and stay date, maintain:
- `Total`
- `Available`
- `Reserved`
- `Confirmed`

### BR-004 — Inventory invariant

For every inventory row:

`Total = Available + Reserved + Confirmed`

All quantities must be non-negative integers.

### BR-005 — No overselling

`Available` must never be less than zero.

A reservation may only be accepted when every requested stay date has enough available inventory.

### BR-006 — Reserve inventory

For a reservation of `N` rooms, on every stay date:

- `Available = Available - N`
- `Reserved = Reserved + N`

### BR-007 — Confirm inventory

When a pending reservation is successfully paid, on every stay date:

- `Reserved = Reserved - N`
- `Confirmed = Confirmed + N`

### BR-008 — Release pending inventory

When a pending reservation is cancelled or expires, on every stay date:

- `Reserved = Reserved - N`
- `Available = Available + N`

### BR-009 — Release confirmed inventory

When a confirmed reservation is cancelled, on every stay date:

- `Confirmed = Confirmed - N`
- `Available = Available + N`

The first release uses simulated payment and cancellation. Real refund and payment-provider reconciliation are out of scope.

## 3. Atomicity and concurrency rules

### BR-010 — Multi-date atomicity

A reservation spanning multiple dates must reserve all dates or none.

### BR-011 — Atomic reservation creation

Reservation creation and all corresponding daily inventory updates must commit in one database transaction.

### BR-012 — Atomic state transition

A reservation state transition and its corresponding inventory movement must commit in one database transaction.

### BR-013 — Concurrency protection

The implementation must use a database-enforced concurrency strategy. Application-side read-then-write checks alone are insufficient.

Candidate techniques to evaluate:
- Conditional atomic updates
- Optimistic concurrency control
- Pessimistic row locking

The selected implementation and trade-offs must be recorded in an ADR.

## 4. Reservation lifecycle rules

### BR-014 — Allowed state transitions

```text
PendingPayment -> Confirmed
PendingPayment -> Expired
PendingPayment -> Cancelled
Confirmed      -> Cancelled
```

No other transitions are allowed in the first release.

### BR-015 — Terminal states

`Expired` and `Cancelled` are terminal states in the first release.

A `Confirmed` reservation may transition to `Cancelled` once.

### BR-016 — Single winner

If payment, cancellation, and expiration race, the database must allow only one valid transition from the current state to commit.

The losing operation must not change inventory.

### BR-017 — Exactly-once effect

The system may execute handlers more than once due to retries, but the business effect of confirming, cancelling, or expiring a reservation must occur at most once.

This is achieved through state guards, transactional updates, and idempotent processing—not by assuming a message or job is delivered exactly once.

## 5. Idempotency rules

### BR-018 — Create-reservation idempotency

A client must supply an idempotency key when creating a reservation.

The key must be scoped to an appropriate caller or client identity and protected by a database uniqueness constraint.

### BR-019 — Key reuse

- Same key + equivalent request: return the original result.
- Same key + different request: reject the request as a conflict.
- Concurrent requests with the same key: create at most one reservation.

## 6. Payment timeout rules

### BR-020 — Expiration eligibility

Only a `PendingPayment` reservation whose payment deadline has passed is eligible for expiration.

### BR-021 — Expiration retry safety

Expiration processing must be safe to retry. A reservation that has already been confirmed, cancelled, or expired must not have inventory released again.

### BR-022 — Time authority

Persist timestamps in UTC. The expiration decision must use a consistent server/database time policy rather than trusting a client-supplied clock.

## 7. Required invariants for tests

Every integration and concurrency test should verify, as applicable:

1. `Total = Available + Reserved + Confirmed`.
2. No inventory quantity is negative.
3. No successful reservations exceed total inventory.
4. A failed multi-date reservation leaves all affected inventory unchanged.
5. A reservation transition and inventory movement are atomic.
6. Retried operations do not apply inventory changes twice.
7. Concurrent operations leave the database in a valid state.
