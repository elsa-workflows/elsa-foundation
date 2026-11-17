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
    const tree = parser.parse(source);
    if (shadowedNamespaces(source, tree).has(namespace))
      return { supported: false, reason: "A local JavaScript binding may shadow this reference." };
    let blocked = false;
    tree.iterate({ enter(ref) {
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
    const tree = parser.parse(source);
    const shadowed = shadowedNamespaces(source, tree);
    if (shadowed.has("args")) return [];
    let parseError = false;
    tree.iterate({ enter({ node, name, from, to, type }) {
      if (type.isError) parseError = true;
      if (name !== "MemberExpression" || isWriteTarget(node)) return;
      const receiver = node.firstChild;
      if (receiver?.name !== "VariableName" || source.slice(receiver.from, receiver.to) !== "args") return;
      const operator = receiver.nextSibling;
      const optional = operator?.name === "?.";
      const member = operator?.nextSibling;
      const bracket = operator?.name === "[" ? operator : optional && member?.name === "[" ? member : null;
      const argument = bracket ? readStringMember(source, bracket.nextSibling) :
        (operator?.name === "." || optional) && member?.name === "PropertyName" ? source.slice(member.from, member.to) : undefined;
      if (!argument || ["__proto__", "prototype", "constructor"].includes(argument)) return;
      const replacement = resolve(argument);
      if (!replacement || shadowed.has(replacement.namespace)) return;
      fixes.push({ from, to, current: source.slice(from, to), replacement: memberAccess(replacement.namespace, replacement.name, optional) });
    } });
    return parseError ? [] : fixes;
  }
};

type JavaScriptTree = ReturnType<typeof parser.parse>;
type JavaScriptNode = JavaScriptTree["topNode"];

function shadowedNamespaces(source: string, tree: JavaScriptTree) {
  const shadowed = new Set<string>();
  tree.iterate({ enter({ name, type, from, to }) {
    const syntax = source.slice(from, to);
    if (type.isError && syntax.includes("\\")) {
      // The grammar can split Unicode-escaped identifiers into errors and partial bindings.
      shadowed.add("args");
      shadowed.add("variables");
    }
    if (name === "VariableDefinition" || name === "ObjectPattern" || name === "ImportDeclaration") {
      for (const namespace of ["args", "variables"])
        if (new RegExp(`\\b${namespace}\\b`).test(syntax)) shadowed.add(namespace);
    }
  } });
  return shadowed;
}

function isWriteTarget(node: JavaScriptNode) {
  for (let parent = node.parent; parent; parent = parent.parent) {
    if (parent.name === "AssignmentExpression") return node.to <= (parent.firstChild?.to ?? -1);
    if (parent.name === "PostfixExpression" ||
        parent.name === "UnaryExpression" && ["delete", "ArithOp"].includes(parent.firstChild?.name ?? "")) return true;
  }
  return false;
}

function readStringMember(source: string, member: JavaScriptNode | null) {
  if (member?.name !== "String" || member.nextSibling?.name !== "]") return;
  const literal = source.slice(member.from, member.to);
  if (literal.startsWith("'") && !literal.includes("\\")) return literal.slice(1, -1);
  try { return JSON.parse(literal) as string; } catch { return undefined; }
}

function memberAccess(namespace: "args" | "variables", name: string, optional = false) {
  return /^[A-Za-z_$][A-Za-z0-9_$]*$/.test(name) ? `${namespace}${optional ? "?." : "."}${name}` :
    `${namespace}${optional ? "?." : ""}[${JSON.stringify(name)}]`;
}
