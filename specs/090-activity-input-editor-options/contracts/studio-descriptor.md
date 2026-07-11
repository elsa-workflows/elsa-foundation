# Studio Descriptor Contract

`StudioActivityInputDescriptor.uiSpecifications` remains extensible and may contain:

```ts
interface StudioActivityInputOption {
  label: string;
  value: string | number | boolean;
}

interface StudioActivityInputOptionsProviderDescriptor {
  key: string;
  dependsOn: string[];
}

interface StudioActivityInputUISpecifications extends Record<string, unknown> {
  options?: StudioActivityInputOption[];
  optionsProvider?: StudioActivityInputOptionsProviderDescriptor;
}
```

Compatibility readers continue accepting legacy primitive `options`, `items`, `values`, and direct descriptor `options` arrays.

Editor rules:

- Scalar + options, absent hint → dropdown.
- Collection + options, absent hint or `checklist` → whole-collection checklist.
- Collection + `dropdown` → existing repeater with dropdown element editors.
- Stale scalar → synthetic unavailable dropdown option retaining the exact value.
- Stale collection items → checked unavailable entries removable by the author.
- Provider loading/failure → constrained editor disabled; failure includes retry and never falls back to text.
