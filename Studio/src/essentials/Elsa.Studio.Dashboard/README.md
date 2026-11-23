# Studio Home, questions and dashboards

`Elsa.Studio.Dashboard` hosts the customizable Home surface at `/`, `/dashboard`, and `/overview`. Questions and Dashboards use separate `/questions` and `/dashboards` entries, each with Mine and Shared with me collections. Named dashboards retain saved question references separately from Home widgets and layout preferences.

## Saved questions and named dashboards

Native controls use installed dataset declarations for fields, metrics, filters, periods, parameters and presentation. Preview, Save, reopening, refresh and drill use the current viewer. Optional Weaver authoring proposes a declarative draft; Preview, Use and Save remain explicit native actions. A missing provider leaves normal editing available.

Named dashboards arrange saved questions as tiles. Sharing a dashboard leaves each question's audience unchanged. The sharing/copy dialogs review dependencies and offer separate private question copies. Shared definitions never carry the author's results or source authority. See the [native domain guide](../../../../Essentials/src/Studio/Dashboard/README.md) and [UX acceptance record](../../../../specs/1217-ai-dashboards/ux-acceptance.md).

Delivery status: component tests, type checking, lint and bundle checks are supporting proof. Current composed-host screenshots, actual IAM/source journeys and independent UX inspection remain incomplete until the integration qualification slice. This source is not final product acceptance.

## Widgets

Modules contribute widgets with `registerHomeWidget` (or the `homeWidgets` / `dashboardWidgets` alias) in `@elsa-workflows/studio-sdk`. A widget is offered only when its `requiresFeature` list is enabled and the session holds all declared permissions. Feature slugs match `Elsa.Studio.*` assembly ids only (`workflows` matches `Elsa.Studio.Workflows`, not `Contoso.Workflows`; `user-tasks` matches `Elsa.Studio.UserTasks`). A missing `enabledFeatures` list hides gated widgets. Unregistered ids (a disabled module, a widget the session cannot see) stay in the saved layout and are skipped at render time.

The built-in default landing is Get started (with docs links), Quick actions, Recent workflows, Recent runs, Needs attention, and Tasks. Tasks is registered and in the default layout, but it only renders when user tasks are enabled.

## Layout persistence

Signed-in users read and write `/_elsa/studio/home-layout`. Admins with `studio/preferences/defaults:manage` (or `*`) read and write `/_elsa/studio/home-layout/default`. **Reset to default** deletes the user document so the admin default, then the built-in layout, applies. When GET falls back to the admin default it still returns that document's ETag; the first user PUT uses `If-None-Match: *`. The reserved subject `__admin_default__` cannot call the per-user route.

## Anonymous and demo sessions

When sign-in is off the client never calls the Home layout API. The layout is stored in `localStorage` under `elsa-studio:home-layout:` (scoped by host, tenant, and subject). If that key is missing, Home uses the built-in default. A reset clears the local key.

See [Studio Preferences API](../../../../Essentials/src/Studio/Preferences/Api/README.md) for the HTTP surface.
