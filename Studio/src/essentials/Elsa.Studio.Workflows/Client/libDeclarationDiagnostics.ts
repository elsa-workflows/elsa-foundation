/**
 * Library declaration-emit gate for `@elsa-workflows/studio-workflows`.
 *
 * vite-plugin-dts logs TypeScript declaration diagnostics (including TS2742) but still
 * finishes the Vite build with exit 0. Throwing here makes `build:lib` fail when
 * declaration generation has genuine errors.
 */

import ts from "typescript";

export interface DeclarationDiagnostic {
  code: number;
  category?: number;
  messageText: string | DiagnosticMessageText;
}

interface DiagnosticMessageText {
  messageText: string;
  next?: DiagnosticMessageText | DiagnosticMessageText[];
}

const typescriptErrorCategory = 1;

function diagnosticMessage(messageText: DeclarationDiagnostic["messageText"]): string {
  return ts.flattenDiagnosticMessageText(messageText as string | ts.DiagnosticMessageChain, "\n");
}

export function failOnDeclarationDiagnostics(diagnostics: readonly DeclarationDiagnostic[]): void {
  const errors = diagnostics.filter(diagnostic => diagnostic.category === undefined || diagnostic.category === typescriptErrorCategory);
  if (errors.length === 0) return;

  const summary = errors.map(diagnostic => `TS${diagnostic.code}: ${diagnosticMessage(diagnostic.messageText)}`).join("\n");
  throw new Error(`Declaration generation failed with ${errors.length} error(s).\n${summary}`);
}
