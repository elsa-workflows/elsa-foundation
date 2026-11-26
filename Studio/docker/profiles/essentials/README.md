# Studio Essentials image profile

This profile derives from the Studio Foundation image. It applies the Studio Essentials settings and shell composition, and bakes the seven curated Studio modules as NuGet archives.

Use the Studio package build artifact and Foundation image from the same version. From the repository root, generate a standalone Docker build context and build the image.

```sh
version=4.0.0-preview.1234.1
context=$(mktemp -d)/essentials
python3 Studio/docker/profiles/essentials/prepare.py /path/to/studio/nuget \
  --version "$version" --output "$context"
docker build --build-arg "STUDIO_VERSION=$version" -t elsa-studio-essentials:local "$context"
```

For a local Foundation build, pass its exact `FOUNDATION_IMAGE` reference as a build argument. Build and pack the listed Studio projects using the same Studio version and the generated frontend assets. After preparing the context, Docker needs only that context and the Foundation base image.

The root-owned `/studio-profile` holds the manifest, lock and baked archives. Nuplane resolves the seven exact versions from this cache-only feed and checks each archive against its locked SHA-512. The watched `/app/packages` feed still takes add-ons such as ETL through the Studio Modules page or a directory mount. Nuplane verifies the curated lock entries while allowing packages outside that lock.

## Qualify on GitHub Actions

Dispatch **Studio Packages (consolidated gated)** with `publish_target=none` on the branch or tag containing the candidate. After it succeeds, use its run ID to dispatch **Studio Docker (consolidated gated)** on the same commit with `qualify_essentials_profile=true` and `publish=false`.

```sh
gh workflow run studio-packages.yml --ref "$ref" -f publish_target=none
gh workflow run studio-docker.yml --ref "$ref" \
  -f packages_run_id="$packages_run_id" -f qualify_essentials_profile=true -f publish=false
```

The profile jobs verify and use the same official package artifact on native Linux AMD64 and ARM64. The default-auth container checks the unauthenticated management challenges and all shipped module assets. A second isolated container uses demo mode to upload the official ETL archive, verify it enters the writable feed while disabled, enable it and check its exact package assets. Each smoke runs with Docker networking disabled.

Download the successful `studio-essentials-profile-<architecture>-<run>-<attempt>` artifact for your architecture. The bundle includes the tested images, their IDs and build digests, a standalone build context, the ETL archive, the smoke tool and evidence. Verify and replay it from the downloaded directory.

```sh
sha256sum --check SHA256SUMS
docker load -i images.tar.gz
profile=$(jq -r .profileImage build.json)
test "$(docker image inspect --format '{{.Id}}' "$profile")" = "$(jq -r .profileImageId build.json)"
python3 smoke.py "$profile" --context context --etl fixtures/Elsa.Studio.Etl.*.nupkg --output replay
```

## Publish versioned candidates

From `main`, obtain the successful build-only Studio Packages run for the current commit and dispatch the profile candidate path.

```sh
gh workflow run studio-packages.yml --ref main -f publish_target=none
gh workflow run studio-docker.yml --ref main \
  -f packages_run_id="$packages_run_id" -f publish=false -f publish_essentials_profile_candidate=true
```

Both native qualification jobs must succeed in the same attempt before the `studio-publish` job loads the bundles and writes to Docker Hub. Each attempt creates unique multi-platform tags in `elsaworkflows/elsa-studio`:

- `foundation-candidate-<version>-<run>-<attempt>`
- `essentials-candidate-<version>-<run>-<attempt>`

The job first stages the manifests using the corresponding `-amd64` and `-arm64` tags. Use the index digests from the Actions summary or `publication.json` to pin both candidates. The ordinary Studio release tags and moving aliases retain their current publication paths. To retry a failed publication, rerun all jobs so both qualifying bundles come from the retry attempt.

The `ELSA_STUDIO_PROFILE_PUBLISHING` repository variable controls versioned candidate image publication.
Set it to `true` to enable this path. Unset it to stop further candidate writes while checking registry state.
The separate consolidated package publisher retains its existing authorization.

## Ordinary Studio Essentials image

The ordinary Studio Docker workflow uses the Foundation Dockerfile and the profile files from the verified source commit. Automatic publication packs the seven modules at the allocated preview version. Manual builds use the selected package artifact after verifying its provenance, inventory, and payload.

The publisher pushes a workflow-scoped staging index and qualifies its exact AMD64 and ARM64 children before assigning the ordinary tags. The qualification artifact records the tested image IDs and whether each runtime ran natively or under emulation. The job records the previous `latest` digest, checks that the source is still on main, and verifies that `latest` resolves to the tested index after promotion. Build-only dispatches qualify a locally loaded AMD64 image.
