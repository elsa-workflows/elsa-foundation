# Elsa.Studio.Hosting.Themes

Optional server-side Studio Theme Store, directory theme sources and material assets. Reference this package alongside `Elsa.Studio.Hosting`; it has no dependency on the Web application or Nuplane.

```csharp
using Elsa.Studio.Hosting;
using Elsa.Studio.Hosting.Themes;

var builder = WebApplication.CreateBuilder(args);
var studio = builder.AddElsaStudio();
studio.AddElsaStudioThemes(options =>
{
    options.StoreFile = "state/themes.json";
    options.AssetsDirectory = "state/materials";
    options.ThemesMode = StudioThemeSourceMode.AddToBuiltIns;
});
var app = builder.Build();
app.UseElsaStudio();
app.MapElsaStudio();
app.Run();
```

Call `AddElsaStudioThemes` once before `Build`. Registration discovers the three features; select the desired IDs in the configured Studio shell's `Features` section of `shells.json`:

| Feature | Capability |
| --- | --- |
| `ThemeStoreCore` | Store reads, material content and core capability endpoint. |
| `ThemePicker` | Picker capability; depends on core. |
| `ThemeManagement` | Theme, default, visibility, import/export and material mutations; depends on core. |

The existing `StudioApi` feature remains required. The addon maps selected feature endpoints through `MapElsaStudio`; it needs no additional mapping call. All theme endpoints use `StudioBridgeAuthorization.PolicyName`, including the host's authentication and policy customization. Base Hosting alone registers no theme routes.

Configuration binds the `Themes` section before the optional callback. Existing string names `BuiltInsOnly`, `DirectoryOnly` and `AddToBuiltIns` remain case insensitive and accept surrounding whitespace. Blank, unknown and numeric mode values fail startup validation. Callbacks use the public `StudioThemeSourceMode` enum.

| Option | Default and meaning |
| --- | --- |
| `StoreFile` | `studio-theme-store.json`: writable managed JSON state. |
| `AssetsDirectory` | `theme-assets`: writable uploaded PNG, WebP and JPEG materials. |
| `ThemesDirectory` | Unset: optional directory of theme collections, or a single JSON collection file. |
| `ThemesMode` | `AddToBuiltIns`: select enabled, published built-in, directory and managed themes. `BuiltInsOnly` and `DirectoryOnly` restrict selection while retaining the complete management inventory. |
| `DefaultThemeId` | Unset: optional configured default overriding the managed default when eligible. |

Relative paths resolve against `ContentRoot`, including existing directory/file links. Applied choices are copied during startup; changing configuration or a retained callback object later does not redirect storage. Restart the host to apply different choices. Keep store and material paths application owned and writable; directory sources are read only. Source collections, including file links and collections discovered by directory scans, are excluded from material listing, content and deletion by physical file identity. The material directory cannot equal or descend from the store file path, and the managed store must be outside the material directory so asset operations cannot expose or delete it. A source directory may contain the managed store; entries resolving to that store are excluded from source enumeration. Storage comparisons respect the containing macOS volume's case sensitivity.

Directory definitions take precedence by ID in read responses. Unrelated writes persist only managed state, preserving existing shadowed definitions and defaults without copying directory content. Explicit save/import/duplicate operations retain their existing JSON formats and validation. Deleting a source-owned definition returns a conflict; remove or edit it at its source. An empty eligible inventory has an empty default ID. Built-in definitions remain reserved and read only, with visibility managed separately.

Mutations share Hosting's process-lifetime configuration gate. JSON writes use temporary sibling files and atomic replacement. Materials accept nonempty PNG/WebP/JPEG uploads up to 2 MiB, publish only completed uploads, and clean cancelled staging files. Content reads retain an opened stream while a concurrent delete removes the directory entry. Asset IDs are generated GUID filenames; unrelated files in the configured directory are neither listed nor served.

Browser pages and bundled assets belong to Hosting's frontend delivery contract; this addon supplies the optional server capability. The reference Web application composes it through these public APIs. Program [#3340](https://github.com/elsa-workflows/elsa-foundation/issues/3340), task [#3360](https://github.com/elsa-workflows/elsa-foundation/issues/3360), and repository spec `1011-studio-theme-store` track qualification and delivery.

The [standalone custom-host guide](../../../docs/custom-hosting.md) covers explicit candidate sources, optional feature selection and supported filesystem limits. Normal managed-file/link qualification does not cover broken/cyclic or externally mutated source trees.

Theme definitions accept optional `workspaceStyle` metadata: `linen`, `minimal`, `quiet`, `focus`
or `open`. Absence retains the theme's original treatment. Save, clone, directory sources and pack
import/export preserve this value; unknown values are rejected. The built-in IDs `builtin:porcelain`,
`builtin:atelier` and `builtin:graphite` are reserved. The original `atelier` ID remains unchanged and is
shown as Atelier Original. See [branding](../../../docs/branding.md#minimal-studio-presets) for host
defaults, independent user choices and login visual customization.
