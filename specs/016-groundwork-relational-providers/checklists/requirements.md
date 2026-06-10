# Requirements Checklist: Groundwork SQL Server And PostgreSQL Providers

**Feature**: [spec.md](../spec.md)

**Created**: 2026-06-10

## Spec Quality

- [x] No implementation placeholders remain.
- [x] Requirements are testable.
- [x] Success criteria are measurable.
- [x] Both provider targets are explicit.
- [x] Container-backed validation is explicit.

## Boundary Quality

- [x] Provider packages remain generic Groundwork packages.
- [x] No Elsa package dependency is required or allowed.
- [x] SQL Server and PostgreSQL differences are provider-local.
- [x] Shared relational behavior belongs in `Groundwork.Relational`.
