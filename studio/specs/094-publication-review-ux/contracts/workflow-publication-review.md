# UI and Client Contract: Workflow Publication Review

## Purpose

Define the observable Studio behavior for reviewing and publishing a workflow, while preserving Foundation as the authority for policy, target resolution, version validity, concurrency, promotion, and activation.

## Modal Shell

The dialog is one modal with exactly three structural regions:

1. **Persistent header** — “Workflow publication” context and current view title.
2. **Scrollable body** — all review, validation, advanced evidence, success, and recovery content.
3. **Persistent footer** — the current action set.

The body alone scrolls. Header and footer remain visible at supported viewport heights. A success/error/status element cannot append below the footer or displace it from view.

## Default Review Contract

The body exposes the following in decision order without requiring a disclosure:

1. **Publication channel** and a short explanation of `default` as the normal publication channel.
2. **Effect**: “Replace the currently published version” for `default` or an occupied named channel; “Create a separate publication channel” for an unused named channel.
3. **Version**: automatic mode reads “Assigned automatically by version policy” with an expected value only as provisional information; exact mode names the requested exact version and its authoritative status.
4. **Readiness**: ready, checking, or blocked with the blocking reason visible.
5. **Comparison baseline**: `channel · version`, or “New channel — no previous publication.”
6. **Compact changes**: a concise draft-change summary.

The dialog explicitly says that the captured current editor state is included and saved as part of publication. It must not claim that the compact changes determine the automatic version.

## Publication Channel Interaction

- UI labels say **Publication channel**, never unexplained “Slot.” Wire fields may retain `slotName`.
- An existing-channel selector lists advertised channels and identifies `default` as normal.
- A distinct “Create new channel” option reveals the channel-name field. Existing-channel selection does not request free text.
- Helper text says that named channels (for example, `canary`) remain separately addressable publications.
- The UI derives and displays effect from the selected/authoritatively resolved channel; it does not expose routine replace/side-by-side radio buttons.
- A selected occupied channel maps to the existing wire intent plus `expectedPublicationId` so Foundation can reject a stale replacement.
- A new channel still requires authoritative review. If it becomes occupied before review completes, Studio shows the resolved replacement effect and must not publish until the author sees current evidence.

## Disclosure Contract

**Changes details** contains the detailed activity, input, output, and trigger counts.

**Advanced details** contains policy source/revision, authoritative trigger claims, internal promoted/Executable/Source Reference/publication identities, candidate/concurrency/preflight evidence, and other technical diagnostics.

Blocking validation errors and trigger conflicts are outside disclosures and announced immediately. A disclosure cannot be required to learn that Publish is blocked.

## Authoritative Review Contract

- Opening review requests authoritative preflight without saving, promoting, or publishing the captured draft.
- Channel and version changes invalidate the preceding preflight immediately.
- Studio automatically calls Foundation's promotion/version preflight for the latest channel and custom-version selection, announces Checking, and disables Publish while it is pending. In automatic mode, Foundation's policy-assigned proposal is rendered; Studio never derives semantic version policy from the draft diff.
- Preflight responses are latest-only: a response that does not match the current captured candidate, normalized channel, version selection, and request generation is discarded.
- The primary mutation button always says **Publish** (or a precise progress label while the mutation itself is active). It does not become “Review target.”
- Publish is enabled only for current matching evidence that permits activation. Final mutation preserves the resolved target, preflight token, and expected-publication concurrency guard.
- Existing permissions and host/workflow policy authority do not change.

## Outcome and Recovery Contract

### Success

The review body is replaced with a compact confirmation that names the confirmed version and Publication channel. The footer offers:

- **Close**
- **Open published executable**

Opening the executable is secondary. If navigation/refresh fails after publication, report that separately without making the completed publication appear to have failed.

### Validation failure before promotion

The body states whether the captured draft was saved and explicitly states that no version or publication was created. Publish remains disabled until a new valid review is created.

### Promotion retained; activation failed

The body states that the promoted version was retained but not activated in the selected Publication channel. The footer offers:

- **Close**
- **Retry publication**

Retry uses the retained promoted version and does not save or promote another version. If preflight is stale, it is refreshed before retrying activation.

## Accessibility Contract

- The dialog is labelled by its visible title and describes dynamic status through a polite live region; blocking errors use alert semantics without duplicating routine status announcements.
- Focus enters a meaningful dialog control, remains trapped according to the existing dialog behavior, and returns to the Review & publish trigger on Close.
- Channel selection, create path/name, Edit version, disclosures, all footer actions, and the scrollable body are keyboard operable.
- Publish disabled state includes visible/in-text reason; checking and mutation progress are announced.
- When the body changes to success/recovery, focus moves to its title or the first meaningful outcome control without causing the persistent footer to disappear.
- Escape is available only when no mutation is in progress, matching existing safety behavior.

## Foundation Capability Relation: Exact Version (Optional)

This is an additive companion contract. It is intentionally capability-discovered, so Studio remains compatible with current Workflow Design capability documents.

### Discovery

Foundation advertises templated `workflow-draft-promote-version-preflight` and
`workflow-draft-promote-exact-version` relations in the existing Workflow Design API capability
declaration only when both its non-mutating promotion preflight and draft promotion honor requested
exact versions. Studio treats either relation's absence as unsupported for exact-version editing.

### Client behavior when absent

- Do not render Edit version or an exact-version field.
- Send the existing automatic-version request shape unchanged.
- Retain all channel, preflight, concurrency, outcome, and retry behavior.

### Client behavior when present

- Show Edit version as progressive disclosure; automatic remains default.
- Include normalized requested version and mode in authoritative preflight identity and stale-result matching.
- Send the exact version only through the advertised Workflow Design relations/contracts.
- Present server-specific invalid/duplicate/non-forward feedback beside the version field and keep Publish disabled.
- Render the server-confirmed promoted version in the success/recovery state, even if it differs from a provisional display value.

### Foundation requirements for the relation

- Validate Semantic Version syntax and precedence server-side.
- Accept only unused versions with precedence greater than the latest promoted version; forward prerelease versions are valid.
- Reject invalid, duplicate, and non-forward requests before promotion with actionable diagnostics.
- Bind exact-version validation to the draft identity and normalized version selection. Publication
  target, policy, concurrency, and review-token evidence remains bound independently by publication
  preflight; promotion and activation each repeat their authoritative checks at mutation time.
- Preserve existing authorization and policy decisions; custom version creates no new permission.
- Return a final confirmed version from promotion/outcome.

## Compatibility Boundaries

- `slotName`, resolved action, expected publication identity, preflight token, promotion, Executable, and Source Reference semantics remain compatible with existing Foundation contracts.
- The Studio redesign may ship without the exact-version capability.
- No new role, policy, workflow persistence field, Studio preference, or modal/wizard route is introduced.
- The current captured-draft save behavior is retained: review itself is read-only; Publish saves the captured state as part of the first mutation path.
