// @vitest-environment node

import { describe, expect, it } from "vitest";
import { read } from "./themeTestUtils";

// The SDK package is type-only at the module boundary, so extensions keep hand-maintained copies of the
// declarations they consume. A field the canonical SDK exposes but a copy omits is invisible to that
// extension's editors, so each copy must carry the input metadata it needs to interpret.
const typingSources = {
  "canonical SDK": "../sdk/index.ts",
  "Secrets copy": "../../../../../../../Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/studio-sdk.d.ts",
  "JavaScript copy": "../../../../../../../Extensions/ExpressionEditors.JavaScript/studio/Elsa.Studio.ExpressionEditors.JavaScript/Client/src/studio-sdk.d.ts",
  "Liquid copy": "../../../../../../../Extensions/ExpressionEditors.Liquid/studio/Elsa.Studio.ExpressionEditors.Liquid/Client/src/studio-sdk.d.ts",
  "Connections copy": "../../../../../../../Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/studio-sdk.d.ts"
};

/** The text between the braces of `interface <name>`, or empty when it is not declared. */
function interfaceBody(source: string, name: string) {
  const start = source.search(new RegExp(`interface ${name}\\b`));
  if (start < 0) return "";
  const open = source.indexOf("{", start);
  let depth = 0;
  for (let index = open; index < source.length; index++) {
    if (source[index] === "{") depth++;
    else if (source[index] === "}" && --depth === 0) return source.slice(open + 1, index);
  }
  return "";
}

describe("StudioActivityInputDescriptor typing copies", () => {
  it.each(Object.entries(typingSources))("%s declares presentation tier, requiredness, and credential metadata", (_label, path) => {
    const body = interfaceBody(read(path), "StudioActivityInputDescriptor");

    expect(body).toMatch(/^\s*tier\?: string \| null;/m);
    expect(body).toMatch(/^\s*isRequired\?: boolean \| null;/m);
    expect(body).toMatch(/^\s*isSensitive\?: boolean \| null;/m);
    expect(body).toMatch(/^\s*isCredential\?: boolean \| null;/m);
  });
});

const propertyEditorSources: Array<keyof typeof typingSources> = ["canonical SDK", "Secrets copy", "Connections copy"];

describe("StudioActivityPropertyEditor reporting typing copies", () => {
  it.each(propertyEditorSources)("%s keeps optional reporting declarations additive", label => {
    const path = typingSources[label];
    const source = read(path);
    const props = interfaceBody(source, "StudioActivityPropertyEditorProps");
    const contribution = interfaceBody(source, "StudioActivityPropertyEditorContribution");

    expect(props).toMatch(/^\s*onValidationChange\?: \(hasError: boolean\) => void;/m);
    expect(contribution).toMatch(/^\s*supportsValidationReporting\?: true;/m);
  });
});

const editorContextBody = (path: string) => interfaceBody(read(path), "StudioActivityPropertyEditorContext");
// The canonical SDK and each copy whose extension consumes the property editor context. A source that stops or
// starts declaring it changes this set, so it cannot drop out of the activityId check silently.
const editorContextSources: Array<keyof typeof typingSources> = ["canonical SDK", "Secrets copy", "Connections copy"];

describe("StudioActivityPropertyEditorContext typing copies", () => {
  it("is declared by exactly the expected sources", () => {
    const declaring = Object.entries(typingSources).filter(([, path]) => editorContextBody(path) !== "").map(([label]) => label);

    expect(declaring).toEqual(editorContextSources);
  });

  it.each(editorContextSources)("%s declares activityId?", label => {
    expect(editorContextBody(typingSources[label])).toMatch(/^\s*activityId\?: string \| null;/m);
  });
});
