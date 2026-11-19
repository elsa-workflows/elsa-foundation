# Feature Specification: Expression capabilities (Elsa 4)

**Feature Branch**: `claude/3052-s1-expression-capabilities`

**Created**: 2026-11-10 · **Rechecked**: 2026-11-19 against the initiative branch at `b6a67a880`

**Status**: Draft — **provisional**, initiative #3052 spec **S-1**. Issue #3026.

**Input**: User description: "Expression capabilities (Elsa 4) — what expressions may do for current time, randomness, GUIDs, built-in helpers, and .NET-implemented functions, and how Elsa 3 time/GUID/string functions convert faithfully."

---

## Provisional status *(read first)*

This spec is **provisional** until it is approved. Every decision it applies is agreed: D-1, D-2, D-5 and D-6 in ADR 0045 §5; the capability profile in ADR 0083; dates, the value model and the .NET interop default in the ADR 0046 amendment; reads by name in ADR 0089; writes in ADR 0090; the invocation and refresh rules of ADR 0092. It does not depend on the unagreed JSON-integrity ADR (A-5): scripts serialize only with `JSON.stringify` (FR-009). Its open questions each carry a recommended answer.

---

## User Scenarios & Testing *(mandatory)*

Actors: **workflow authors** (write JavaScript expressions in input bindings and in `RunJavaScript` scripts), **host developers** (register .NET-implemented functions), and **teams migrating Elsa 3 workflows** to Elsa 4.

### User Story 1 - Current time, randomness, and GUIDs in expressions (Priority: P1)

A workflow author writes an expression that needs the current time, a random value, or a fresh GUID — for a timestamp, a correlation value, a jitter, a generated identifier. The expression produces the value at evaluation time; no special configuration, activity shape, or declaration is required.

**Why this priority**: This is the core capability the spec exists to grant. It reverses the earlier rule that expressions had to be free of time and randomness, which blocked a large share of real workflows and the Elsa 3 migration.

**Independent Test**: Author an input binding and a script that each call for the current time, a random number, and a new GUID; evaluate; confirm each yields a well-formed value with no configuration and no refusal.

**Acceptance Scenarios**:

1. **Given** an input-binding expression that reads the current time (`new Date()`, `Date.now()`, `Temporal.Now.instant()`), **When** it is evaluated, **Then** it returns the host clock's time, in UTC with invariant formatting.
2. **Given** a script that calls `newGuid()`, **When** it runs, **Then** it returns a GUID as a string.
3. **Given** a script that calls `Math.random()`, **When** it runs twice, **Then** it may return different values, and this is not treated as an error.
4. **Given** a workflow that stores the IANA time zone `Europe/Amsterdam` and a UTC instant, **When** an expression converts the instant to that zone with the native date facilities (`Temporal`, or `Intl.DateTimeFormat` with `timeZone`), **Then** it gets the local time in that zone, daylight saving included.

### User Story 2 - Elsa 3 time / GUID / string functions convert faithfully (Priority: P1)

A team migrates an Elsa 3 workflow whose expressions use the Elsa 3 time, GUID, JSON and Base64 string functions (and native `Date`/`Math`). The conversion reproduces the same behaviour in Elsa 4 rather than refusing it.

**Why this priority**: The initiative is a faithful Elsa 3 → Elsa 4 conversion. These functions are among the most used in the corpus (for example `newGuid` 22 uses in 18 definitions; `Date` 24 uses in 10 definitions), so refusing them blocks migration broadly.

