# Studio questions and dashboards

`Elsa.Studio.Dashboard` adds saved questions and named dashboards: the `/questions` and `/dashboards` entries, each with Mine and Shared with me collections. It needs the backend's `elsa.api.studio-dashboard` capability and ships as a feed add-on for Studio Essentials. Home is not part of this module: the Studio shell owns Home and its widget area in every flavor (see [Home in the SDK guide](../../../../Studio/src/essentials/Elsa.Studio.Hosting/Client/src/sdk/README.md#home-widget-and-section-contributions)). Named dashboards keep saved question references separately from Home widgets and the Home layout.

## Saved questions and named dashboards

Native controls use installed dataset declarations for fields, metrics, filters, periods, parameters and presentation. Preview, Save, reopening, refresh and drill use the current viewer. Optional Weaver authoring proposes a declarative draft; Preview, Use and Save remain explicit native actions. A missing provider leaves normal editing available.

Named dashboards arrange saved questions as tiles. Sharing a dashboard leaves each question's audience unchanged. The sharing/copy dialogs review dependencies and offer separate private question copies. Shared definitions never carry the author's results or source authority. See the [native domain guide](../../../../essentials/src/Studio/Dashboard/README.md) and [UX acceptance record](../../../../specs/1217-ai-dashboards/ux-acceptance.md).

Delivery status: component tests, type checking, lint and bundle checks are supporting proof. Current composed-host screenshots, actual IAM/source journeys and independent UX inspection remain incomplete until the integration qualification slice. This source is not final product acceptance.
