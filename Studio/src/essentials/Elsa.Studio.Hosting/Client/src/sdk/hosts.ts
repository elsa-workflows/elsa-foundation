/**
 * The two hosts a Studio page can switch between: the Elsa server behind the active backend, and the Studio host
 * serving the UI. Pages that offer a Server / Studio switch (console, module management, package feeds, feature
 * management) key their selection on this id.
 */
export type StudioHostId = "server" | "studio";

/** The host every Server / Studio switch opens on when nothing else (a URL target, a pending action) picks one. */
export const defaultStudioHostId: StudioHostId = "server";
