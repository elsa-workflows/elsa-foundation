# Tasks: The ETL runtime store's schema on all four EF engines

**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

## Model

- [x] T001 Check constraints as per-engine templates; SQLite text byte-identical; drop `HasColumnType("INTEGER")`.
- [x] T002 Provider contexts with collation, utf8mb4, 64-character `RunKey` and short AK names on the server engines; `[EfModule]` unchanged until spec 1206.
- [x] T003 `RunKeyOf` seam; stores and codec derive and check run keys through it.
- [x] T004 Exact totals and counts: not needed; the existing aggregates pass on all three server engines.
- [x] T005 SQLite snapshot unchanged (pending-model-changes check).

## Tooling and migrations

- [x] T006 Runtime factories; the ETL migration script takes the store; generate internal runtime `Initial` for three engines.

## Tests

- [x] T007 `IEtlRuntimeTestDatabase`; non-racing runtime-store tests become contracts; SQLite subclasses keep SQLite-only cases.
- [x] T008 Server subclasses and provider checks (migrate/validate/model, collation, maximum-length and case-only run ids, check-constraint refusals) in `etl-ef`.

## Evidence and docs

- [x] T009 Bite-proofs for the seam, constraint rendering and collation; list what is not bite-proven (existing aggregate queries unchanged).
- [x] T010 Persistence README, ETL README, evidence README.
