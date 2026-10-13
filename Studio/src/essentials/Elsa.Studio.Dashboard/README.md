# Studio Home

`Elsa.Studio.Dashboard` hosts the customizable Home surface at `/`, `/dashboard`, and `/overview`. There is no separate Dashboard navigation entry.

## Widgets

Modules contribute widgets with `registerHomeWidget` (or the `homeWidgets` / `dashboardWidgets` alias) in `@elsa-workflows/studio-sdk`. A widget is offered only when its `requiresFeature` list is enabled and the session holds any declared permissions.

## Layout persistence

Signed-in users read and write `/_elsa/studio/home-layout`. Admins with `studio-preferences.manage-defaults` (or `*`) read and write `/_elsa/studio/home-layout/default`. **Reset to default** deletes the user document so the admin default, then the built-in layout, applies. When GET falls back to the admin default it still returns that document's ETag; the first user PUT uses `If-None-Match: *`. The reserved subject `__admin_default__` cannot call the per-user route.

## Anonymous and demo sessions

When sign-in is off the client never calls the Home layout API. The layout is stored in `localStorage` under `elsa-studio:home-layout:` (scoped by host, tenant, and subject). If that key is missing, Home uses the built-in default. A reset clears the local key.

See [Studio Preferences API](../../../Essentials/src/Studio/Preferences/Api/README.md) for the HTTP surface.
