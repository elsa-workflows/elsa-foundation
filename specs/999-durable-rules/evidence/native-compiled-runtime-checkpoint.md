# Native Rules publication and compiled-runtime checkpoint

This continues [#3181](https://github.com/elsa-workflows/elsa-foundation/issues/3181) from `c8d8c31bd53c9b231e880947d55fd236f0feb960`. It does not close the Rules leaf or the unified-execution program. Commands, results and log hashes are retained in [the receipt](native-compiled-runtime-receipt.json).

## Evidence added

- Actual CLR reconciliation, tenant-scoped `ISubmitWorkflowDefinitionCommand`, mediator-dispatched `PublishWorkflow`, registered compiler and SQLite persistence produce an active publication. A fresh tenant-bound scope reads the definition/version, executable, source reference and publication ledger. Assertions check their identities and the complete Rules artifact/pin, including canonical bytes, hash and vocabulary count. The executable cache is disabled in both publication and native-runtime fixtures so readback exercises persistence.
- The successful Sequence journey transfers that actual published executable unchanged to an isolated durable runtime without design-store services. It uses a test-created live source reference and the real start dispatcher in the owning partition. Fresh reads verify the behavioral hash and retained resume-target metadata. This proves artifact execution, not production deployment or transfer of publication authority.
- The real Rules store and evaluator derive `derived` from `seed`, then consume a second round establishing the fixed point. The following WriteLine has no invocation before delivery. After canonical result delivery, one following-activity attempt and the Sequence complete; the delivery obligation is acknowledged.
- A one-round budget faults through the native activity and parent; a semantic refusal retains its diagnostic/result and follows Sequence's ordinary Refused completion; parent cancellation before model work preserves the seed facts and settles delivery with parent-terminal proof. Each case asserts authoritative persisted state.
- The existing Flowchart parallel sibling/join journey retains its assertions and uses the shared durable fixture. Rules completion alone does not release the join or following activity.

## Review and fixture corrections

The first executable checks found fixture issues rather than new production defects. The generic behavioral harness normalizes an executable's persistence mode, which changed the compiled content hash; the fixture now saves the unchanged compiled artifact and verifies a fresh read. Its generic start helper resolves stores under the default scope; the fixture now invokes the registered start dispatcher inside the owning partition. Resume-target dictionaries are compared structurally rather than by record reference equality.

Global CLR reconciliation, tenant-owned workflow mutations and fresh verification use separate explicitly bound scopes. The full publication fixture composes the normal trigger extractor and recurring-schedule projection feature required by the durable activation path. It does not replace missing services with successful stubs. Review also strengthened the persisted-publication pin assertion to compare the full artifact, not just its implementation identity. Shared setup and cleanup were extracted; only fixture-owned temporary files are deleted.

No production runtime behavior changed in this checkpoint, so no new production-code removal control is claimed. Earlier identity/result-descriptor controls remain documented in the [compiler/composite checkpoint](native-integration-checkpoint.md).

## Remaining acceptance

The Rules implementation identity is explicitly synthetic in these source-built tests. Workers are stepped through their real execution services; this is not a background-process or cluster demonstration. The manually supplied runtime source reference is not a REST publication claim. Workbench HTTP authorization, six-module package-host integration, unavailable retry in the complete native journey, and retained upgrade/rollback acceptance remain open. T027–T032 remain incomplete until their integrated gates pass.

For Workbench qualification, preserve the actual package/deployment identity reader. It requires the semantic Rules assemblies in the default load context and matching package entries/assets in the active host dependency manifest. An isolated Workbench build using exact local-feed Rules packages plus normal host feature discovery is the next candidate; Nuplane-only loading and a source-build identity substitute are not equivalent evidence. This composition is not yet implemented or qualified by this checkpoint.

## Validation

Final uncached-fixture run: **274 Rules tests passed**, zero failures or skips. **585 architecture tests passed**, zero failures or skips. Restore, map generation, generated solution-filter check and diff checks passed. Maps report zero runtime-to-design signals, missing catalogs or unlinked catalogs. Four bounded review rounds ended clean within this checkpoint, including independent source/evidence review; compilation attempts are not counted as review rounds. Existing unrelated build/analyzer warnings were not suppressed. The final cache setting affects only the test fixture; no production sources changed after architecture validation.
