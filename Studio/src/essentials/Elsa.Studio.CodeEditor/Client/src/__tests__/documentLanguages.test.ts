import { EditorState } from "@codemirror/state";
import { syntaxTree } from "@codemirror/language";
import { describe, expect, it } from "vitest";
import { loadCodeMirrorLanguageExtensions } from "../engines/codeMirrorLanguages";

// Exercise the real lazy syntax support rather than only checking the adapter label.
describe("document syntax support", () => {
  it.each([
    ["json", '{"name":"Ada"}', "JsonText", "PropertyName"],
    ["xml", '<order id="1">Ada</order>', "Document", "Element"],
    ["html", '<p class="name">Ada</p>', "Document", "Element"]
  ])("parses %s with its language grammar", async (language, value, root, token) => {
    const extensions = await loadCodeMirrorLanguageExtensions(language);
    const state = EditorState.create({ doc: value, extensions });
    const tree = syntaxTree(state);
    expect(tree.type.name).toBe(root);
    expect(tree.toString()).toContain(token);
    expect(tree.toString()).not.toContain("⚠");
  });

  it("keeps unsupported media bodies as plain text", async () => {
    expect(await loadCodeMirrorLanguageExtensions("plaintext")).toEqual([]);
  });
});
