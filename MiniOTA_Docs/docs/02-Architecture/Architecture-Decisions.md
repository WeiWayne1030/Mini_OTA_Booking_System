# Architecture Decision Records — Mini OTA Booking System

This file tracks important architectural decisions and why they were made. Add a new ADR when a decision materially affects system structure, data consistency, operations, or future change cost.

## ADR-001 — Use PostgreSQL as the source of truth

- **Status:** Accepted
- **Date:** 2026-11-06

### Context

The system must manage daily room inventory and reservations under concurrent writes. Inventory changes require atomic multi-row operations and durable transactional consistency.

### Decision

Use PostgreSQL as the primary database and source of truth for reservation state and inventory.

### Alternatives considered

- Redis as the primary inventory store
- An in-memory store
- A document database

### Consequences

Positive:
- ACID transactions support atomic reservation workflows.
- Constraints and conditional updates can enforce important invariants.
- Row-level locking and isolation controls provide useful concurrency experiments.

Trade-offs:
- Database capacity and contention will become measurable bottlenecks.
- The team must understand transaction scope, isolation, indexes, and lock behavior.

## ADR-002 — Use daily inventory

- **Status:** Accepted
- **Date:** 2026-11-06

### Context

A booking consumes inventory for each occupied night. Different dates may have different availability.

### Decision

Represent inventory independently for each stay date and room type. The first release contains one hotel and one room type, but the model should not make multi-date behavior difficult to test.

### Alternatives considered

- Assign a physical room number at reservation time
- Maintain only one aggregate inventory number for the entire date range

### Consequences

Positive:
- Availability can be queried per date.
- Multi-date atomicity and contention can be tested explicitly.
- Inventory changes are easy to inspect during experiments.

Trade-offs:
- A reservation spanning multiple nights touches multiple inventory rows.
- Lock ordering and transaction duration must be considered to reduce deadlocks.

## ADR-003 — Use Layered Architecture

- **Status:** Accepted
- **Date:** 2026-11-06

### Context

The project is intended as a learning system that will grow through multiple technical phases. HTTP concerns, use-case orchestration, domain rules, and infrastructure concerns should remain distinguishable.

### Decision

Use four projects:
- `MiniOTA.Api`
- `MiniOTA.Application`
- `MiniOTA.Domain`
- `MiniOTA.Infrastructure`

### Alternatives considered

- A single-project CRUD application
- Clean Architecture with additional abstractions and project boundaries
- Microservices

### Consequences

Positive:
- Clear responsibility boundaries
- Easier unit and integration testing
- Infrastructure can evolve without placing database details in domain logic

Trade-offs:
- More initial project setup
- Requires discipline around dependency direction and avoiding unnecessary abstractions

## ADR-004 — Use .NET 9 and Visual Studio

- **Status:** Accepted (revised: originally targeted .NET 10 LTS)
- **Date:** 2026-11-06

### Context

The project is new and should use a supported .NET release. The developer uses Windows 11 and prefers Visual Studio for C# development. The local environment check found .NET SDK 6, 8, and 9 installed with Visual Studio 2022 Professional (17.14); .NET 10 was not installed.

### Decision

Target .NET 9 (SDK 9.0.x) and use Visual Studio 2022. All projects use `net9.0`.

### Alternatives considered

- .NET 10 LTS (not installed locally; would require additional setup)
- Use VS Code as the primary IDE

### Consequences

Positive:
- Works with the tooling already installed; no extra setup.
- Integrated debugging, test discovery, and project management in Visual Studio.

Trade-offs:
- .NET 9 is a standard-term-support release; plan to migrate to a later LTS if the project outlives .NET 9 support.

## ADR-005 — Add Redis and RabbitMQ only in later phases

- **Status:** Accepted
- **Date:** 2026-11-06

### Context

The primary learning objective is to understand database concurrency and consistency. Introducing distributed infrastructure too early could hide the source of correctness problems.

### Decision

Start with ASP.NET Core and PostgreSQL. Introduce Redis and RabbitMQ only after the baseline reservation workflow and its database concurrency behavior are understood.

### Alternatives considered

- Introduce Redis and RabbitMQ in the first commit
- Start with a microservices architecture

### Consequences

Positive:
- Easier debugging and lower local setup complexity
- Clear baseline for measuring the effect of each additional technology
- Better understanding of what each component actually solves

Trade-offs:
- The initial version will not demonstrate distributed messaging or caching.
- Later phases will require architecture and operational changes.

## ADR template

Copy this template for future decisions:

```markdown
## ADR-NNN — Decision title

- **Status:** Proposed / Accepted / Superseded / Rejected
- **Date:** YYYY-MM-DD

### Context
What problem or constraint requires a decision?

### Decision
What are we choosing?

### Alternatives considered
What other options were evaluated?

### Consequences
What benefits, costs, risks, and follow-up work result?
```
