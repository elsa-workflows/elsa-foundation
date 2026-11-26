import { liquid } from "@codemirror/lang-liquid";
import type { Extension } from "@codemirror/state";

export function createLiquidCodeMirrorExtensions(): Extension[] {
  return [liquid()];
}
