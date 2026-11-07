import React from "react";
import { StudioField } from "@elsa-workflows/studio-ui";
import { defaultPolicy, type Invocation, type Policy } from "./types";
import { parseInvocationJson } from "./jsonPayload";

export interface InvocationDraft {
  jobType: string; handlerVersion: string; queue: string; inputAlias: string; inputVersion: number;
  resultAlias: string; resultVersion: number; input: string; notBefore: string; policy: Policy;
}
export function invocationDraft(value?: Invocation, valueJson?: string): InvocationDraft {
  return { jobType: value?.handler.jobType ?? "", handlerVersion: value?.handler.version ?? "1", queue: value?.queue ?? "default",
    inputAlias: value?.input.schema.alias ?? "", inputVersion: value?.input.schema.version ?? 1,
    resultAlias: value?.resultSchema.alias ?? "", resultVersion: value?.resultSchema.version ?? 1,
    input: valueJson ?? JSON.stringify(value ? value.input.value : {}, null, 2), notBefore: value?.notBefore ?? "", policy: structuredClone(value?.policy ?? defaultPolicy) };
}
export function utc(value: string) {
  // DateTimeOffset API projections use +00:00. Change only that suffix, preserving
  // all seven fractional digits rather than routing the instant through Date serialization.
  const normalized = value.replace(/\+00:00$/, "Z");
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$/.test(normalized) || !Number.isFinite(Date.parse(normalized)) || new Date(normalized).toISOString().slice(0, 19) !== normalized.slice(0, 19)) throw new Error("Enter an exact UTC time ending in Z, including seconds.");
  return normalized;
}
export function toInvocation(value: InvocationDraft): Invocation {
  for (const name of [value.jobType, value.handlerVersion, value.queue, value.inputAlias, value.resultAlias])
    if (!name.trim() || name.length > 256) throw new Error("Handler, queue and schema identities must be nonblank and at most 256 characters.");
  for (const version of [value.inputVersion, value.resultVersion]) if (!Number.isInteger(version) || version < 1 || version > 2147483647) throw new Error("Schema versions must be positive whole numbers.");
  const input = parseInvocationJson(value.input);
  const payload = { schema: { alias: value.inputAlias, version: value.inputVersion }, value: input };
  if (new TextEncoder().encode(JSON.stringify(payload)).length > 1048576) throw new Error("The input envelope must fit within 1 MiB.");
  if (!Number.isInteger(value.policy.retry.maximumAttempts) || value.policy.retry.maximumAttempts < 1 || value.policy.retry.maximumAttempts > 1000)
    throw new Error("Maximum attempts must be a whole number from 1 to 1,000.");
  return { handler: { jobType: value.jobType, version: value.handlerVersion }, queue: value.queue,
    input: payload, resultSchema: { alias: value.resultAlias, version: value.resultVersion },
    notBefore: value.notBefore ? utc(value.notBefore) : null, policy: structuredClone(value.policy) };
}
export function InvocationFields({ value, change }: { value: InvocationDraft; change(value: InvocationDraft): void }) {
  const field = (key: "jobType" | "handlerVersion" | "queue" | "inputAlias" | "resultAlias", label: string) => <StudioField label={label}><input required maxLength={256} value={value[key]} onChange={event => change({ ...value, [key]: event.target.value })} /></StudioField>;
  const retry = value.policy.retry;
  return <div className="jobs-stack"><div className="jobs-form-grid">{field("jobType", "Registered job type")}{field("handlerVersion", "Handler version")}{field("queue", "Queue")}
    {field("inputAlias", "Input schema alias")}<StudioField label="Input schema version"><input type="number" min={1} max={2147483647} step={1} required value={value.inputVersion} onChange={event => change({ ...value, inputVersion: Number(event.target.value) })} /></StudioField>
    {field("resultAlias", "Result schema alias")}<StudioField label="Result schema version"><input type="number" min={1} max={2147483647} step={1} required value={value.resultVersion} onChange={event => change({ ...value, resultVersion: Number(event.target.value) })} /></StudioField>
  </div><StudioField label="Input JSON" description="Use the registered input schema. Numbers that Studio cannot send without changing their value are refused. This submits independent work without attaching it to a workflow."><textarea required rows={9} spellCheck={false} value={value.input} onChange={event => change({ ...value, input: event.target.value })} /></StudioField>
    <details><summary>Execution policy and optional delay</summary><div className="jobs-form-grid">
      <StudioField label="Not before (UTC)" description="Optional ISO UTC instant, for example 2026-11-06T12:00:00Z."><input value={value.notBefore} onChange={event => change({ ...value, notBefore: event.target.value })} /></StudioField>
      <StudioField label="Maximum attempts" description="Includes the first authorized attempt."><input type="number" min={1} max={1000} step={1} value={retry.maximumAttempts} onChange={event => change({ ...value, policy: { ...value.policy, retry: { ...retry, maximumAttempts: Number(event.target.value) } } })} /></StudioField>
      {([['initialBackoff', 'Initial retry delay'], ['maximumBackoff', 'Maximum retry delay']] as const).map(([key, label]) => <StudioField key={key} label={label} description="Duration as days.hours:minutes:seconds; days are optional."><input required value={retry[key]} onChange={event => change({ ...value, policy: { ...value.policy, retry: { ...retry, [key]: event.target.value } } })} /></StudioField>)}
      <StudioField label="Backoff multiplier"><input type="number" min={1} step="any" required value={retry.backoffMultiplier} onChange={event => change({ ...value, policy: { ...value.policy, retry: { ...retry, backoffMultiplier: Number(event.target.value) } } })} /></StudioField>
      <StudioField label="Jitter fraction"><input type="number" min={0} max={1} step="any" required value={retry.jitterFraction} onChange={event => change({ ...value, policy: { ...value.policy, retry: { ...retry, jitterFraction: Number(event.target.value) } } })} /></StudioField>
      {([['attemptTimeout', 'Attempt timeout'], ['minimumKeyRecognition', 'Minimum producer-key recognition']] as const).map(([key, label]) => <StudioField key={key} label={label} description="Duration as days.hours:minutes:seconds; days are optional."><input required value={value.policy[key]} onChange={event => change({ ...value, policy: { ...value.policy, [key]: event.target.value } })} /></StudioField>)}
      <label><input type="checkbox" checked={retry.retryTimeouts} onChange={event => change({ ...value, policy: { ...value.policy, retry: { ...retry, retryTimeouts: event.target.checked } } })} /> Retry timed-out attempts within the budget</label>
    </div></details></div>;
}
