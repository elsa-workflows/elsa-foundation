import { javascript } from "@codemirror/lang-javascript";
import type { Extension } from "@codemirror/state";

export function createJavaScriptCodeMirrorExtensions(): Extension[] {
  return [javascript({ jsx: true, typescript: true })];
}
