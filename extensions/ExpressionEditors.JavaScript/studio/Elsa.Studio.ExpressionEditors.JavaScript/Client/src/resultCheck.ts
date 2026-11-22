import { parser } from "@lezer/javascript";
import type { StudioExpressionResultCheck, StudioExpressionResultType } from "@elsa-workflows/studio-sdk";

type Node = NonNullable<ReturnType<typeof parser.parse>["topNode"]["firstChild"]>;
type Scalar = "number" | "string" | "boolean";
const numbers = new Set(["double", "single", "decimal", "byte", "sbyte", "int16", "uint16", "int32", "uint32", "int64", "uint64"]);
const comment = (node: Node) => node.name === "LineComment" || node.name === "BlockComment";

export const javaScriptResultCheck: StudioExpressionResultCheck = {
  async check({ source, expectedType, resolveParameterType }) {
    const expected = scalar(expectedType);
    if (!expected) return;
    const tree = parser.parse(source);
    let invalid = false;
    tree.iterate({ enter(node) { if (node.type.isError) invalid = true; } });
    if (invalid) return;
    const statements = children(tree.topNode).filter(node => !comment(node));
    if (statements.length !== 1 || !["ExpressionStatement", "ReturnStatement"].includes(statements[0].name)) return;
    const expression = children(statements[0]).find(node => !comment(node) && node.name !== "return" && node.name !== ";");
    if (!expression) return;
    // A line terminator in a comment after return makes it a bare return at runtime.
    const returnKeyword = statements[0].getChild("return");
    if (returnKeyword && /[\r\n\u2028\u2029]/.test(source.slice(returnKeyword.to, expression.from))) return;

    const value = unwrap(expression);
    let actual = literalType(value, source);
    let declaredType: StudioExpressionResultType | undefined;
    if (!actual && value.name === "MemberExpression") {
      const path = parameterPath(value, source);
      if (!path) return;
      declaredType = await resolveParameterType(path[0], path.slice(1));
      actual = scalar(declaredType);
    }
    if (!actual || actual === expected) return;
    const name = declaredType ? displayAlias(declaredType.alias) : actual;
    const target = displayAlias(expectedType.alias);
    return {
      message: `This expression returns ${name}. This field expects ${target}.`,
      // Only a proven primitive and a single returned expression reach here. No local String
      // declaration or assignment can precede evaluation of either supported operand.
      fix: expected === "string" ? {
        from: expression.from,
        to: expression.to,
        replacement: `String(${source.slice(expression.from, expression.to)})`,
        label: "Wrap in String(…)"
      } : undefined
    };
  }
};

function scalar(type: StudioExpressionResultType | undefined): Scalar | undefined {
  if (!type || type.collectionKind && type.collectionKind.toLowerCase() !== "single") return;
  const name = displayAlias(type.alias).toLowerCase();
  if (numbers.has(name)) return "number";
  if (name === "boolean" || name === "string") return name;
}

function displayAlias(alias: string) {
  return alias.split(",", 1)[0].trim().replace(/^System\./i, "");
}

function children(node: Node) {
  const result: Node[] = [];
  for (let child = node.firstChild; child; child = child.nextSibling) result.push(child);
  return result;
}

function unwrap(node: Node): Node {
  while (node.name === "ParenthesizedExpression") {
    const inner = children(node).find(child => !comment(child) && child.name !== "(" && child.name !== ")");
    if (!inner) break;
    node = inner;
  }
  return node;
}

function literalType(node: Node, source: string): Scalar | undefined {
  switch (node.name) {
    case "Number": {
      const text = source.slice(node.from, node.to);
      // Restrict fixes to numeric spellings that are also valid in strict JavaScript.
      if (!/^0[0-9]/.test(text) && Number.isFinite(Number(text))) return "number";
      break;
    }
    case "BooleanLiteral": return "boolean";
    case "String":
    case "TemplateString": return "string";
    case "CallExpression": {
      const callee = node.firstChild;
      if (callee?.name === "VariableName" && node.lastChild?.name === "ArgList" &&
          source.slice(callee.from, callee.to) === "String") return "string";
      break;
    }
    case "UnaryExpression": {
      const [operator, operand] = children(node).filter(child => !comment(child));
      if (operator?.name === "ArithOp" && /^[+-]$/.test(source.slice(operator.from, operator.to)) &&
          operand?.name === "Number") return literalType(operand, source);
    }
  }
}

// Only static member reads directly rooted at the portable parameter namespace are sound here.
function parameterPath(node: Node, source: string): string[] | undefined {
  const path: string[] = [];
  while (node.name === "MemberExpression" && path.length < 65) {
    const [receiver, operator, member, close] = children(node).filter(child => !comment(child));
    if (!receiver || !operator || !member) return;
    const text = source.slice(member.from, member.to);
    if (operator.name === "." && member.name === "PropertyName" && /^[\p{ID_Start}_$][\p{ID_Continue}$]*$/u.test(text)) {
      path.unshift(text);
    } else if (operator.name === "[" && member.name === "String" && close?.name === "]") {
      if (text.startsWith('"')) {
        try { path.unshift(JSON.parse(text) as string); }
        catch { return; }
      } else if (/^'[^'\\]*'$/.test(text)) {
        path.unshift(text.slice(1, -1));
      } else return;
    } else return;
    node = unwrap(receiver);
  }
  if (path.length && node.name === "VariableName" && source.slice(node.from, node.to) === "args") return path;
}
