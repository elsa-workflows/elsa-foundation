import type {
  ElsaStudioModuleApi, StudioWeaverPromptDispatcher, StudioWeaverPromptRequest, StudioWeaverRegistry, StudioContributionComposition,
  StudioContributionListOptions
} from "../../sdk";

type Registration = {
  moduleId?: string;
  status: "registering" | "loaded" | "failed" | "retired";
  pending: Set<() => void>;
  cleanup: Set<() => void>;
};
type Registry = {
  add(value: unknown): void;
  list(options?: StudioContributionListOptions<unknown>): unknown[];
  compose(options?: StudioContributionListOptions<unknown>): StudioContributionComposition<unknown>[];
};
type Generation = {
  current: boolean;
  revision: number;
  registrations: Map<string, Registration>;
  origins: WeakMap<object, Set<Registration>>;
  adds: Map<Registry, Registry["add"]>;
};

// Private lifetime observations. Resource moduleId is never used to infer who registered an object.
const generations = new WeakMap<StudioWeaverRegistry, Generation>();
const promptOrigins = new WeakMap<StudioWeaverPromptRequest, { generation: Generation; registration: Registration }>();

export function initializeWeaverRegistryGeneration(weaver: StudioWeaverRegistry) {
  const known = generations.get(weaver);
  if (known) return known;
  const generation: Generation = { current: true, revision: 0, registrations: new Map(), origins: new WeakMap(), adds: new Map() };
  generations.set(weaver, generation);
  const direct: Registration = { status: "loaded", pending: new Set(), cleanup: new Set() };
  for (const value of Object.values(weaver)) {
    if (!isRegistry(value) || generation.adds.has(value)) continue;
    const add = value.add; const list = value.list; const compose = value.compose;
    generation.adds.set(value, add);
    // SDK composition short-circuits the host veto before runtime availability. Preserve existing
    // direct registrations as unattributed; they do not prove an installed module's support.
    for (const entry of compose.call(value, { hostPolicy: () => false, includeHidden: true, includeUnavailable: true })) {
      record(generation, entry.contribution, direct);
    }
    value.add = contribution => { record(generation, contribution, direct); add.call(value, contribution); };
    const optionsFor = (options: StudioContributionListOptions<unknown> = {}): StudioContributionListOptions<unknown> => ({
      ...options,
      hostPolicy: context => available(generation, context.contribution) ? options.hostPolicy?.(context) : false
    });
    value.list = options => list.call(value, optionsFor(options));
    value.compose = options => compose.call(value, optionsFor(options));
  }
  return generation;
}

export function beginWeaverModuleRegistration(api: ElsaStudioModuleApi, moduleId: string) {
  const generation = initializeWeaverRegistryGeneration(api.weaver);
  const previous = generation.registrations.get(moduleId);
  if (previous) withdraw(api, previous, "retired");
  const registration: Registration = { moduleId, status: generation.current ? "registering" : "retired", pending: new Set(), cleanup: new Set() };
  generation.registrations.set(moduleId, registration);
  generation.revision++;
  const scopedRegistries = new Map<Registry, Registry>();
  const dispatcher = scopeDispatcher(api.weaver, generation, registration);
  const legacy = api.ai === api.weaver ? dispatcher : scopeDispatcher(api.ai, generation, registration);
  const weaver = new Proxy(api.weaver, {
    get(target, key) {
      if (key === "dispatchPrompt" || key === "onPrompt" || key === "publishPromptResult" || key === "onPromptResult") return dispatcher[key];
      const value: unknown = Reflect.get(target, key, target);
      if (!isRegistry(value)) return value;
      let scoped = scopedRegistries.get(value);
      if (!scoped) {
        scoped = new Proxy(value, {
          get(registry, property) {
            if (property === "add") return (contribution: unknown) => {
              record(generation, contribution, registration);
              generation.adds.get(registry)!.call(registry, contribution);
            };
            return Reflect.get(registry, property, registry);
          }
        });
        scopedRegistries.set(value, scoped);
      }
      return scoped;
    }
  });
  generations.set(weaver, generation);
  return {
    api: new Proxy(api, { get: (target, key) => key === "weaver" ? weaver : key === "ai" ? (api.ai === api.weaver ? weaver : legacy) : Reflect.get(target, key, target) }),
    pending() { return generation.current && registration.status === "registering"; },
    complete() {
      if (!generation.current || registration.status !== "registering") return false;
      registration.status = "loaded";
      generation.revision++;
      for (const activate of registration.pending) activate();
      registration.pending.clear();
      return true;
    },
    fail() { if (registration.status !== "retired") withdraw(api, registration, "failed"); }
  };
}

/** Explicit lifetime withdrawal only; a missing/incomplete inventory is not an instruction to retire. */
export function retireWeaverModuleRegistration(api: ElsaStudioModuleApi, moduleId: string) {
  const registration = generations.get(api.weaver)?.registrations.get(moduleId);
  if (registration) withdraw(api, registration, "retired");
}

export function retireWeaverRegistryGeneration(api: ElsaStudioModuleApi) {
  const generation = initializeWeaverRegistryGeneration(api.weaver);
  generation.current = false;
  for (const registration of generation.registrations.values()) withdraw(api, registration, "retired");
}

