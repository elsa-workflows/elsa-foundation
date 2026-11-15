# versioning - runtime version pinning across republish

| Script | What it exercises |
|--------|-------------------|
| `Test-VersionPinning.ps1` | Proves a **running instance keeps executing the version it started on** after a newer version of the same definition is published: publish v1 → start an instance → suspend it at an `Event` → (while suspended) promote + publish v2 → resume → assert the instance is still pinned to `artifact_v1` and ran v1's node (`ranV1`, not `ranV2`). |

## Validation and failures

Submit and promote apply the same composed draft validation gate (#2792). The former `KNOWN ISSUE #1058`
early return is removed: a refused promotion fails the journey. The script must reach its final assertions
that the original instance completes on the first artifact and runs `ranV1`, never `ranV2`.

The journey also checks:
- an instance is pinned to its content-addressed artifact and resumes on it;
- different version bodies yield different artifacts (content-addressed);
- **publishing v2 retires v1's live publication** — you cannot START a new instance on the old artifact afterward,
  which is why the pinning instance must be launched *before* the republish.

Requires the server from source (see ../README.md).
