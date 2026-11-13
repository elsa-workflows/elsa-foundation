# Feature Specification: Expression capabilities (Elsa 4)

**Feature Branch**: `claude/3052-s1-expression-capabilities`

**Created**: 2026-11-10

**Status**: Draft — **provisional**, initiative #3052 spec **S-1**. Written as if its dependent ADRs are agreed; see **Dependencies & agreed decisions** and **Open questions**. Issue #3026.

**Input**: User description: "Expression capabilities (Elsa 4) — what expressions may do for current time, randomness, GUIDs, built-in helpers, and .NET-implemented functions, and how Elsa 3 time/GUID/string functions convert faithfully."

---

## Provisional status *(read first)*

This spec is **provisional**. It encodes the position reached on the #3052 decision board and is written as though the decisions and ADRs it rests on are final. Where a dependency is not yet settled, the spec records it under **Open questions** and proceeds on the stated position; if an approver settles it the other way, the affected requirements change. Findings that contradict a dependency ADR are flowed back as comments on that ADR's PR, not resolved silently here.

The one dependency still not agreed by every approver is **D-6** (function effect declaration). Its direction is agreed by two approvers and FR-012 follows it; see **Open questions**.

---

## User Scenarios & Testing *(mandatory)*

Actors: **workflow authors** (write JavaScript expressions in input bindings and in `RunJavaScript` scripts) and **teams migrating Elsa 3 workflows** to Elsa 4.

### User Story 1 - Current time, randomness, and GUIDs in expressions (Priority: P1)

A workflow author writes an expression that needs the current time, a random value, or a fresh GUID — for a timestamp, a correlation value, a jitter, a generated identifier. The expression produces the value at evaluation time; no special configuration, activity shape, or "pinned" declaration is required.

**Why this priority**: This is the core capability the spec exists to grant. It reverses the earlier rule that expressions had to be free of time/randomness, which blocked a large share of real workflows and the Elsa 3 migration.

**Independent Test**: Author an input binding and a script that each call for the current time, a random number, and a new GUID; evaluate; confirm each yields a well-formed value with no configuration and no refusal.

**Acceptance Scenarios**:

1. **Given** an input-binding expression that reads the current time, **When** it is evaluated, **Then** it returns the host clock's time, expressed in UTC with invariant formatting.
2. **Given** a script that generates a new GUID, **When** it runs, **Then** it returns a GUID as a JSON string.
3. **Given** a script that calls for a random number, **When** it runs twice, **Then** it may return different values, and this is not treated as an error.

### User Story 2 - Elsa 3 time / GUID / string functions convert faithfully (Priority: P1)

A team migrates an Elsa 3 workflow whose expressions use the Elsa 3 time, GUID, and Base64 string functions (and native `Date`/`Math`). The conversion reproduces the same behaviour in Elsa 4 rather than refusing it.

**Why this priority**: The whole initiative is a faithful Elsa 3 → Elsa 4 conversion. These functions are among the most frequently used in the corpus (e.g. `newGuid` 22 uses / 18 definitions; `Date` 24 uses / 10 definitions), so refusing them would block migration broadly.

**Independent Test**: Convert corpus workflows that use only the faithful functions; confirm each converts without refusal and the converted expression produces equivalent output.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 expression using `Date.now()`, `new Date().toISOString()`, `new Date().getTime()`, or `Math.random()`, **When** it is converted, **Then** it converts without refusal and uses the standard JavaScript facility, whether or not a source time zone is set.
4. **Given** an Elsa 3 expression using zone-dependent `Date` usage (for example `getHours()`, `toLocaleString()`, or `new Date('2026-10-28T09:15:00')`) and a conversion with **no** source time zone, **When** it is converted, **Then** it is refused with a diagnostic that quotes the expression and says to set the source time zone or rewrite with UTC methods or an explicit offset.
5. **Given** the same expression and a conversion with source time zone `Europe/Amsterdam`, **When** it is converted and evaluated, **Then** it gives the result Elsa 3 gave on a server in that zone, daylight saving included.
2. **Given** an Elsa 3 expression using `newGuid()`, `newGuidString()`, `newShortGuid()`, `getGuidString()`, or `getShortGuid()`, **When** it is converted, **Then** it converts without refusal and returns the same string shape Elsa 3 produced — `newShortGuid` reproduces Elsa 3's short-GUID encoding exactly.
3. **Given** an Elsa 3 expression using `stringToBase64()` / `stringFromBase64()`, **When** it is converted, **Then** it converts without refusal and produces the same UTF-8 / standard-Base64 result.

