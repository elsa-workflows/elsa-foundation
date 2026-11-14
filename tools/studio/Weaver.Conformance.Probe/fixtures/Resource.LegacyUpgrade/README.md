This is the independent, Weaver 1.0-compatible T038 resource author. Its source is
compiled separately against the pinned old distribution and the current product
archives. `upgrade_distribution.py` copies the existing `WidgetExecutionJournal`
source verbatim into each author before packing. The ordinary nine-tool Acme.Home
inventory is unchanged.

The opt-in feature is `AcmeWeaverUpgradeResource`, package
`Acme.Weaver.UpgradeResource`, module `Acme.Upgrade`. Tools are
`acme_upgrade_read_widget`, `acme_upgrade_set_visibility`, and
`acme_upgrade_admin_visibility`; capabilities are `acme.upgrade.read`,
`acme.upgrade.write`, and `acme.upgrade.admin`. The write and Admin operations
change only visibility of the existing `acme.home:status` card through the actual
personal Home preference service and its revision CAS. The old engine owns
preparation, digest, deadline, approval and durable persistence. No new privilege
member or synthetic class set is written by this fixture.

Runtime inputs are a fixed `StudioHostId`, a private preprovisioned
`ExecutionObservationFile`, and optional `OwnerAvailable` (default true). The
authenticated owning read is `/_qualification/acme/upgrade-widget`; execution
observations are `/_qualification/acme/upgrade-widget/execution-entries`. The
journal counts entry to the tool, not successful owner changes. Missing or failed
journals cannot assert zero dispatch. A failure or cancellation resolving the
actual entry owner invalidates the observation persistently instead of leaving
a valid zero. The separate `Resource.LegacyUpgrade.Tests` package-consumer
fixture authors twelve controls for missing/unauthenticated ownership,
cancellation and preservation of an already invalidated failure; all are UNRUN.

The root-owned `UpgradeRecordInspection.cs.in` supplies
`UpgradeRecordInspection.MapEndpoints(endpoints)`. It reuses
`UpgradeOwner.AuthorizeAsync(null, null, false, ct)` for the current native
actor/tenant and read permission, then verifies the session's real owner columns
before loading protected content. The inspection/row supplementation remains
separate from native proposal creation and execution.

This source checkpoint depends on the root runtime publish-manifest helpers and
the root-owned inspection fixture. All new helper controls, author compilation,
old/current package production, native restarts and upgrade journeys are UNRUN.
Source alone establishes neither actual-writer evidence nor qualification.

Historical source acquisition supports a shallow CI checkout: only a missing
exact pinned commit is fetched from origin, with a sixty-second timeout, no tags,
no FETCH_HEAD write and no branch movement. Commit/tree and current driver
cleanliness checks still apply. Published dependency proof checks every selected
managed/native/runtime-target asset against the retained archive hash, nuspec
identity and unique exact ZIP entry before comparing cache and publish bytes.
