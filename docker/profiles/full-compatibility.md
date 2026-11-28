# Archived Full server profile

The Full profile ran the classic workflow engine with a broader module selection. Its last
published release, `full-4.0.0-preview.3034.1`, stays available under its original tag.
Current profile releases publish the Native and Flowchart Actor Foundation paths through
[Essentials and Elsa 3 import](README.md). The original [Full package roots and default shell](https://github.com/elsa-workflows/elsa-foundation/tree/e758c2cc86ccdb4294be0aa63bdfe933060db75e/docker/profiles/full)
show exactly which classic modules the published image contains.

```sh
image='elsaworkflows/elsa-foundation-host@sha256:2cb8a2f0e000baee188d5c37d65bdd7a6e6f9b94428f7d5d3be20feebbc3df37'
docker pull "$image"
```

The [official Docker release](https://github.com/elsa-workflows/elsa-foundation/actions/runs/46099430200)
verified the locked Full image on native [Linux AMD64](https://github.com/elsa-workflows/elsa-foundation/actions/runs/46099430200/job/140841715789)
and [Linux ARM64](https://github.com/elsa-workflows/elsa-foundation/actions/runs/46099430200/job/140841715778).
The published image index matches the [two staged native manifests](https://github.com/elsa-workflows/elsa-foundation/actions/runs/46099430200/job/140843201337).
The release smoke starts the stock shell offline with a clean package cache and checks authenticated
workflow execution, Decisions and Secrets. The profile embeds its package archives, desired manifest
and Nuplane lock. The same release publishes matching Foundation and builder tags
`4.0.0-preview.3034.1` and `builder-4.0.0-preview.3034.1`.

## Keep an existing deployment and its data together

Use the image digest already deployed if it differs from the pin above. Keep its mounted shell files,
package versions, volumes, database, executable artifacts, keys and operational configuration together.
Stop new ingress and recurring producers before draining pending work on the earlier host. Keep that
environment available for earlier run history and rollback. Start an Actor deployment with a dedicated
Actor database and follow the [Curated Essentials transition guidance](essentials-curated/README.md#workflow-execution).
An image change alone does not transfer existing runs between engines.

The default Full shell also uses the following settings with the
`CShells__Shells__default__Features__` environment variable prefix. Retain their existing values
when replacing a container for the same deployment.

| Feature setting | Value |
| --- | --- |
| `WorkflowsRuntimeEntityFrameworkCore__RecoveryContinuationSigningKey` and `WorkflowsRuntimeEntityFrameworkCore__HierarchyCursorSigningKey` | Separate persistent signing keys, each at least 32 UTF-8 bytes. |
| `WorkflowsRuntimeApi__WorkflowAlterationPayloadProtectionActiveKeyId` | ID of the active payload protection key. |
| `WorkflowsRuntimeApi__WorkflowAlterationPayloadProtectionKeys__<id>` | Base64-encoded 32-byte key for that ID. |
| `Secrets__EncryptionKey` | Encryption key for the Secrets store. |

Keep the [Data Protection key ring](../../docs/docker.md#data-protection-keys) if sign-in sessions
must survive replacement. An administrator with many permissions can receive a chunked cookie.
Configure intermediate proxies to allow a combined `Cookie` header greater than 8 KiB.

## Verify the archived image

Run the matching Python smoke from a separate source checkout. The script creates disposable
containers with its own settings and data.

```sh
archive="$(mktemp -d)"
git -c core.fsmonitor=false worktree add --detach "$archive/source" e758c2cc86ccdb4294be0aa63bdfe933060db75e
python3 "$archive/source/tools/docker-profiles/smoke.py" "$image" --variant full
```

The [classic Workbench preservation guidance](../../docs/docker.md#workbench-mixed-preview-compatibility)
covers the separate earlier Workbench deployment. The archived
[BPMN apphost qualification](https://github.com/elsa-workflows/elsa-foundation/blob/e758c2cc86ccdb4294be0aa63bdfe933060db75e/extensions/Bpmn/tests/Hosting/Tests/README.md)
uses its own Workbench fixtures. Existing Full deployments that added packages must keep their own
package closure, feature settings and providers.
