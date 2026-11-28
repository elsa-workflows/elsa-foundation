// Runtime entry for the vendored `@elsa-workflows/studio-ui` bundle. Re-exports the
// host-agnostic Studio UI primitives so a single shared copy is served through the
// import map (`/studio/vendor/studio-ui.js`) and consumed by module clients that mark
// `@elsa-workflows/studio-ui` external. Mirrors the react/react-query vendor shims.
export * from "../app/ui/shared";
