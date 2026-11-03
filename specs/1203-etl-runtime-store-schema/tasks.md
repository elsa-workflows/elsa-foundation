# Tasks: The ETL runtime store's schema on all four EF engines

**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

## Model

- [ ] T001 Check constraints as per-engine templates; SQLite text byte-identical; drop `HasColumnType("INTEGER")`.
- [ ] T002 Provider contexts with collation, utf8mb4, 64-character `RunKey` and short AK names on the server engines; `[EfModule]` unchanged until spec 1204.
- [ ] T003 `RunKeyOf` seam; stores and codec derive and check run keys through it.
- [ ] T004 Exact in-memory totals and `LongCount` counts.
- [ ] T005 SQLite snapshot unchanged (pending-model-changes check).

## Tooling and migrations

- [ ] T006 Runtime factories; the ETL migration script takes the store; generate internal runtime `Initial` for three engines.

## Tests

- [ ] T007 `IEtlRuntimeTestDatabase`; non-racing runtime-store tests become contracts; SQLite subclasses keep SQLite-only cases.
- [ ] T008 Server subclasses and provider checks (migrate/validate/model, collation, maximum-length and case-only run ids, check-constraint refusals) in `etl-ef`.

## Evidence and docs

- [ ] T009 Bite-proofs for the seam, constraint rendering, collation and totals; list what is not bite-proven.
- [ ] T010 Persistence README, ETL README, evidence README.
