# Foundation and Studio source cutover and recovery

This runbook describes the coordinated repository-source and writer transition that could follow the
Foundation/Studio consolidation. It is an operations plan, not approval to merge, switch a source,
change publisher settings, publish packages or images, or clean up either repository. Publisher
procedures are detailed in [the publisher cutover runbook](consolidation-publisher-cutover.md).

## Evidence boundary and recovery inputs

The isolated rehearsal was seeded from the Foundation baseline
`0e0c1ff6fa101282cef65e1eabf030b72de32acf` and Studio baseline
`3eef8fa788bfe2ebd4686df0e9a116532c9b0dc9`. The private control-room record
`import-history-verification.json` records complete-history Git bundles captured at
`2026-10-11T20:20:00.171622Z`:

| Source | Bundle | SHA-256 | Captured source head | Working-tree state not in the bundle |
|---|---|---|---|---|
| Foundation | `foundation-baseline-20261011-2020.bundle` | `88de40b0f8b73ff5ba289a514e5716628702c36ceb46b87c86216fbda307c5fa` | `e4a699879791fb7eb2bc1c6874f772f4a7fe5355` | 6 tracked status entries; 0 untracked files |
| Studio | `studio-baseline-20261011-2020.bundle` | `5f5d852f7517155da1e71fd3291edcb68070d5cc4a8b47ea467a406bc23f9e38` | `c3be2553650e2158616bc874c97ad2b47077bd20` | 15 untracked entries / 240 untracked files; 0 tracked status entries |

The recorded bundles passed `git bundle verify` and contain complete Git history. A bundle does not
contain uncommitted, untracked, ignored, or private working files. The status counts above are not a
recovery copy of those files. Before any source switch, each affected owner must identify their local
work, make a separate recovery copy with a recorded location and checksum, and verify that it can be
read back. If ownership or contents are uncertain, retain the original repository and its files and
record the uncertainty; do not represent Git history as a substitute for working-file recovery.

The acknowledged no-write window ended at `2026-10-11T21:15:00Z`. It was limited to the agreed
capture and rehearsal and did not freeze either organization globally. It has expired. None of the
refs, bundle checksums, owner dispositions, or publisher states above is a fresh cutover capture.
Do not infer current readiness from this snapshot.

## Required cutover record

Before scheduling a cutover, fill in and review a dated record containing all of the following:

| Required field | Record at cutover |
|---|---|
| Approval | Named decision owner and explicit approval for the bounded source switch and each publisher-control change |
| Window | Exact UTC start and end, including the abort deadline |
| Source refs | Exact current refs and clean/dirty status for both repositories, plus verified bundle locations and SHA-256 values |
| Delta interval | Exact UTC start and end of the final capture; all commits, PRs, branches, tags and local-only changes since the recorded baselines |
| Owners and writers | Named owner and disposition for each active/queued job, open PR, claim, bot, schedule, deployment, package publisher and image publisher |
| Recovery | Read-back verification of the complete-history bundles and owner-approved working-file recovery copies |
| Publisher controls | Prior value, approver, application time and read-back evidence for each temporary hold and the eventual single-writer switch |
| End state | Exact authoritative source refs, enabled writer, disabled writers, package/image state and decision on any unresolved item |

No cutover begins with a blank owner, delta, recovery, or publisher-control field. Record each
owner's response or explicit lack of response; do not treat silence as disposition. If the window
expires, stop and take a new capture and obtain fresh coordination before restarting.

Assign named people to these responsibilities in the record: Foundation source/PR and delta owner;
Studio source/PR and delta owner; each owner of local-only or ignored work; Actions operator for
active/queued workflow disposition and hold read-back; package/image release owner for registry and
tag floors; cutover lead for the UTC window and final ref comparison; and recovery custodian for
bundle and working-file read-back. One person may hold multiple roles, but every role must have an
explicit name and acknowledgement before the window starts.

## Switch sequence

1. **Approve and bound the operation.** Obtain explicit approval for the Foundation main merge, the
   source-of-truth switch, and each reversible publisher hold or activation. Record the exact UTC
   window, operators, abort deadline, and recovery owner. This document itself changes no settings.
2. **Reconcile owners and writers.** Capture fresh refs and status from both live repositories.
   Identify every open PR, local claim, active or queued Actions run, bot/schedule, deployment, and
   package/image writer. Obtain named owner dispositions and arrange a bounded no-write interval.
   Preserve all owner-only work before asking its owner to stop writing.
