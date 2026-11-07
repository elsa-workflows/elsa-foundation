// Acceptance-only declarations for the Node APIs used by owned-host controls.
// Keep these local rather than leaking Node globals into browser packages.
declare module "node:fs" {
  export function existsSync(path: string): boolean;
  export function renameSync(oldPath: string, newPath: string): void;
}
declare module "node:path" {
  export function join(...paths: string[]): string;
}
declare module "node:crypto" {
  export function randomUUID(): string;
}
