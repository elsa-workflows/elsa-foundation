# Public theme package contract

Namespace/package Elsa.Studio.Hosting.Themes:

- StudioThemeStoreOptions: StoreFile=studio-theme-store.json, AssetsDirectory=theme-assets, ThemesDirectory=null, ThemesMode=StudioThemeSourceMode.AddToBuiltIns, DefaultThemeId=null.
- StudioThemeSourceMode: BuiltInsOnly, DirectoryOnly, AddToBuiltIns. Legacy configuration and response strings retain these names; numeric/blank/unknown configuration values are invalid.
- ThemeHostingExtensions.AddElsaStudioThemes(this StudioBuilder, Action<StudioThemeStoreOptions>? configure=null): StudioBuilder; bind Themes before callback, before Build, once per application.
- Existing ThemeStoreCoreStudioFeature, ThemePickerStudioFeature and ThemeManagementStudioFeature keep IDs ThemeStoreCore/ThemePicker/ThemeManagement. Features are independently selected, with picker/management depending on core.

No new public endpoint DTOs, writer, file/provider framework or service interface. Existing /_elsa/theme-store routes retain their authorization and JSON shapes. Missing optional features yield absent routes. Invalid theme modes/storage choices fail options startup validation. Installed choices are copied and require restart.

The writable managed document stays separate from directory-enriched responses. Explicit theme mutations operate only on intended managed records. Unrelated writes cannot adopt directory definitions, overwrite shadowed managed records or turn source policy into stored authority. Source-owned mutations that cannot persist return an explicit conflict/refusal. Preserve JSON compatibility, reserved built-in IDs, source precedence and import/export version1.

Store/asset paths are host-owned local files, independent of source location. Theme and asset IDs never select arbitrary paths. Material upload types/2MiB bound, appearance safety rules, atomic write/cancellation cleanup and the whole-operation mutation gate remain mandatory.
