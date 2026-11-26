# Curated Essentials server profile

This profile carries the features of the existing `elsaworkflows/elsa-essentials-host` image. It starts
`Elsa.Foundation.Host.dll` with a curated NuGet package set, the shared OpenIddict token store, and module
management. The shell file stays mountable at `/app/shells.json`. Development and Production keep their
standard configuration overlays.

The Docker Images workflow qualifies the architecture-specific build before publishing a candidate under
`elsaworkflows/elsa-essentials-host:profile-$ELSA_VERSION`. Supply the same environment variables, signing keys,
mounts and database settings as the existing Essentials host. In Production, prepare module schemas before
starting the process with its default Validate policy.

To build locally, copy this directory, select the matching released Foundation and builder version, and follow
the [lock and build steps](../README.md#generate-and-review-locks). Keep the `settings` directory and both shell
files in the build context. `settings/appsettings.json` and `settings/appsettings.Production.json` layer onto the
Foundation settings at image build time. The runtime and migration CLI both see them in the standard files.

Nuplane admits each curated archive using its version and SHA-512 lock. The writable watched `/app/packages`
directory accepts separately selected add-ons. The baked cache supplies the curated manifest.