export function isWeaverRegistryCurrent(api: ElsaStudioModuleApi) {
  return generations.get(api.weaver)?.current !== false;
}

export function isWeaverContributionCurrent(api: ElsaStudioModuleApi, contribution: unknown) {
  const generation = generations.get(api.weaver);
  return !generation || available(generation, contribution);
}

/** Private discovery observations; registration identity is not a resource ownership declaration. */
export function observeWeaverRegistrations(api: ElsaStudioModuleApi) {
  const generation = initializeWeaverRegistryGeneration(api.weaver);
  return {
    revision: generation.revision,
    current: generation.current,
    modules: [...generation.registrations].map(([moduleId, registration]) => ({ moduleId, status: registration.status }))
  };
}

export function observeWeaverContributionOrigin(api: ElsaStudioModuleApi, contribution: unknown) {
  const generation = generations.get(api.weaver);
  const origins = contribution && typeof contribution === "object" ? generation?.origins.get(contribution) : undefined;
  if (!origins || origins.size !== 1) return undefined;
  const origin = [...origins][0];
  return origin.moduleId ? { moduleId: origin.moduleId, status: origin.status } : undefined;
}

/** Host copies preserve private dispatch provenance without adding caller-controlled wire fields. */
export function copyWeaverPromptOrigin(source: StudioWeaverPromptRequest, target: StudioWeaverPromptRequest) {
  const origin = promptOrigins.get(source);
  if (origin) promptOrigins.set(target, origin);
}

export function isWeaverPromptRequestCurrent(api: ElsaStudioModuleApi, request: StudioWeaverPromptRequest) {
  const origin = promptOrigins.get(request);
  return !origin || origin.generation === generations.get(api.weaver) && origin.generation.current && origin.registration.status === "loaded";
}

function scopeDispatcher(dispatcher: StudioWeaverPromptDispatcher, generation: Generation, registration: Registration): StudioWeaverPromptDispatcher {
  const current = () => generation.current && registration.status === "loaded";
  return {
    dispatchPrompt(request) {
      if (current()) { promptOrigins.set(request, { generation, registration }); dispatcher.dispatchPrompt(request); }
      else if (request.requestId) dispatcher.publishPromptResult({ requestId: request.requestId, status: "failed", text: "" });
    },
    onPrompt(listener) { return subscribe(registration, () => dispatcher.onPrompt(listener)); },
    publishPromptResult(result) { if (current()) dispatcher.publishPromptResult(result); },
    onPromptResult(listener) { return subscribe(registration, () => dispatcher.onPromptResult(listener)); }
  };
}

/** Keep the original callback and SDK Set semantics; only its attachment lifetime belongs here. */
function subscribe(registration: Registration, attach: () => () => void) {
  if (registration.status !== "registering" && registration.status !== "loaded") return () => {};
  let cancelled = false;
  let unsubscribe: (() => void) | undefined;
  const activate = () => { registration.pending.delete(activate); if (!cancelled) unsubscribe = attach(); };
  const release = () => {
    cancelled = true;
    registration.pending.delete(activate);
    registration.cleanup.delete(release);
    // Repeated explicit calls retain the SDK's ordinary delete/re-add behavior. Removing the
    // handle above prevents a later automatic withdrawal from repeating a manual unsubscribe.
    unsubscribe?.();
  };
  registration.cleanup.add(release);
  if (registration.status === "loaded") activate();
  else registration.pending.add(activate);
  return release;
}

function withdraw(api: ElsaStudioModuleApi, registration: Registration, status: "failed" | "retired") {
  registration.status = status;
  const generation = generations.get(api.weaver);
  if (generation) generation.revision++;
  let cleanupFailed = false;
  for (const release of registration.cleanup) {
    try { release(); } catch { cleanupFailed = true; }
  }
  registration.pending.clear();
  registration.cleanup.clear();
  if (cleanupFailed) {
    // A broken custom disposer must neither replace the primary registration/connection failure
    // nor stop the remaining owned cleanup. The native SDK's disposers only delete from a Set.
    try { api.diagnostics.add({ moduleId: registration.moduleId ?? "", status: "failed", reason: "Module prompt subscription cleanup could not complete." }); } catch { /* Preserve the primary failure. */ }
  }
}

function record(generation: Generation, contribution: unknown, registration: Registration) {
  if (!contribution || typeof contribution !== "object") return;
  let origins = generation.origins.get(contribution);
  if (!origins) { origins = new Set(); generation.origins.set(contribution, origins); }
  origins.add(registration);
  generation.revision++;
}

function available(generation: Generation, contribution: unknown) {
  if (!generation.current) return false;
  if (!contribution || typeof contribution !== "object") return true;
  const origins = generation.origins.get(contribution);
  // Objects not registered through a loader remain compatible but unattributed. A second origin
  // cannot overwrite a failed origin or make the same object ambiguously belong to two modules.
  return !origins || (origins.size === 1 && [...origins][0].status === "loaded");
}

function isRegistry(value: unknown): value is Registry {
  return !!value && typeof value === "object" && "add" in value && typeof value.add === "function"
    && "list" in value && typeof value.list === "function" && "compose" in value && typeof value.compose === "function";
}
