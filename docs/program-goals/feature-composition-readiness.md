# Feature Composition Readiness

Status: active.

Area: feature composition / CShells and Nuplane shell readiness.

Steward(s): Joey plus active architects/agents.

## Purpose

Advance feature composition without letting the CShells Appsettings Generator guess feature IDs, required activations, settings, secrets, host-loading shape, or dependency policy.

This bucket keeps Feature Composition Explorer work separate from generator implementation until a bounded feature slice has enough classified evidence.

## In Scope

- Feature Composition Explorer readiness.
- Bounded feature dependency classification passes.
- Bounded settings classification passes.
- Generator readiness checks for selected slices.
- Host-loading and assembly-scanning output-shape decisions where they affect composition.
- External package/version compatibility evidence for selected feature sets.

## Out Of Scope

- Implementing the CShells Appsettings Generator before required activations, settings, secrets, and host-loading are classified.
- Treating `src/Apps/Elsa.Server` as canonical shell composition policy.
- Broad runtime execution design.
- Broad constitution ratification unrelated to composition/configuration.

## Active Objectives

1. Use the Feature Composition Explorer before the CShells Appsettings Generator.
2. Apply reviewed dependency/settings labels only to bounded feature slices backed by evidence or architecture review.
3. Leave unknown, disputed, or merely inferred activations/settings pending review.
4. Run a generator readiness pass before any generator implementation.

## Linked Surfaces

- [CShells composition evidence](../reports/cshells-composition-evidence.md)
- [Feature dependency map](../maps/feature-dependency-map.md)
- [Feature map](../maps/feature-map.md)
- [Package map](../maps/package-map.md)
- [Skills catalog](../skills/catalog.md)
- [Unfinished work](../reports/unfinished-work.md)

## Current Roadmap Notes

- The Feature Composition Explorer is closer than generator implementation.
- Start with one bounded shell goal or domain cluster.
- Before using generated maps as strong evidence, establish freshness with `dotnet run --project tools/maps/Elsa.Maps.Generator -- check`. If it is red or you cannot run it, refresh the relevant map first and review generated findings before continuing. See the [maps index](../maps/README.md#freshness).

## Drift / Review Notes

- Composition readiness should not pull the repo back into broad operating-model cleanup.
- If classification language becomes stable architecture vocabulary, revisit glossary or constitution placement through Source-of-Truth Audit.

## Removal or Completion Conditions

Complete or pause this bucket when a bounded feature slice is classified enough for reliable exploration or generator readiness, or when the generator work moves into its own implementation spec.
