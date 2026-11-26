// The module type-checks against browser + Vite types only (no @types/node). Node-environment
// contract tests read stylesheets from disk, so declare just the fs surface they use.
declare module "node:fs" {
  export function readFileSync(path: URL, encoding: "utf8"): string;
  export function readdirSync(path: URL, options: { recursive: true }): string[];
}
