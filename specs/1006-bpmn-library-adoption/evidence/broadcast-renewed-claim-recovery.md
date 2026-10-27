# Actual pre-dispatch claim renewal and standard-host recovery

This bounded T051 case proves that the actual source-built Foundation processor renews the
same unexpired physical source before dispatch. It then proves crash recovery of the saved
cohort through the standard host. T051 remains unchecked; this receipt does not close
historical upgraded-host recovery, Studio, deletion, delivery or production acceptance.

The source parent is `1e5f9fa2e0dd4626cf8456032db0dd853f01182e` with an explicit worktree
candidate suffix. The [machine receipt](broadcast-renewed-claim-recovery.json) binds the
changed harness files and three actual host receipts. Each actual case verifies the same
1,380 source files and the full probe, standard and reader binary manifests before/after.
The standard binary contains no recovery probe. Whole databases and host configuration
remain private.

## Actual sequence

1. Normal authenticated APIs publish a signal-start binding, arm two parallel exact catches,
   then execute a signal publisher. The selected provider, processor and dispatchers are real.
2. The scoped test-only dispatcher subscribes to EF's public `SaveChangesCompleted` diagnostic
   for its exact `RuntimeDbContext`. It observes the first protocol-one source claim at fence
   one with no saved cohort. It refuses an enclosing provider or ambient transaction,
   unsubscribes and holds the call. Encoded physical identities are decoded with the public
   provider codec. The diagnostic marker supplies no application authority.
3. A separate coherent SQLite snapshot, read through the actual checked EF provider, must
   independently prove the physically committed source: Delivering, fence one, exact owner,
   original deadline and absent cohort. After a real two-second delay, another independent
   read must show the entire checked source projection unchanged before release.
4. Release lets the actual processor execute its normal pre-dispatch renewal. After it saves
   the cohort, a third checked read must bind the exact commit-specific source ID, canonical
   intent, owner and fence, with a higher physical revision and an extended deadline. Release
   and this subsequent cohort cut must both precede the original deadline. This bounds the
   renewal before expiry without inferring its clock sample from timeout configuration.
5. The owned host is killed before any recipient dispatch. Later matching catches/binding
   have been added after the saved decision. The standard host, without the probe, reclaims
   under a different owner/fence and completes only the saved start and exact callbacks.

In the final actual case, revision advances **2→4**, the source deadline advances
**3.164655 seconds**, and the post-renewal cohort cut occurs **56.767361 seconds** before the
initial deadline. The original source stays under fence one until the crash. Standard-host
recovery uses fence two and records Delivered with the unchanged one-start/two-resume cohort.
The original callbacks and join complete, the exact keyed start parks, and later recipients
remain excluded. This is ordinary pre-dispatch renewal. It does not prove renewal during a
running handler or revival of an expired but unreclaimed claim.

## Verification and review

- Actual renewal/crash/recovery: **1 passed, 0 failed, 0 skipped**.
- Post-cohort and committed-completion/provider-return-loss regressions: **2/0/0**.
- The extracted actual acceptance guard: **9/0/0**, comprising the real positive fixture and
  eight invalid-input controls. They reject an unchanged deadline, different commit-specific
  source, owner, fence or intent, unchanged revision, and expired release or cohort cut.
  These are evidence-input controls, not production source mutation tests.
- Checked source reader controls: **8/0/0**; owned process controls: **10/0/0**;
  actual cleanup failure controls: **3/0/0**.
- Two separate locked probe/standard builds pass. The unchanged checked-reader binary and
  its complete build closure are bound in all three actual receipts.

Three independent component reviews found and closed the omitted-snapshot observation error
and the missing exact-source/unexpired-cut guards before final qualification. A source without
selection omits `recipientSnapshot`; requiring a present JSON null was incorrect. No actual
host run with that incorrect observer is counted as a pass. Final source, actual receipts and
portable evidence are reviewed independently. These are component audits; full-PR rounds
remain **zero**, with six as the bound.

The [main host receipt](broadcast-renewed-claim-host.json) carries the common manifests.
[Post-cohort](broadcast-renewed-claim-after-cohort.json) and
[committed completion](broadcast-renewed-claim-after-source-commit.json) receipts reference
those identical verified manifests. All owned case processes terminated. The existing user
backend and Studio remained running.

Production source, project graph and frozen authoring are unchanged. Architecture **590**,
locked Server restore and generated-map checks are inherited from the
[earlier supporting qualification](broadcast-competing-proposal-recovery.json), not rerun or
restamped here. The frozen fingerprint remains
`41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.

Supported redrive, same-artifact behavior, remaining scopes, retention/GC/republish and other
T051 cases remain open, along with T045/native composites, actual historical upgraded-host
recovery, final current Studio, legacy writer/duplicate semantics deletion and guards,
full-PR review, exact-head required checks/protected merge/resulting-main verification and
separate ADR 0063 acceptance. Production #2898/#2907 and the program stay open. Orleans and
the task-runner follow-up remain deferred.
