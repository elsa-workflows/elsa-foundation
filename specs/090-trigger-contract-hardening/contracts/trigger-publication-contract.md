# Trigger Publication Contract

## Scope

This contract governs publication-time assessment and registration of first-party Event, Timer, Cron, and HttpEndpoint start triggers. It does not define diagnostics APIs, host health, request/schedule dispatch, or publication-wide transactionality.

## Authority chain

1. Authored activity capability and catalog fallback are inputs to compilation.
2. Compilation projects trigger intent into the executable node.
3. Runtime preflight considers only executable nodes marked as triggers.
4. Every configured stimulus provider is asked whether it recognizes each marked node.
5. Exactly one provider must recognize the node.
6. The recognizing provider returns zero or more normalized descriptors.
7. All descriptors and required recurring schedule materialization are validated before trigger or schedule replacement begins.

Runtime does not read activity catalog or workflow-definition state during steps 3–7.

## Provider identity and claiming

- Every provider has a stable, nonblank `ProviderId`.
- First-party ids are explicit constants; a compatibility default derives a deterministic id from a provider's public CLR identity.
- Renaming a fallback-identified provider changes its identity and is a compatibility-affecting provider change.
- Zero claims is an unrecognized-trigger failure.
- More than one claim is an ambiguous-trigger failure listing provider ids in ordinal order.
- Registration order never selects a winner.

## Recognition outcomes

| Outcome | Meaning | Publication result |
|---|---|---|
| Not recognized | Provider does not own this activity type | Try remaining providers |
| Recognized with descriptors | Provider owns an active start trigger | Validate all descriptors and provider-owned materialization |
| Recognized with no descriptors | Provider owns an intentionally non-starting node | Succeed with no start binding |

## Validation ordering

For one artifact, all of the following complete before the first trigger/schedule delete or save:

1. exact-one provider recognition for every classified node;
2. descriptor identity and duplicate validation;
3. existing provider-specific index validators, including HTTP uniqueness;
4. complete Timer/Cron schedule calculation, including a future occurrence.

After semantic validation succeeds, existing replacement ordering applies. Infrastructure failures during delete/save may still leave partial state; this contract does not promise cross-store rollback.

## Failure contract

A trigger preflight failure names, when available:

- artifact id;
- executable node id;
- activity type;
- recognizing or conflicting provider ids;
- failed descriptor/projection facet.

The failure is typed in Runtime vocabulary. Raw parser or scheduling-library exceptions do not escape the boundary unwrapped.

## Compatibility contract

- Existing extractor and indexer entry-point signatures remain available.
- Existing executable and durable state shapes remain readable.
- Same-version activity catalog content is never rewritten to correct classification.
- Corrected classification takes effect on republish.
- `Recognized([])` remains behaviorally unchanged.
