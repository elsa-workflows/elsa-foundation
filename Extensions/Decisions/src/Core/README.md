# Elsa.Decisions.Core

Immutable decision artifacts, exact tagged scalar values, safe diagnostics and compile/evaluate ports. This package takes no evaluator, workflow, EF, ASP.NET, identity or Studio dependency.

`IDecisionCompiler` and `IDecisionEvaluator` are single replacement contracts. Compilation does not publish or persist. Evaluation consumes a frozen artifact and copied caller inputs, returning actual outcome/trace without external business actions. `DecisionPermissions` supplies the domain grant IDs; the direct application owns authorization and retention.

`DecisionArtifact` defensively copies executable bytes. A constructor or JSON deserialization does not establish trust: the evaluator verifies profile/semantics, digest, canonical executable content and bounds on every supplied artifact. Publication provenance is established by the owning application, never by accepting an arbitrary SourceHash.

Use exact tagged numbers with invariant decimal text, including values above 2^53; do not pass numbers through binary floating point. Scalar kinds are `number`, `string`, `boolean`, `null`. Boolean values use lowercase text, and null has a null value. Compound results contain these same tagged scalars.

Canonical profile: [DMN contract](../../../../specs/1195-business-decisions/contracts/dmn-profile.md). Terms: [framework glossary](../../../../docs/glossary/root.md).