### User Story 3 - A relied-on live .NET object fails clearly, not cryptically (Priority: P1)

A migrating team has an Elsa 3 script that called a .NET member on a value the engine used to hand over as a live .NET object (a "proxy") — for example a .NET method on a GUID or collection, or a reference to a .NET type. While live-.NET-object proxies are disabled, values crossing the expression boundary are JSON, so that usage cannot work. The team is told exactly where and why at conversion or publish time, instead of the workflow failing with an obscure error at run time.

**Why this priority**: Protecting migrations from silent breakage is a crucial part of the import. A clear, located refusal is the difference between a confident migration and a workflow that breaks unpredictably in production.

**Independent Test**: Convert / publish a script that invokes a .NET-style member on a boundary value or references a .NET type; confirm a refusal is produced that quotes the offending expression and explains the JSON-only contract, and that no such case passes to run time.

**Acceptance Scenarios**:

1. **Given** a script that invokes a .NET-style method on a boundary value (e.g. `someValue.ToString("N")`, `.ToArray()`, `.Where(...)`), **When** it is converted or published, **Then** it is refused with a diagnostic that quotes the offending expression and explains that values are JSON and live .NET objects are disabled.
2. **Given** a script that references a .NET type (e.g. `DateTime.Now`, `new Guid(...)`, `Convert.ToBase64String(...)`), **When** it is converted or published, **Then** it is refused with the same clear guidance.
3. **Given** a script that reads a property whose name happens to be PascalCase (a JSON key, e.g. `payload.OrderId`), **When** it is converted or published, **Then** it is **not** refused.
4. **Given** live-.NET-object proxies are enabled by a setting, **When** the same proxy-using script is converted, **Then** the proxy-dependent-usage refusal does not apply.
5. **Given** a script that defines its own member and invokes it with a PascalCase name (e.g. `const helpers = { Format: x => ... }; helpers.Format(v)`), **When** it is converted or published, **Then** it is **not** refused.

### User Story 4 - Built-in helpers and .NET functions, consistent across surfaces (Priority: P2)

An author uses the built-in helpers and the .NET-implemented functions (GUID, Base64, `now`, `toJson`) and expects them to behave the same whether written in an input binding or in a script, and to exchange plain JSON values.

**Why this priority**: Consistency between the two expression surfaces removes a class of surprises; the JSON-only contract is what keeps results portable and persistable.

**Independent Test**: Call each built-in / .NET function from both an input binding and a script; confirm identical availability and that every argument and result is a JSON value.

**Acceptance Scenarios**:

1. **Given** a .NET-implemented function (e.g. `toJson`, `stringToBase64`, a GUID function, `now`), **When** it is called from an input binding and from a script, **Then** it is available in both and behaves identically.
2. **Given** any .NET-implemented function, **When** it is called, **Then** every argument and its result is a JSON value (no live .NET object is passed in or returned).

### Edge Cases

- **Random reproducibility**: random values are genuinely non-deterministic; the platform does not guarantee reproducible randomness, and tests must not depend on a fixed sequence. (Known accepted limitation.)
- **Refused functions used in a workflow**: a workflow using `parseGuid`, byte/stream helpers, or host configuration (`getConfig`/`getConfiguration`) is refused at conversion with guidance (configuration access is deferred to a later spec).
- **Dynamic / computed member access** (e.g. `value[name]()`): cannot be classified statically; it is allowed through (a recorded detector gap) and may fail at run time as it could in Elsa 3.
- **A boundary value that was a .NET collection**: a property read such as `.Count` is allowed (treated as a JSON key and resolves to `undefined` if absent); only a .NET-style **invocation** or a .NET **type reference** is refused.
- **Clock in tests**: because time is drawn from the host clock, tests control time through the host's time source rather than by pinning expression output.
- **Date string parsed from a non-literal** (e.g. `new Date(input)`): the converter cannot see whether the text carries an offset. Without a source time zone it converts with a behaviour warning that quotes the expression (FR-024); with one it converts without a warning.
- **A script-defined member that shadows a .NET-style name**: if a script defines a member `Format` and also invokes `Format` on a boundary value, the exemption hides the second call (a recorded detector gap, like computed access).

## Requirements *(mandatory)*

### Functional Requirements

**Capabilities granted to expressions**

