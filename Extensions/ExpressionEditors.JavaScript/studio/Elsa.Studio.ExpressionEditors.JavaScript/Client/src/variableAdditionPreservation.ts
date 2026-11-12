type Fields = Readonly<Record<string, unknown>>;
const record = (value: unknown): value is Fields => value !== null && typeof value === "object" && !Array.isArray(value);

/**
 * Conservative source admission for adding an unrelated lexical variable. This is not a
 * JavaScript validator or evaluator. Only a parameter/numeric operand, or one binary
 * operation over such operands, has a proof here; all other source needs a wider proof.
 * No strings, comments, escapes, member access or calls can observe ambient globals.
 */
export function preservesJavaScriptWithAddedVariable(expression: Fields): boolean {
  if (typeof expression.language !== "string" || expression.language.toLowerCase() !== "javascript"
    || typeof expression.source !== "string" || !record(expression.parameters)
    || expression.capabilityProfile !== "binding-pure-v1") return false;
  const parameters = expression.parameters;
  const identifier = /^[A-Za-z_$][A-Za-z0-9_$]*$/;
  const number = /^(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?$/;
  // Reserved words and host/global surfaces never stand in for a declared operand.
  const reserved = new Set(("await break case catch class const continue debugger default delete do else enum export extends false finally for function if implements import in instanceof interface let new null package private protected public return static super switch this throw true try typeof var void while with yield arguments eval globalThis Function Object Reflect Proxy Math JSON variables getVariable constructor __proto__ prototype NaN Infinity undefined").split(" "));
  const operand = (source: string) => number.test(source) || (identifier.test(source) && !reserved.has(source)
    && !source.startsWith("get") && Object.prototype.hasOwnProperty.call(parameters, source));
  let source = expression.source.trim();
  if (source.startsWith("(") && source.endsWith(")")) source = source.slice(1, -1).trim();
  if (operand(source)) return true;
  const binary = /^([A-Za-z_$][A-Za-z0-9_$]*|(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)\s*(===|!==|==|!=|<=|>=|&&|\|\||\?\?|[+*/%<>-])\s*([A-Za-z_$][A-Za-z0-9_$]*|(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)$/u.exec(source);
  return binary !== null && operand(binary[1]) && operand(binary[3]);
}
