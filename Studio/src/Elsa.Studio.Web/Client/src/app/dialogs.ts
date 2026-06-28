import { createDialogController } from "../sdk";

/**
 * Shared singleton dialog controller for the Studio host.
 *
 * `createStudioRegistry` exposes `dialogController.api` to every module as `api.dialogs`,
 * and the host-rendered `<DialogHost />` subscribes to the same controller. Because
 * `@elsa-workflows/studio-sdk` and `react` are externalized in module bundles, modules
 * and the host share this one instance at runtime.
 */
export const dialogController = createDialogController();

export const dialogs = dialogController.api;
