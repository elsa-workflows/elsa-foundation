# Try Sketchbook

In Studio, open Settings → Appearance → Studio preset → Sketchbook. Workspace style, color scheme, mode and navigation remain independent choices. Existing browser preferences are preserved until the preset is selected.

For a fresh-profile host default, configure:

```json
{"Studio":{"Appearance":{"WorkspaceStyle":"sketchbook","PaletteId":"builtin:sketchbook","Mode":"light"}}}
```

For the identity host's actual password page:

```json
{"Identity":{"Login":{"Layout":"sketchbook","Palette":"sketchbook","Artwork":"sketchbook","Tagline":"A workspace for your workflows.","Description":"Design processes, work with decisions and data, and follow each execution."}}}
```

Restart the identity host after changing its configuration. Login follows OS light/dark and contrast preferences; its presentation is independent of a browser's signed-in Studio choices. Custom logos and renderers retain precedence. OIDC provider pages remain configured at the provider.

The bundled drawing depicts workflows, decisions, data and events. It is decorative; form labels and essential copy remain HTML text. [Branding and login configuration](../../Studio/docs/branding.md) covers the existing configuration contracts.
