# Shared field contract

StudioField retains label, description, meta and React children. Native-child adoption preserves caller id/ref/events/value and merges aria-describedby. StudioFormField offers stable render-function control attributes and optional hint/error/adjacent actions. A visible label targets the actual native control; group legends never substitute for individual choice/control names. Metadata and actions are outside the input label. Never blanket-disable auxiliary actions by setting the decorative fieldset disabled.

Outline geometry and state styling are shared; colours and surfaces come from semantic theme tokens. Long labels remain in flow and textarea labels remain outside its scrolling element. Existing module layout classes and native requirements/serialization are retained.

Setting and activity-property editor contributions may declare optional `presentsField` when they own their shared field and descriptor caption. Existing contributions omit it and retain host captions. Explicit opaque controls pass `controlId` and forward `data-studio-field-control` to their native editor. No arbitrary component tree is recursively cloned.
