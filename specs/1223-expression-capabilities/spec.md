# Feature Specification: Expression capabilities (Elsa 4)

**Feature Branch**: `claude/3052-s1-expression-capabilities`

**Created**: 2026-11-10 · **Rechecked**: 2026-11-19 against the initiative branch at `9aeda8dd3`

**Status**: Implemented — PR #4855 (2026-11-20). Approved 2026-11-19 by @j03y-nxxbz as coordinator (the approval plan), with every open question decided as recommended; applies ADR 0045 §5, ADR 0083, ADR 0046, ADR 0089, ADR 0090 and ADR 0092 as merged on the initiative branch. Initiative #3052 spec **S-1**. Issue #3026.

**Input**: User description: "Expression capabilities (Elsa 4) — what expressions may do for current time, randomness, GUIDs, built-in helpers, and .NET-implemented functions, and how Elsa 3 time/GUID/string functions convert faithfully."

---

## Basis

Every decision this spec applies is agreed: D-1, D-5 and D-6 in ADR 0045 §5 and its *D-2 execution contract* section; the capability profile in ADR 0083; dates, the value model and the .NET interop default in the ADR 0046 amendment; reads by name in ADR 0089; writes in ADR 0090; the invocation and refresh rules of ADR 0092. It does not depend on the JSON-integrity ADR (A-5): scripts serialize only with `JSON.stringify` (FR-009).

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

**Why this priority**: The initiative is a faithful Elsa 3 → Elsa 4 conversion. These functions are among the most used in the corpus (for example the GUID functions `newGuid`, `newGuidString` and `newShortGuid` together 22 uses in 18 definitions; `Date` 24 uses in 10 definitions), so refusing them blocks migration broadly.

**Independent Test**: Convert corpus workflows that use only the faithful functions; confirm each converts without refusal and the converted expression produces equivalent output.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 expression using `Date.now()`, `new Date().toISOString()`, `new Date().getTime()`, or `Math.random()`, **When** it is converted, **Then** it converts without refusal and uses the standard JavaScript facility, under every date interpretation (FR-034).
2. **Given** an Elsa 3 expression using zone-dependent `Date` usage (for example `getHours()`, `toString()` on a `Date`, or `new Date('2026-10-28T09:15:00')`) and the default date interpretation `Refuse`, **When** it is converted, **Then** it is refused with a diagnostic that quotes the expression and says how to fix it: set the Elsa 3 server's time zone (`SourceTimeZone`), switch to `AssumeUtc`, or rewrite with UTC methods or an explicit offset.
3. **Given** the same expression and the date interpretation `SourceTimeZone` = `Europe/Amsterdam`, **When** it is converted and evaluated, **Then** the converted code names the zone explicitly (for example the hour of the instant in `Europe/Amsterdam`), the engine still runs in UTC, and the result is what Elsa 3 gave on a server in that zone, daylight saving included, with no warning.
3a. **Given** the same expression and the date interpretation `AssumeUtc`, **When** it is converted, **Then** it converts, evaluates in UTC, and carries a behaviour warning that quotes the expression and says the result differs from Elsa 3 if the Elsa 3 server was not on UTC.
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
- **Date string parsed from a non-literal** (for example `new Date(input)`): the converter cannot see whether the text carries an offset. It follows the date interpretation like any zone-dependent usage (FR-024).
- **A script-defined member that shadows a .NET-style name**: if a script defines a member `Format` and also invokes `Format` on another value, the exemption hides the second call (a recorded detector gap).
- **`JSON.stringify(variables)`** (including a converted `toJson(variables)`): passing the whole `variables` root as a value is a computed read (ADR 0089) and is reported as possibly reading every visible variable; that is the accepted cost of the read rule, not something this spec changes.
- **A local named like a built-in** (`const newGuid = ...`): gets a publish warning (FR-027); the script's own declaration wins inside the script. A local named `variables` is a publish error, because it would break pinning; a script's own `args` follows spec 1222.

## Requirements *(mandatory)*

### Functional Requirements

**Capabilities granted to expressions**

