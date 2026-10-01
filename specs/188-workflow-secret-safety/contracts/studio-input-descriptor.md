# Contract: Studio input descriptor and editors

Repository: `elsa-workflows/elsa-foundation-studio`. Planned from this repository; nothing in Studio is changed by
this specification task. Paths were read from Studio `origin/main` (see [research R14](../research.md)).

## SDK type (FR-015)

```ts
export interface StudioActivityInputDescriptor extends StudioActivityPropertyDescriptor {
  // existing members unchanged
  /** Activity-declared: the value must not be shown or logged. Absent on older backends. */
  isSensitive?: boolean | null;
  /** Activity-declared: accepts only a secret reference. Implies isSensitive. Absent on older backends. */
  isCredential?: boolean | null;
}
```

Applied to the canonical `src/apps/Elsa.Studio.Web/Client/src/sdk/index.ts` and to every hand-maintained copy that
declares `StudioActivityInputDescriptor`: `src/extensions/Elsa.Studio.Secrets/Client/src/studio-sdk.d.ts`,
`src/extensions/Elsa.Studio.ExpressionEditors.JavaScript/Client/src/studio-sdk.d.ts`,
`src/extensions/Elsa.Studio.ExpressionEditors.Liquid/Client/src/studio-sdk.d.ts`.

The backend view fields `isSensitive` and `isCredential` already reach the descriptor through
`toActivityDescriptor`'s pass-through of `inputs`; no mapping change is needed.

## Credential inputs (FR-016)

- Default syntax: `Secret` when `isCredential` is true, ahead of `descriptor.defaultSyntax` and `"Literal"`
  (`readWrappedInputValue`, `src/essentials/Elsa.Studio.Workflows/Client/src/activityProperties.ts`).
- The syntax picker for the input offers only `Secret` (`ActivityPropertiesPanel.tsx`). No literal or text editor is
  rendered for the input. If the `Secret` expression descriptor is not available (Secrets module not installed),
  the input renders a fixed "Install the Secrets module to bind this credential" state, not a literal box.
- An existing authored literal on a credential input loads as an error state that shows no value and offers
  "Replace with secret". The backend refuses to save it anyway.

## Masked editor (FR-017, FR-009)

- New built-in property editor `studio.property.password`, registered in
  `src/apps/Elsa.Studio.Web/Client/src/app/propertyEditors.tsx` with an order that wins over the single-line editor.
- `supports`: text-like type and (`hasUiHint(descriptor, "password")` or `isSensitive && !isCredential`).
- Renders `type="password"` with `autoComplete="new-password"`. It never renders the stored value. A stored value
  shows as "Value set" with a "Replace" action, which starts an empty field.

## Tests (vitest)

- `src/apps/Elsa.Studio.Web/Client/src/__tests__/registry.test.ts`: resolution order, password ahead of single-line.
- `src/apps/Elsa.Studio.Web/Client/src/__tests__/property-editors.test.tsx`: masked while typing, no prefill on
  reload.
- `src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/`: credential input resolves to the secret picker with
  literal entry unavailable.
