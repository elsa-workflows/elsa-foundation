# Foundation server image profiles

A profile runs `Elsa.Foundation.Host.dll`. `packages.txt` selects the NuGet packages available to the process. `shells.json` selects the CShells features active in each shell. Several deployments can use the same image digest with different shell files.

| Profile | Intended use |
| --- | --- |
| [Essentials](essentials/shells.json) | Design, publish and execute a workflow with a small activity set, local identity and SQLite. The runtime API works with HTTP, cron and timer triggers switched off. The shell keeps the recurring-schedule projection that publishing requires. |
| [Elsa 3 import](elsa3-import/shells.json) | Analyze and import Elsa 3 reusable activity definitions through the migration API. Includes mapping, publishing, persistence and expressions used by the import path. The same image can [run already-converted Elsa 4 files](elsa3-import/migrated-files.md) from a read-only `migrated/` mount. |
| [Full](full/shells.json) | Explore a broader server composition, including import, native BPMN runtime and design, decisions, diagnostics, connections and Studio backend services. XML interchange stays opt-in pending [published activity version support](https://github.com/elsa-workflows/elsa-foundation/issues/5809). The linked shell file is the exact selection. Run a separate compatible Studio for a browser UI. |

## Build and run a profile

Use Docker with Linux containers, Python 3 and the .NET SDK selected by `global.json`. Choose a published `elsaworkflows/elsa-foundation-host` image by digest. Download `package-versions.props` from the `elsa-foundation-nuget-packages` artifact of the Packages run for the **same commit**. The Docker preview tag uses an image build number. Each NuGet package has its own computed version.

From the repository root, set `FOUNDATION_IMAGE` to the digest-pinned image reference and `VERSIONS` to the path to those props. Then run:

```sh
profile=essentials
context="$(mktemp -d)/context"
python3 docker/profiles/prepare.py --profile "docker/profiles/$profile" \
  --foundation-image "$FOUNDATION_IMAGE" --versions "$VERSIONS" --output "$context"
docker build -f docker/profiles/Dockerfile --build-arg FOUNDATION_IMAGE="$FOUNDATION_IMAGE" \
  -t "elsa-server-${profile}:local" "$context"
python3 tools/docker-profiles/smoke.py "elsa-server-${profile}:local" --variant "$profile"
```

The smoke test supplies temporary credentials and starts a fresh container with networking disabled. For Essentials it also starts the same image with a different mounted shell and exercises the authenticated workflow API again.

Keep `packages.lock.json`, `nuplane.lock.json`, the Foundation digest and the source profile together for each released image. To reproduce it, pass `--lock <saved-directory>/packages.lock.json` to `prepare.py` with the same base and version plan. The builder checks the NuGet restore and the generated Nuplane lock. A package update gets a new reviewed lock and a new image. A custom build can use `--nuget-config` for its package sources.

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
  elsa-server-essentials:local
```

This example expects a TLS certificate in `elsa.pfx` readable by the container user and its `ASPNETCORE_Kestrel__Certificates__Default__Password` in `elsa.env`. Clients use HTTPS on port 8443. Port 8080 remains internal for the Docker health check. The local SQLite database is `/app/data/elsa.db`. Persist the [Data Protection key ring](../../docs/docker.md#data-protection-keys) for deployments that retain sign-in sessions across container replacement. Supply deployment secrets independently of the image. On Full, an administrator with permissions from many modules can receive a chunked cookie whose combined `Cookie` header exceeds 8 KiB. Configure any intermediate proxy to accept the complete header. For the shipped `default` shell, the following feature settings use the environment key prefix `CShells__Shells__default__Features__`.

| Feature setting (after prefix) | Value |
| --- | --- |
| `FoundationIdentityAspNetCoreIdentityEntityFrameworkCore__SeedAdminPassword` | Password for the initial `admin` user. |
| `WorkflowsRuntimeEntityFrameworkCore__RecoveryContinuationSigningKey` and `WorkflowsRuntimeEntityFrameworkCore__HierarchyCursorSigningKey` | Separate persistent signing keys, each at least 32 UTF-8 bytes. |
| `WorkflowsRuntimeApi__WorkflowAlterationPayloadProtectionActiveKeyId` | ID of the active payload protection key. |
| `WorkflowsRuntimeApi__WorkflowAlterationPayloadProtectionKeys__<id>` | Base64-encoded 32-byte key for that ID. |
| `Secrets__EncryptionKey` | Supply for shells that enable Secrets, such as Full. |

## Create another profile

Copy a profile directory. Reuse a flat package pack with a line such as `@engine` in `packages.txt`. Each pack lists package roots. Profiles can share a pack while activating different shell features. By default packs are read from `../packs` relative to the profile directory. Pass `--packs-directory` to reuse packs from another workspace. List additional roots on their own lines; use `Package.Id [1.2.3]` for an external package with an exact version. Include packages providing features selected through CShells dependencies as well as optional features operators may enable. The builder restores the transitive dependency graph and stages the archives in a local directory feed. The host enforces `nuplane.lock.json` on startup, with the Nuplane cache initially empty. [Foundation feed and package-closure guidance](../../docs/host-feeds/pinning-and-closures.md)

Edit `shells.json` to choose defaults. To reuse a feature selection, the [composition planner](../../docs/reference/embedded-runtime-profile.md) can expand versioned profiles and groups and report missing required features. Optional packages may be baked into an image with their features disabled. To add a package a deployment does not carry, rebuild a derived profile with a new lock.

## Build in GitHub Actions

The **Docker Images** manual workflow has a **build_profiles** option. It uses the Foundation digest and package versions from that workflow and builds each profile on native Linux AMD64 and ARM64 runners. A successful job exports `image.tar.gz`, the lock files and `shells.json` as a workflow artifact. Download the artifact for your architecture and run `docker load -i image.tar.gz` to import the tested image. To publish the tested images, a maintainer can select **publish_profiles** on `main`. Once all six native builds and smoke tests pass, the workflow publishes one multi-platform tag per profile in `elsaworkflows/elsa-foundation-host`, for example `essentials-4.0.0-preview.<run>.<attempt>`. It promotes the images from the artifacts. The existing compiled Essentials and Workbench images have their own publication paths. Foundation maintainers own the packaged profile definitions and their release inputs here.