- **FR-001**: Expressions MUST be permitted to produce non-deterministic values — current time, random values, and newly generated GUIDs are allowed and MUST NOT be treated as errors. *(D-1)*
- **FR-002**: The current time available to expressions MUST come from the host clock, and MUST be expressed in UTC with invariant (culture-independent) formatting. *(ADR 0046 / ADR 0045 §5)*
- **FR-003**: The same expression capabilities MUST be available identically in input-binding expressions and in `RunJavaScript` scripts, under a single read-only capability profile. *(ADR 0083 — `binding-readonly-v1`)*
- **FR-004**: Standard JavaScript date/time, internationalization, and math facilities MUST be available to expressions (the capability previously removed under the determinism rule is restored). *(reverses the earlier ambient-capability strip)*
- **FR-005**: Randomness MUST be available as the standard JavaScript capability. The platform MUST NOT guarantee a controllable or reproducible random sequence; this is a documented limitation. *(D-1)*

**Built-in and .NET-implemented functions**

- **FR-006**: GUID functions `newGuid`, `newGuidString`, `newShortGuid`, `getGuidString`, and `getShortGuid` MUST be available and MUST return GUID strings. `newShortGuid` MUST reproduce Elsa 3's short-GUID encoding exactly. GUID values MUST be generated with cryptographic-grade uniqueness.
- **FR-007**: Base64 string functions `stringToBase64` and `stringFromBase64` MUST be available, operating on UTF-8 text with standard Base64 encoding, and MUST return strings.
- **FR-008**: A current-time helper (`now`) MUST be available and MUST return an ISO-8601 UTC string.
- **FR-009**: A JSON-serialization helper (`toJson`) MUST be available and MUST produce canonical JSON via the shared read-only encoder. *(ADR 0091 — A-5)*
- **FR-010**: The existing null/whitespace helpers (`isNullOrEmpty`, `isNullOrWhiteSpace`) MUST remain available as already specified. *(spec 247)*
- **FR-011**: Under `binding-readonly-v1`, while live-.NET-object proxies are disabled, every value crossing the expression boundary — function arguments, function results, inputs, and outputs — MUST be a JSON value. No .NET-implemented function may accept or return a live .NET object. *(D-5, A-1)*
- **FR-012**: A .NET-implemented function registered without a declaration MUST be treated as effect-free: no commits are placed around an input evaluation because it calls one. The function registration API MUST let a host declare a function **effectful** when registering it (one optional flag for the host developer, never visible to workflow authors); only for such a function are commits placed before and after an input evaluation that calls it. The declaration MUST NOT refuse, skip or deduplicate a call; idempotency stays with the designer. The built-in functions of this spec (FR-006 to FR-010) are registered without the declaration. No other metadata or effect profile is attached to functions. Placing the commits belongs to the execution-contract details of ADR 0045 §3, ADR 0032 and ADR 0020; this spec provides and records the declaration. *(D-6 — see OQ-1)*

**Functions that remain unavailable**

- **FR-013**: `parseGuid` (returns a live .NET GUID), byte and stream helpers (binary data, not JSON), and host-configuration access (`getConfig` / `getConfiguration`) MUST NOT be available in this spec. Configuration access is deferred to a later spec. Migration cost of deferring, in the 36-definition JTest corpus: `getConfig` 0 uses; `getConfiguration` 1 use in 1 definition (`javascript-script-handler-functions`; Elsa 3 itself has only `getConfig`, so this call was already unknown there); `parseGuid` 1 use in 1 definition (`javascript-elsa-functions`).

**Elsa 3 conversion**

- **FR-014**: Converting an Elsa 3 workflow whose expressions use only the faithful functions MUST succeed without refusal — specifically zone-independent `Date` usage (FR-021), zone-dependent `Date` usage when a source time zone is set (FR-022), `Math.random()`, the `newGuid` family, `stringToBase64` / `stringFromBase64`, and `toJson`.
- **FR-015**: Converting an Elsa 3 workflow that references a .NET type or constructor (e.g. `Guid`, `DateTime`) or that uses a refused function (FR-013) MUST be refused at conversion with a clear diagnostic and guidance.

**Protecting migrations from disabled proxies**

