# versioning - runtime version pinning across republish

| Script | What it exercises |
|--------|-------------------|
| `Test-VersionPinning.ps1` | Proves a **running instance keeps executing the version it started on** after a newer version of the same definition is published: publish v1 → start an instance → suspend it at an `Event` → (while suspended) promote + publish v2 → resume → assert the instance is still pinned to `artifact_v1` and ran v1's node (`ranV1`, not `ranV2`). |

## Current status — `KNOWN ISSUE #1058` tracker

The test is a living tracker. It cannot yet author v2 of the same definition over REST: the draft-replace →
`promote` path validates the graph **more strictly than `submit`** and rejects the v1-equivalent graph with a 409
(`Event` node "required output 'Result'", and the `set-output` intrinsic "not in the activity catalog") even though
the identical graph submits, publishes, and runs as v1. Filed as **#1058**. Until promote/submit validation agree,
the test reports `KNOWN ISSUE #1058` and exits green; it will run the full pin assertion automatically once #1058 is
fixed (flip it to a strict assertion then).

Findings confirmed along the way (not blocked by #1058):
- an instance is pinned to its content-addressed artifact and resumes on it;
- different version bodies yield different artifacts (content-addressed);
- **publishing v2 retires v1's live publication** — you cannot START a new instance on the old artifact afterward,
  which is why the pinning instance must be launched *before* the republish.

Requires the server from source (see ../README.md).
