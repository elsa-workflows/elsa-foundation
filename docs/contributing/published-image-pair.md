# Verify the published Studio–Workbench pair

This guide checks whether the published Elsa Studio image can create and run a workflow against the published Elsa.Workbench image. To try Elsa without pinning images or checking a workflow, use the [published-image quick start](../../docker/compose/README.md#quick-start--published-images-no-clone-or-build).

The published stack is a local demo: it uses demo credentials and SQLite workflow data inside the container. Do not expose it to a network. The cleanup command below removes this project's Data Protection volume and container-held demo data.

## Prepare an isolated, pinned stack

You need Docker Engine and the Compose v2 plugin. Start from the [canonical Compose file](../../docker/compose/docker-compose.images.yml) at the same Foundation revision as this guide, and save a temporary copy as `pair-compose.yml`. Keep its configuration, including the Studio login-return origin, unchanged.

Choose a unique Compose project name. **Before starting anything**, check that host ports `13000` and `14000` are unused and that no existing containers, network, or volumes use that project name. If project resources already exist, choose another project name. If either port is occupied, wait until it is free; do not stop another process. Keep these ports because the browser URLs and login-return configuration depend on them.

In the temporary copy, pin both images and bind both ports to loopback:

1. Replace the Workbench and Studio `:latest` image references with their exact index-digest references from the table below. Use the **index digest** for the Compose pin; the child digest is recorded to identify the Linux/amd64 image selected during the run.
2. Change the Workbench port mapping to `127.0.0.1:13000:8080`.
3. Change the Studio port mapping to `127.0.0.1:14000:8080`.

Use the same unique project name and temporary file for every Compose command. Validate and start the isolated project:

```bash
docker compose -p elsa-pair-check -f ./pair-compose.yml config --quiet
docker compose -p elsa-pair-check -f ./pair-compose.yml pull
docker compose -p elsa-pair-check -f ./pair-compose.yml up --detach
```

Replace `elsa-pair-check` if that name is already in use. Do not run `up` until the port and project-resource checks above are clear. `config --quiet` must succeed before pulling or starting the images.

## Run the workflow journey

Open <http://localhost:14000> and sign in with the demo account shown in the [quick start](../../docker/compose/README.md#quick-start--published-images-no-clone-or-build).

1. Open **Workflows → Definitions** and create a workflow using the default Flowchart.
2. Choose **Add activity**, search for `WriteLine`, and select **Write Line** (singular). Set its **Text** property to a unique message you can recognize in the output.
3. Choose **Review & publish**, then **Publish** in the dialog. Note the published artifact identifier.
4. Open **Workflows → Executables** and run that published artifact. The editor's Run action starts a draft test run; use Executables for this check.
5. Open the resulting run in history. Inspect its Write Line timeline entry, **Activity** tab, and **Issues** tab.

Check that the run is **Completed**, belongs to the published artifact, and has zero incidents. Its timeline should contain exactly one completed Write Line activity, the Issues view should be empty, and the Workbench output should contain the message once.

If needed, inspect the Workbench output with:

```bash
docker compose -p elsa-pair-check -f ./pair-compose.yml logs --no-color elsa-workbench
```

Whether the journey passes or fails, remove only this named project and verify its resources and ports are gone:

```bash
docker compose -p elsa-pair-check -f ./pair-compose.yml down --volumes --remove-orphans
```

Use the same project name and temporary file as at startup. Do not run global prune or cleanup commands. Keep any failure diagnostics before cleanup; do not report a pass if cleanup is incomplete.

## Recorded image provenance and hosted result

The index digest is the immutable Compose pin. The child digest records the selected Linux/amd64 manifest. Preview numbers are CI run counters, not library or package versions.

| Image | Publishing source revision and run | Exact Compose image reference | Linux/amd64 child manifest |
|---|---|---|---|
| Studio | `7cb2f2c2381411694c470cdb9179402571bcf832`; [Docker run 37496940056](https://github.com/elsa-workflows/elsa-foundation-studio/actions/runs/37496940056) | `elsaworkflows/elsa-studio@sha256:1a4401d2f5bee207c3edbc51d4157ecb23ba796fb1dacc9e5ff6775f6c4f6ad0` | `sha256:d151aabcc161474ba5a7d18d9979c65f381571bfb2e96d1840bfc0b99e8bb647` |
| Workbench | `592d6c0eb2a1ae45563ce5b234fb4e97af2867ed`; [Docker run 37446588616](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37446588616) | `elsaworkflows/elsa-workbench@sha256:73ef26e41760eaa4618a37cfbd7285002408f634e4089201b3864cb1f6147904` | `sha256:e56e093b73ea239c7833107719e23bf1d03a4faaf07d1b704a8523ac88461e46` |

Hosted run [37560339935](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37560339935) passed on Linux/amd64 using harness candidate `3b2b0050f13d524f538e0d6236cba6a1d92d93b0`, tested in merge commit `4be3ef37a856ce91a31502d48137992240dda1bc`. It authenticated, created definition `149MbiwnulV`, configured Write Line, published `artifact-13d54b3842bd`, ran it as `149Mc1lCEoe`, verified completion, matching artifact, zero incidents, one completed Write Line, an empty Issues view, the expected output once, and successful owned cleanup. This is hosted browser automation, not a human usability trial.

The run used Linux/amd64 only. The host dependency manifests reported Elsa.Workbench `1.0.0` (trimmed manifest SHA256 `ac36519fb9a858b98e2a95d7b122c52acf71c19a7c321779ae088978fd1eeb74`) and Elsa.Studio.Web `1.0.0` (trimmed manifest SHA256 `8f7cb0a4b68b4ead88d5ba1655f1b64d8edc2e970d7c0774ff5520123da4baea`); these are host dependency-manifest entries, not complete image package inventories. Both image refs were absent locally before the pull, but layer reuse was not reported, so this does not establish a cold-cache run.

After integration, the published-image browser journey passed against Foundation `main` revision `48bfcea5f5bc8c05189e9b5f334064e8408ffcba` in [run 37563378328](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37563378328). [Foundation #2470 was closed with that main verification](https://github.com/elsa-workflows/elsa-foundation/issues/2470#issuecomment-6030329022). These recorded runs do not establish compatibility for every subsequent image pair or a human usability result.

An earlier candidate using unchanged main Compose configuration failed to return to Studio after sign-in. This revision's canonical Compose file adds the Studio login-return origin while preserving the existing origins. Use that configuration without a separate login-origin override.
