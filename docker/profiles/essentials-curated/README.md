# Curated Essentials server profile

This profile carries the features of the existing `elsaworkflows/elsa-essentials-host` image. It starts
`Elsa.Foundation.Host.dll` with a curated NuGet package set, the shared OpenIddict token store, and module
management. The shell file stays mountable at `/app/shells.json`. Development and Production keep their
standard configuration overlays.

The Docker Images workflow qualifies the architecture-specific build before publishing a candidate under
`elsaworkflows/elsa-essentials-host:profile-$ELSA_VERSION`. Supply the same environment variables, signing keys,
mounts and database settings as the existing Essentials host. In Production, prepare module schemas before
starting the process with its default Validate policy. The default SQLite paths are relative to `/app`.
If you mount a volume at `/app/data`, set `ConnectionStrings__Elsa` and
`CShells__Shells__default__Features__FoundationIdentityOpenIddict__ConnectionString` to database paths on that
volume. Use the same connection settings for the migration CLI and the running containers.

For Production, supply the existing recovery continuation signing key under
`CShells__Shells__default__Features__WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence__RecoveryContinuationSigningKey`.
Move the value from `WorkflowsRuntimeEntityFrameworkCore__RecoveryContinuationSigningKey` when updating an
existing Curated deployment. Use at least 32 UTF-8 bytes and the same value on every node. The other Docker profiles
have their own [feature settings](../README.md#configure-a-deployment).

## Workflow execution

The default shell selects `Foundation: BuiltIn` for Native and Flowchart workflows. `Provider` selects the
database used by that foundation. Its dedicated
SQLite file is `/app/data/elsa-kernel.db`. Mount a writable volume at `/app/data` to keep workflow
execution state across container replacement. Run one process per SQLite file. The existing module
connections still hold workflow definitions, published executables, identity, and other module data.

To use a dedicated PostgreSQL database for workflow execution, set these feature values under the
`CShells__Shells__default__Features__WorkflowsRuntimeKernelHosting__` environment variable prefix.

| Setting | Value |
| --- | --- |
| `Provider` | `PostgreSQL` |
| `SqlitePath` | Empty |
| `PostgresConnectionString` | Connection string supplied through your deployment configuration |

The default shell delivers standalone Event, HTTP, Timer and Cron through the prepared Runtime store and
the same Actor Foundation execution host. Its installed publisher authority pins the configured tenant and
handoff mode. Install a fresh dedicated Actor Foundation database when moving
to the new store format during the preview. An older Actor Foundation database is refused before its data
or schema changes. Keep the original volume or PostgreSQL database with the earlier matching image for a
rollback. Workflow definitions, publication and identity keep their existing module connections.

Pause standalone sources and settle any pending deliveries with the previous Runtime owner before
switching a reused workflow-control database. Use its [delivery inspection and recovery controls](../../../docs/standalone-triggers.md)
to drain accepted work or cancel eligible pending recipients. Use the earlier Runtime owner for history
and replay of its standalone receipts. If the old queue still has work, keep it with the earlier owner.
To run the new preview separately, use a fresh workflow-control database, then publish the workflows and
bind their standalone resources in that deployment. Keep the matching Runtime database or rollback
snapshot with each Actor Foundation database.

## Build

To build locally, copy this directory, select the matching released Foundation and builder version, and follow
the [lock and build steps](../README.md#generate-and-review-locks). Keep the `settings` directory and both shell
files in the build context. `settings/appsettings.json` and `settings/appsettings.Production.json` layer onto the
Foundation settings at image build time. The runtime and migration CLI both see them in the standard files.

Nuplane admits each curated archive using its version and SHA-512 lock. The writable watched `/app/packages`
directory accepts separately selected add-ons. The baked cache supplies the curated manifest.
