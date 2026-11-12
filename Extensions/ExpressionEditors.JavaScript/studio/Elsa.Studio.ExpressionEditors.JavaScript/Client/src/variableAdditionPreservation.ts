type Fields = Readonly<Record<string, unknown>>;
const record = (value: unknown): value is Fields => value !== null && typeof value === "object" && !Array.isArray(value);

/**
 * Conservative source admission for adding an unrelated lexical variable. This is not a
 * JavaScript validator or evaluator. Only an args.<declared parameter>/numeric operand, or one binary
 * operation over such operands, has a proof here; all other source needs a wider proof.
 * Only direct args.<key> access is admitted; other members, strings, comments, escapes and calls refuse.
 */
export function preservesJavaScriptWithAddedVariable(expression: Fields): boolean {
  if (typeof expression.language !== "string" || expression.language.toLowerCase() !== "javascript"
    || typeof expression.source !== "string" || !record(expression.parameters)
    || expression.capabilityProfile !== "binding-pure-v1") return false;
  const parameters = expression.parameters;
  const number = /^(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?$/;
  const operand = (source: string) => {
    if (number.test(source)) return true;
    const parameter = /^args\.([A-Za-z_$][A-Za-z0-9_$]*)$/.exec(source)?.[1];
    return parameter !== undefined && !["constructor", "__proto__", "prototype"].includes(parameter)
      && Object.prototype.hasOwnProperty.call(parameters, parameter);
  };
  let source = expression.source.trim();
  if (source.startsWith("(") && source.endsWith(")")) source = source.slice(1, -1).trim();
  if (operand(source)) return true;
  const binary = /^(args\.[A-Za-z_$][A-Za-z0-9_$]*|(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)\s*(===|!==|==|!=|<=|>=|&&|\|\||\?\?|[+*/%<>-])\s*(args\.[A-Za-z_$][A-Za-z0-9_$]*|(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)$/u.exec(source);
  return binary !== null && operand(binary[1]) && operand(binary[3]);
}
