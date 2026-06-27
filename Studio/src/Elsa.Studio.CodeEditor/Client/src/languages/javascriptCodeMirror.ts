import { javascript } from "@codemirror/lang-javascript";

export function createJavaScriptCodeMirrorExtensions() {
  return [javascript({ jsx: true, typescript: true })];
}
