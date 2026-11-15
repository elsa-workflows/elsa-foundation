declare module "node:crypto" {
  export function randomUUID(): string;
}
declare module "node:path" {
  export function join(...paths: string[]): string;
}
declare module "node:fs" {
  export function renameSync(oldPath: string, newPath: string): void;
}
