import { liquidLanguage } from "@codemirror/lang-liquid";
import type { StudioExpressionSourceInsertion } from "@elsa-workflows/studio-sdk";

/** Fluid parameter roots are aliases, not JavaScript's args object. */
export const liquidSourceInsertion: StudioExpressionSourceInsertion = {
  render({ alias, memberPath, source, selection }) {
    const from = Math.min(selection.anchor, selection.head), to = Math.max(selection.anchor, selection.head);
    if (![from, to].every(position => Number.isInteger(position) && position >= 0 && position <= source.length))
      return { supported: false, reason: "The expression selection changed. Choose the insertion position again." };
    if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(alias) || ["empty", "blank", "true", "false", "nil", "null", "__proto__", "constructor", "prototype"].includes(alias))
      return { supported: false, reason: "This parameter name cannot be inserted as a Liquid root." };
    let blocked = false, inOutput = false;
    liquidLanguage.parser.parse(source).iterate({ enter(ref) {
      // A recovered interpolation without a closing delimiter cannot establish that following Text is template text.
      if (ref.name === "Interpolation" && !ref.node.getChild("}}") && from >= ref.from && !(from <= ref.from && to >= source.length)) blocked = true;
      const inside = from === to ? from > ref.from && (from < ref.to || from === ref.to && ref.node.lastChild?.type.isError === true) : from < ref.to && to > ref.from;
      if (!inside) return;
      if (["Comment", "CommentText", "RawDirective", "RawText"].includes(ref.name)) blocked = true;
      if (from <= ref.from && to >= ref.to) return;
      if (ref.name === "StringLiteral" || ref.name === "FilterName" || ref.name === "Tag" || ref.name === "EndTag") blocked = true;
      if (ref.name === "Interpolation") {
        if (from >= ref.from + 2 && to <= ref.to - 2) inOutput = true;
        else blocked = true;
      }
      if (ref.name === "Filter" && !source.slice(ref.from, from).includes(":")) blocked = true;
    } });
    if (blocked) return { supported: false, reason: "Insert in template text or an output value, outside strings, tag instructions, comments and filter names." };
    const reference = alias + memberPath.map(segment => `[${JSON.stringify(segment)}]`).join("");
    const insert = inOutput ? reference : `{{ ${reference} }}`;
    // In template text the caret lands before the closing tag, ready for a transformation filter.
    const caret = inOutput ? reference.length : 3 + reference.length;
    return { supported: true, insert, selection: { anchor: caret, head: caret } };
  }
};