- **FR-001**: Expressions MUST be permitted to produce non-deterministic values — current time, random values, and newly generated GUIDs are allowed and MUST NOT be treated as errors. *(D-1)*
- **FR-002**: The current time available to expressions MUST come from the host's injectable clock, and every expression MUST evaluate in UTC with the invariant culture; a converted expression that reproduces Elsa 3's local time does so with the zone written in its code (FR-023). *(ADR 0045 §5 D-1, ADR 0046 Dates)*
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
- **FR-012**: Functions MUST carry no effect metadata in this spec. This spec does not implement the effectful declaration of ADR 0045 §5 D-6 (and its consumers in ADR 0045 §3, ADR 0032 and ADR 0083); #4831 implements the declaration and its commit boundary together. Every registered function, the built-ins included, is effect-free under D-6: calling one adds no commit boundary, and a call is never refused, skipped or deduplicated; idempotency stays with the designer. The effectful declaration D-6 allows arrives together with the commit boundary that acts on it (#4831), so a host is never offered a declaration that has no effect. None of the Elsa 3 functions this spec converts is effectful. *(D-6)*
- **FR-027**: Function names MUST follow ADR 0045 §5 D-5: the read roots (`variables`, `input`, `activities`, `workflow`, `args`), the built-in function names of this spec, and every registered namespace root are reserved. A host-registered function MUST carry a namespace prefix (`acme.lookup`); its namespace root is installed as a read-only global object holding the namespace's functions. A duplicate registration, or a registration under a reserved name, MUST fail at startup with a message naming the function. A script that declares `variables` MUST get the existing publish error for reserved names (the declaration would break pinning); a script's own `args` stays as spec 1222 defines it; `complete` keeps the error spec 1208 gives it. A script that declares `input`, `activities`, `workflow`, a built-in function name of this spec or a namespace root MUST get a publish warning of its own that names it.
- **FR-029**: Registering a function MUST mark it as allowed in `binding-readonly-v1`, and MUST state whether it reads workflow variables (the existing host-function declaration's flag, ADR 0089 R3). The built-in functions of this spec read no variables, so calling one never makes publish report an expression as possibly reading every variable. *(ADR 0083, ADR 0089)*

**Functions that remain unavailable**

- **FR-013**: Byte and stream helpers (`bytesToString`, `bytesFromString`, `bytesToBase64`, `bytesFromBase64`, `streamToBytes`, `streamToBase64` — binary data, not JSON) and host-configuration access (`getConfig` / `getConfiguration`) MUST NOT be available in this spec. Configuration access is deferred to a later spec. Migration cost in the 36-definition JTest corpus: `getConfig` 0 uses; `getConfiguration` 1 use in 1 definition (`javascript-script-handler-functions`; Elsa 3 had only `getConfig`, so this call already failed there).

**Elsa 3 conversion**

- **FR-014**: Converting an Elsa 3 workflow whose expressions use only the faithful functions MUST succeed without refusal — zone-independent `Date` usage (FR-021), zone-dependent `Date` usage under `AssumeUtc` or `SourceTimeZone` (FR-022), `Math.random()`, the GUID functions and `parseGuid`, `stringToBase64` / `stringFromBase64`, and `toJson` (rewritten to `JSON.stringify` with a behaviour warning, FR-009).
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

- **FR-021**: Zone-independent `Date` usage MUST convert faithfully under every date interpretation (FR-034): `Date.now()`; `new Date()` used as an instant; `getTime()` / `valueOf()` and epoch arithmetic; `new Date(number)`; `Date.UTC(...)`; `toISOString()` / `toJSON()` (including a `Date` returned as a value, which leaves as UTC in both versions); the UTC getters and setters (`getUTCHours()` ...); parsing a string literal that carries `Z` or an explicit offset; and parsing a date-only literal (`new Date('2026-10-28')`), which ECMAScript reads as UTC.
- **FR-022**: Zone-dependent `Date` usage MUST follow the date interpretation (FR-034): under `Refuse` it is refused with a diagnostic that quotes the offending expression and guidance that says how to fix it: set the date interpretation to `SourceTimeZone` with the IANA time zone of the Elsa 3 server (for example `Europe/Amsterdam`), or set it to `AssumeUtc` to accept UTC, or rewrite the expression with UTC methods or an explicit offset; under `AssumeUtc` it converts, evaluates in UTC, and carries a behaviour warning that quotes the expression and says the result differs from Elsa 3 if the Elsa 3 server was not on UTC; under `SourceTimeZone` it converts with no warning, rewritten with the zone written in the code (FR-023). Zone-dependent usage is: the local-time getters and setters (`getHours()`, `getDate()`, `getDay()`, `getMonth()`, `getFullYear()`, `getTimezoneOffset()`, `setHours()` ...); local formatting (`toString()`, `toDateString()`, `toTimeString()`); the local-time multi-argument constructor (`new Date(2026, 9, 28)`); and parsing a date-time string literal without an offset (`new Date('2026-10-28T09:15:00')`, `Date.parse('2026-10-28T09:15:00')`). Methods that exist only on `Date` (`getHours` ...) are recognised by name; the generic names (`toString`, `valueOf`) count only when the receiver is known to be a `Date` (a `new Date(...)`, a `Date`-returning call, a date value rewritten under FR-032, or a `const`, `let` or `var` alias of one in its declaring scope that the script never assigns again; `var` added by the coordinator on 2026-11-20 in #4855, because Elsa 3 scripts mostly declare with `var`). A `Date` reached indirectly is not classified (a recorded gap). Locale formatting (`toLocale*String()`) falls under FR-033.
- **FR-023**: Every expression, converted or not, MUST evaluate in UTC (FR-002); no expression carries an ambient time zone. Under `SourceTimeZone`, the converter MUST instead write the zone into the converted code, so a reader sees where Elsa 3's local time is reproduced. Each zone-dependent call is rewritten into an explicit `Temporal` computation in that IANA zone that gives the result Elsa 3 gave on a server in that zone, daylight saving included: a local getter reads the field of the instant in the zone (`d.getHours()` becomes the hour of `d` in `Europe/Amsterdam`); a local setter changes that field in the zone and keeps the `Date`'s instant in step; the local multi-argument constructor and an offset-less parse build the instant from the wall-clock time in the zone, with `Date`'s roll-over of out-of-range fields and its resolution of skipped and repeated hours; a non-literal parse goes through one helper that parses text with an offset or a date-only text natively and other text in the zone. The rewrite uses a small helper written once into the converted expression, the way the converted null/whitespace helpers are (spec 247). A getter or setter name that exists only on `Date` is rewritten whatever its receiver, and the helper leaves a receiver that is not a `Date` untouched. Local formatting with `toString()`, `toDateString()` or `toTimeString()` on a `Date` is refused under `SourceTimeZone`, because Elsa 3's text (for example `GMT+0100 (Central European Standard Time)`) depends on the server's operating system; the guidance says to use `toISOString()` or `Intl.DateTimeFormat` with an explicit `timeZone` and locale.
- **FR-024**: A string parse whose argument is not a literal (`new Date(input)`, `Date.parse(input)`) cannot be classified statically, and offset-less text would give a different instant. It MUST be treated as zone-dependent usage under FR-022: refused under `Refuse` with the same guidance, converted with a behaviour warning under `AssumeUtc`, converted under `SourceTimeZone`. *(see OQ-3)*
- **FR-034**: The Elsa 3 converter feature MUST offer one **date interpretation** setting, set by the host and recorded on each converted workflow and in its published conversion contract (ADR 0046 *Dates*: a declared source time zone or an explicit assume-UTC policy). Its values: `Refuse` (the default: nothing is assumed), `AssumeUtc` (convert as if the Elsa 3 server ran on UTC, with a behaviour warning on each affected expression), and `SourceTimeZone` with an IANA time zone ID (convert with that zone written explicitly into the converted code, no warning; FR-023). It is the single date setting of ADR 0046 *Dates*: this spec defines it and applies it to scripts; date interpretation at the JSON to .NET boundary (literals, variable defaults) belongs to the JSON-integrity spec (S-9, spec 1207), which uses the same setting, so a host sets it once. *(see OQ-3)*
- **FR-033**: Elsa 3 evaluated scripts in the server's culture; Elsa 4 evaluates in the invariant culture (FR-002). A locale-dependent call without an explicit locale — `toLocaleString()`, `toLocaleDateString()` and `toLocaleTimeString()` on any value, `localeCompare` without a locale, and `Intl` constructors without a locale — MUST be refused at conversion with a diagnostic that quotes the expression and says to pass an explicit locale. The same call with an explicit locale converts. *(see OQ-9)*

**Profile and surfaces**

- **FR-031**: The capability-profile constant MUST be `binding-readonly-v1` everywhere the expression contract names a profile (definitions, the converter, the evaluators, the executable hash). There is no alias or reader for `binding-pure-v1`: compatibility is owed only from the first stable release, so stored fixtures and golden hashes are regenerated. The `Inline` activity's replay justification that leaned on `binding-pure-v1` is removed; its basis is the D-2 execution contract. *(ADR 0083)*

### Key Entities

- **Expression**: author-written JavaScript evaluated in an input binding or a script; consumes and produces JSON values.
- **Capability profile** (`binding-readonly-v1`): the capabilities granted to expressions on both surfaces — standard JS intrinsics plus the registered .NET-implemented functions (ADR 0083; spec 095's expression contract holds the full definition).
- **Host-function registration**: a function's name, whether it reads variables, and its implementation; the only registration surface for .NET-implemented functions.
- **Known .NET-object value**: a value the converter can show was a live .NET object in Elsa 3 (FR-016).
- **Conversion / publish diagnostic**: a refusal or warning carrying a code, a message that quotes the offending expression, and guidance.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of Elsa 3 corpus workflows whose JavaScript uses only the faithful functions (`Math`, the GUID functions, `stringToBase64`/`stringFromBase64`, `toJson` as rewritten) and zone-independent `Date` usage convert without refusal; under `AssumeUtc` or `SourceTimeZone`, the same holds for workflows that also use zone-dependent `Date` usage. (The JTest corpus has no literal zone-dependent usage: its `Date` use is `getTime()` busy loops, `toISOString()`, `Date.now()` and three non-literal string parses, which need `AssumeUtc` or `SourceTimeZone`, FR-024.)
- **SC-002**: Zero workflows that use a member of a known .NET-object value (FR-016) or a .NET type convert without refusal — every such case is refused at conversion or publish, with a diagnostic that quotes the offending expression.
- **SC-003**: An author can obtain the current time, a random value, and a new GUID in an expression with no special configuration and no activity-shape change.
- **SC-004**: No expression capability differs between input bindings and scripts — the same functions and intrinsics are available on both surfaces.
- **SC-005**: Every value produced by a built-in or .NET-implemented function is a JSON value that round-trips through persistence with no .NET type leakage.
- **SC-006**: A migrating team reading a .NET-usage refusal can identify the exact offending expression from the diagnostic alone.
- **SC-007**: Under the default `Refuse`, zero converted workflows with zone-dependent `Date` usage convert — every such case is refused with a diagnostic that quotes the expression and names both fixes (`SourceTimeZone`, `AssumeUtc`); under `AssumeUtc`, every such expression carries a behaviour warning.
- **SC-008**: For the same GUID or Base64 input, each converted Elsa 3 GUID and Base64 function returns text identical to Elsa 3's; every converted `toJson` carries a behaviour warning.

## Dependencies & agreed decisions

- **ADR 0045 §5 (D-1, D-5, D-6)** — expressions may read the clock, generate random values and GUIDs; Elsa 3 date and GUID functions convert directly; .NET-implemented functions exchange JSON by default, follow one naming rule, and are effect-free unless declared effectful (the declaration ships with its commit boundary, #4831).
- **D-2 execution contract** (ADR 0045 §3, ADR 0032, ADR 0020 as amended) and **ADR 0092** decisions 8 and 9 — when a value is recorded, reused or evaluated again; the safety basis for FR-001 and FR-020.
- **ADR 0083** — the capability profile `binding-readonly-v1`; left to this spec: the constant rename, the `Inline` justification, and the registration that marks a function as allowed.
- **ADR 0046** (as amended) — *Dates*: UTC clock, IANA zones, the converter's explicit date interpretation (source time zone or assume-UTC); the core value model; V9 precision loss; V10 .NET interop off by default, opt-in through one replaceable engine strategy (#4103).
- **ADR 0089** — scripts read variables by name (`variables.X`); host functions that read variables declare it (R3).
- **ADR 0090** — which surfaces may write engine-owned state.
- **Spec 247** (Elsa 3 JavaScript host functions) — this spec supersedes its refusals of the GUID functions, `parseGuid`, the Base64 pair and `toJson` (247 FR and research R-6); its inlined null/whitespace helpers stay (FR-010).
- **Spec 1222** (scripts read by name) — the host-function declaration this spec extends, the read roots, and import codes `VF-IMP-010`/`VF-IMP-011`.
- **Spec 1228** (value model) — the boundary rules for host-function arguments (FR-015, FR-016 there) and the default-only interop stance.
- **Spec 1231** (activities change variables) — script writes, which this spec does not change.
- **Spec 1230 / ADR 0093** (background eligibility) — unchanged: `RunJavaScript` stays ineligible; input bindings run on the coordinator, so functions called from them do not affect eligibility.

## Resolved questions

Each question was decided as recommended; the spec text follows the decision.

- **OQ-2 (non-blocking)**: expression diagnostics carry no JavaScript source line/column; FR-018 quotes the offending expression instead. **Decided:** keep quoting; thread precise coordinates through all expression diagnostics in a separate follow-up.
- **OQ-3**: zone-dependent date usage, including a non-literal parse (`new Date(input)`), when the converting host has not said which time zone the Elsa 3 server used. **Decided:** a converter setting, the date interpretation (FR-034): `Refuse` by default, `AssumeUtc` (convert in UTC with a behaviour warning) or `SourceTimeZone` (convert with the given zone written explicitly into the converted code). The refusal tells the user to set the Elsa 3 server's time zone or to switch to `AssumeUtc`. No default is taken from the converting server, which need not be in the Elsa 3 server's zone; a default of `AssumeUtc` would contradict ADR 0046's rule that nothing is assumed without configuration. In the JTest corpus 3 definitions (`javascript-date-methods`, `performance-minimal-baseline`, `composition-execute-workflow-root`) are refused under `Refuse` and convert under either other value.
- **OQ-4**: a corpus scan found that a PascalCase-invocation rule refuses none of the 36 definitions, while the corpus does use .NET members in camelCase (Elsa 3's engine resolved them that way). **Decided:** FR-016's rule — refuse member use on values the converter knows were .NET objects (direct use and plain aliases), keep the .NET-type and PascalCase rules as the publish-time net, and record indirect flows as a gap. A full data-flow analysis is rejected as too heavy for the signal.
- **OQ-5**: Elsa 3's `toJson` wrote different text from `JSON.stringify` (escaping, dates, `undefined`, `NaN`); a value-for-value Elsa 4 helper would mean a second JSON serializer with no purpose an author would choose it for. **Decided:** no `toJson` in Elsa 4; the converter rewrites it to `JSON.stringify` with a behaviour warning (FR-009). The stricter alternative refuses `toJson` unless its result is parsed straight back (`JSON.parse(toJson(x))`, the corpus's copy idiom), where only the `undefined`/`NaN` cases differ.
- **OQ-6**: should the 10 Nov `now()` helper stay? **Decided:** drop it. Elsa 3 had no `now`, native `Date` and `Temporal` cover it, and as a reserved name (FR-027) it would warn on the common `const now = new Date()`.
- **OQ-7**: the profile constant is shared with the Liquid evaluator. **Decided:** one constant, renamed everywhere (FR-031); Liquid's own filters are unchanged by this spec, and unblocking its date filters under D-1 is a separate follow-up.
- **OQ-8**: Elsa 3 handed date-typed values to scripts as a JavaScript `Date`, Elsa 4 as ISO text. **Decided:** the converter rewrites direct reads into a `Date` (FR-032), which keeps converted scripts working; refusing every use of a date-typed value would refuse common, faithful scripts.
- **OQ-9**: Elsa 3 used the server's culture. **Decided:** refuse locale-dependent calls without an explicit locale (FR-033). A source-culture option beside the source time zone would let them convert, but ADR 0046 does not define one, so it would need an ADR amendment; not proposed now.

## Measured after implementation

JTest corpus (36 definitions, local checkout, throwaway probe through `ConversionServices` against the real activity catalog, 2026-11-19): **19/36** convert under `Refuse`, **21/36** under `AssumeUtc` and under `SourceTimeZone` = `Europe/Amsterdam` (SC-001). The two extra are `javascript-date-methods` (`new Date(dateInput)`) and `performance-minimal-baseline` (`new Date(getStartTime())`): non-literal parses, refused under `Refuse` with guidance naming both settings (SC-007). `composition-execute-workflow-root` is refused for activity reasons outside this spec. No S-1 construct refuses any other definition: the GUID functions and `toJson` (`javascript-json-methods`, with the rewrite warning) convert; the remaining JavaScript refusals are the byte helpers (`javascript-elsa-functions`, FR-013) and `base64Encode`/`base64Decode`, which Elsa 3 never had (`javascript-script-handler-functions`). `VF-IMP-012` refuses no corpus definition, as the OQ-4 scan found (SC-002 is held by the representative cases in the converter tests).

## Assumptions

- The host provides an injectable clock (`TimeProvider`) that the expression engine draws current time from; tests control time through it.
- By default no .NET interop strategy is installed (ADR 0046 V10); this spec ships only that default.
- The Elsa 3 converter feature carries the date interpretation setting (FR-034) and records it on each converted workflow; this spec defines it and applies it to scripts; S-9 (spec 1207) applies the same setting to date values at the JSON to .NET boundary.
- `RunJavaScript` scripts and input bindings share one evaluation surface and one capability profile.
- The host runtime resolves IANA time zone IDs (on Windows through ICU, as .NET 10 does by default).
- The Elsa 3 corpus figures cited are indicative of migration impact; exact faithful-conversion coverage is validated during implementation against the available corpus.
