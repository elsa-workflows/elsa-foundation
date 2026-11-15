// Local Node process typings for the live journeys. The npm `@types/node` package is not
// installed: Vite and Vitest take it as an optional peer and then leak Node globals into
// every browser package that references `vite/client`.
declare namespace NodeJS {
  interface ProcessEnv {
    [name: string]: string | undefined;
  }

  interface Process {
    env: ProcessEnv;
  }
}

declare const process: NodeJS.Process;

// Durable source-reconciliation receipts are shared across separate host runs.
declare module "node:fs" {
  export function readFileSync(path: string): Uint8Array;
  export function readFileSync(path: string, encoding: "utf8"): string;
  export function writeFileSync(path: string, data: string): void;
  export function existsSync(path: string): boolean;
  export function unlinkSync(path: string): void;
}

// Receipt hashes consume the captured PNG/ARIA bytes without decoding them.
declare module "node:crypto" {
  interface Hash {
    update(data: Uint8Array): Hash;
    digest(encoding: "hex"): string;
  }

  export function createHash(algorithm: "sha256"): Hash;
}
