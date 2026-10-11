# UI State Model: Workflow Publication Review

This feature adds no persisted Studio domain data. It re-expresses an existing captured workflow publication around explicit client-side decision state and server-authoritative evidence.

## Publication Channel Selection

**Owner**: Publication review session.

| Field | Values | Notes |
| --- | --- | --- |
| `mode` | `existing` \| `create` | Existing is the default when the normal `default` channel is available. |
| `channelName` | normalized channel name | Maps to wire-level `slotName`; names are trimmed before an authoritative request. |
| `selectedExistingChannel` | an advertised channel or `default` | Used only in `existing` mode. |
| `newChannelName` | locally entered candidate | Present only in `create` mode; it becomes `channelName` after normalization. |

`default` is displayed as the normal publication channel. User-facing copy explains that a named channel such as `canary` remains separately addressable. The UI does not independently persist or infer publication occupancy.

## Channel Target View

Derived from the latest matching authoritative preflight plus the last known channel list:

| Property | Meaning |
| --- | --- |
| `effect` | `replace` or `create`; user-facing result, never an independent routine choice. |
| `baseline` | The selected channel and its current published version, or no previous publication. |
| `expectedPublicationId` | Wire-level optimistic-concurrency guard for an occupied target. |
| `canPublish` | True only with current, ready, matching authoritative evidence. |

Before a matching preflight completes, the display may state the anticipated effect from the known channel list, but readiness is `checking` and the definitive effect/baseline is the server result. If the create path becomes occupied, the authoritative result supersedes the anticipated “create” effect and must make the replacement risk visible.

## Version Selection

**Owner**: Publication review session.

| Field | Values | Rules |
| --- | --- | --- |
| `mode` | `automatic` \| `exact` | Starts as `automatic`; `exact` is reachable only when capability is present. |
| `automaticLabel` | provisional policy-assigned display value | Informational only; server confirms the final version. |
| `requestedExactVersion` | normalized Semantic Version or raw input | Sent only in `exact` mode and only to a capability that supports it. |
| `capability` | absent or advertised exact-version relation | Absent hides Edit version and preserves existing automatic requests. |

The authoritative server validates exact versions. A valid request is a syntactically valid, unused Semantic Version whose precedence is greater than the latest promoted version. Forward prerelease labels are valid. Studio may provide format feedback but cannot make a version publishable from local validation alone.

## Authoritative Preflight Evidence

**Owner**: Foundation; cached only for the active review session.

Existing evidence remains the baseline:

- immutable captured draft candidate hash and definition identity;
- normalized channel / resolved action;
- preflight token;
- policy source and revision;
- activation permission and trigger claims/conflicts;
- the target's publication identity for concurrency protection.

When exact-version capability is advertised, matching evidence additionally carries the normalized requested-version disposition and the server's expected/confirmed version display. A preflight is usable only when all relevant values match the current review snapshot:

1. captured candidate hash / revision;
2. normalized publication channel;
3. requested-version mode and exact requested value when present;
4. target occupancy/concurrency identity;
5. latest request generation.

## Review Phase

| Phase | Meaning | Footer actions |
| --- | --- | --- |
| `reviewing` | Initial or changed-target authoritative evidence is pending. | Close/Cancel; disabled Publish with Checking state. |
| `ready` | Matching preflight permits activation. | Cancel and Publish. |
| `blocked` | Local validation, server validation, policy, or trigger conflict blocks mutation. | Close/Cancel; Publish disabled. |
| `publishing` | The captured snapshot is saving, promoting, or activating. | Disabled Close and disabled Publish with progress. |
| `savedFailure` | Captured draft saved but promotion did not complete. | Close and contextual retry when safe. |
| `activationRecovery` | Version promoted and retained; activation did not complete or preflight went stale. | Close and Retry publication. |
| `success` | Activation completed. | Close and Open published executable. |

The implementation may retain compatible internal `PublicationReviewPhase` names, but the rendered state must have these observable meanings. At most one current action footer is rendered.

## Publication Outcome

Foundation-owned facts rendered after mutation:

- confirmed promoted version and definition version identity;
- Executable identity and Source Reference identity;
- active Publication channel;
- activation outcome;
- optional executable-inspection navigation target.

Internal IDs are not part of the routine outcome hierarchy but remain available in Advanced details when appropriate. A refresh/navigation failure after a truthful success is a secondary UI failure and must not rewrite the publication outcome.

## State Transitions

### Open review

1. Finish any existing autosave, capture the current draft, and pause autosave as existing publication preparation does.
2. Discover policy/channels and create the review state.
3. Start authoritative preflight for the default/current selection.
4. Enter `ready` only when preflight matches and permits the current choice; otherwise enter `blocked` or keep `reviewing`.

### Change existing channel or create path/name

1. Normalize the current user selection for display/request purposes.
2. Invalidate usable evidence and increment request generation.
3. Enter `reviewing`; Publish is disabled and an inline Checking status is announced.
4. Request preflight for the latest selection. Ignore any result whose generation or intent no longer matches.
5. Render final replace/create effect, baseline, conflict, and readiness from the matching response.

### Change version mode or exact version

1. Switching to `exact`, changing exact text, or returning to automatic invalidates evidence exactly as a channel change does.
2. While local format is incomplete, remain blocked/checking as appropriate and do not mutate.
3. Send the exact value only when the capability relation permits it.
4. A matching server result decides whether Publish becomes enabled.

### Publish

1. Reject locally known invalid drafts before any mutation.
2. Save the immutable captured snapshot once.
3. Promote it once, obtaining the server-confirmed version.
4. Activate the promoted version with matching preflight token, resolved target, and expected-publication concurrency data.
5. Enter `success` with confirmed version/channel/Executable or `activationRecovery` if activation fails after promotion.

### Retry activation

1. Retain saved draft and promoted version.
2. If evidence is stale, refresh it first for the same promoted version/target.
3. Retry only activation; never save or promote a second version.

### Close

- Before promotion: release the review and resume existing editor behavior; explain if no mutation occurred.
- After save but before promotion: preserve the saved-draft truth in the status.
- After retained promotion or success: close without changing the already durable server outcome.
- Restore focus to the Review & publish trigger unless the user deliberately selected Open published executable.
