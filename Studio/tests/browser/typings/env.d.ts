/// <reference types="vite/client" />

// Local Node typings for Playwright specs that write screenshots or read env.
// Do not install npm `@types/node`: Vite/Vitest treat it as an optional peer and leak
// Node globals into browser packages (same reason as tests/live/typings).

declare namespace NodeJS {
  interface ProcessEnv {
    [name: string]: string | undefined;
  }

  interface Process {
    env: ProcessEnv;
    arch: string;
    platform: string;
    cwd(): string;
  }
}

declare const __dirname: string;

interface Buffer extends Uint8Array {
  toString(encoding?: string): string;
}
declare const Buffer: {
  from(data: string, encoding?: string): Buffer;
};

interface AxeViolationNode {
  target: unknown[];
  failureSummary?: string;
}

interface AxeViolation {
  id: string;
  impact?: string | null;
  help: string;
  nodes: AxeViolationNode[];
}

interface Window {
  axe: {
    run: (root?: unknown, options?: object) => Promise<{ violations: AxeViolation[] }>;
  };
  __homeDashboardRequests?: string[];
  __homeDashboardLayouts?: unknown[];
}

declare const process: NodeJS.Process;

declare module "node:fs/promises" {
  export function mkdir(path: string, options?: { recursive?: boolean }): Promise<string | undefined>;
  export function writeFile(path: string, data: string | Uint8Array): Promise<void>;
  export function readFile(path: string): Promise<Buffer>;
}

declare module "node:path" {
  export function resolve(...paths: string[]): string;
  export function join(...paths: string[]): string;
  export function dirname(path: string): string;
}

declare module "*.png" {
  const src: string;
  export default src;
}

declare module "*?url&no-inline" {
  const src: string;
  export default src;
}