3. **Prove recovery.** Verify the fresh complete-history bundles and independently recover/read back
   every owner-approved dirty, untracked, ignored, or private-work copy. Record hashes and locations.
   An unresolved file or owner is a stop condition, not an assumed deletion.
4. **Capture and reconcile final deltas.** Record the exact UTC start and end of the delta interval.
   Compare both sources against the baselines and against the immediately preceding capture; reconcile
   each new commit, PR, tag, branch, local change, and publisher run to a named owner and destination.
   Include changes created during the interval. A clean checkout or matching commit graph alone does
   not account for these deltas.
5. **Hold automatic Foundation publication before merging.** Foundation's existing package workflow
   is attached to pushes to `main`, and its Docker workflow can consume the resulting package artifact.
   Before the candidate reaches Foundation `main`, obtain explicit approval for a temporary,
   reversible hold that prevents both automatic Foundation package publication and any downstream
   image publication. Record previous control values, the authorized operator, application time,
   expiry/restore owner, and read-back proof that the hold is effective. This is a required future
   gate; this rehearsal has not requested or applied the hold. Do not substitute a manual publish,
   `publish-state` edit, or assumption about a dry run for the hold.
6. **Merge while preserving ancestry.** Merge the reviewed candidate with a true merge commit. Do
   not squash or rebase it: the imported Studio ancestry and mapped history are part of the evidence.
   Verify the merge commit and both source ancestry proofs on the resulting `main` ref. If either
   source delta is missing, stop and reconcile it before switching authority.
7. **Switch one writer at a time.** Follow the Studio publisher runbook to confirm the standalone
   Studio package and image writers cannot write, then qualify the consolidated build-only path and
   compare package and image floors. Only after the old writers are disabled and their admitted jobs
   are terminal may the separately approved consolidated writer be enabled. Keep Foundation's
   temporary package/image hold in place until its relocated-input plan and feed floors have been
   independently reconciled and a separate Foundation publisher decision is approved. At no point
   should two repositories be eligible to publish the same identities.
8. **Verify and hand over.** Capture resulting refs, workflow states, outstanding jobs, artifact/feed
   state, and the named owner for each remaining issue before ending the window. Leave unresolved
   deltas visible and assigned.

## Abort and recovery

- **Before the Foundation merge:** abort by leaving both original repositories authoritative and
  their writers unchanged. Preserve the capture and record the reason; do not clean up either source.
- **After merge but before a source switch:** keep the original Studio repository as the operational
  source. Do not reset or force-push `main`, delete imported refs, or rewrite history. Correct forward
  in a reviewed commit, or use a reviewed revert that preserves the merge and imported ancestry.
- **After any publisher dispatch:** setting a variable false prevents later admission only; it does
  not cancel work already queued or running. Cancel or wait for terminal completion of every active
  and queued run in both old and consolidated Studio package/image workflows. Verify the actual run
  state before allowing the old writer to resume. For a Foundation publisher hold, likewise verify
  that no package or downstream image job was already admitted before relying on the hold.
- **After a partial package publication:** inspect each NuGet and npm registry identity/version and
  preserve run logs, artifacts and provenance. Treat any version accepted by a registry as consumed
  and immutable. Recover by completing forward with a new monotonic version above every affected
  feed floor; reuse a version only when registry evidence establishes that no package was written.
  Duplicate-skipping flags do not establish completeness and are not recovery. Do not delete an
  accepted package or silently mix artifacts from separate runs.
- **After an image push:** inspect the digest for every affected tag. Image aliases can move, so
  record the observed digest and explicitly reconcile which digest each tag should identify before
  retrying or restoring aliases.
- **When restoring a prior writer:** first prove all replacement writers are disabled, all of their
  active and queued runs are terminal, and partial external writes have an owner-approved disposition.
  Then enable only the selected old writer and verify the resulting control state. Never resume two
  writers for the same package or image identities.
- **Cleanup:** branch deletion, worktree removal, repository rename/archive, and removal of recovery
  copies are separate operations requiring separate explicit approval after the new source and
  recovery evidence have been accepted. None follows from a successful merge or source switch.

Pending runtime, feed, artifact and browser qualification remains pending until exact-head evidence is
recorded. This runbook records required gates; it does not assert that a live cutover or publisher
qualification has passed.