- **FR-016**: While live-.NET-object proxies are disabled, both conversion and publish MUST detect and refuse script usage that depends on .NET members of boundary values — specifically .NET-style **method invocations** and references to **.NET types** (static access or construction).
- **FR-017**: The refusal in FR-016 MUST quote the offending expression and explain that boundary values are JSON and live .NET objects are disabled. (A precise source line/column is **not** required; see the follow-up noted in Open questions.)
- **FR-018**: The detection in FR-016 MUST allow property **reads** (which may legitimately address JSON keys, including PascalCase keys) and MUST NOT attempt to classify dynamic/computed member access (a recorded gap). It MUST NOT refuse the invocation of a member defined in the same script — a property key of an object literal, a class method or field, or a member assignment (`x.Format = ...`) — such as `helpers.Format(x)` where `helpers` is declared in the script.
- **FR-019**: The detection in FR-016 MUST be active only while proxies are disabled. If a setting enables live-.NET-object proxies, this detection MUST NOT apply. *(A-1)*

**Execution contract context**

- **FR-020**: A recorded step's expression values MUST NOT be re-evaluated on retry or recovery; recovery runs forward from recorded values, and a genuine re-run is a new evaluation that may produce new values. *(D-2 — this is the contract under which non-deterministic expressions are safe; it is not re-specified here.)*

