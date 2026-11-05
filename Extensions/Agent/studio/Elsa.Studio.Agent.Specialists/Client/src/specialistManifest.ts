/**
 * The specialist implementation payload (Elsa.Agent.Specialists.Core SpecialistManifest), in web casing. The generic
 * activity contract owns the public inputs and results; this payload holds only implementation settings. The form
 * patches the saved payload member by member, so anything it does not edit (or cannot read) is kept as saved.
 */
export interface SpecialistManifest {
  instructions: string;
  model: { providerKey: string; connectionName: string; modelId: string };
  settings: Record<string, unknown>;
  inputConstraints: Record<string, unknown>;
  resultConstraints: Record<string, unknown>;
  limits: { maximumModelCalls: number; timeoutMilliseconds: number; maximumOutputTokens?: number | null };
  allowContinuity?: boolean;
}

/** Published bounds the server enforces (SpecialistActivityProvider and SpecialistTurnLimits). */
export const specialistBounds = {
  instructionsLength: 32_768,
  modelCalls: { minimum: 1, maximum: 32, default: 8 },
  timeoutSeconds: { minimum: 1, maximum: 1_800, default: 300 }
} as const;

/** Schema "2" adds explicit conversation continuity; schema "1" never continues a conversation. */
export const continuitySchemaVersion = "2";

/** The members the server's closed manifest schema accepts; it refuses any other. */
export const manifestMembers = ["instructions", "model", "settings", "inputConstraints", "resultConstraints", "limits", "allowContinuity"] as const;

export function initialSpecialistManifest(): SpecialistManifest {
  return {
    instructions: "",
    model: { providerKey: "", connectionName: "", modelId: "" },
    settings: {},
    inputConstraints: {},
    resultConstraints: {},
    limits: { maximumModelCalls: specialistBounds.modelCalls.default, timeoutMilliseconds: specialistBounds.timeoutSeconds.default * 1000 }
  };
}

export const record = (value: unknown): Record<string, unknown> =>
  value && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : {};
const text = (value: unknown) => typeof value === "string" ? value : "";

/** A typed view of the saved payload for display and checks. It is never written back as a whole. */
export function readSpecialistManifest(payload: unknown, schemaVersion: string): SpecialistManifest {
  const source = record(payload);
  const model = record(source.model);
  const limits = record(source.limits);
  const initial = initialSpecialistManifest();
  const manifest: SpecialistManifest = {
    instructions: text(source.instructions),
    model: { providerKey: text(model.providerKey), connectionName: text(model.connectionName), modelId: text(model.modelId) },
    settings: record(source.settings),
    inputConstraints: record(source.inputConstraints),
    resultConstraints: record(source.resultConstraints),
    limits: {
      maximumModelCalls: typeof limits.maximumModelCalls === "number" ? limits.maximumModelCalls : initial.limits.maximumModelCalls,
      timeoutMilliseconds: typeof limits.timeoutMilliseconds === "number" ? limits.timeoutMilliseconds : initial.limits.timeoutMilliseconds,
      ...(typeof limits.maximumOutputTokens === "number" ? { maximumOutputTokens: limits.maximumOutputTokens } : {})
    }
  };
  if (schemaVersion === continuitySchemaVersion) manifest.allowContinuity = source.allowContinuity === true;
  return manifest;
}

/** Saved members the server would refuse: unknown members, and limits or constraint maps of the wrong shape. */
export function unreadableMembers(payload: unknown) {
  const source = record(payload);
  const limits = record(source.limits);
  const unknown = Object.keys(source).filter(key => !(manifestMembers as readonly string[]).includes(key));
  const malformed = (["maximumModelCalls", "timeoutMilliseconds", "maximumOutputTokens"] as const)
    .filter(key => key in limits && limits[key] !== null && !Number.isInteger(limits[key]))
    .map(key => `limits.${key}`);
  return { unknown, malformed };
}

/** Seconds as typed become whole milliseconds, or null when they are not a number of milliseconds. */
export function secondsToMilliseconds(value: string) {
  const seconds = Number(value.trim());
  if (!value.trim() || !Number.isFinite(seconds)) return null;
  const milliseconds = Math.round(seconds * 1000);
  return Math.abs(seconds * 1000 - milliseconds) < 1e-6 ? milliseconds : null;
}

/** Field-level problems the form can show before the server's own diagnostics. They never replace server validation. */
export function specialistProblems(manifest: SpecialistManifest, model?: { maximumOutputTokens: number; temperature?: { minimum?: number; maximum?: number } } | null) {
  const problems: Partial<Record<"instructions" | "model" | "modelCalls" | "timeout" | "outputTokens" | "temperature", string>> = {};
  if (!manifest.instructions.trim()) problems.instructions = "Instructions are required.";
  else if (manifest.instructions.length > specialistBounds.instructionsLength) problems.instructions = `Instructions are limited to ${specialistBounds.instructionsLength.toLocaleString()} characters.`;
  if (!manifest.model.providerKey || !manifest.model.connectionName || !manifest.model.modelId) problems.model = "Choose a model connection.";
  const calls = manifest.limits.maximumModelCalls;
  if (calls < specialistBounds.modelCalls.minimum || calls > specialistBounds.modelCalls.maximum)
    problems.modelCalls = `Use between ${specialistBounds.modelCalls.minimum} and ${specialistBounds.modelCalls.maximum} model calls.`;
  const seconds = manifest.limits.timeoutMilliseconds / 1000;
  if (seconds < specialistBounds.timeoutSeconds.minimum || seconds > specialistBounds.timeoutSeconds.maximum)
    problems.timeout = `Use a timeout between ${specialistBounds.timeoutSeconds.minimum} and ${specialistBounds.timeoutSeconds.maximum} seconds.`;
  const tokens = manifest.limits.maximumOutputTokens;
  if (tokens != null && (tokens < 1 || (model && tokens > model.maximumOutputTokens)))
    problems.outputTokens = model ? `Use between 1 and ${model.maximumOutputTokens} output tokens for this model.` : "Use a positive number of output tokens.";
  const temperature = manifest.settings.temperature;
  const range = model?.temperature;
  if (typeof temperature === "number" && range && ((range.minimum != null && temperature < range.minimum) || (range.maximum != null && temperature > range.maximum)))
    problems.temperature = `This model accepts a temperature ${[range.minimum != null ? `from ${range.minimum}` : "", range.maximum != null ? `up to ${range.maximum}` : ""].filter(Boolean).join(" ")}.`;
  return problems;
}
