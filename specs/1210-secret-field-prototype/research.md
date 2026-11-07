# Research

- **Decision**: Prototype within Secrets using native inputs/selects/textarea and semantic tokens. **Rationale**: avoids committing a shared contract before exercising the real dialog across themes. **Alternatives**: immediate global CSS conversion has uncontrolled coverage; adding Material Web changes dependency and styling contracts.
- **Decision**: Float ordinary text/password labels; keep select, date and multiline labels raised. **Rationale**: native prompts, long values and multiline scrolling need a stable name position. **Alternative**: forcing every native control into the empty floating state risks overlap.
- **Decision**: Associate descriptions/errors beside each field, place scope guidance inside More options and store guidance beside Store. **Rationale**: current dialog has value help after expiry and scope help outside the optional section.
- **Decision**: Enumerate themes/modes from the current registry and use an isolated interactive preview plus real-host proof. **Rationale**: completeness remains executable and fixture evidence is not confused with product acceptance.

References: [Material Web text fields](https://github.com/material-components/material-web/blob/main/docs/components/text-field.md), [Studio token contract](../../Studio/docs/design-tokens.md), [UX acceptance](../../docs/contributing/studio-ux-acceptance.md).
