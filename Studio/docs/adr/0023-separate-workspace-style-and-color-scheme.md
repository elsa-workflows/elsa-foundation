# Choose workspace style and color scheme independently

Owner decision, 29 October 2026, implemented under Foundation #3447. This extends ADRs 0021 and 0022.

An existing theme remains a named preset of colors, typography, shape and layout. Browser-local appearance adds independent optional workspace-style and color-scheme overrides. Choosing colors preserves workspace arrangement, requested mode and navigation. Choosing a workspace style preserves colors, requested mode and explicit navigation. Applying a complete preset intentionally clears both overrides.

Quiet Studio, Focus Canvas and Open Studio are supported workspace compositions. Null style preserves the existing preset exactly. Color schemes reuse selectable published themes from the configured theme store; there is no second palette inventory or changed theme-pack schema. Supported modes come from the selected scheme, while an unsupported requested mode remains remembered.

The host publishes separate style and palette identities. Modules read semantic tokens and the existing structural-layout contract; a palette name never selects behavior. Surface-owned styles remain deferred with their owning components. Live appearance updates preserve resource component instances and authoring/inspection state.

Navigation remains independently overridable under ADR 0022. Workspace defaults are left for Quiet/Focus and top for Open. Floating tools retain keyboard-accessible sizing and use adaptive docked behavior on narrow displays. Reset clears both new overrides with the existing appearance/chrome keys. Pending custom identities remain protected until a successful store load; preview is transient and does not replace persisted choices.
