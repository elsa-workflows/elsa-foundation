# Quickstart & verification inventory: Expression capabilities (Elsa 4) — S-1

The finite set of checks that demonstrate S-1. All automated checks are **xUnit** (no FluentAssertions — constitutionally pinned). These verify the design's behaviour; they are not a production-acceptance claim.

## A. Engine capabilities (`binding-readonly-v1`)

1. **Intrinsics restored** — in both an input binding and a `RunJavaScript` script: `typeof Date`, `typeof Intl`, `typeof Temporal` are not `undefined`; `Math.random` is callable.
2. **Clock source** — with a controlled host `TimeProvider`, `new Date().toISOString()`, `Date.now()` and `Temporal.Now.instant()` reflect the provided instant in UTC.
3. **Invariant/UTC** — date formatting does not vary with server culture or server time zone.
4. **Randomness** — two `Math.random()` calls are allowed to differ; no error; no reproducibility assertion.
5. **Surface parity** — every capability in (1)–(4) and every built-in function behaves identically in an input binding and in a script.
5a. **Date helpers** — `Temporal.Instant.from('2026-01-15T08:15:00Z').toZonedDateTimeISO('Europe/Amsterdam').hour === 9`; the July equivalent gives `10`; `Intl.DateTimeFormat('en-GB', { timeZone: 'Europe/Amsterdam', hour: 'numeric' })` agrees.
5b. **Profile** — expressions carry `binding-readonly-v1`; no reader accepts `binding-pure-v1`; regenerated fixtures and golden hashes pass.

## B. Built-in functions (JSON in / out)

6. `newGuid`, `newGuidString`, `getGuidString` return a 36-character GUID string; `parseGuid('{6F9619FF-8B86-D011-B42D-00CF4FC964FF}')` returns `6f9619ff-8b86-d011-b42d-00cf4fc964ff`; invalid text fails the evaluation.
7. `newShortGuid` / `getShortGuid` for a fixed GUID equal Elsa 3's algorithm output (standard Base64 of `ToByteArray()` with `/`, `+`, `=` removed).
8. `stringFromBase64(stringToBase64(x)) === x` for representative UTF-8 `x`, and `stringToBase64` matches Elsa 3 for the same `x`.
9. `toJson` equals Elsa 3 3.5.3's output for representative values, including HTML-sensitive characters (`<` → `<`) and non-ASCII letters (kept).
10. No built-in accepts or returns a live .NET object.
11. **Registration** — a duplicate name, an unprefixed host function or a reserved name fails startup with the function's name; `const newGuid = 1` in a script gets the publish warning `Scripts/ReservedName`; calling a built-in adds no fallback read marker (`ReadsVariables = false`).
11a. **Effect declaration** — a function registered with `Effectful` reports it, one without reports effect-free; a call runs exactly as often either way.
11b. **Precision** — a number argument beyond 2^53 − 1 passed to a host function logs a warning naming the function and the argument path; the call returns.

## C. Conversion — faithful (no refusal)

12. Zone-independent `Date` usage (`Date.now()`, `getTime()`, `toISOString()`) and `Math.random()` convert without refusal, with or without a source time zone.
12a. Zone-dependent usage (`getHours()`, `toLocaleString()`, `new Date('2026-10-28T09:15:00')`) is refused without a source time zone; with `Europe/Amsterdam` it converts and evaluates as Elsa 3 did on a server in that zone (winter and summer dates); with source time zone `UTC` (assume-UTC) it converts and evaluates in UTC.
12b. A non-literal parse (`new Date(input)`) converts with a behaviour warning without a source time zone, and without a warning with one.
13. The GUID functions and `parseGuid` convert without refusal; `'id-' + newGuid()`, `newGuid().toString()` and `toJson(newGuid())` convert.
14. `stringToBase64`/`stringFromBase64` and `toJson` convert without refusal.
15. **SC-001 measure**: 100% of the available corpus workflows whose JS uses only faithful functions and zone-independent `Date` usage convert without refusal; with a source time zone set, also those with zone-dependent usage. **SC-007**: none with zone-dependent usage converts without a source time zone.

## D. Conversion — refused (with guidance)

16. Byte/stream helpers, `getConfig`/`getConfiguration` and `crypto.randomUUID` are refused with guidance.
17. `Guid`/`DateTime`/`Random` globals are refused with `VF-IMP-005` as today, with reworded guidance (no "no clock" text).

## E. .NET-usage analysis

18. Conversion: `newGuid().toString('N')`, `const g = parseGuid(s); g.toByteArray()`, `variables.upload.openReadStream()` and `toJson(variables.upload)` (with `upload: FormFile` in Elsa 3) are refused `VF-IMP-012`; the message quotes the offending expression.
19. A .NET type reference (`TimeSpan.FromHours(1)`, `new Uri(s)`) is refused at conversion (`VF-IMP-012`) and at publish (`Scripts/DotNetMemberUsage`).
20. A PascalCase invocation on a value the script did not define (`a.Where(f)`) is refused at conversion and publish.
21. PascalCase property reads (`payload.OrderId`, `variables.Order.Status`), native calls (`s.toUpperCase()`, `a.map(f)`, `JSON.stringify(o)`, `new Date()`, `Intl.DateTimeFormat('en-GB')`) are **allowed**.
22. A computed member invocation (`o[name]()`) is **allowed** (recorded gap).
22a. An invocation of a member the script defines (`helpers.Format(x)`) is **allowed**.
23. The analyzer is one component used by both the converter and publish validation, registered as part of the default JavaScript strategy (no setting).
24. **SC-002 measure**: every corpus and representative case of member use on a known .NET-object value or a .NET type reference is refused at conversion or publish.

## F. Documentation & surfaces

26. `Extensions/Elsa3/src/Mapping/README.md` conversion table updated (refused → converted rows; `VF-IMP-012` added).
27. Spec 247 R-6 rows updated where this spec supersedes them.
28. JS feature docs note the `binding-readonly-v1` capabilities, the built-ins and the registration rules; `EXTENSION_POINTS.md` lists the host-function registration.