**Time zones in converted Elsa 3 scripts** *(ADR 0046 Dates: Elsa 3 evaluated script dates in the server's time zone)*

- **FR-021**: Zone-independent `Date` usage MUST convert faithfully whether or not a source time zone is set: `Date.now()`; `new Date()` used as an instant; `getTime()` / `valueOf()` and epoch arithmetic; `new Date(number)`; `Date.UTC(...)`; `toISOString()` / `toJSON()` (including a `Date` returned as a value, which leaves as UTC in both versions); the UTC getters and setters (`getUTCHours()` ...); and parsing a string literal that carries `Z` or an explicit offset.
- **FR-022**: Zone-dependent `Date` usage MUST convert only when the conversion has a **source time zone** set — the conversion option of ADR 0046 *Dates*, recorded in the published conversion contract — and MUST be refused otherwise, with a diagnostic that quotes the offending expression and guidance to set the source time zone or to rewrite with UTC methods or an explicit offset. Zone-dependent usage is: the local-time getters and setters (`getHours()`, `getDate()`, `getDay()`, `getMonth()`, `getFullYear()`, `getTimezoneOffset()`, `setHours()` ...); local formatting (`toString()`, `toDateString()`, `toTimeString()`, `toLocaleString()`, `toLocaleDateString()`, `toLocaleTimeString()`); the local-time multi-argument constructor (`new Date(2026, 9, 28)`); and parsing a string literal without an offset (`new Date('2026-10-28T09:15:00')`, `Date.parse('2026-10-28T09:15:00')`).
- **FR-023**: When a source time zone is set, a converted expression MUST evaluate with that IANA time zone as its local time zone, so zone-dependent usage gives the result Elsa 3 gave on a server in that zone, daylight saving included. The clock still supplies the same instant (FR-002). Expressions that were not converted, and converted expressions without a source time zone, evaluate in UTC.
- **FR-024**: A string parse whose argument is not a literal (`new Date(input)`, `Date.parse(input)`) cannot be classified statically. Without a source time zone it MUST convert with a behaviour warning that quotes the expression, following ADR 0046's rule for non-literal date expressions; with a source time zone set it converts without a warning. *(see OQ-3)*

### Key Entities

- **Expression**: author-written JavaScript evaluated in an input binding or a script; consumes and produces JSON values.
- **Capability profile** (`binding-readonly-v1`): the read-only set of capabilities granted to expressions on both surfaces — standard JS intrinsics plus the registered .NET-implemented functions.
- **.NET-implemented (host) function**: a function installed into the engine that exchanges only JSON values (GUID family, Base64 pair, `now`, `toJson`).
- **Boundary value**: any value crossing between the host and an expression; always a JSON value while proxies are disabled.
- **Conversion / publish diagnostic**: a refusal carrying a code, a message that quotes the offending expression, and guidance.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of Elsa 3 corpus workflows whose JavaScript uses only the faithful functions (`Math`, the `newGuid` family, `stringToBase64`/`stringFromBase64`, `toJson`) and zone-independent `Date` usage convert without refusal; with a source time zone set, the same holds for workflows that also use zone-dependent `Date` usage. (The JTest corpus has no literal zone-dependent usage: its `Date` use is `getTime()` busy loops, `toISOString()`, `Date.now()` and three non-literal string parses, FR-024.)
- **SC-007**: Zero converted workflows with zone-dependent `Date` usage convert without a source time zone — every such case is refused with a diagnostic that quotes the expression.
- **SC-002**: Zero workflows that depend on a disabled proxy or a .NET type reach run time undetected — every such case is refused at conversion or publish, with a diagnostic that quotes the offending expression.
- **SC-003**: An author can obtain the current time, a random value, and a new GUID in an expression with no special configuration and no activity-shape change.
- **SC-004**: No expression capability differs between input bindings and scripts — the same functions and intrinsics are available on both surfaces.
- **SC-005**: Every value produced by a built-in / .NET-implemented function is a JSON value that round-trips through persistence with no .NET type leakage.
- **SC-006**: A migrating team reading a proxy-dependent-usage refusal can identify the exact offending expression from the diagnostic alone (no need to inspect run-time stack traces).

## Dependencies & agreed decisions

This spec is written as if the following are final. Each is named so reviewers can confirm the spec rests on the right text.

- **D-1** — expressions need not be deterministic; clock, randomness, and GUIDs are allowed. *(agreed)*
- **D-2** — execution contract: a recorded step never re-runs; retry/recovery reuses recorded values; a re-run is a new evaluation. *(agreed)* — the safety basis for FR-001/FR-020.
- **D-5** — .NET-implemented functions are installed in the engine and exchange JSON values. *(agreed)*
- **ADR 0045 §5** — execution contract and expression capabilities; hands this spec the built-in-helper and conversion jobs. *(PR #3384)*
- **ADR 0083 / `binding-readonly-v1`** — the read-only capability profile; this spec delivers it by re-admitting native JS intrinsics and registering the JSON-exchanging .NET functions. *(PR #3390)*
- **ADR 0046** — date/time semantics (UTC, invariant) and, in *Dates*, the converter's source time zone option, recorded in the published conversion contract; FR-021 to FR-024 apply it to scripts. *(#3422)*
- **ADR 0091 (A-5)** — shared read-only canonical JSON encoder, used by `toJson`. *(provisional)*
- **A-1** — live-.NET-object proxies are off by default. This spec does not implement proxies; while they are disabled it protects migrations (FR-016 to FR-019). It takes no position on when or whether .NET interop is enabled.
- **D-6** — undeclared functions are effect-free; a host may declare a function effectful at registration. *(direction agreed by two approvers; see OQ-1; #3384)*

## Open questions

- **OQ-1 (D-6 — direction agreed by Joey + Sipke; Frans differs — call)**: FR-012 follows the agreed direction: a function without a declaration is effect-free, a host may declare a function effectful at registration (the only function metadata), the built-ins carry no declaration, and the declaration never refuses, skips or deduplicates a call. Frans's position is no metadata at all (every registered function possibly effectful at the durability boundary); it goes to the approvers' call if he holds it. If D-6 settles that way, FR-012's declaration is dropped and every function call in an input evaluation gets the commits. ADR 0045 §5 text: #3384.
- **OQ-3 (for review)**: FR-024 converts a non-literal date-string parse with a behaviour warning when no source time zone is set, as ADR 0046 does for non-literal date expressions. The stricter reading refuses it. In the JTest corpus this decides 3 definitions (`javascript-date-methods`, `performance-minimal-baseline`, `composition-execute-workflow-root`).
- **OQ-2 (follow-up, not blocking)**: expression diagnostics do not currently carry a JavaScript source line/column; FR-017 quotes the offending expression text instead. Threading precise source coordinates would improve all expression diagnostics equally and is proposed as a separate follow-up rather than folded into this spec.

## Assumptions

- The host provides a clock/time source that the expression engine draws current time from; tests control time through that source rather than by pinning expression output.
- Live-.NET-object proxies are disabled by default (A-1). No part of this spec implements or enables them; while they are disabled, FR-011 and FR-016 to FR-019 apply. This spec takes no position on when or whether .NET interop is enabled.
- The Elsa 3 converter feature provides the source time zone option of ADR 0046 *Dates* and records it on each converted workflow; this spec consumes it for scripts.
- `RunJavaScript` scripts and input bindings share one evaluation surface and one capability profile, so a capability granted to one is granted to both.
- The Elsa 3 corpus figures cited (e.g. `newGuid`, `Date` usage) are indicative of migration impact; exact faithful-conversion coverage is validated during implementation against the available corpus.
- The canonical JSON encoder (ADR 0091) is available for `toJson` to build on; if its shape changes, `toJson`'s output tracks it.
