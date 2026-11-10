# Mini OTA Booking System — Software Requirements Specification

- **Document version:** 0.2
- **Status:** Requirements baseline for the first implementation
- **Architecture:** Layered Architecture
- **Backend:** ASP.NET Core / C#
- **Target framework:** .NET 9
- **Primary database:** PostgreSQL

## 1. Purpose

Build a deliberately small online hotel-booking system to learn and demonstrate high-concurrency application design, transactional consistency, reservation lifecycle management, and performance testing.

The first release is a learning baseline, not a production-ready commercial OTA.

## 2. Scope

### 2.1 In scope

- One hotel
- One room type
- Inventory maintained separately for each stay date
- Availability query
- Reservation creation
- Simulated payment
- Reservation cancellation
- Expiration of unpaid reservations
- Automated unit and integration tests
- Concurrent booking and load-test scenarios

### 2.2 Out of scope

- Real payment gateway integration
- Multiple hotels or room types
- Coupons, promotions, and loyalty programs
- User membership tiers and authentication beyond what is required for the learning baseline
- Third-party OTA integrations
- Production-grade tax, invoicing, and refund workflows

## 3. Actors

### Customer

- Search availability by check-in date, check-out date, and room count
- Create a reservation
- Retrieve reservation details
- Simulate payment
- Cancel a reservation

### System

- Maintain daily inventory
- Enforce reservation state transitions
- Expire unpaid reservations
- Release inventory exactly once
- Record operational logs needed to diagnose failures

## 4. Terminology

| Term | Definition |
|---|---|
| Stay date | A night for which inventory is consumed |
| Check-in date | First occupied night; included in the stay-date range |
| Check-out date | Departure date; excluded from the stay-date range |
| Available | Inventory not currently held or confirmed |
| Reserved | Inventory temporarily held for a pending reservation |
| Confirmed | Inventory allocated to a successfully paid reservation |
| Reservation | A booking record covering one or more stay dates |
| Idempotency key | Client-supplied key used to identify retries of the same create-reservation operation |

## 5. Functional Requirements

### FR-001 — Query availability

The system shall accept:
- `CheckInDate`
- `CheckOutDate`
- `RoomCount`

The system shall check every stay date from check-in inclusive to check-out exclusive. The result shall indicate whether the requested room count is available for every night.

### FR-002 — Create reservation

The system shall:
1. Validate the date range and room count.
2. Check and reserve the requested inventory for every stay date.
3. Create a reservation in `PendingPayment` state.
4. Set a payment deadline.
5. Return the reservation identifier and payment deadline.

Inventory changes and reservation creation shall commit atomically. If any stay date cannot be reserved, the operation shall fail without partially changing inventory.

### FR-003 — Simulated payment

The system shall allow a pending reservation to be paid through a simulated payment operation.

On successful simulated payment:
- The reservation shall transition to `Confirmed`.
- The corresponding daily inventory shall move from `Reserved` to `Confirmed`.

Payment shall not succeed for an expired or cancelled reservation.

### FR-004 — Retrieve reservation

The system shall provide a way to retrieve a reservation and its current status.

### FR-005 — Cancel reservation

The system shall allow cancellation according to the business rules. Inventory shall be released in the same database transaction as the reservation state change.

### FR-006 — Expire unpaid reservation

The system shall identify pending reservations whose payment deadline has passed.

On successful expiration:
- The reservation shall transition to `Expired`.
- The corresponding daily inventory shall move from `Reserved` to `Available`.

Expiration processing shall be safe to retry and shall not release inventory more than once.

### FR-007 — Idempotent reservation creation

Reservation creation shall require an idempotency key. Repeating a request with the same key and equivalent request data shall return the original operation result rather than creating another reservation.

The system shall reject reuse of the same key with materially different request data.

## 6. Business Rules

The authoritative business rules are maintained in [Business-Rules.md](Business-Rules.md). The key constraints are:

- Inventory shall never become negative.
- For each stay date, `Total = Available + Reserved + Confirmed`.
- A multi-date reservation shall reserve all requested stay dates or none.
- Reservation state changes and corresponding inventory changes shall be atomic.
- A reservation may only follow an allowed state transition.
- Payment, cancellation, and expiration races shall have one winning valid transition.

## 7. Non-Functional Requirements

### NFR-001 — Concurrency safety

Concurrent requests for the same inventory shall not cause overselling or negative inventory.

### NFR-002 — Data consistency

PostgreSQL transactions and appropriate concurrency-control techniques shall protect inventory and reservation consistency.

### NFR-003 — Performance measurement

The initial load-test scenario shall target:
- 100 virtual users
- 1 minute of execution
- Throughput / request rate
- P95 and P99 latency
- Error rate
- Final inventory and reservation consistency

The target is a measurement scenario, not a guaranteed performance SLA. Later experiments may increase load to 500 and 1,000 virtual users.

### NFR-004 — Reliability

A failed reservation operation shall not leave partial inventory updates.

### NFR-005 — Observability

Logs should include, where applicable:
- `ReservationId`
- `RequestId` or trace identifier
- `IdempotencyKey` (avoid logging sensitive values unnecessarily)
- Operation name
- Inventory date and quantity changes
- Failure details

## 8. Acceptance Criteria

### AC-001 — Successful booking

Given sufficient inventory for every requested stay date, a valid reservation request creates a pending reservation and reserves the requested inventory.

### AC-002 — Insufficient inventory

If any requested stay date has insufficient inventory, reservation creation fails and no stay date is partially reserved.

### AC-003 — Concurrent booking

Given inventory of 10 rooms for a stay date and 100 concurrent requests each requesting one room:
- No more than 10 requests may successfully reserve that date.
- Available inventory shall never be negative.
- The inventory invariant shall hold after all requests complete.

### AC-004 — Multi-date atomicity

If one date in a multi-date request is unavailable, the reservation fails and inventory for all dates remains unchanged by that request.

### AC-005 — Idempotency

Repeated equivalent requests with the same idempotency key shall not create duplicate reservations or reserve inventory twice.

### AC-006 — Expiration

An expired pending reservation shall release its reserved inventory exactly once, including when expiration processing is retried.

### AC-007 — Payment and expiration race

If payment and expiration execute concurrently, only one valid state transition shall win. Inventory shall reflect that winning transition exactly once.

### AC-008 — Cancellation and payment race

If cancellation and payment execute concurrently, only a valid transition shall commit and inventory shall remain consistent.

## 9. Initial Technical Stack

| Concern | Technology |
|---|---|
| API | ASP.NET Core |
| Language | C# |
| Runtime / SDK | .NET 9 |
| Architecture | Layered Architecture |
| ORM | Entity Framework Core |
| Database | PostgreSQL |
| Cache / distributed coordination | Redis, later phase |
| Message broker | RabbitMQ, later phase |
| Unit / integration tests | xUnit |
| Load testing | k6 |
| Local infrastructure | Docker Desktop |

Exact package versions shall be selected and recorded during environment setup and project creation.

## 10. Milestones

1. Requirements and architecture baseline
2. Environment verification and setup
3. ERD, schema, and database migrations
4. Availability and reservation APIs
5. Concurrency control and idempotency
6. Expiration processing
7. Redis and RabbitMQ experiments
8. Load testing, profiling, and performance report

## 11. Open Design Items

The following details are intentionally deferred to database and detailed design:
- Reservation payment timeout duration
- Whether inventory is represented by one row per room type and stay date
- Exact concurrency-control implementation and isolation level
- API authentication strategy
- Retry policy and background-job scheduling interval
- Whether reservation history is stored as an audit trail