**Independent Test**: Convert corpus workflows that use only the faithful functions; confirm each converts without refusal and the converted expression produces equivalent output.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 expression using `Date.now()`, `new Date().toISOString()`, `new Date().getTime()`, or `Math.random()`, **When** it is converted, **Then** it converts without refusal and uses the standard JavaScript facility, whether or not a source time zone is set.
2. **Given** an Elsa 3 expression using zone-dependent `Date` usage (for example `getHours()`, `toString()` on a `Date`, or `new Date('2026-10-28T09:15:00')`) and a conversion with **no** source time zone, **When** it is converted, **Then** it is refused with a diagnostic that quotes the expression and says to set the source time zone or rewrite with UTC methods or an explicit offset.
3. **Given** the same expression and a conversion with source time zone `Europe/Amsterdam`, **When** it is converted and evaluated, **Then** it gives the result Elsa 3 gave on a server in that zone, daylight saving included.
4. **Given** an Elsa 3 expression using `newGuid()`, `newGuidString()`, `newShortGuid()`, `getGuidString()`, `getShortGuid()` or `parseGuid(text)`, **When** it is converted, **Then** it converts without refusal and yields the same text Elsa 3 produced for that GUID.
5. **Given** an Elsa 3 expression using `stringToBase64()` / `stringFromBase64()`, **When** it is converted, **Then** it converts without refusal and produces the same text as Elsa 3.
6. **Given** an Elsa 3 script that reads a `DateTime` variable and calls `getTime()` on it, **When** it is converted, **Then** it converts and returns the same number as Elsa 3.
7. **Given** an Elsa 3 expression calling `toLocaleString()` without a locale, **When** it is converted, **Then** it is refused with guidance to pass an explicit locale.
8. **Given** an Elsa 3 expression using `toJson(x)`, **When** it is converted, **Then** it is rewritten to `JSON.stringify(x)` and converts with a behaviour warning that quotes the expression and names the text differences.

### User Story 3 - A relied-on live .NET object fails clearly, not cryptically (Priority: P1)

A migrating team has an Elsa 3 script that called a .NET member on a value Elsa 3 handed over as a live .NET object — for example `newGuid().toString('N')`, a member of a `Guid` or `FormFile` variable, or a reference to a .NET type such as `DateTime`. Elsa 3's engine resolved .NET members in camelCase as well as PascalCase. By default Elsa 4 hands expressions JSON values, so that usage cannot work. The team is told exactly where and why at conversion or publish time, instead of the workflow failing with an obscure error or a silently different value at run time.

**Why this priority**: Protecting migrations from silent breakage is a crucial part of the import. A clear, located refusal is the difference between a confident migration and a workflow that breaks unpredictably in production.

**Independent Test**: Convert a script that uses a member of a value that was a .NET object in Elsa 3, and publish a script that references a .NET type or invokes a PascalCase member; confirm each is refused with a diagnostic that quotes the offending expression and explains the JSON-only default; confirm JSON-key reads and native JavaScript calls are not refused.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 script that uses a member of a value known to be a .NET object in Elsa 3 (for example `newGuid().toString('N')`, or `variables.upload.openReadStream()` where `upload` was declared with a .NET type), **When** it is converted, **Then** it is refused with a diagnostic that quotes the offending expression and explains that values are JSON.
2. **Given** a script that references a .NET type (for example `TimeSpan.FromHours(1)` or `new Uri(s)`), **When** it is converted or published, **Then** it is refused with the same clear guidance.
3. **Given** a script that invokes a PascalCase member on a value it did not define (for example `payload.Items.Where(f)`), **When** it is converted or published, **Then** it is refused.
4. **Given** a script that reads a PascalCase property of a JSON value (`payload.OrderId`), calls a native JavaScript facility (`Intl.DateTimeFormat('en-GB')`), or calls a member it defines itself (`const helpers = { Format: x => x }; helpers.Format(v)`), **When** it is converted or published, **Then** it is **not** refused.
5. **Given** `newGuid()` used as text (`'id-' + newGuid()`, `newGuid().toString()`, or returned as a value), **When** it is converted, **Then** it is **not** refused: the text is the same in both versions.

### User Story 4 - Built-in helpers and .NET functions, consistent across surfaces (Priority: P2)

An author uses the built-in functions and expects them to behave the same in an input binding and in a script, and to exchange plain JSON values. A host developer registers a function of their own and gets a clear startup failure, not a silent override, when its name collides.

**Why this priority**: Consistency between the two expression surfaces removes a class of surprises; the JSON-only contract keeps results portable and persistable; the naming rule keeps modules from overriding each other.

**Independent Test**: Call each built-in function from both an input binding and a script; confirm identical availability and that every argument and result is a JSON value. Register two host functions with one name; confirm startup fails.

