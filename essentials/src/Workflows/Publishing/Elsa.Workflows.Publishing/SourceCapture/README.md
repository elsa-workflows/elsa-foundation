# Native published-root capture

`WorkflowsPublishingReleaseCaptureFeature` registers the native `IReleaseCaptureSource` strategy.
It requires the shared Capture and Runtime source closure features and one explicit native source
context and export policy. The policy contracts live in Publishing.Core. It registers no compiler,
permissive authorization policy, catalog contributor, import or activation authority.

The exact selected authored version is copied through its qualified Design snapshot port. A canonical
activation source reference must agree with an actually activated Active/Retired journal association.
Draft/test references, candidates and timestamp-only legacy references do not qualify. Legacy adoption
is an owner operation; a missing journal is never replaced by a label. An imported deployment source
requires its later owning provenance adapter and is refused by this initial profile.
Any timestamp-only legacy Published reference on the selected version refuses this profile, including
a mixed history that also has a canonical reference. An owner-qualified adoption must resolve it first.

The selected definition/version labels remain separate from the stored immutable executable's original
full producer pin. Equal behavior published by different definitions never rewrites that pin. Runtime
closure leases remain held through the complete awaited callback, with export authorization before reads.

This partial source seam selects roots. Full reusable catalog acquisition and provider/module inspection,
owner import/staging and complete release journeys remain separate delivery work.
