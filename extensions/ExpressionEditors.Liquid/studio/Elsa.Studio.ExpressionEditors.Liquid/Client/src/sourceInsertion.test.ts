import { expect, it } from "vitest";
import { liquidSourceInsertion as adapter } from "./sourceInsertion";
const render = (source: string, position: number, head = position) => adapter.render({ alias: "data", memberPath: ["a.b", "", 'quoted"key', "class", "日本語"], source, selection: { anchor: position, head } });
it("wraps template text and leaves the caret ready for a filter", () => {
  const result = render("Hello ", 6);
  expect(result.supported).toBe(true);
  if (!result.supported) return;
  expect(result.insert).toContain('{{ data["a.b"]'); expect(result.insert).toContain('["日本語"] }}');
  expect(result.selection?.anchor).toBe(result.insert.length - 3);
});
it("inserts bare parameter access within an existing output", () => {
  const result = render("{{ old }}", 3, 6); expect(result.supported).toBe(true);
  if (result.supported) expect(result.insert.startsWith("data[")).toBe(true);
});
it.each([['{{ "text" }}', 6], ['{% assign x = value %}', 16], ['{% comment %}value{% endcomment %}', 15], ['{{ data | upcase }}', 12]])("preserves unsupported context %s", (source, position) => {
  expect(render(source as string, position as number)).toMatchObject({ supported: false });
});

it.each(["{% comment %}note", "{% raw %}note", '{{ "unfinished'])("preserves an unfinished lexical boundary: %s", source => {
  expect(render(source, source.length).supported).toBe(false);
});
it("supports a filter's value argument", () => { expect(render("{{ data | append: other }}", 18, 23).supported).toBe(true); });
