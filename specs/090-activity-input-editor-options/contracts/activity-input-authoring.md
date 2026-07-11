# Activity Input Authoring Contract

## Attribute surface

`ActivityInputAttribute` adds optional `UIHint`, `Options`, `OptionsProvider`, and `OptionsProviderDependencies` properties. `ActivityInputOptionAttribute` is repeatable on an activity input property and accepts `label` plus an attribute-compatible scalar `value`.

Examples:

```csharp
[ActivityInput(UIHint = ActivityInputUIHints.CheckList, Options = ["GET", "POST"])]
public InputArgument<ICollection<string>>? Methods { get; set; }

[ActivityInput(UIHint = ActivityInputUIHints.Dropdown)]
[ActivityInputOption("Low", 1)]
[ActivityInputOption("High", 10)]
public InputArgument<int>? Priority { get; set; }

[ActivityInput(OptionsProvider = "catalog.fields", OptionsProviderDependencies = [nameof(Entity)])]
public InputArgument<string>? Field { get; set; }
```

## Canonical hints

- `ActivityInputUIHints.Dropdown` → `"dropdown"`
- `ActivityInputUIHints.CheckList` → `"checklist"`

Unknown hints remain legal so Studio plugins can claim them.

## Reconciliation rules

- String shorthand expands to `{label: value, value}`.
- Enum option values serialize as enum names.
- Numeric values must be finite and losslessly representable as a browser number; mathematically integral values outside ±9,007,199,254,740,991 are invalid.
- Derived-property option declarations replace the nearest inherited set; no declaration inherits the nearest base set.
- Static shorthand, typed option attributes, and provider keys cannot be combined.
- Invalid metadata throws an `InvalidOperationException` identifying activity type and input name.
