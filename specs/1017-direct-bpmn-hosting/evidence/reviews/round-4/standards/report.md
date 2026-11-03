# Complete PR3410 review round 4 — standards

Reviewed head: `cdc02819f852684dc855ecb111bfa261171f40b6`.
Merge base: `3386a1abcc01b5ff07f127e4bc0cc07ecbe0bc37`.
Observed newer origin/main: `8b5f2b3d8662a7f31fd8b2b39d2d19e588b1cbbf`, not incorporated into the reviewed head or treated as qualified here.

**Verdict: one actionable P2 finding. This standards lens is not clean.** No implementation, repository, GitHub or goal mutations were made. No other round4 reviewer report was consulted.

## S1 — P2: Wrap malformed owned timer-delivery JSON at the BPMN boundary

Location: `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnDurationTimer.cs:74`.

After `RevalidateSourceAsync` successfully verifies the exact source, scope, fingerprint and current delivery claim, `payload.Deserialize<BpmnDurationTimer>(Web)` can raise a raw `System.Text.Json.JsonException`. There is no owned-data wrapper in this handler or in `RuntimePostCommitIntentDispatcher.DispatchAsync(PostCommitDeliveryContext, ...)`, which returns the handler's task directly. This crosses from the BPMN feature into its Runtime consumer, contrary to ratified framework §2.23.5. It is persisted owned intent interpretation, not transport request parsing.

A synthetically malformed retained timer payload containing the JSON string `"broken-owned-timer"`, with an otherwise matching fresh source/claim, reproduces the problem through the actual public runtime dispatcher and actual BPMN handler. It returns a raw JsonException with no inner exception and a message containing only the CLR timer type and JSON position, omitting the model execution and original outbox identity. The missing-payload control traverses the same source checks and returns BpmnModelValidationException as expected. No interpreter decision, queue consumption or physical recovery claim is involved in this negative probe.

Wrap the owned typed-payload interpretation using the existing BpmnModelValidationException, preserving the JsonException as InnerException and including the source's model/native execution ID, outbox ID and operation/kind. Preserve requested cancellation and the existing physical source/claim checks. Audit the analogous new owned-payload `Deserialize` calls in BpmnNativeWorkDelivery and BpmnEmbeddingDelivery as part of this same boundary correction; they have the same unwrapped shape, although this review's dynamic counterexample targets the timer line above.

Evidence:

- `probe-dispatcher.log`: both actual-dispatcher observations and full stack; failing path is line74, control is line73.
- `probe/Program.cs`: probe source. It uses interface-backed source/qualification test doubles and the actual boundary/handler/dispatcher products; it does not replace their source checks with a fake boundary.
- `provenance.json`: executing product hashes and byte-equivalence of the implicated classes to this exact reviewed head.
- `generate-probe-project.py`: generates a fresh probe project against an explicit current source-built Workbench product directory, without requiring a historical machine path.

## Scope and checks

Read the live repository and org AGENTS, ratified framework/Elsa rules and the contributor review checklist. Draft/provisional sections were not treated as ratified blockers. Reviewed the whole PR's production/package/API/migration/registry/retention changes, declared profile and compatibility boundaries, task/spec lifecycle and evidence index. Particular attention went to new Core ownership/public extension contracts, internal implementations, optional constructor/wire/fingerprint preservation, scoped initialization and provider default refusal, single checkpoint authority, physical migration history, native policy preservation and deletion/retention protection. No further concrete standards finding survived.

Fetched #2898, PR3410 and all paginated discussion:86 issue comments,213 PR comments at the initial refresh, zero reviews and zero review comments. The repeated CLA notices are identical; their content, the skipped CodeRabbit notice and GitGuardian notice were inspected once. All unique Codex discussion content was inspected with URLs/SHA values normalized only in the reading copy; complete raw JSON remains retained locally. Existing claim6461197535 and the completed-round1–3 history remain unchanged. The new qualification checkpoint is6533484429. A final incremental read also inspected the round4-start checkpoint6533520934 (214 total PR comments), which freezes this review head and records the advanced main without claiming round completion. No human hold or owner decision was invented from bot notices.

Independently verified **304 asset hashes** across the104 frozen baseline assets,32 latest gate assets,66 actual Studio assets and102 embedded ETL-origin cancellation assets. Reconciled the six compressed TRX results:473 BPMN +3083 Runtime +39 hosting +1788 RuntimeEF +594 Architecture +212 native providers =6189 passed, zero skipped. Those are audited recorded results at their declared executing sources, not tests freshly rerun at cdc02819f or required-head CI clearance.

The unchanged Studio verifier was freshly run over the latest actual bundle. It verifies two saved families, four completed new runs, nine original-run observations,12 whole coupled stores,84/92 retained original material/incarnation records and preserved authoring. Its result correctly keeps `directModelStudioAcceptanceProved:false`. The13 recorded current-bundle controls are retained qualification, not claimed as a fresh review rerun. No browser interaction or new actual-host journey was performed by this reviewer.

The probe executes source-built products identifying source76e8139dc7744781fba864f8247c180134eaaaed. Implicated source files and all production files under Essentials/src/Extensions are byte-equivalent at cdc02819f. The only fresh .NET runs were the narrow standalone negative probe and its public-dispatcher variant; both commands exited0, recording the expected observations. They are not whole-project or actual-host acceptance. Temporary project references are historical command provenance; reproduction generates fresh references to explicitly supplied products.

The current public API/version audit retains its source boundaries. The existing unanswered magnitude card/needs:decision remains a merge hold; no preview exception, automatic magnitude pass or binary ApiCompat was inferred. Owner-deferred GitGuardian39189012/scan, exact-head required checks, protected merge and resulting-main qualification remain separate known gates. Pending non-required CLA was not signed. Broader #2907/direct-model Studio, upgraded-host/wider ETL, #3242 and Orleans/task-runner deferrals remain open as declared.

## Reproduction

From a checkout built at76e8139dc, or a later build with the recorded implicated-source equivalence:

1. Run `python3 generate-probe-project.py --products <source-built-workbench-output> --output <new-private-probe-directory>` beside this report/probe source.
2. Run `dotnet run --project <new-private-probe-directory>/Probe.csproj -c Release` through the normal build-slot wrapper.
3. Observe the missing-payload domain-exception control followed by the raw-JsonException malformed-payload result.

The prebuilt products and every raw fixture row remain untouched. No owned app host was launched; no user's process or tab was touched. Repository worktree remains clean at the reviewed head.
