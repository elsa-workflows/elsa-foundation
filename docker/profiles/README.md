# Foundation server image profiles

A profile runs `Elsa.Foundation.Host.dll`. `packages.txt` selects the NuGet packages available to the process. `shells.json` selects the CShells features active in each shell. Several deployments can use the same image digest with different shell files.

| Profile | Intended use |
| --- | --- |
| [Essentials](essentials/shells.json) | Design, publish and execute a workflow with a small activity set, local identity and SQLite. The runtime API works with HTTP, cron and timer triggers switched off. The shell keeps the recurring-schedule projection that publishing requires. |
| [Elsa 3 import](elsa3-import/shells.json) | Analyze and import Elsa 3 reusable activity definitions through the migration API. Includes mapping, publishing, persistence and expressions used by the import path. The same image can [run already-converted Elsa 4 files](elsa3-import/migrated-files.md) from a read-only `migrated/` mount. |
| [Full](full/shells.json) | Explore a broader server composition, including import, native BPMN runtime and design, decisions, diagnostics, connections and Studio backend services. XML interchange stays opt-in pending [published activity version support](https://github.com/elsa-workflows/elsa-foundation/issues/5809). The linked shell file is the exact selection. Run a separate compatible Studio for a browser UI. |

## Use a released profile

Select a published profile and set `ELSA_VERSION` to its release tag suffix, such as `4.0.0-preview.1234.1`. The same version identifies the paired images `elsaworkflows/elsa-profile-builder` and `elsaworkflows/elsa-foundation-host`. Each Elsa NuGet package has its own version, selected by the builder's release plan.

```sh
image="elsaworkflows/elsa-foundation-host:essentials-${ELSA_VERSION}"
docker pull "$image"
```

Use that image with the deployment settings below. The `elsa3-import` and `full` profiles use the same tag pattern.

## Change only the shell defaults

Mount an edited `shells.json` when deploying a released profile, or bake it into a small derived image. Replace the example tag with your selected release and put `shells.json` beside this Dockerfile.

```dockerfile
FROM elsaworkflows/elsa-foundation-host:essentials-4.0.0-preview.1234.1
COPY shells.json /app/shells.json
```

Build from that directory. The released image already carries the locked packages. Use the standalone build below when you need to change the package set.

```sh
image=elsa-server-essentials:local
docker build -t "$image" .
```

## Author a standalone profile

Start an empty directory and copy `Dockerfile`, `packages.txt` and `shells.json` from one of the profile directories or its release artifact. The directory is the complete build context. Docker with BuildKit and Linux containers is sufficient. The builder image carries the .NET SDK, package version plan, feeds and built-in package packs.

- Edit `packages.txt` to select what the image carries. Lines such as `@engine`, `@http-trigger` and `@sqlite` select built-in groups of package roots. List a first-party package by ID to use the release plan. For example, an `Elsa.Expressions.Liquid` line selects that package at the matching release version. Give an external package an exact pin using `Package.Id [1.2.3]`. List your CLR activity package explicitly so the builder includes its assembly in the activity reconciliation folder. The builder restores the dependencies of the listed packages.
- Edit `shells.json` to select active features and their settings. A package can remain available while its features are disabled. Keep the features and package dependencies the shell needs to activate. Do not include deployment credentials in the profile directory.

### Generate and review locks

From the customer profile directory, set `ELSA_VERSION` to the paired release version and export a candidate lock set into a separate directory. The `locks` target resolves packages and exports the lock files without producing a runtime image.

```sh
candidate=$(mktemp -d)
docker buildx build --target locks --output "type=local,dest=$candidate" \
  --build-arg "ELSA_VERSION=$ELSA_VERSION" .
```

Review `packages.lock.json` for the resolved NuGet versions and dependency changes. Review `nuplane.lock.json` for the exact archives and SHA-512 hashes staged in the image. For an update, compare each candidate file against the current lock before replacing it. Copy both reviewed files into the profile directory and keep them with `packages.txt`, `shells.json` and the Dockerfile.

```sh
cp "$candidate/packages.lock.json" "$candidate/nuplane.lock.json" .
```

The default Docker target requires both locks and fails if either is missing or the locked package closure differs. Build or replay it with the same profile files, paired release and platform.

```sh
image=elsa-server-essentials:local
docker build --build-arg "ELSA_VERSION=$ELSA_VERSION" -t "$image" .
```

If you have both reviewed lock files from a release artifact, you can go straight to this build. Changes to `shells.json` affect only the final layer and leave the package lock set valid. For a package update, change the roots or paired release, generate and review both locks, then build again.

The build generates `/app/profile-desired.json` from the verified Nuplane lock. At startup, Nuplane reads the exact package IDs, versions and feed names from that manifest and verifies the baked archives in `/app/profile-packages` against the strict lock. Startup runs offline and skips directory-based discovery of desired packages.

The build defaults to the local Linux architecture. For a different target, use the same `--platform linux/amd64` or `--platform linux/arm64` on both builds and make sure your BuildKit installation can run that platform. For release replay, use the artifact's exact `PROFILE_BUILDER_IMAGE` and `FOUNDATION_IMAGE` references as build arguments. The runtime stage checks that the Foundation files match those used to resolve the profile.

To define your own named packs, put the package lists in `packs/`, add `COPY packs/ /profile/packs/` to the `profile-builder` stage, and give both `assemble` commands `--packs-directory /profile/packs`. Keep the packs the manifest references in that directory. The standard Dockerfile uses the packs supplied by the builder image.

For private feeds, supply a NuGet configuration that covers the whole package graph, including Elsa's feeds. In both `RUN elsa-profile assemble` instructions, add `--mount=type=secret,id=nuget,required=true` immediately after `RUN` and pass `--nuget-config /run/secrets/nuget` to the assembler. Add `--secret id=nuget,src=/path/to/NuGet.config` to both Docker build commands. BuildKit mounts the file only during the restore step.

## Configure a deployment

Copy the profile's `shells.json` from source or from `/app/shells.json` in the built image. Edit the copy, then mount it at the **same path**. The mount replaces the complete file, including its feature lists. Omit the mount to use the shipped defaults. Environment variables override individual settings; command-line settings take precedence. Restart the container to apply a revised mount.

```sh
docker run --rm --name elsa -p 127.0.0.1:8443:8443 \
  --env-file ./elsa.env \
  -e 'ASPNETCORE_URLS=http://+:8080;https://+:8443' \
  -e ASPNETCORE_Kestrel__Certificates__Default__Path=/run/secrets/elsa.pfx \
  --mount "type=bind,source=$PWD/elsa.pfx,target=/run/secrets/elsa.pfx,readonly" \
  --mount type=volume,source=elsa-data,target=/app/data \
  --mount "type=bind,source=$PWD/shells.json,target=/app/shells.json,readonly" \
  "$image"
```

This example expects a TLS certificate in `elsa.pfx` readable by the container user and its `ASPNETCORE_Kestrel__Certificates__Default__Password` in `elsa.env`. Clients use HTTPS on port 8443. Port 8080 remains internal for the Docker health check. The local SQLite database is `/app/data/elsa.db`. Persist the [Data Protection key ring](../../docs/docker.md#data-protection-keys) for deployments that retain sign-in sessions across container replacement. Supply deployment secrets independently of the image. On Full, an administrator with permissions from many modules can receive a chunked cookie whose combined `Cookie` header exceeds 8 KiB. Configure any intermediate proxy to accept the complete header. For the shipped `default` shell, the following feature settings use the environment key prefix `CShells__Shells__default__Features__`.

| Feature setting (after prefix) | Value |
| --- | --- |
| `FoundationIdentityAspNetCoreIdentityEntityFrameworkCore__SeedAdminPassword` | Password for the initial `admin` user. |
| `WorkflowsRuntimeEntityFrameworkCore__RecoveryContinuationSigningKey` and `WorkflowsRuntimeEntityFrameworkCore__HierarchyCursorSigningKey` | Separate persistent signing keys, each at least 32 UTF-8 bytes. |
| `WorkflowsRuntimeApi__WorkflowAlterationPayloadProtectionActiveKeyId` | ID of the active payload protection key. |
| `WorkflowsRuntimeApi__WorkflowAlterationPayloadProtectionKeys__<id>` | Base64-encoded 32-byte key for that ID. |
| `Secrets__EncryptionKey` | Supply for shells that enable Secrets, such as Full. |

## Build in GitHub Actions

Use **build_profiles** on a manual **Docker Images** dispatch to build only the profile builder and profiles. The workflow publishes a run-specific Foundation base tag for the build. It leaves `latest` and the existing Workbench and compiled Essentials publication paths alone. It generates one lock set per profile for the native Linux AMD64 and ARM64 builds. Each image runs the profile smoke with networking disabled and an empty Nuplane cache before export. Essentials also runs with mounted shells that enable HTTP and scheduling separately.

Artifacts are retained for seven days. Download the artifact for your architecture and run `docker load -i image.tar.gz` to import the tested runtime image. The profile files and both locks are the build context for replay. To replay a build-only run, load the native builder artifact's `image.tar.gz` too. Use the `builderImage`, `foundationImage` and `platform` values in the profile artifact's `build.json` as the `PROFILE_BUILDER_IMAGE` and `FOUNDATION_IMAGE` build arguments and the `--platform` option.

To publish the tested images, a maintainer can select **publish_profiles** on `main`. After all six native builds and smoke tests pass, the workflow stages the tested archives by architecture. It publishes the matching Foundation and builder tags before exposing multi-platform profile tags such as `essentials-4.0.0-preview.<run>.<attempt>`. Foundation maintainers own the packaged profile definitions and their release inputs here.
