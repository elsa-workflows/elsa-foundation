# Consumer guide (test-pinned claims)

`claims.json` carries the behavioural knowledge that `docs/contracts/` structurally cannot: the rules a
consumer needs but no descriptor field can express. Each claim is **one sentence in a typed envelope**, and
each names the tests that pin it.

This exists because the measured cost of omitting these facts is high and repeatable. Three independent
benchmarked sessions hit the same JavaScript single-expression error and concluded multi-line JavaScript
was impossible; one published a throwaway probe workflow purely to learn how captured content projects
into the script engine. None of that is discoverable from a schema, and all of it is cheap to state.

## The envelope

| Field | Meaning |
|---|---|
| `id` | Stable identifier, `<scope>.<slug>`. Referenced by the pinning tests, so renaming one is a breaking change to the pair. |
| `scope` | Which surface the sentence is about (`expressions.javascript`, `publishing`, …) — filter before you read. |
| `kind` | `constraint` (a rule you must satisfy when authoring, or the server rejects the submission), `behaviour` (what the server does that the shapes do not show), or `representation` (how something is written on the wire or in storage). |
| `stability` | `stable` — a change here is a breaking change. `provisional` — true today and pinned, but under review and may change deliberately. |
| `statement` | The claim. One sentence. |
| `rationale` | Optional: usually the concrete failure the claim prevents. |
| `tests` | Fully-qualified test methods that pin the claim. Never empty. |
| `since` | The contract schema version the claim was first published in. |

## Why you can trust a claim

Two gates run in CI, in both directions, after the test suite:

- **Gate A** — every test a claim names must exist and carry `[ConsumerContract("<id>")]`. A claim whose pin
  was deleted or renamed fails the build.
- **Gate B** — every `[ConsumerContract]` id must appear in `claims.json`. A test pinning an unpublished
  claim fails the build.

The gates check the *link*; CI running the suite is what checks the *assertion*. Together that means a
published behavioural claim is never older than the last green build — which is the whole point, because an
unmaintained prose note is worse than no note at all. A claim that cannot be pinned is therefore not
published: Gate A rejects an empty `tests` list.

Run the gate locally:

```bash
dotnet run --project tools/contracts/Elsa.Contracts.Generator -c Release -- claims
```

## Adding a claim

1. Write (or find) a test that fails if the behaviour changes, and mark it
   `[ConsumerContract("<scope>.<slug>")]` — the attribute lives in `Elsa.ConsumerGuide.Testing`, which is
   dependency-free so carrying a pin never drags a package into an unrelated suite.
2. Add the envelope to `claims.json` naming that test's fully-qualified method.
3. Run the gate. If it passes in both directions, the claim is publishable.

State only what the test proves. A sentence broader than its pin is the failure mode this whole mechanism
exists to prevent.
