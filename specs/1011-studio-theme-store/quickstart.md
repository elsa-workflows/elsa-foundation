# Qualification journeys

1. AddElsaStudio(...).AddElsaStudioThemes(options => { options.StoreFile="state/themes.json"; options.AssetsDirectory="state/assets"; options.ThemesDirectory="sources/themes.json"; }); enable the desired existing shell feature IDs in the selected shell. Build, UseElsaStudio, MapElsaStudio; no copied class or custom theme Map call.
2. Run with a content root different from CWD. Exercise core/picker/management feature combinations and authenticated/anonymous requests. Verify base host has no theme routes/dependency.
3. Save a managed theme, overlay its ID from the directory, perform an unrelated visibility/default/asset write, remove the directory definition and verify the original managed theme returns. A directory-only new ID must disappear, not resurrect from the managed file.
4. Exercise source modes, reserved/retired IDs, import/export and appearance validation, atomic concurrent edits, valid/invalid/cancelled asset operations and nondefault store/asset paths.
5. In the browser, delete the selected custom theme when the projected default is empty and inspect the next selection/empty state; verify Appearance and picker semantics under empty DirectoryOnly.
6. Run affected whole .NET/frontend checks and causal controls, package/inventory/locked restore and Web smoke, independent/external review, exact-head CI and actual resulting-main checks. Final cross-package isolated consumers remain #3364.
