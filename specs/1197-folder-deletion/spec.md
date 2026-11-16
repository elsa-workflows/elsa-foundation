# Feature Specification: Safe Workflow Folder Deletion

**Status**: Implemented
**Created**: 2026-11-03
**Issue**: #3713
**Program-goal state**: none/free-flow

## User Scenarios & Testing

### 1. Delete a folder and preserve its contents

Given Demo contains two workflows, deleting it previews both workflows even when the browser is filtered or paginated. Confirmation moves those workflows to its parent, or Unfiled at the root, and removes only Demo. Child folders move up one level with their hierarchy and contents intact.

### 2. Explicitly delete directly assigned workflows

An initially unchecked checkbox names the number of active workflows directly in Demo. Checking it changes the confirmation button to name that count and explains that the workflows remain restorable. Confirmation soft-deletes those workflows and moves their organization to the surviving parent/Unfiled. Workflows inside child folders remain unchanged. Already deleted workflows retain their deletion facts and remain restorable.

### 3. Cancel or recover from a stale or denied operation

Cancel makes no changes. A contents change after preview requires a fresh preview and explicit confirmation. Denied resource access, name collisions and persistence failures leave the folder, workflows and hierarchy unchanged. The dialog retains actionable error/reload controls and restores focus when closed.

## Requirements

- **FR-001**: Preview authoritative counts, folder name, surviving destination, direct child-folder count and active/already-deleted workflow counts independently of browse state.
- **FR-002**: Preserve every workflow by default; move direct child folders up one level without flattening descendants. Reject sibling-name collisions without partial changes.
- **FR-003**: Offer an unchecked opt-in to soft-delete only active workflows directly in the folder. Clearly exclude child-folder workflows, state recoverability, and reflect the exact count in the action label.
- **FR-004**: Validate every affected resource and tenant before returning inventory or mutating; reject stale content/destination/hierarchy observations atomically.
- **FR-005**: Retain authored drafts, immutable versions, tags, favorites, publication and runtime state. Record attributable folder, placement and soft-deletion facts with the mutation.
- **FR-006**: Keep existing empty-only API clients safe. Advertise richer deletion only when supported by the backend and organization is enabled.

## Success Criteria

- Both preservation and opt-in scenarios produce the stated durable state after restart, with zero lost workflow identities or versions.
- All denied, stale, collision and cancellation cases cause zero partial mutations.
- Keyboard and pointer users can inspect counts, choose, confirm, cancel and reload the dialog; focus returns to a surviving navigation target.

## Assumptions and Scope

This extends spec 1173's empty-folder-only scope on the owner's approval. No recursive folder deletion or permanent workflow deletion is included. Counts include previously deleted definitions because they also require a surviving folder association. Existing published executables and running instances continue under existing lifecycle rules.
