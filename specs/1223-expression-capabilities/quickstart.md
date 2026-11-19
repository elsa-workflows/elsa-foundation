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
9. No `toJson` global exists in Elsa 4. Invalid Base64 fails; invalid UTF-8 gives U+FFFD.
10. No built-in accepts or returns a live .NET object.
11. **Registration** — a duplicate name, an unprefixed host function or a reserved name fails startup with the function's name; `const newGuid = 1` gets the publish warning `Scripts/ShadowedFunction`; `const variables = 1` gets the publish error `Scripts/ReservedName`, `const workflow = 1` the warning `Scripts/ShadowedFunction`, and a converted script that declares its own `args` publishes as spec 1222 defines; a host function `acme.lookup` is callable as `acme.lookup(x)`; calling a built-in adds no fallback read marker (`ReadsVariables = false`).
11a. **No effect metadata** — the registration has no effectful option; calling a built-in adds no commit.
11b. **Precision** — a number argument carrying the value model's loss information passed to a host function logs a warning naming the function and the argument path; the call returns.

## C. Conversion — faithful (no refusal)

12. Zone-independent `Date` usage (`Date.now()`, `getTime()`, `toISOString()`) and `Math.random()` convert without refusal under every date interpretation.
12a. Zone-dependent usage (`getHours()`, `new Date('2026-10-28T09:15:00')`): under `Refuse` (default) it is refused and the message names both fixes (`SourceTimeZone` with the Elsa 3 server's IANA zone, `AssumeUtc`); under `AssumeUtc` it converts, evaluates in UTC and carries a behaviour warning; under `SourceTimeZone` = `Europe/Amsterdam` the converted code names the zone explicitly and, on a UTC engine, gives what Elsa 3 gave on a server in that zone (winter and summer dates), with no warning; local `toString()` on a `Date` is refused.
12b. A non-literal parse (`new Date(input)`) behaves as zone-dependent usage under each value; `new Date('2026-10-28')` converts under every value.
12f. The date interpretation is recorded on each converted workflow and in its conversion contract; S-9 applies the same setting at the JSON to .NET boundary.
12c. `variables.due.getTime()` and `variables.due < new Date()` with `due: DateTime` in Elsa 3 convert and equal Elsa 3 (FR-032).
12d. `n.toLocaleString()`, `a.localeCompare(b)` and `new Intl.NumberFormat()` without a locale are refused; with `'nl-NL'` they convert (FR-033).
12e. `toLocaleString()` on a number is not treated as zone-dependent; `toString()` on a `Date` is.
13. The GUID functions and `parseGuid` convert without refusal; `'id-' + newGuid()`, `newGuid().toString()` and `toJson(newGuid())` (as `JSON.stringify(newGuid())`) convert.
14. `stringToBase64`/`stringFromBase64` convert without refusal; `toJson(x)` is rewritten to `JSON.stringify(x)` with a behaviour warning that quotes it and names the differences.
15. **SC-001 measure**: 100% of the available corpus workflows whose JS uses only faithful functions and zone-independent `Date` usage convert without refusal; under `AssumeUtc` or `SourceTimeZone`, also those with zone-dependent usage. **SC-007**: under `Refuse`, none with zone-dependent usage converts.

## D. Conversion — refused (with guidance)

16. Byte/stream helpers, `getConfig`/`getConfiguration` and `crypto.randomUUID` are refused with guidance.
17. `Guid`/`DateTime`/`Random` globals are refused with `VF-IMP-005` as today, with reworded guidance (no "no clock" text).

## E. .NET-usage analysis

18. Conversion: `newGuid().toString('N')`, `const g = parseGuid(s); g.toByteArray()`, `variables.upload.openReadStream()` and `toJson(variables.upload)` (with `upload: FormFile` in Elsa 3) are refused `VF-IMP-012`; the message quotes the offending expression.
19. A .NET type reference (`TimeSpan.FromHours(1)`, `new Uri(s)`) is refused at conversion (`VF-IMP-012`) and at publish (`Scripts/DotNetMemberUsage`).
20. A PascalCase invocation on a value the script did not define (`a.Where(f)`) is refused at conversion and publish.
20a. On `items: List<string>` in Elsa 3, `variables.items.map(f)` is allowed and `variables.items.Add(x)` refused; `const Path = {…}; Path.join(a, b)` and `acme.Lookup(x)` are allowed.
21. PascalCase property reads (`payload.OrderId`, `variables.Order.Status`), native calls (`s.toUpperCase()`, `a.map(f)`, `JSON.stringify(o)`, `new Date()`, `Intl.DateTimeFormat('en-GB')`) are **allowed**.
22. A computed member invocation (`o[name]()`) is **allowed** (recorded gap).
22a. An invocation of a member the script defines (`helpers.Format(x)`) is **allowed**.
23. The analyzer is one component used by both the converter and publish validation, registered as part of the default JavaScript strategy (no setting).
24. **SC-002 measure**: every corpus and representative case of member use on a known .NET-object value or a .NET type reference is refused at conversion or publish.

## F. Documentation & surfaces

26. `Extensions/Elsa3/src/Mapping/README.md` conversion table updated (refused → converted rows; `VF-IMP-012` added).
27. Spec 247 R-6 rows updated where this spec supersedes them.
28. JS feature docs note the `binding-readonly-v1` capabilities, the built-ins and the registration rules; `EXTENSION_POINTS.md` lists the host-function registration.
