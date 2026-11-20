// Narrow Node APIs used by the source-inspection tests. Keep browser globals unchanged.
declare module "node:fs" {
  export function existsSync(path: string | URL): boolean;
  export function readFileSync(path: string | URL, encoding: "utf8"): string;
}

declare module "node:url" {
  export function fileURLToPath(url: string | URL): string;
}
