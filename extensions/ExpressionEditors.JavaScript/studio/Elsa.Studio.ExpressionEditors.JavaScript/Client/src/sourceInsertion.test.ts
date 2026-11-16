import { expect, it } from "vitest";
import { javaScriptSourceInsertion as adapter } from "./sourceInsertion";
const render = (source: string, position: number, head = position) => adapter.render({ alias: "data", memberPath: ["a.b", "", 'quoted"key', "class", "日本語"], source, selection: { anchor: position, head } });
it("renders literal serialized segments without treating punctuation as executable access", () => {
  expect(render("", 0)).toMatchObject({ supported: true, insert: String.raw`args.data["a.b"][""]["quoted\"key"]["class"]["日本語"]` });
});
it.each([['"text"', 3], ["// comment", 5], ["/* comment */", 6], ["/word/g", 3], ["`template text`", 5]])("preserves unsupported literal context %s", (source, position) => {
  expect(render(source as string, position as number)).toMatchObject({ supported: false });
});
it("supports template interpolation code and replacing a complete literal", () => {
  expect(render("`text ${value}`", 8, 13).supported).toBe(true);
  expect(render('"text"', 0, 6).supported).toBe(true);
});

it.each(["1; // note", "// comment\n", "'unfinished", '"unfinished', "/* unfinished", "`unfinished", "/unfinished"])("rejects a lexical EOF boundary: %s", source => {
  const position = source.endsWith("\n") ? source.length - 1 : source.length;
  expect(render(source, position).supported).toBe(false);
});
it("keeps an unfinished interpolation's code position usable", () => { expect(render("`hi ${value", 11).supported).toBe(true); });

it("keeps cross-interpolation delimiters untouched and accepts ordinary incomplete code", () => {
  expect(render("`text ${value}`", 9, 14).supported).toBe(false);
  expect(render("call(", 5).supported).toBe(true);
});
