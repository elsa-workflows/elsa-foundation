// Host-agnostic primitives shared with module clients via `@elsa-workflows/studio-ui`.
export * from "./shared";

// Host-only surface (bound to the singleton dialog controller + Radix dialog).
export * from "./dialog/DialogHost";
