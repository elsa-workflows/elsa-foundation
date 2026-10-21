# Data Model: Studio Feature Impact Review

This is a client-side model for the existing Feature Management page. It introduces no persisted entity, database migration, or backend request shape.

## Host Draft

One instance per existing target: Studio or Server.

| Field | Meaning |
|---|---|
| Host identity and visible label | Which of the two existing targets owns this state. |
| Loaded baseline | The complete feature state returned for this host, including existing configuration and dependency/setting metadata. |
| Desired state | The complete locally staged feature state for this host. |
| Configuration revision | The existing revision associated with the loaded host configuration. |
| Effective enabled-state roots | Per-host direct enable/disable requests whose requested state remains in the current desired state and still differs from baseline. Retain effective roots across pending edits rather than only the latest action; prune or mark a root inapplicable when a later cascade overwrites its requested state or it returns to baseline. |
| Pending setting edits | Direct setting-field edits whose submitted values still differ from this host's baseline. Track these independently from enabled-state roots so a later cascade cannot erase a still-effective setting change. Raw local values retained on disabled features may remain available for re-enabling, but are not described as applied while the outgoing contract omits them. |
| Retained automatic causes | The earlier required-edge cause/path for an automatic state change that remains different from baseline after its initiating root is reverted. This is historical context, never a current pending root or a new direct edit. |
| Current catalog snapshot | The feature and required/optional dependency relationships used to explain the desired state. |

**Validation rules**:

- Editing or selecting features for one host never changes the other host's draft or roots.
- An enabled-state root is applicable only while its requested state remains effective in the current desired state and differs from baseline. A later cascade that overwrites that state, or an edit that returns it to baseline, prunes or marks the root inapplicable.
- A setting edit leaves the pending set when its field returns to the loaded baseline. It remains represented when the feature's enabled-state root is superseded and the setting value is still changed and included in the outgoing configuration.
- Required dependency/dependant paths are used to explain automatic changes. Optional relationships do not create a path.
- The current draft is derived through the existing cascade behavior; provenance describes its causes and does not create a second cascade.
- Derive dirty comparison, impact and submitted configuration through the existing disabled-feature projection: Apply sends an empty configuration for disabled features. Show outgoing clears, omit unapplied retained-value edits, and do not classify an automatic clear as a direct setting edit. Preserve raw draft values for existing re-enable behavior. Every draft mutation still invalidates review, including a retained-value edit that does not alter the outgoing projection.
- Preserve enough earlier required-edge cause information to explain a remaining automatic state difference when no effective root currently requires it. Clear it when that feature returns to baseline or Reset replaces the draft.

## Impact Entry

One row for a feature whose current desired state differs from its loaded baseline.

| Field | Meaning |
|---|---|
| Feature identity and display name | The changed feature as shown to the operator. |
| Baseline and desired state | The loaded and requested enabled/configuration state. |
| Change classification | Direct if an effective enabled-state root or pending setting edit directly changes this feature; otherwise automatic. |
| Pending roots | Every effective enabled-state root whose required dependency/dependant path causes the automatic change. A direct row can also identify other effective roots that require it. Superseded roots never classify or explain current impact. |
| Reason paths | A cycle-safe required-edge explanation path from each applicable effective enabled-state root, including transitive relationships. If no effective root requires a retained automatic difference, describe its earlier cascade cause/path as historical instead. |
| Changed setting fields | Names of directly changed setting fields as applicable; value presentation follows existing sensitive-setting handling. |
| Reload implication | Existing known metadata only; unknown remains explicitly unknown. |

**Validation rules**:

- The impact list is independent of the feature list's text/category filters.
- Include only current net differences from the host's baseline. Do not retain an entry solely because an earlier action touched that feature.
- Every automatic entry includes all applicable effective enabled-state roots, not only the last action. A root whose request was overwritten by a later cascade is excluded.
- Optional dependencies are excluded from required reason paths.
- A retained automatic difference with no effective current root stays automatic and is explained as an earlier cascade effect. Do not relabel it as direct, list its reverted initiator as pending or imply a disabled initiator currently requires it.
- Example: baseline A=off and B=off with required A → B; enable A then disable A leaves A=off and B=on. Omit net-zero A; show B as retained from the earlier required-dependency enable, with no active A root. Preserve this existing desired state rather than adding automatic dependency cleanup.
- A feature that returns to baseline is omitted as net-zero and cannot remain as a reason for another row solely because an earlier action touched it.
- Example: with baseline `A=off`, `B=on`, and required edge `A → B`, disabling B and then enabling A produces `A=on`, `B=on`. B is net-zero; the earlier `(B, off)` request is superseded and cannot classify or explain any surviving impact. Any still-changed setting field on B remains a direct setting edit independently of that superseded state request.

## Impact Review Snapshot

An ephemeral view of the impact entries and the inputs they describe.

| Field | Meaning |
|---|---|
| Selected host | The exact Studio or Server target being reviewed. |
| Complete desired feature state | The full state to be applied, not just the visible rows or changed rows. |
| Loaded catalog snapshot | The dependency and setting metadata used to derive impact. |
| Configuration revision | The existing host feature-configuration revision associated with the loaded state. |
| Review validity | Current until any draft mutation, host selection change, catalog refresh, or catalog replacement; then invalidated and requires a new review. |

The existing configuration revision does not atomically represent package/manifest dependency metadata that changes after catalog load. A review is not a server-issued graph token and does not promise detection of such an out-of-band change.

## State Transitions

```text
Loaded host state
    └── stage direct edit / cascade ──> Host draft + effective state roots + pending setting edits
            └── review ──> Current impact snapshot
                    ├── cancel ──> Same host draft (no saved change)
                    ├── draft/host/catalog change ──> Invalidated snapshot ──> Review again
                    ├── Apply failure returned ──> Client draft + visible failure; persisted host state may need refresh
                    └── Apply accepted ──> Returned host catalog becomes new baseline
```

Reset replaces only the selected host's desired state with its loaded baseline, clears the prior review, and leaves the other host draft alone.