**Acceptance Scenarios**:

1. **Given** a built-in function (for example `stringToBase64`, a GUID function), **When** it is called from an input binding and from a script, **Then** it is available in both and behaves identically.
2. **Given** any .NET-implemented function, **When** it is called, **Then** every argument and its result is a JSON value (no live .NET object is passed in or returned).
3. **Given** a host that registers a function under a name already registered, or under a built-in name, **When** the host starts, **Then** startup fails with a message naming the function.
4. **Given** a number argument that JavaScript cannot hold exactly, **When** it is passed to a .NET-implemented function, **Then** the call runs and a warning names the function and the argument.

### Edge Cases

- **Random reproducibility**: random values are genuinely non-deterministic; the platform does not guarantee reproducible randomness, and tests must not depend on a fixed sequence. (Known accepted limitation.)
- **Refused functions used in a workflow**: a workflow using byte or stream helpers, or host configuration (`getConfig`/`getConfiguration`), is refused at conversion with guidance (configuration access is deferred to a later spec).
- **Dynamic / computed member access** (for example `value[name]()`): cannot be classified statically; it is allowed through (a recorded detector gap) and may fail at run time.
- **A .NET-object value reached indirectly** (passed as a function argument, read through a computed name, or stored in an object first): the converter does not follow it; it may fail at run time (a recorded detector gap). Direct use and plain `const`/`let` aliases are followed.
- **Clock in tests**: time is drawn from the host clock, so tests control time through the host's time source rather than by pinning expression output.
- **Date string parsed from a non-literal** (for example `new Date(input)`): the converter cannot see whether the text carries an offset. Without a source time zone it is refused (FR-024); with one it converts.
- **A script-defined member that shadows a .NET-style name**: if a script defines a member `Format` and also invokes `Format` on another value, the exemption hides the second call (a recorded detector gap).
- **`JSON.stringify(variables)`** (including a converted `toJson(variables)`): passing the whole `variables` root as a value is a computed read (ADR 0089) and is reported as possibly reading every visible variable; that is the accepted cost of the read rule, not something this spec changes.
- **A local named like a built-in** (`const newGuid = ...`): gets a publish warning (FR-027); the script's own declaration wins inside the script. A local named like a read root (`const variables = ...`) is a publish error, because it would break pinning.

## Requirements *(mandatory)*

### Functional Requirements

**Capabilities granted to expressions**

- **FR-001**: Expressions MUST be permitted to produce non-deterministic values — current time, random values, and newly generated GUIDs are allowed and MUST NOT be treated as errors. *(D-1)*
- **FR-002**: The current time available to expressions MUST come from the host's injectable clock, and expressions MUST evaluate in UTC with the invariant culture, except a converted expression with a source time zone (FR-023). *(ADR 0045 §5 D-1, ADR 0046 Dates)*
- **FR-003**: The same capabilities — native intrinsics and built-in and registered functions — MUST be available identically in input-binding expressions and in `RunJavaScript` scripts, under the capability profile `binding-readonly-v1`. Writes to engine-owned state stay as ADR 0090 and the activities-change-variables spec define them: `RunJavaScript` may write variables; input bindings may not. *(ADR 0083, ADR 0090)*
- **FR-004**: The standard JavaScript `Date`, `Temporal`, `Intl` and `Math` facilities MUST be available to expressions, restoring what the earlier determinism rule removed.
- **FR-005**: Randomness MUST be available as the standard JavaScript capability. The platform MUST NOT guarantee a controllable or reproducible random sequence; this is a documented limitation. *(D-1)*
- **FR-025**: The built-in date helpers that ADR 0045 §5 and ADR 0046 leave to this spec are the native `Temporal` and `Intl` facilities. Converting an instant to or from an IANA time zone (`Temporal.Instant.prototype.toZonedDateTimeISO(zone)`, `Temporal.ZonedDateTime`, `Intl.DateTimeFormat` with `timeZone`) MUST work for every IANA time zone ID the host knows, daylight saving included. No separate date host function is added.

**Built-in and .NET-implemented functions**

