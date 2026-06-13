# Extension points - Admin core

The admin core owns the server-side manifest contribution event used by the React admin host.

## Events

### `OnAdminModuleManifestsCollecting`

- **Contract defined in:** `Elsa.Admin.Core`
- **Purpose:** lets installed server-side features contribute React admin module manifests.
- **Publisher:** `Elsa.Admin.Api` when handling `GET /_elsa/admin/modules`.
- **Handlers:** admin module packages such as dashboard and weather samples.

Handlers should add manifest records only. They must not perform frontend asset loading or mutate the admin shell.
