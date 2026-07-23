# Contract — Groundwork admission skip-if-current

Durable invariants for the spec-133 skip-if-current fast path. Any change to the stamp mechanism must keep all of
these.

## C-1 — The stamp is an optimization, never a correctness gate

A boot may skip the inspection/validation walk **only** when a persisted stamp `Covers` the current composed
plan. Absence of a stamp, an unreadable stamp, a stamp table that does not exist, or any fingerprint mismatch
**must** fall through to the full `InspectRuntimeAdmissionAsync` walk. No code path may treat "stamp present" as
authorization to apply schema or to bypass admission failure.

## C-2 — Crash safety: stamp is written strictly after a durable, ready admission

The stamp is written only after the Groundwork apply has committed durably and admission reported ready. A crash
at any point before the stamp write leaves no stamp for that target, so the next boot re-walks and re-admits
idempotently. Pinned by
`GroundworkAdmissionSkipStampTests.Crash_between_apply_and_stamp_write_leaves_no_stamp_and_the_next_boot_re_walks`.

## C-3 — Fingerprint covers the full plan input surface

The stamp equivalence (`GroundworkAdmissionSkipStamp.Covers`) must require equality of every input the diff-plan
walk compares: the physical-target fingerprint (manifest contents, routes, projected columns, index sets,
provider identity), the wider composition fingerprint, the provider version, and the stamp format version. A
change to any of these must move a fingerprint so a stale skip from a plan change is impossible.

## C-4 — Scope is one stamp per physical target

One stamp row per `(manifest identity, provider name)`, matching Groundwork's applied-state granularity and the
initializer's one-admission-per-target model.

## C-5 — Separate from SchemaVersion and from Groundwork's own state

The stamp lives in the Elsa-owned `elsa_groundwork_admission_stamp` table. It must never read or write Groundwork's
`groundwork_*` tables as the skip lever, and must never read, write, or interpret the frozen legacy
`SchemaVersion` stamp.

## C-6 — Opt-in default

Because the fingerprint covers the plan but not live provider state, the skip cannot detect out-of-band drift
introduced while the host was down. The switch (`SkipSchemaInspectionWhenPlanUnchanged`) therefore defaults off;
enabling it is an explicit operator choice to trade per-boot drift re-validation for the fast path.

## C-7 — Locked apply protocol unchanged

When the walk runs (no covering stamp), admission behaves exactly as before, including safe-only auto-apply
authorization that denies destructive and semantic-migration operations. Skip-if-current never batches, reorders,
or authorizes operations.
