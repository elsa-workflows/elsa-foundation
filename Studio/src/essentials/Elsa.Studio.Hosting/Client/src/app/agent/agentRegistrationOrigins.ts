import type {
  ElsaStudioModuleApi, StudioAgentRegistry, StudioContributionComposition,
  StudioContributionListOptions
} from "../../sdk";

type Registration = { moduleId?: string; status: "registering" | "loaded" | "failed" | "retired" };
type Registry = {
  add(value: unknown): void;
  list(options?: StudioContributionListOptions<unknown>): unknown[];
  compose(options?: StudioContributionListOptions<unknown>): StudioContributionComposition<unknown>[];
};
type Generation = {
  current: boolean;
  registrations: Map<string, Registration>;
  origins: WeakMap<object, Set<Registration>>;
  adds: Map<Registry, Registry["add"]>;
};

// Private lifetime observations. Resource moduleId is never used to infer who registered an object.
const generations = new WeakMap<StudioAgentRegistry, Generation>();

export function initializeAgentRegistryGeneration(agent: StudioAgentRegistry) {
  const known = generations.get(agent);
  if (known) return known;
  const generation: Generation = { current: true, registrations: new Map(), origins: new WeakMap(), adds: new Map() };
  generations.set(agent, generation);
  const direct: Registration = { status: "loaded" };
  for (const value of Object.values(agent)) {
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

export function beginAgentModuleRegistration(api: ElsaStudioModuleApi, moduleId: string) {
  const generation = initializeAgentRegistryGeneration(api.agent);
  const previous = generation.registrations.get(moduleId);
  if (previous) previous.status = "retired";
  const registration: Registration = { moduleId, status: generation.current ? "registering" : "retired" };
  generation.registrations.set(moduleId, registration);
  const scopedRegistries = new Map<Registry, Registry>();
  const agent = new Proxy(api.agent, {
    get(target, key) {
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
  generations.set(agent, generation);
  return {
    api: new Proxy(api, { get: (target, key) => key === "agent" ? agent : Reflect.get(target, key, target) }),
    pending() { return generation.current && registration.status === "registering"; },
    complete() {
      if (!generation.current || registration.status !== "registering") return false;
      registration.status = "loaded";
      return true;
    },
    fail() { if (registration.status !== "retired") registration.status = "failed"; }
  };
}

/** Explicit lifetime withdrawal only; a missing/incomplete inventory is not an instruction to retire. */
export function retireAgentModuleRegistration(api: ElsaStudioModuleApi, moduleId: string) {
  const registration = generations.get(api.agent)?.registrations.get(moduleId);
  if (registration) registration.status = "retired";
}

export function retireAgentRegistryGeneration(api: ElsaStudioModuleApi) {
  const generation = initializeAgentRegistryGeneration(api.agent);
  generation.current = false;
  for (const registration of generation.registrations.values()) registration.status = "retired";
}

export function isAgentRegistryCurrent(api: ElsaStudioModuleApi) {
  return generations.get(api.agent)?.current !== false;
}

export function isAgentContributionCurrent(api: ElsaStudioModuleApi, contribution: unknown) {
  const generation = generations.get(api.agent);
  return !generation || available(generation, contribution);
}

function record(generation: Generation, contribution: unknown, registration: Registration) {
  if (!contribution || typeof contribution !== "object") return;
  let origins = generation.origins.get(contribution);
  if (!origins) { origins = new Set(); generation.origins.set(contribution, origins); }
  origins.add(registration);
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
