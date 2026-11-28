import type { Diagnostic } from "./decisionsApi";
import type { Clause, Table } from "./decisionXml";

function columnName(clause: Clause, index: number, output: boolean) {
  return clause.name.trim() || `${output ? "output" : "input"} ${index + 1}`;
}

export function decisionDiagnosticMessage(diagnostic: Diagnostic, table: Table | null) {
  if (diagnostic.code !== "UnsupportedExpression" || !diagnostic.clauseId || !table) return diagnostic.message;

  const inputIndex = table.inputs.findIndex(clause => clause.id === diagnostic.clauseId);
  const outputIndex = table.outputs.findIndex(clause => clause.id === diagnostic.clauseId);
  const output = inputIndex < 0 && outputIndex >= 0;
  const index = output ? outputIndex : inputIndex;
  const clause = output ? table.outputs[index] : table.inputs[index];
  if (!clause) return diagnostic.message;

  const name = columnName(clause, index, output);
  let guidance: string;
  if (!output && clause.type === "number")
    guidance = `The “${name}” input already supplies the value. Enter a condition such as < 21, not “${name} < 21”.`;
  else if (!output && clause.type === "string")
    guidance = `This condition tests the “${name}” input. Match text with a quoted value such as "male".`;
  else if (!output && clause.type === "boolean")
    guidance = `This condition tests the “${name}” input. Use true or false as the condition.`;
  else if (output && clause.type === "number")
    guidance = `The “${name}” output expects a number value, not a condition; for example, 21.`;
  else if (output && clause.type === "string")
    guidance = `The “${name}” output expects a quoted text value, such as "Approved".`;
  else if (output && clause.type === "boolean")
    guidance = `The “${name}” output expects true or false as the result value.`;
  else return diagnostic.message;

  return `${diagnostic.message.trim()} ${guidance}`;
}