- **FR-006**: The GUID functions `newGuid`, `newGuidString`, `getGuidString`, `newShortGuid` and `getShortGuid` MUST be available, MUST take no arguments, and MUST return a new GUID as a string generated with `Guid.NewGuid()`. `newGuid`, `newGuidString` and `getGuidString` return the canonical 36-character form (Elsa 3's `newGuid` returned a .NET `Guid` whose text is this form). `newShortGuid` and `getShortGuid` return Elsa 3's short form: the standard Base64 of the GUID's 16 bytes in .NET byte order, with every `/`, `+` and `=` removed (at most 22 characters).
- **FR-026**: `parseGuid(text)` MUST be available and MUST return the canonical 36-character string of the GUID `text` denotes, accepting the forms .NET `Guid.Parse` accepts; text that is not a GUID MUST fail the evaluation as Elsa 3's did. *(ADR 0045 §5 D-1: Elsa 3 GUID function names never cause a refusal)*
- **FR-007**: The Base64 string functions `stringToBase64` and `stringFromBase64` MUST be available, operating on UTF-8 text with standard Base64 encoding, and MUST return strings. Invalid Base64 fails the evaluation and invalid UTF-8 decodes with U+FFFD replacement characters, as in Elsa 3.
- **FR-008**: *Retired* (the `now()` helper; see OQ-6).
- **FR-009**: Elsa 4 MUST NOT provide a `toJson` function: `JSON.stringify` is the one way a script turns a value into JSON text, the same serializer the engine uses for script results. The converter MUST rewrite an Elsa 3 `toJson(x)` call to `JSON.stringify(x)` and attach a behaviour warning that quotes the expression and names the differences: Elsa 3 wrote HTML-sensitive characters (`<`, `>`, `&`, `'`, `+`, `` ` ``) and characters outside the Basic Multilingual Plane as `\uXXXX` escapes and a `Date` with trimmed fractional seconds; it wrote `undefined` as `null` and failed on `NaN`, where `JSON.stringify` omits an `undefined` property and writes `NaN` as `null`. The parsed value is the same apart from those `undefined` and `NaN` cases. *(see OQ-5)*
- **FR-010**: The existing null/whitespace helpers (`isNullOrEmpty`, `isNullOrWhiteSpace`) MUST remain available as already specified. *(spec 247)*
- **FR-011**: By default — no .NET interop strategy installed — every value crossing between an expression and a .NET-implemented function (arguments and results) MUST be a value of the core value model as JSON. No .NET-implemented function may accept or return a live .NET object. Script values passed as arguments map to the value model as a script result does (a `Date` becomes its ISO 8601 UTC text, `undefined` becomes `null`). *(ADR 0045 §5 D-5, ADR 0046 V10)*
- **FR-028**: A number argument that carries the value model's loss information (it lost precision when it entered JavaScript), passed to a .NET-implemented function, MUST log a warning that names the function and the argument path; the call MUST NOT be refused. A function MUST NOT turn a number into a string to avoid the loss. *(ADR 0046 V9, value-model spec FR-015 and FR-016)*
- **FR-012**: The host-function registration MUST let a host declare a function **effectful** (one optional flag for the host developer, never visible to workflow authors). A function registered without the declaration is effect-free. The declaration MUST NOT refuse, skip or deduplicate a call; idempotency stays with the designer. The built-in functions of this spec are registered without it. It is the only effect metadata a function carries. This spec records the declaration; placing the commit before and after an input evaluation that calls a declared function belongs to the execution-contract implementation (ADR 0045 §3 as amended by D-2, ADR 0032, ADR 0020). *(D-6)*
- **FR-027**: Function names MUST follow ADR 0045 §5 D-5: the read roots (`variables`, `input`, `activities`, `workflow`, `args`), the built-in function names of this spec, and every registered namespace root are reserved. A host-registered function MUST carry a namespace prefix (`acme.lookup`); its namespace root is installed as a read-only global object holding the namespace's functions. A duplicate registration, or a registration under a reserved name, MUST fail at startup with a message naming the function. A script that declares a read root MUST get the existing publish error for reserved names (the declaration would break pinning); a script that declares a built-in name or a namespace root MUST get a publish warning of its own that names it.
- **FR-029**: Registering a function MUST mark it as allowed in `binding-readonly-v1`, and MUST state whether it reads workflow variables (the existing host-function declaration's flag, ADR 0089 R3). The built-in functions of this spec read no variables, so calling one never makes publish report an expression as possibly reading every variable. *(ADR 0083, ADR 0089)*

**Functions that remain unavailable**

- **FR-013**: Byte and stream helpers (`bytesToString`, `bytesFromString`, `bytesToBase64`, `bytesFromBase64`, `streamToBytes`, `streamToBase64` — binary data, not JSON) and host-configuration access (`getConfig` / `getConfiguration`) MUST NOT be available in this spec. Configuration access is deferred to a later spec. Migration cost in the 36-definition JTest corpus: `getConfig` 0 uses; `getConfiguration` 1 use in 1 definition (`javascript-script-handler-functions`; Elsa 3 had only `getConfig`, so this call already failed there).

**Elsa 3 conversion**

- **FR-014**: Converting an Elsa 3 workflow whose expressions use only the faithful functions MUST succeed without refusal — zone-independent `Date` usage (FR-021), zone-dependent `Date` usage when a source time zone is set (FR-022), `Math.random()`, the GUID functions and `parseGuid`, `stringToBase64` / `stringFromBase64`, and `toJson` (rewritten to `JSON.stringify` with a behaviour warning, FR-009).
- **FR-015**: Converting an Elsa 3 workflow that references a .NET type or constructor (for example `Guid`, `DateTime`) or that uses a refused function (FR-013) MUST be refused at conversion with a clear diagnostic and guidance. `crypto.randomUUID`, which Elsa 3 did not provide, stays refused.

**Protecting migrations from the JSON-only default**

- **FR-016**: At **conversion**, the converter MUST refuse a member access — a read or an invocation, in any casing — on a value it knows was a live .NET object in Elsa 3, except `toString()` with no arguments on a GUID; and it MUST refuse passing a known .NET-object value other than a GUID to `toJson` (Elsa 3 serialized its .NET properties, which `JSON.stringify` of the Elsa 4 value does not reproduce). It knows a value was a .NET object when the value is used directly, or through a `const`/`let` alias in the same script, and is the result of `newGuid()` or `parseGuid(...)`, or a read of a variable, input or activity output whose Elsa 3 type was a .NET type that Elsa 3's engine handed to scripts as a wrapped object (for example `Guid`, `FormFile`, `Stream`). On a value whose Elsa 3 type was a list or dictionary, which Elsa 3's engine gave JavaScript array or object behaviour, property reads and invocations of native JavaScript `Array`/`Object` members are allowed and other invocations are refused. *(see OQ-4)*
- **FR-032**: Elsa 3's engine handed a `DateTime` or `DateTimeOffset` value to scripts as a JavaScript `Date`; Elsa 4 hands it over as ISO 8601 text (ADR 0046). The converter MUST rewrite each direct read of a date-typed Elsa 3 variable, input or activity output into a JavaScript `Date` of the same instant (`null` stays `null`), so member calls, comparisons and arithmetic behave as in Elsa 3; zone-dependent use of the result then follows FR-022. A date value reached indirectly (a computed read) is not rewritten (a recorded gap). *(see OQ-8)*
- **FR-017**: At **conversion and publish**, the analysis MUST refuse a reference to a .NET type (static access or construction) whose name is in a catalog of .NET types and is not a JavaScript global, and an invocation of a PascalCase member on a value the script did not define. Neither rule applies to a name the script declares itself or to a registered function or namespace. At conversion, the .NET globals the converter already refuses keep their existing refusal and code.
- **FR-018**: The refusals of FR-016 and FR-017 MUST quote the offending expression and explain that values are JSON and live .NET objects are not available. A precise source line/column is not required (OQ-2). Conversion uses the import diagnostic `VF-IMP-012`; publish uses a script validation error of its own.
- **FR-019**: The analysis MUST allow property reads of any casing on values not covered by FR-016 (they may address JSON keys, including PascalCase keys), invocations whose receiver is a JavaScript global (`Intl.DateTimeFormat(...)`, `Math.max(...)`), and invocations of a member defined in the same script (an object-literal key, a class method or field, or a member assignment `x.M = ...`). It MUST NOT classify dynamic/computed member access (a recorded gap).
- **FR-030**: The analysis belongs to the default JSON-only engine strategy and is replaceable with it. This spec adds no switch; the opt-in .NET interop strategy and its own publish warning are a separate feature (#4103), which decides whether this analysis applies under it. *(ADR 0045 §5 D-5, ADR 0046 V10)*

**Execution contract context**

- **FR-020**: Expression values recorded in an invocation's input snapshot MUST NOT be re-evaluated when that invocation is retried or recovered. A re-execution or a reschedule (a new invocation), a background refresh of a stale snapshot, and each pass of a loop that re-materializes its inputs evaluate again and may produce new values. *(D-2; ADR 0092 decisions 8 and 9 — this is the contract under which non-deterministic expressions are safe; it is not re-specified here.)*

**Time zones in converted Elsa 3 scripts** *(ADR 0046 Dates: Elsa 3 evaluated script dates in the server's time zone)*

- **FR-021**: Zone-independent `Date` usage MUST convert faithfully whether or not a source time zone is set: `Date.now()`; `new Date()` used as an instant; `getTime()` / `valueOf()` and epoch arithmetic; `new Date(number)`; `Date.UTC(...)`; `toISOString()` / `toJSON()` (including a `Date` returned as a value, which leaves as UTC in both versions); the UTC getters and setters (`getUTCHours()` ...); parsing a string literal that carries `Z` or an explicit offset; and parsing a date-only literal (`new Date('2026-10-28')`), which ECMAScript reads as UTC.
- **FR-022**: Zone-dependent `Date` usage MUST convert only when the conversion has a **source time zone** set — the conversion option of ADR 0046 *Dates*, recorded in the published conversion contract; the assume-UTC policy is the source time zone `UTC` — and MUST be refused otherwise, with a diagnostic that quotes the offending expression and guidance to set the source time zone or to rewrite with UTC methods or an explicit offset. Zone-dependent usage is: the local-time getters and setters (`getHours()`, `getDate()`, `getDay()`, `getMonth()`, `getFullYear()`, `getTimezoneOffset()`, `setHours()` ...); local formatting (`toString()`, `toDateString()`, `toTimeString()`, `toLocaleString()`, `toLocaleDateString()`, `toLocaleTimeString()`); the local-time multi-argument constructor (`new Date(2026, 9, 28)`); and parsing a date-time string literal without an offset (`new Date('2026-10-28T09:15:00')`, `Date.parse('2026-10-28T09:15:00')`). Methods that exist only on `Date` (`getHours` ...) are recognised by name; the generic names (`toString`, `valueOf`) count only when the receiver is known to be a `Date` (a `new Date(...)`, a `Date`-returning call, a date value rewritten under FR-032, or a `const`/`let` alias of one). A `Date` reached indirectly is not classified (a recorded gap). Local formatting with `toLocale*String()` also falls under FR-033.
- **FR-023**: When a source time zone is set, a converted expression MUST evaluate with that IANA time zone as its local time zone, so zone-dependent usage gives the result Elsa 3 gave on a server in that zone, daylight saving included. The clock still supplies the same instant (FR-002). The zone is part of the converted expression's published form. Expressions that were not converted, and converted expressions without a source time zone, evaluate in UTC.
- **FR-024**: A string parse whose argument is not a literal (`new Date(input)`, `Date.parse(input)`) cannot be classified statically, and offset-less text would give a different instant. Without a source time zone it MUST be refused with a diagnostic that quotes the expression and the guidance of FR-022, as ADR 0046 *Dates* requires for converting scripts that parse dates; with a source time zone set it converts. *(see OQ-3)*
- **FR-033**: Elsa 3 evaluated scripts in the server's culture; Elsa 4 evaluates in the invariant culture (FR-002). A locale-dependent call without an explicit locale — `toLocaleString()`, `toLocaleDateString()` and `toLocaleTimeString()` on any value, `localeCompare` without a locale, and `Intl` constructors without a locale — MUST be refused at conversion with a diagnostic that quotes the expression and says to pass an explicit locale. The same call with an explicit locale converts. *(see OQ-9)*

**Profile and surfaces**

- **FR-031**: The capability-profile constant MUST be `binding-readonly-v1` everywhere the expression contract names a profile (definitions, the converter, the evaluators, the executable hash). There is no alias or reader for `binding-pure-v1`: compatibility is owed only from the first stable release, so stored fixtures and golden hashes are regenerated. The `Inline` activity's replay justification that leaned on `binding-pure-v1` is removed; its basis is the D-2 execution contract. *(ADR 0083)*

### Key Entities

- **Expression**: author-written JavaScript evaluated in an input binding or a script; consumes and produces JSON values.
- **Capability profile** (`binding-readonly-v1`): the capabilities granted to expressions on both surfaces — standard JS intrinsics plus the registered .NET-implemented functions (ADR 0083; spec 095's expression contract holds the full definition).
- **Host-function registration**: a function's name, whether it reads variables, whether it is effectful, and its implementation; the only registration surface for .NET-implemented functions.
- **Known .NET-object value**: a value the converter can show was a live .NET object in Elsa 3 (FR-016).
- **Conversion / publish diagnostic**: a refusal or warning carrying a code, a message that quotes the offending expression, and guidance.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of Elsa 3 corpus workflows whose JavaScript uses only the faithful functions (`Math`, the GUID functions, `stringToBase64`/`stringFromBase64`, `toJson` as rewritten) and zone-independent `Date` usage convert without refusal; with a source time zone set, the same holds for workflows that also use zone-dependent `Date` usage. (The JTest corpus has no literal zone-dependent usage: its `Date` use is `getTime()` busy loops, `toISOString()`, `Date.now()` and three non-literal string parses, which need a source time zone, FR-024.)
- **SC-002**: Zero workflows that use a member of a known .NET-object value (FR-016) or a .NET type convert without refusal — every such case is refused at conversion or publish, with a diagnostic that quotes the offending expression.
- **SC-003**: An author can obtain the current time, a random value, and a new GUID in an expression with no special configuration and no activity-shape change.
- **SC-004**: No expression capability differs between input bindings and scripts — the same functions and intrinsics are available on both surfaces.
- **SC-005**: Every value produced by a built-in or .NET-implemented function is a JSON value that round-trips through persistence with no .NET type leakage.
- **SC-006**: A migrating team reading a .NET-usage refusal can identify the exact offending expression from the diagnostic alone.
- **SC-007**: Zero converted workflows with zone-dependent `Date` usage convert without a source time zone — every such case is refused with a diagnostic that quotes the expression.
- **SC-008**: For the same GUID or Base64 input, each converted Elsa 3 GUID and Base64 function returns text identical to Elsa 3's; every converted `toJson` carries a behaviour warning.

## Dependencies & agreed decisions

- **ADR 0045 §5 (D-1, D-5, D-6)** — expressions may read the clock, generate random values and GUIDs; Elsa 3 date and GUID functions convert directly; .NET-implemented functions exchange JSON by default, follow one naming rule, and carry only an optional effectful declaration.
- **D-2 execution contract** (ADR 0045 §3, ADR 0032, ADR 0020 as amended) and **ADR 0092** decisions 8 and 9 — when a value is recorded, reused or evaluated again; the safety basis for FR-001 and FR-020.
- **ADR 0083** — the capability profile `binding-readonly-v1`; left to this spec: the constant rename, the `Inline` justification, and the registration that marks a function as allowed.
- **ADR 0046** (as amended) — *Dates*: UTC clock, IANA zones, the converter's source time zone; the core value model; V9 precision loss; V10 .NET interop off by default, opt-in through one replaceable engine strategy (#4103).
- **ADR 0089** — scripts read variables by name (`variables.X`); host functions that read variables declare it (R3).
- **ADR 0090** — which surfaces may write engine-owned state.
- **Spec 1222** (scripts read by name) — the host-function declaration this spec extends, the read roots, and import codes `VF-IMP-010`/`VF-IMP-011`.
- **Spec 1228** (value model) — the boundary rules for host-function arguments (FR-015, FR-016 there) and the default-only interop stance.
- **Spec 1231** (activities change variables) — script writes, which this spec does not change.
- **Spec 1230 / ADR 0093** (background eligibility) — unchanged: `RunJavaScript` stays ineligible; input bindings run on the coordinator, so the effectful declaration does not affect eligibility.

## Open questions

Each question has a recommended answer; the spec text follows the recommendation.

- **OQ-2 (non-blocking)**: expression diagnostics carry no JavaScript source line/column; FR-018 quotes the offending expression instead. **Recommended:** keep quoting; thread precise coordinates through all expression diagnostics in a separate follow-up.
- **OQ-3**: FR-024 converts a non-literal date-string parse with a behaviour warning when no source time zone is set, as ADR 0046 does for non-literal date expressions; the stricter reading refuses it. In the JTest corpus this decides 3 definitions (`javascript-date-methods`, `performance-minimal-baseline`, `composition-execute-workflow-root`). **Recommended:** refuse without a source time zone (FR-024). ADR 0046's behaviour warning covers non-literal expressions bound to date *members*; for scripts the ADR says converting needs the source time zone, and offset-less text gives a different instant. The 3 definitions convert once a source time zone is set. (The 10 Nov draft recommended the warning.)
- **OQ-4**: a corpus scan found that a PascalCase-invocation rule refuses none of the 36 definitions, while the corpus does use .NET members in camelCase (Elsa 3's engine resolved them that way). **Recommended:** FR-016's rule — refuse member use on values the converter knows were .NET objects (direct use and plain aliases), keep the .NET-type and PascalCase rules as the publish-time net, and record indirect flows as a gap. A full data-flow analysis is rejected as too heavy for the signal.
- **OQ-5**: Elsa 3's `toJson` wrote different text from `JSON.stringify` (escaping, dates, `undefined`, `NaN`); a value-for-value Elsa 4 helper would mean a second JSON serializer with no purpose an author would choose it for. **Recommended:** no `toJson` in Elsa 4; the converter rewrites it to `JSON.stringify` with a behaviour warning (FR-009). The stricter alternative refuses `toJson` unless its result is parsed straight back (`JSON.parse(toJson(x))`, the corpus's copy idiom), where only the `undefined`/`NaN` cases differ.
- **OQ-6**: should the 10 Nov `now()` helper stay? **Recommended:** drop it. Elsa 3 had no `now`, native `Date` and `Temporal` cover it, and as a reserved name (FR-027) it would warn on the common `const now = new Date()`.
- **OQ-8**: Elsa 3 handed date-typed values to scripts as a JavaScript `Date`, Elsa 4 as ISO text. **Recommended:** the converter rewrites direct reads into a `Date` (FR-032), which keeps converted scripts working; refusing every use of a date-typed value would refuse common, faithful scripts.
- **OQ-9**: Elsa 3 used the server's culture. **Recommended:** refuse locale-dependent calls without an explicit locale (FR-033). A source-culture option beside the source time zone would let them convert, but ADR 0046 does not define one, so it would need an ADR amendment; not proposed now.
- **OQ-7**: the profile constant is shared with the Liquid evaluator. **Recommended:** one constant, renamed everywhere (FR-031); Liquid's own filters are unchanged by this spec, and unblocking its date filters under D-1 is a separate follow-up.

## Assumptions

- The host provides an injectable clock (`TimeProvider`) that the expression engine draws current time from; tests control time through it.
- By default no .NET interop strategy is installed (ADR 0046 V10); this spec ships only that default.
- The Elsa 3 converter feature provides the source time zone option of ADR 0046 *Dates* and records it on each converted workflow; this spec consumes it for scripts.
- `RunJavaScript` scripts and input bindings share one evaluation surface and one capability profile.
- The host runtime resolves IANA time zone IDs (on Windows through ICU, as .NET 10 does by default).
- The Elsa 3 corpus figures cited are indicative of migration impact; exact faithful-conversion coverage is validated during implementation against the available corpus.
