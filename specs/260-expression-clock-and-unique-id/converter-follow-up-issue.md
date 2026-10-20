<!-- Draft body for the needs-triage follow-up issue (ADR 0088 D5). File only after ADR 0088 is accepted; then delete this file. -->

# Elsa 3 converter: lower newGuid() and new Date() to Clock and UniqueId parameters (ADR 0088 D5)

## Problem

Elsa 4 now has `Clock` and `UniqueId` expression parameters and a `binding-pure-v2` JavaScript profile with a `Date`
that cannot read the clock (spec 260, #3026). The Elsa 3 converter still refuses `newGuid()`, `newGuidString()`,
`newShortGuid()`, `new Date()` and `Date.now()` with `VF-IMP-005` (spec 247). In the JTest corpus, time or randomness is
the only JavaScript refusal left in 19 of the 33 definitions that use JavaScript.

## Rules settled in ADR 0088 D5

- Each `newGuid()` and `newGuidString()` call gets its own `UniqueId` parameter.
- All no-argument `new Date()` and `Date.now()` calls in one expression share one `Clock` parameter, rewritten to
  `new Date(args.now)` and `Date.parse(args.now)`.
- Converted expressions are stamped `binding-pure-v2`.
- A call inside a loop or a nested function is refused: one parameter is one value per evaluation. This covers the six
  `wait(ms)` busy loops, which keep the Delay activity guidance.
- `newShortGuid()` is refused, with guidance to use `newGuidString()`.
- `Extensions/Elsa3/src/Mapping/README.md` records the three differences: a retry sees the first attempt's value; IDs
  are UUID version 7, not version 4; a GUID arrives as a string, not a CLR `Guid`.

## Acceptance

- Each rule has a converter test, plus one test for each refusal.
- The corpus is re-measured: the number of definitions whose JavaScript fully converts, against 7/33 after spec 247.
- The refusal guidance table in the mapping README is updated.

## Context

ADR 0088, spec 260, spec 247 (`specs/247-elsa3-js-host-functions`, research R-6 and R-9), spec 235 (#2707).
