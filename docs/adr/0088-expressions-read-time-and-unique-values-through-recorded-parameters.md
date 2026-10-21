# Expressions Read Time And Unique Values Through Recorded Parameters

Status: accepted (2026-10-21, approved by the owner; proposed 2026-10-20). Settles [#3026](https://github.com/elsa-workflows/elsa-foundation/issues/3026) through
an owner grilling session; specified by [spec 260](../../specs/260-expression-clock-and-unique-id/spec.md).

Related: [ADR 0045 §3 and §5](0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md)
(pinned inputs, pure expressions, the #977 and #2717 amendments), [ADR 0083](0083-javascript-binding-grammar-is-pinned-at-publish.md)
(binding grammar and the `binding-pure-v1` profile), [ADR 0032](0032-runtime-checkpoint-cadence-is-policy-driven-per-workflow.md)
(coalesced commits), gap row 1 of [#1152](https://github.com/elsa-workflows/elsa-foundation/issues/1152), and
[spec 247](../../specs/247-elsa3-js-host-functions/spec.md) research R-4 to R-6 and R-9.

## Context

Elsa 4 expressions have no clock and no randomness. `IsolatedJintEngine.DisableAmbientCapabilities` removes `Date`,
`Temporal`, `Intl` and `Math.random`, and Liquid's time filters are unavailable, so that an expression returns the same
result whenever the runtime evaluates it again for the same step: on a retry, on the replay of a coalesced segment and
when inputs are re-materialized. ADR 0045 §5 lets time and randomness in only "as explicit pinned parameters or through a
separately specified deterministic runtime capability". Neither exists yet.

Elsa 3 workflows use both. In the JTest corpus of 33 definitions with JavaScript (measured 2026-10-19, after spec 247):

- `newGuid()`, `newGuidString()` and `newShortGuid()` appear 22 times in 18 definitions. Every call is the only one in
  its expression, and none is inside a loop or a nested function.
- `Date` appears 24 times in 10 definitions. Six are a `wait(ms)` busy loop. The rest read "now", parse an ISO string,
  subtract dates or call `getTime()` and `toISOString()`. None reads local time or a locale.
- For 19 of the 33 definitions, time or randomness is the only JavaScript refusal left.

The `WorkflowInstance` binding (#2717) is replay-stable because it reads committed state. "Now" and a new ID have no
committed source, so they must be recorded. Elsa 3 also gave a new value on every evaluation, including every `While`
pass, and a migrated deadline loop such as `while (new Date() < deadline)` relies on that.

Two runtime facts, verified on `947cb78e1`, shape the decision:

- An activity's `ActivityInputSnapshot` is committed before the activity runs and already carries `MaterializedAt`.
- A `While` pass re-materializes its inputs into a transient snapshot (`StructuralParentEvaluationSupport.ConstructActivityAsync`).
  The pass's decision commits together with `ParentCompletionProcessedWorkItemId` on the completed child, and a
  redelivered child-completion work item returns before re-materializing
  (`WorkflowParentActivityCompletionSchedulerWorkHandler.cs:129-136`). A committed evaluation therefore never runs again.

## Decision

### D1. Two parameter binding kinds: `Clock` and `UniqueId`

An expression reads the current time or a new unique value only through a parameter it declares:

```json
{ "now":     { "kind": "Clock" },
  "orderId": { "kind": "UniqueId" } }
```

These are the sixth and seventh canonical parameter kinds, beside literal, workflow request, variable, activity result
and `WorkflowInstance`. Like `WorkflowInstance` they are not ambient accessors, they carry no dependency policy and they
are not sensitive. They are parameter kinds, not sandbox surface, so they are valid under every capability profile and
in every expression language that takes parameters, including Liquid.

- **`Clock`** binds the snapshot's `MaterializedAt` as an ISO 8601 UTC string with millisecond precision
  (`yyyy-MM-ddTHH:mm:ss.fffZ`), the format of JavaScript's `toISOString()`, so `new Date(args.now).toISOString() === args.now`.
  Every `Clock` parameter in one materialization binds the same instant.
- **`UniqueId`** binds a new UUID version 7 as a lowercase hyphenated string. Each `UniqueId` parameter in a
  materialization gets its own value. Version 7 is time-ordered and unique, which suits correlation and tracking IDs; it
  is not a secret and must not be used as an unguessable token.

There is no `member` or `format` on either kind. A second ID format, for example Elsa 3's short GUID, is added only when
there is a need for it.

An activity that produces the time or an ID was considered and is not part of this decision. It cannot run once per
`While` pass inside a condition, and the converter could not lower to it in place. It can still be added later as an
authoring convenience.

### D2. The value is recorded with the input snapshot, and fixed once something depending on it commits

A `Clock` or `UniqueId` value is resolved when the inputs that contain it are materialized, and it lives in that
snapshot. No separate store records it. The rule is:

> A value may change until something that depends on it commits. From then on, every reader sees the committed value.

Applied to each case:

| Case | Behavior |
|---|---|
| Retry of an activity | Reads the committed `InputSnapshot`, so it sees the first attempt's values. |
| Replay of a coalesced segment that never flushed | Materializes again and may get new values. Nothing from the lost segment was observable, because side-effecting activities still flush before they run (ADR 0032 R2). |
| `While` pass | Each pass re-materializes and gets a new time and new IDs, as in Elsa 3. A pass's values are fixed when its decision commits. The commit marker prevents a committed pass from running again. |
| Re-materialization of an already committed snapshot | Does not happen. A committed snapshot is read, not rebuilt. |

`Clock` binds the `materializedAt` passed to materialization, never a second clock reading. The inspection records of a
`While` pass take their timestamp from a separate `GetUtcNow()` call today, and that timestamp must not be used for
`Clock`.

The rule applies wherever a portable expression is evaluated, not only to activity inputs. The spec lists every
evaluation site and the commit that fixes each one's values, and a site with no such commit cannot bind these kinds.

### D3. One value per declared parameter

An expression that needs two IDs declares two parameters. A pinned seed with a deterministic generator in the sandbox
and a count declared at publish were rejected. The first adds a stateful host function, which is an ambient accessor
under another name. The second covers no case the corpus has that separate parameters do not. A real need for many IDs
per evaluation is met by a loop activity, or later by its own capability under a new profile version.

### D4. `binding-pure-v2`: a `Date` without a clock, for JavaScript

A new JavaScript capability profile, `binding-pure-v2`, is `binding-pure-v1` plus a `Date` that cannot read the clock.

| | `Date` under `binding-pure-v2` |
|---|---|
| **Allowed** | `new Date(isoString)`, `new Date(epochMilliseconds)`, `new Date(year, month, ...)`, `Date.UTC`, `Date.parse`, the getters and setters, `getTime`, `valueOf`, `toISOString`, `toJSON`, `toString`, and comparison and arithmetic on dates |
| **Removed** | `new Date()` with no argument, `Date()` called as a function, `Date.now()`, `toLocaleString`, `toLocaleDateString`, `toLocaleTimeString` |
| **Constrained** | The engine time zone is UTC, so local getters equal UTC getters and `toString` is the same on every host. Strings parse only in the ECMAScript date-time string format (ISO 8601); any other string gives `Invalid Date`. |

`Temporal`, `Intl` and `Math.random` stay removed.

**Diagnostics are part of the contract.** A removed member is not left `undefined`: it throws a `TypeError` that names
the member, the profile and the change to make. Examples:

- `new Date()`: "`new Date()` with no argument reads the clock, which binding-pure-v2 does not allow. Declare a `Clock`
  parameter and write `new Date(args.now)`."
- `Date.now()`: "`Date.now()` reads the clock, which binding-pure-v2 does not allow. Declare a `Clock` parameter and
  write `Date.parse(args.now)`."
- `toLocaleString()`: "`toLocaleString()` depends on a locale, which binding-pure-v2 does not provide. Use
  `toISOString()`, or format the parts with the UTC getters."
- A non-ISO string: the result is `Invalid Date`, and a later `toISOString()` reports that only ISO 8601 date-time
  strings parse.

Authoring validation reports the same messages for the removed forms it can find without evaluating the source. The
documentation carries the full allowed, removed and constrained table with a rewrite for each removed form.

**Versioning.** Newly published JavaScript expressions are stamped `binding-pure-v2`. An executable stamped
`binding-pure-v1` keeps evaluating with `Date` undefined, and the runtime selects the sandbox surface from the stamped
profile, as ADR 0083 requires of anything pinned at publish. The editor's typings follow the profile. Amending v1 in
place was rejected: no successful v1 result would change, but pinned executables that used to fault would start to
succeed under an unchanged name.

Liquid keeps `binding-pure-v1`. Matching Liquid date filters is not part of this decision.

### D5. The Elsa 3 converter lowers, with documented differences

The converter work is a separate follow-up issue. Its rules are settled here:

- Each `newGuid()` and `newGuidString()` call gets its own `UniqueId` parameter.
- All no-argument `new Date()` and `Date.now()` calls in one expression share one `Clock` parameter, written as
  `new Date(args.now)` and `Date.parse(args.now)`.
- Converted expressions are stamped `binding-pure-v2`.
- A call inside a loop or a nested function is refused, because one parameter is one value per evaluation (D3). This
  covers the `wait(ms)` busy loops, which keep the Delay activity guidance.
- `newShortGuid()` is refused, with guidance to use `newGuidString()`.

The lowering counts as faithful. "A new value each time this step is evaluated" is kept. The mapping README records three
differences: a retry sees the first attempt's value, which is an Elsa 4 guarantee rather than a regression; IDs are UUID
version 7 instead of version 4; and a GUID arrives as a string, not a CLR `Guid`.

## Consequences

- The 19 definitions blocked only by time or randomness can convert, apart from the `wait(ms)` loops and the one
  `newShortGuid()`.
- ADR 0045 §5's "separately specified deterministic runtime capability" now exists. Expressions stay pure in the sense
  that matters: the same snapshot gives the same result.
- The runtime supports two JavaScript profiles. The difference is one switch in the engine setup plus the editor
  typings for each profile.
- A runtime build that predates the new kinds or `binding-pure-v2` cannot read an executable that uses them. Before GA
  this is accepted, not mitigated, as for #2717.
- A `UniqueId` value reveals its creation time to the millisecond.
- Plain-source JavaScript is stamped `binding-pure-v2` at publish, so republishing an unchanged definition produces a new
  executable fingerprint once. Stored executables keep the profile they were stamped with.
- Today the only way to declare these parameters is a full portable expression definition. A Studio control for them is
  separate work.

## Tests the spec must name

- Retry: a faulted and retried activity sees the same `Clock` and `UniqueId` values as its first attempt.
- Replay: a coalesced segment that never flushed may produce new values, and nothing from it is observable.
- `While`: each pass gets a new time and new IDs; a redelivered child completion after a committed pass does not
  evaluate again.
- Re-materialization: a committed snapshot is read back unchanged.
- Two `UniqueId` parameters in one snapshot differ; two `Clock` parameters are equal and equal `MaterializedAt`.
- `binding-pure-v1` executables still see `Date` as undefined; `binding-pure-v2` allows exactly the listed surface.
- Every removed member throws its documented message; non-ISO strings give `Invalid Date`; local getters equal UTC
  getters.

## Follow-up

- Spec 260 specifies D1 to D4.
- [#3166](https://github.com/elsa-workflows/elsa-foundation/issues/3166) (needs-triage) covers the converter rules in D5 and updates the mapping README and refusal guidance.
