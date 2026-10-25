# Home navigation contract

## Destinations

| Entry | Contract |
|---|---|
| Create workflow | Internal `/workflows/definitions?homeAction=create`; Workflows consumes only recognized create, preserves other query/hash/history state and invokes existing creation once. |
| Browse workflows / Browse all | Existing `/workflows/definitions` route. |
| Continue one / chosen candidate | `/workflows/definitions?definition=<encoded exact definition ID>`; load that definition's current draft. |
| Needs attention | Runs-owned `/workflows/instances?incidentHealth=active`, with no added lifecycle predicate. |

Home uses existing guarded internal navigation. Links retain native modified-click behavior. No Home backend write. Recognized create plus selected definition consumes the intent and retains the editor; unknown intent never opens creation. The lazy list must acknowledge a consumed request so later list remount does not replay it.

## Reads and access

Existing GET `/design/workflows/definitions` returns the current complete authorized collection. There is no server take=8 guarantee. Preserve current draftId, filter actual drafts and establish uniqueness before displaying at most eight recent choices. Names may duplicate; IDs distinguish choices. Failed/denied/unsupported is not an empty response. Server read/manage and activity-catalog/capability checks remain authoritative. Do not infer a grant from feature availability or denial from missing exact client keys; current wildcard/subtree/manage→read and anonymous auth-off behavior must survive.

## Recovery and preservation

Selected identity survives stale/no-draft/unavailable outcomes. Retry reads the same identity; Browse is explicit; no silent create/clone/substitution. Late old responses cannot replace a new target. Chooser dismissal restores focus. Home widget contents, order/options and onboarding are unchanged. No persisted credential, runtime artifact or publication-policy change.
