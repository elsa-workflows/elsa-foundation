# Optional administration contract

Public namespace Elsa.Studio.Hosting.Nuplane:

- StudioNuplaneOptions.ManagementFile defaults to nuplane-management.json.
- StudioNuplaneOptions.DirectoryFeedName defaults unset; selects the only directory feed or registered packages fallback.
- StudioBuilder.AddElsaStudioNuplane(Action<StudioNuplaneOptions>? configure=null, Action<NuplaneBuilder>? configureNuplane=null): StudioBuilder.
- IEndpointRouteBuilder.MapElsaStudioNuplane(): IEndpointRouteBuilder.

Bind Studio:Hosting:Nuplane before callbacks; relative paths use ContentRoot. No duplicate registration/after-Build
composition. Caller NuplaneBuilder configures trusted roots/participation/roster with existing infrastructure APIs.
Root-owned services and existing sharing/discovery remain authoritative. No optional dependency leaks into Hosting.

Existing routes remain /_elsa/module-management/{registry,packages/upload,packages/drop-folder/{fileName},reconcile,
feeds,feeds/{name},retention-policy,prune}, /modularity/features and /modularity/features/apply, plus package
/_content/{packageId}/{**assetPath}. Reads/mutations select the existing Api read/manage policies.

Feature catalog/apply use shared Modularity.Core wire models. Invalid/duplicate/incomplete-state requests are400; mandatory/dependency activation refusals and stale revisions are409,
unsupported ownership is read-only, and save/refresh/reload failures explicit. Do not claim a
reload failure rolled back an already committed file. Unchanged secret placeholders preserve stored values.

Retention selects nullable keepNewestInactiveVersionsPerPackage in a Studio-owned policy section; explicit
selection supersedes a legacy-policy refusal. Null retains all. Prune defaults Preview; Execute requires observed
root/epoch/policy confirmation and complete known protection. Never enroll automatically, manufacture current
confirmation, accept arbitrary paths, or call local Directory.Delete. Return actual refusal/partial/cancelled facts.

Package bytes use stock immutable extracted installs under admitted ownership. Fresh in-admission preflight caps
assets at16MiB/metadata1MiB, with at most4 active allocated asset responses and32 total active/queued asset requests. Keep the permit through response
completion and ownership through detachment. Unknown/busy/unsupported authority and shapes refuse safely; no
ordinary-reader fallback. Preserve no-cache, MIME, conditional and Range behavior. Arbitrary external mutation
cannot be converted into an identity-bound allocation guarantee by this adapter.
