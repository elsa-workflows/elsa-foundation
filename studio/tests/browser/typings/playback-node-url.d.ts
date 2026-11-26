// The isolated Playwright host resolves its owned working directory in ESM.
declare module "node:url" {
  export function fileURLToPath(url: URL | string): string;
}
