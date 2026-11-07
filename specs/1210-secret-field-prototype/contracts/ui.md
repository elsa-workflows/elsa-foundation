# Prototype UI contract

- Keep the existing accessible control names (Technical name, Type, Value, Expires, Display name, Description, Tags, Store, Scope, Custom scope value).
- Each label targets exactly one native control. Help/error IDs are referenced by aria-describedby; invalid fields expose aria-invalid. Reveal is a separate named button.
- Text/password empty labels float on focus, nonempty content or native autofill. Select/date/multiline labels stay raised. Label area cannot overlay input text or trailing actions.
- Disabled controls are identifiable and unavailable; read-only text remains focusable and selectable. Pending creation uses disabled fields and busy form state, with draft retained on failure.
- Use Studio semantic roles and inherit typography/radius; respect high contrast, forced colors and reduced motion.
- This contract is module-internal prototype evidence, not a newly published studio-ui API.

## Preview presentation comparison

The preview offers Filled and Outlined radio choices in its toolbar and open-dialog header. Outlined is the default; URL `field-style` overrides the stored preview choice. Outlined keeps labels permanently raised on the border, including empty/select/date/multiline fields. Long/enlarged labels wrap in flow. Switching preserves existing controls, values, validation and keyboard containment; a checked radio is each group's only tab stop. This choice does not alter the composed product or freeze a shared API.
