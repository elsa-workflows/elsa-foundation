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

it("repairs direct argument reads inside template interpolations and skips lookalike text", () => {
  const source = '`args.Variable1: ${args.Variable1}` + "args.Variable1" /* args.Variable1 */';
  const fixes = adapter.findArgumentFixes!(source, name => name === "Variable1" ? { namespace: "variables", name } : undefined);
  expect(fixes).toEqual([{
    from: source.indexOf("${") + 2,
    to: source.indexOf("}"),
    current: "args.Variable1",
    replacement: "variables.Variable1"
  }]);
});

it("leaves shadowed arguments alone and escapes unusual public input names", () => {
  const resolve = () => ({ namespace: "args" as const, name: "Input numbers" });
  expect(adapter.findArgumentFixes!("(args) => args.numbers", resolve)).toEqual([]);
  expect(adapter.findArgumentFixes!("({ args }) => args.numbers", resolve)).toEqual([]);
  expect(adapter.findArgumentFixes!("args.numbers.reduce(sum, 0)", resolve)).toEqual([
    { from: 0, to: 12, current: "args.numbers", replacement: 'args["Input numbers"]' }
  ]);
  expect(adapter.render({ namespace: "variables", alias: "Input numbers", memberPath: [], source: "", selection: { anchor: 0, head: 0 } }))
    .toMatchObject({ supported: true, insert: 'variables["Input numbers"]' });
});

it.each([
  String.raw`((\u0061rgs) => args.Count)({Count: 7})`,
  "args.Count = 5",
  "args.Count.member = 5",
  "args.Count++",
  "delete args.Count"
])("does not offer a read repair for %s", source => {
  expect(adapter.findArgumentFixes!(source, () => ({ namespace: "args", name: "count" }))).toEqual([]);
});

it("blocks insertion when a plain or escaped local binding can shadow the namespace", () => {
  for (const source of ["(args) => x", String.raw`(\u0061rgs) => x`])
    expect(render(source, source.length).supported).toBe(false);
});

it.each([
  ['args["Count"]', "args.count"],
  ["args['Count']", "args.count"],
  ["args?.Count", "args?.count"],
  ['args?.["Count"]', "args?.count"]
])("preserves read semantics and optional chaining for %s", (source, replacement) => {
  expect(adapter.findArgumentFixes!(source, () => ({ namespace: "args", name: "count" })))
    .toEqual([{ from: 0, to: source.length, current: source, replacement }]);
});
