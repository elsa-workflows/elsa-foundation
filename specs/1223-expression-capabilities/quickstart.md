# Quickstart & verification inventory: Expression capabilities (Elsa 4) — S-1

The finite set of checks that demonstrate S-1. All automated checks are **xUnit** (no FluentAssertions — constitutionally pinned). This is a provisional spec; these verify the design's behaviour, they are not a production-acceptance claim.

## A. Engine capabilities (`binding-readonly-v1`)

1. **Intrinsics restored** — in both an input binding and a `RunJavaScript` script: `typeof Date`, `typeof Intl`, `typeof Temporal` are not `undefined`; `Math.random` is callable.
2. **Clock source** — with a controlled host `TimeProvider`, `new Date().toISOString()` and `Date.now()` reflect the provided instant in UTC.
3. **Invariant/UTC** — date formatting does not vary with server culture; values are UTC.
4. **Randomness** — two `Math.random()` calls are allowed to differ; no error; no reproducibility assertion.
5. **Surface parity** — every capability in (1)–(4) and every host function behaves identically in an input binding and in a script.

## B. Host functions (JSON in / out)

6. Each of `newGuid`, `newGuidString`, `newShortGuid`, `getGuidString`, `getShortGuid` returns a JSON string.
7. `newShortGuid()` matches the Elsa 3 short-GUID encoding for a known GUID (byte-for-byte).
8. `stringFromBase64(stringToBase64(x)) === x` for representative UTF-8 `x`.
9. `now()` parses as a UTC ISO-8601 instant equal to the controlled clock.
10. `toJson(v)` equals the shared canonical encoder's output for representative values (object, array, string, nested).
11. No host function accepts or returns a live .NET object (every value is JSON).

## C. Conversion — faithful (no refusal)

12. Corpus/representative Elsa 3 expressions using `new Date()`, `Date.now()`, `Math.random()` convert without refusal.
13. Expressions using the GUID family convert without refusal and preserve string shape.
14. Expressions using `stringToBase64`/`stringFromBase64` and `toJson` convert without refusal.
15. **SC-001 measure**: 100% of the available corpus workflows whose JS uses only faithful functions convert without refusal.

## D. Conversion — refused (with guidance)

16. `parseGuid`, byte/stream helpers, `getConfig`/`getConfiguration` are refused with guidance.
17. `Guid`/`DateTime` constructors and static access are refused with guidance.

## E. Proxy-dependent-usage detector (proxies disabled)

18. A PascalCase **method invocation** on a boundary value is refused `VF-IMP-010`; the message quotes the offending expression.
19. A **CLR-type** reference (`DateTime.Now`, `new Guid(...)`, `Convert.*`) is refused `VF-IMP-010`.
20. A PascalCase **property read** (`payload.OrderId`) is **allowed**.
21. Native JS invocations (`s.toUpperCase()`, `a.map(f)`, `JSON.stringify(o)`) and `new Date()` are **allowed**.
22. A computed member invocation (`o[name]()`) is **allowed** (recorded gap).
23. The detector runs at **publish** as well as convert (same refusal on a hand-authored script).
24. **With proxies enabled** (future setting simulated), the same proxy-using script is **not** refused by this detector.
25. **SC-002 measure**: no proxy-/CLR-dependent script reaches run time undetected — each is caught at convert or publish.

## F. Documentation & surfaces

26. `Extensions/Elsa3/src/Mapping/README.md` conversion table updated (refused → converted rows; `VF-IMP-010` added).
27. Spec 247 R-6 notes updated where this spec supersedes them (time/GUID/Base64/`toJson`).
28. Owning feature docs note the `binding-readonly-v1` capabilities.

## G. Governance / provisional surfaces (not code)

29. D-6 no-metadata observation + future attribute path surfaced on ADR 0045 §5's PR (#3384).
30. The two #3390 findings (native intrinsics admitted; Jint RNG not injectable) posted on PR #3390.
31. Draft PR opened against `initiative/3052-elsa3-to-elsa4-conversion`, body "Part of #3052", approval plan Sipke + Frans, both requested; pointer comment on #3052.
