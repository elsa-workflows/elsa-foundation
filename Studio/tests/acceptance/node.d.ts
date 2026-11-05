declare module "node:fs" {
  export function renameSync(oldPath: string, newPath: string): void;
}
