# Recovery of the 20 remaining merge 245 artifacts

This one-off path prepares the bounded recovery in [#2653](https://github.com/elsa-workflows/elsa-foundation/issues/2653), under [M4 #2505](https://github.com/elsa-workflows/elsa-foundation/issues/2505). It does not replace the normal publisher or change release policy. The reviewed source merge that installs the mechanism and the approval to execute it are separate decisions. Publication, record writeback for newly confirmed uploads, publisher restart, images and standalone Studio handover must not be inferred from the earlier source merge or the completed 118-row tracking repair.

The original main merge `24533cfe6fcdd000f518d1855a525cfe6b37d6f9` produced 138 packages in run 38185385882. After an admitted partial publication, 118 exact artifact versions were verified live and recorded by the approved repair at `bbe803c40c996e8a89c934631ec9930cf90251b2`. The 20 remaining candidates are explicit in `merge245-exact20.json`, alongside the public identities and hashes for all 138 original artifacts and their expected state rows. No private collector files, local paths or credentials belong in this manifest. Candidate archive SHA256 is `6848547ea1ee218ba234e3d294f43cf5fd42396620fe0125a22d0bb074eab211`; manifest SHA256 is `7ed68791f5f5d85bd36053fa236cc8ce96002eb7eeb87e25081c21b1f41e4ee1`.

The mechanism downloads the original Actions artifact 11719153513; it never rebuilds packages or patches/recomputes the old 138-package plan. The archive expires 9 January 2027. Expiry, a changed digest, changed state, newly present candidate version or higher feed version invalidates this attempt. Preserve the original state commit and source refs. Do not delete immutable packages to make this plan pass.

## Read-only qualification

The default command performs fresh GET-only artifact, feed, state and publisher-control checks. It does not retrieve the Feedz API key or call package/state mutation operations:

```bash
dotnet run --project tools/versioning/Elsa.Versioning.Publisher -c Release -- recover-exact20
```

The manual **Exact20 recovery** workflow defaults to this preflight. Both read-only and execution runs hold the workflow-level `publish-state` concurrency group shared with Packages and active Publish repair. [GitHub concurrency](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency) may replace an older pending run in this group with a newer pending run; it does not cancel the active run. The observation does not freeze other source writers or establish a continuing publication window after the run ends. An external operator could still alter repository controls outside this group; controls/state are rechecked and drift refuses the operation.

## Execution decision and sequence

Before execution, root must publish the exact reviewed implementation head, current required checks, original-artifact read-only proof, complete allowlist/versions, fresh feed/state/hold evidence, partial-success behavior and recovery limits. Obtain the separate owner decision for those 20 immutable uploads and their confirmed-row record update. After direct human approval, the control room records a public receipt on issue2505 authored by `sfmskywalker`, with these exact lines and the approved current workflow commit:

```text
APPROVE EXACT20 RECOVERY
manifest_sha256=7ed68791f5f5d85bd36053fa236cc8ce96002eb7eeb87e25081c21b1f41e4ee1
workflow_commit=<full approved main SHA>
```

The mutating command GET-verifies the receipt's issue, author and exact manifest/workflow binding; pass its numeric ID through `approval_comment_id`. Preparation must not create this receipt. The account is also used by agents, so this is a trusted control-room recording boundary, not cryptographic proof of a second human actor. A confirmation field alone is not approval evidence. No protected-environment setting is created or inferred.

After that decision and an authorized source merge, use only a first-attempt manual run on `main` with `execute` true and confirmation `RECOVER-EXACT20-24533cfe6fcdd000f518d1855a525cfe6b37d6f9`. Do not rerun a failed job, run an old workflow revision, execute locally, enable Packages/Docker, or replay 38185385882. The command validates its actual run/repository/workflow identity and checked-out/current-main commit before admission. Both automatic Foundation publishers must remain disabled.

Fresh preflight verifies all 138 package/index identities, the 118 live byte matches, 20 exact GET 404s, previous version floors, original archive/nuspec provenance, expected state tip and bytes, effective holds and nonterminal publisher/repair runs. It emits the complete canonical proposed record for review without writing it. Each candidate also gets a fresh absence and version-floor check immediately before its single PUT; external writers are outside the workflow lock and a later race cannot be made atomic with Feedz. Sequential uploads follow actual package dependencies; there is one upload attempt per candidate and no duplicate skip. Each accepted or ambiguous upload requires bounded exact package-byte readback. Ambiguity or failure stops further uploads. Only byte-confirmed successful rows may advance, preserving every other row, all 138 IDs and `last_publish_commit` at the original source commit24533cfe. The state commit uses canonical `PublishedVersions.Serialize()`, an expected parent and a no-force push, followed by exact remote tip/byte readback.

## Failure and rollback

No successful upload can be undone by reverting the source PR: feed versions are immutable. Stop on the first uncertain or failed operation, retain the run log/proposed successful-row evidence, and leave both publisher holds in place. A failed upload response may still have admitted a package; exact readback determines whether it can be recorded, without repeating that upload. An unavailable or mismatching readback is not success.

If the record push fails or readback differs, do not force it, reset state or replay packages. Independently reconcile the current feed bytes and state parent. Any partial recovery invalidates this pinned attempt and requires a new reviewed allowlist/state snapshot and a fresh owner decision for remaining effects. No automatic resume/retry is provided. A source-only revert removes the new mechanism; it neither reverses package publication nor changes publisher settings or the separately retained state history.

## Qualification boundary

Tests against fake HTTP and local bare Git exercise refusal, partial-success and state-write behavior. The real original producer artifact must also pass candidate GET-only verification. Neither simulation nor a successful preflight is live publication proof. Keep #2653 open while exact-head review/CI or producer qualification is incomplete; M4 remains incomplete until the separately approved operation and operational handover are verified and accepted.
