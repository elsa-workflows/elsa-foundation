import { useEffect, useMemo, useState } from "react";
import { authSessionEndedEvent, type StudioExpressionResultCheck, type StudioExpressionResultDiagnostic, type StudioExpressionResultType } from "@elsa-workflows/studio-sdk";
import { readPortableExpressionDefinition } from "./activityProperties";
import { matchesParameterOffer, readActivityDataParameterSource, type ActivityDataTarget } from "./api/activityDataSelection";
import type { ActivityDataSelectionHost } from "./activityDataSelection";

export function useExpressionResultDiagnostic(check: StudioExpressionResultCheck | undefined, source: string, syntax: string,
  descriptor: { typeName: string; collectionKind?: string | null }, ownerValue: unknown, target: ActivityDataTarget, host: ActivityDataSelectionHost | null) {
  const { nodeId, inputKey } = target;
  const { typeName, collectionKind } = descriptor;
  const { requestSources, ownerContext, documentRevision, baseRevision } = host ?? {};
  const assessment = useMemo(() => check && source.trim() ? {
    check, source, syntax, expectedType: { alias: typeName, collectionKind }, ownerValue,
    target: { nodeId, inputKey }, requestSources, ownerContext, documentRevision, baseRevision
  } : null, [baseRevision, check, collectionKind, documentRevision, inputKey, nodeId, ownerContext, ownerValue, requestSources, source, syntax, typeName]);
  const [checked, setChecked] = useState<{ assessment: NonNullable<typeof assessment>; diagnostic: StudioExpressionResultDiagnostic }>();
  useEffect(() => {
    if (!assessment) return;
    const controller = new AbortController();
    const abort = () => { controller.abort(); setChecked(undefined); };
    window.addEventListener(authSessionEndedEvent, abort);
    // A request uses the current draft snapshot. Wait until the author pauses before doing that work.
    const timer = window.setTimeout(async () => {
      if (controller.signal.aborted) return;
      const { check, expectedType, source } = assessment;
      try {
        const diagnostic = await check.check({
          source, expectedType,
          resolveParameterType: (name, memberPath) => resolveParameterType(assessment, name, memberPath, controller.signal)
        });
        if (!controller.signal.aborted && diagnostic) setChecked({ assessment, diagnostic });
      } catch {
        // A missing, stale, or refused fact is inconclusive.
      }
    }, 220);
    return () => {
      window.clearTimeout(timer);
      controller.abort();
      window.removeEventListener(authSessionEndedEvent, abort);
    };
  }, [assessment]);
  return checked && checked.assessment === assessment ? checked.diagnostic : undefined;
}

async function resolveParameterType(assessment: {
  ownerValue: unknown; syntax: string; target: ActivityDataTarget;
  requestSources?: ActivityDataSelectionHost["requestSources"]; documentRevision?: string; baseRevision?: string;
}, name: string, memberPath: readonly string[], signal: AbortSignal): Promise<StudioExpressionResultType | undefined> {
  const { ownerValue, syntax, target, requestSources, documentRevision, baseRevision } = assessment;
  if (!requestSources || !target.nodeId || !baseRevision || signal.aborted) return;
  const definition = readPortableExpressionDefinition(ownerValue, syntax);
  if (!definition || !Object.hasOwn(definition.parameters, name)) return;
  const parameterSource = readActivityDataParameterSource(definition.parameters[name]);
  if (!parameterSource || (parameterSource.kind === "ActivityResult" && parameterSource.isOptional) ||
      (parameterSource.kind === "WorkflowRequest" && parameterSource.path != null)) return;
  const result = await requestSources(target, signal, 0, { expressionType: syntax, parameterSource, memberPath, includeMemberShapes: true });
  if (signal.aborted || result.state !== "supported" || result.expressionType !== syntax || !result.expressionParametersEnabled ||
      result.documentRevision !== documentRevision || result.baseRevision !== baseRevision ||
      result.target?.target?.nodeId !== target.nodeId || result.target.target.inputKey !== target.inputKey ||
      result.target.isSensitive || result.target.isCredential || result.sources.length !== 1) return;
  const source = result.sources[0];
  const shape = source.shape;
  if (!matchesParameterOffer(parameterSource, memberPath, source) || shape?.state !== "known" ||
      shape.isNullable !== false || shape.presence === "optional" || shape.isSensitive || shape.isCredential) return;
  if (!shape.type || typeof shape.type.collectionKind !== "string") return;
  return { alias: shape.type.alias, collectionKind: shape.type.collectionKind };
}
