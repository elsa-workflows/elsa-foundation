import type { StudioExpressionEditorContribution } from "@elsa-workflows/studio-sdk";
import type { NativeBlockContext } from "./nativeBlocks";

const identities = new WeakMap<object, number>();
let nextIdentity = 0;
function identity(value: object) {
  let id = identities.get(value);
  if (id === undefined) { id = ++nextIdentity; identities.set(value, id); }
  return id;
}

/** The composing host asks the language owner. Missing, ambiguous or faulting support refuses. */
export function composeExpressionScopePreservation(
  editors: readonly StudioExpressionEditorContribution[]
): NonNullable<NativeBlockContext["expressionScopePreservation"]> {
  const owners = editors.flatMap(editor => editor.variableAdditionPreservation ? [{ editor, proof: editor.variableAdditionPreservation }] : []);
  return {
    revision: JSON.stringify(owners.map(({ editor, proof }) => [editor.id, identity(editor), proof.language, proof.revision, identity(proof.allows)]).sort()),
    allowsVariableAddition(expression) {
      if (typeof expression.language !== "string") return false;
      const matches = owners.filter(({ proof }) => proof.language.toLowerCase() === (expression.language as string).toLowerCase());
      if (matches.length !== 1 || !matches[0].proof.revision) return false;
      try { return matches[0].proof.allows(expression) === true; }
      catch { return false; }
    }
  };
}
