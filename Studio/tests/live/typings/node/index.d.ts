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
  export function readFileSync(path: string, encoding: "utf8"): string;
  export function writeFileSync(path: string, data: string): void;
}
