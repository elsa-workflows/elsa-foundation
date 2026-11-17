import { parser } from "@lezer/javascript";
import type { StudioExpressionReferenceFix, StudioExpressionSourceInsertion } from "@elsa-workflows/studio-sdk";

/** Literal access and source-position interpretation belong to the installed JavaScript adapter. */
export const javaScriptSourceInsertion: StudioExpressionSourceInsertion = {
  render({ namespace = "args", alias, memberPath, source, selection }) {
    const from = Math.min(selection.anchor, selection.head), to = Math.max(selection.anchor, selection.head);
    if (![from, to].every(position => Number.isInteger(position) && position >= 0 && position <= source.length))
      return { supported: false, reason: "The expression selection changed. Choose the insertion position again." };
    if (!alias || ["__proto__", "prototype", "constructor"].includes(alias))
      return { supported: false, reason: "This parameter name cannot be inserted safely in JavaScript." };
    let blocked = false;
    parser.parse(source).iterate({ enter(ref) {
      // Lezer does not flag an unterminated RegExp token; its end boundary is conservatively unsafe.
      const lexicalEnd = ref.name === "LineComment" || ref.name === "RegExp" || ref.node.lastChild?.type.isError === true;
      const inside = from === to ? from > ref.from && (from < ref.to || from === ref.to && lexicalEnd) : from < ref.to && to > ref.from;
      if (!inside || from <= ref.from && to >= ref.to) return;
      if (["String", "RegExp", "LineComment", "BlockComment"].includes(ref.name)) blocked = true;
      if (ref.name === "TemplateString") {
        let interpolation = false;
        for (let node = ref.node.firstChild; node; node = node.nextSibling) {
          if (node.name === "Interpolation" && from >= node.from + 2 && to <= node.to - (node.lastChild?.type.isError ? 0 : 1)) interpolation = true;
        }
        if (!interpolation) blocked = true;
      }
    } });
    if (blocked) return { supported: false, reason: "Choose a JavaScript code position outside string, comment, regular-expression or template text." };
    const root = memberAccess(namespace, alias);
    const insert = root + memberPath.map(segment => `[${JSON.stringify(segment)}]`).join("");
    return { supported: true, insert, selection: { anchor: insert.length, head: insert.length } };
  },

  findArgumentFixes(source, resolve) {
    const fixes: StudioExpressionReferenceFix[] = [];
    const shadowed = new Set<string>();
    parser.parse(source).iterate({ enter(ref) {
      const { node, name, from, to } = ref;
      // Conservatively refuse fixes when a local binding could shadow either runtime namespace.
      if (name === "VariableDefinition" || name === "ObjectPattern" || name === "ImportDeclaration") {
        const declared = source.slice(from, to);
        for (const namespace of ["args", "variables"])
          if (new RegExp(`\\b${namespace}\\b`).test(declared)) shadowed.add(namespace);
      }
      if (name !== "MemberExpression") return;
      const receiver = node.firstChild;
      const operator = receiver?.nextSibling;
      const member = operator?.nextSibling;
      if (receiver?.name !== "VariableName" || source.slice(receiver.from, receiver.to) !== "args" ||
          operator?.name !== "." || member?.name !== "PropertyName") return;
      const argument = source.slice(member.from, member.to);
      if (["__proto__", "prototype", "constructor"].includes(argument)) return;
      const replacement = resolve(argument);
      if (!replacement) return;
      fixes.push({ from, to, current: source.slice(from, to), replacement: memberAccess(replacement.namespace, replacement.name) });
    } });
    if (shadowed.has("args")) return [];
    return fixes.filter(fix => !shadowed.has("variables") || !fix.replacement.startsWith("variables"));
  }
};

function memberAccess(namespace: "args" | "variables", name: string) {
  return /^[A-Za-z_$][A-Za-z0-9_$]*$/.test(name) ? `${namespace}.${name}` : `${namespace}[${JSON.stringify(name)}]`;
}
