# Decisions Studio extension points

`DecisionsStudioFeature` contributes the `decisions` module through the public Studio SDK. It owns the `/decisions` feature area, resource routes and deferred editor, and requires the backend Decisions API capabilities. Configure it through the normal Studio feature composition; it uses the selected backend and authenticated SDK HTTP client.

The module contributes a `decision-version` property editor for ordinary workflow activity bindings. It discovers readable published versions through backend capabilities; the server reauthorizes and freezes the selection at publication. The editor cannot grant source access or evaluation authority.

The supported profile and lifecycle are documented in [spec 1195](../../../../specs/1195-business-decisions/contracts/). Public Studio extension contracts live in [module UI](../../../../Studio/docs/module-ui.md); the module adds no shell-wide editor or execution contracts.
