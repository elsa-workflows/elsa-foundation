# One-job Azure runner pilot

This dispatch-only pilot exercises existing source-only CI guards on a disposable
worker. It does not replace any required check or establish product, browser or
performance acceptance. Normal [CI and merge gates](../../docs/contributing/merging.md)
still apply.

The small pilot workflow uses JSON, a YAML subset, so the standard-library JSON
parser can validate its actual fields without another dependency. The guard
rejects duplicate fields and unsupported syntax; comments or display names cannot
substitute for the immutable checkout, main-only dispatch and permission fields.

## Controller prerequisites

1. Merge the reviewed workflow and guard source normally. Read current main and
   bind the dispatch to its exact SHA. Never dispatch a PR/fork or arbitrary branch.
2. Use the existing Codex App installation's `organization_self_hosted_runners`
   write permission, downscoped in a task-owned controller. Do not alter grants or
   use personal credentials. Keep App credentials on the controller, not the guest.
3. Snapshot existing runner groups. Create **one new** group named
   `elsa-foundation-ci-pilot` with `visibility=selected`, only repository ID
   `1232994340`, `allows_public_repositories=true`, `restricted_to_workflows=true`
   and exactly this selected workflow:

   ```text
   elsa-workflows/elsa-foundation/.github/workflows/ci-azure-pilot.yml@refs/heads/main
   ```

   Read back the group and selected repositories. Require exact equality before
   registration, including workflow restrictions. Do not change the Default group.
   A missing/unsupported restriction stops the pilot; never fall back to labels.
   This server-side boundary excludes jobs defined by fork and PR-branch workflows.
4. Prepare a dedicated disposable Ubuntu 24.04 x64 worker (2 CPUs/8 GiB is enough).
   Use a new non-sudo account, no Docker-group membership, no Azure managed identity,
   controller keys, storage tokens or other work. Root controls its external lifetime.
   Do not borrow another validation lane or reuse its stores/caches/credentials.

## Frozen runner and single-job assignment

Download the official `actions/runner` v2.338.2 Linux x64 release and verify its
SHA256 before extraction:

```text
https://github.com/actions/runner/releases/download/v2.338.2/actions-runner-linux-x64-2.338.2.tar.gz
baebf204307bac6193480852732f0aba2ae97b6c5597c244f4d5558390147c40
```

The controller must also check the installation's currently offered runner
download version and support before registration. Do not silently substitute or
auto-upgrade a different runner. Retain the release identity and measured bytes.

Generate a random label `elsa-ci-pilot-` followed by 32 lowercase hex characters.
Dispatch this workflow on main with exactly `trusted_sha` and `runner_label`.
Verify the resulting queued run's event, source SHA, workflow ref, attempt 1 and
requested group/label. A root-owned regular file at
`/etc/elsa-ci-pilot/assignment.json`, with root-owned non-writable parent and mode
0444, binds the worker to the actual `sha`, `label` and decimal-string `run_id`.
It may also retain non-secret VM, group and runner-version/digest metadata.

Only then request an organization JIT configuration for that exact new group and
label. Verify the registration's group/labels. JIT credentials are secret: private
stdin or mode-0600 transfer only, no console, command-line argument, public artifact
or logged response body. The pinned runner supports
`ACTIONS_RUNNER_INPUT_JITCONFIG`, and removes this input from its environment when
reading it. Launch the official listener with this input and a bounded external
15-minute lifetime. Do not install a persistent service or generic runner.

The pilot verifies its server-provided context, actual checkout SHA, dispatch
payload and external assignment before guard commands. It runs the fixed commands
once, serially, each with a 60-second limit. A failure stops further commands;
there is no retry, timeout relaxation, synthetic status or required-check mutation.
The proof ledger predeclares all commands. Each attempted command retains its argv,
elapsed time and `passed`, `failed`, `timed_out` or `start_failed` outcome; subsequent
commands remain explicitly `not_run`. A timeout/start failure has no invented exit
code, and its retained stdout/stderr hashes still accompany the failure report.

## Evidence and rollback

Record only non-secret registration ID, group ID/policy, VM identity, runner
version/digest, source SHA, label, run/job IDs, start/completion times and exit state.
Download the `ci-azure-pilot-<run>-<attempt>` artifact and independently verify its
source identity, every actual command/exit code and retained file hashes. Keep
failed, canceled, skipped and unrun outcomes visible. The first pilot proves a
runner/artifact path, not a global CI speedup.

After the single job, verify that the ephemeral runner has deregistered. If it is
still registered, delete **only its recorded registration ID**. Confirm the guest's
owned processes are terminal; stop/delete the dedicated worker under its budget.
Remove only the recorded new group once empty, unless an explicit measured scaling
decision retains it. Re-read the original groups to verify they are unchanged.
If a dispatch never receives a runner, cancel only that recorded pilot run. Do not
touch existing CI/main/PR jobs or organizational policies as cleanup.

Official contracts: [runner-group restrictions](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/manage-access),
[group REST API](https://docs.github.com/en/rest/actions/self-hosted-runner-groups),
[JIT runner API](https://docs.github.com/en/rest/actions/self-hosted-runners),
[runner release](https://github.com/actions/runner/releases/tag/v2.338.2).
