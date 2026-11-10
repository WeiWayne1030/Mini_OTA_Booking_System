# Mini OTA Booking System

A learning project for building a small hotel-booking system and studying high-concurrency application design.

## Project goals

- Build a reservation workflow with daily room inventory.
- Preserve inventory consistency under concurrent requests.
- Learn database transactions, concurrency control, and idempotency.
- Add Redis and RabbitMQ incrementally, only when the baseline system is understood.
- Measure behavior with automated tests and k6 load tests.

## Confirmed technology decisions

| Area | Choice |
|---|---|
| Language / Backend | C# / ASP.NET Core |
| Framework | .NET 9 |
| IDE | Visual Studio |
| Architecture | Layered Architecture |
| Database | PostgreSQL |
| ORM | Entity Framework Core |
| Cache / distributed coordination | Redis (later phase) |
| Message broker | RabbitMQ (later phase) |
| Load testing | k6 |
| Automated testing | xUnit |

## Scope of the first release

- One hotel
- One room type
- Daily inventory
- Availability query
- Reservation creation
- Simulated payment
- Reservation cancellation
- Expiration of unpaid reservations

Real payment processing, multiple hotels, promotions, loyalty programs, and third-party OTA integrations are out of scope.

## Documentation map

| Path | Purpose |
|---|---|
| `docs/01-Requirements/SRS.md` | System requirements and acceptance criteria |
| `docs/01-Requirements/Business-Rules.md` | Domain invariants and reservation rules |
| `docs/02-Architecture/System-Architecture.md` | Components, responsibilities, and request flows |
| `docs/02-Architecture/Architecture-Decisions.md` | Architecture Decision Records (ADRs) |

## Planned milestones

1. Requirements and architecture documentation
2. Local development environment setup
3. Database design and migrations
4. Availability and reservation APIs
5. Concurrency control and idempotency
6. Expiration processing and distributed messaging
7. Load testing and performance analysis

## Current status

**Requirements baseline:** SRS v0.2  
**Current activity:** Verify the local Windows development environment before installing missing tools.

## Development principles

- Start with the simplest correct implementation.
- Keep inventory and reservation state changes atomic.
- Add infrastructure only when a concrete requirement justifies it.
- Record important design decisions and their trade-offs.
- Keep documentation, implementation, and tests aligned.
