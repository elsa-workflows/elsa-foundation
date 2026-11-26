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
