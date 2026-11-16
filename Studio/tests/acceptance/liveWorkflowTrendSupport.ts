import { expect } from "@playwright/test";
import type { QuestionDefinition, QueryPredicate, QueryResult, QueryValue } from "../../src/essentials/Elsa.Studio.Dashboard/Client/src/native/types";
import type { Backend } from "./dashboardJourneySupport";

export type TrendFact = { id: string; definitionId: string; status: string; runKind: string; startedAt: string };
type Published = { artifactId: string; sourceReferenceId: string };
type TrendDefinition = { id: string; folderId: string; folderPath: string; faultVersion: string; faultPublished: Published };
export type TrendSeed = { definitions: TrendDefinition[]; facts: TrendFact[]; currentFrom: string; baselineFrom: string };

/** Controlled start instants; normal publication, execution, test runs and checkpoints supply every fact. */
export async function seedWorkflowTrend(backend: Backend, suffix: string): Promise<TrendSeed> {
  const now = new Date(); const week = new Date(now); week.setUTCHours(0, 0, 0, 0);
  week.setUTCDate(week.getUTCDate() - (week.getUTCDay() + 6) % 7);
  const current = new Date(week.getTime() + Math.max(1, Math.floor((now.getTime() - week.getTime()) / 2))).toISOString();
  const baseline = new Date(Date.parse(current) - 7 * 86_400_000).toISOString();
  const seed: TrendSeed = { definitions: [], facts: [], currentFrom: week.toISOString(), baselineFrom: new Date(week.getTime() - 7 * 86_400_000).toISOString() };
  const activities = new Map<string, string>();
  for (const name of ["WriteLine", "Fault"]) {
    const key = `Elsa.Activities.Primitives.Activities.${name}`;
    const catalog = await backend.api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(key)}`);
    const activity = catalog.items.find(item => item.definition.activityTypeKey === key); expect(activity).toBeTruthy();
    activities.set(name, activity!.definition.headVersionId);
  }
  const state = (name: string) => ({ variables: [], inputs: [], outputs: [], rootActivity: {
    nodeId: `trend-${name.toLowerCase()}`, activityVersionId: activities.get(name),
    inputs: name === "WriteLine" ? [{ referenceKey: "text", value: { expressionType: "Literal", value: "Owned Dashboard trend witness" } }] : [], outputs: []
  } });
  const created: string[] = [];
  try {
    for (const [index, label] of ["Increasing", "Decreasing"].entries()) {
      const submitted = await backend.api<{ definition: { id: string }; version: { id: string } }>("/design/workflows/definitions/submit", "POST", { name: `${label} rate ${suffix}`, state: state("WriteLine") });
      created.push(submitted.definition.id);
      const completed = await backend.api<Published>(`/publishing/workflows/${submitted.version.id}/publish`, "POST", {});
      const runMany = async (published: Published, version: string, period: string, count: number, fault: boolean, test = false) => {
        for (let number = 0; number < count; number++) seed.facts.push(await executeTrendRun(backend, submitted.definition.id, published, version, period, fault, test));
      };
      await runMany(completed, submitted.version.id, baseline, index === 0 ? 3 : 2, false);
      await runMany(completed, submitted.version.id, current, index === 0 ? 1 : 3, false);
      if (index === 0) await runMany(completed, submitted.version.id, current, 4, false, true);
      const fault = await backend.api<{ id: string }>("/design/workflows/versions/ingest", "POST", { operationKey: `fault-${suffix}-${index}`, definitionId: submitted.definition.id, state: state("Fault") });
      const faultPublished = await backend.api<Published>(`/publishing/workflows/${fault.id}/publish`, "POST", {});
      await runMany(faultPublished, fault.id, baseline, index === 0 ? 1 : 2, true);
      await runMany(faultPublished, fault.id, current, index === 0 ? 3 : 1, true);
      if (index === 0) await runMany(faultPublished, fault.id, baseline, 1, true, true);
      else await runMany(faultPublished, fault.id, current, 4, true, true);
      // Current organization is intentionally assigned AFTER all historical fixture starts.
      const folder = await backend.api<{ id: string }>("/design/workflows/folders", "POST", { name: `${label} folder ${suffix}` });
      await backend.api("/design/workflows/definitions/move", "POST", { definitionIds: [submitted.definition.id], folderId: folder.id, operationKey: `move-${suffix}-${index}` });
      const organization = await backend.api<{ folderId: string; folderBreadcrumb: { name: string }[] }>(`/design/workflows/definitions/${submitted.definition.id}/organization`);
      expect(organization.folderId).toBe(folder.id);
      seed.definitions.push({ id: submitted.definition.id, folderId: folder.id, folderPath: "/" + organization.folderBreadcrumb.map(item => item.name).join("/"), faultVersion: fault.id, faultPublished });
    }
    return seed;
  } catch (error) {
    for (const id of created) await backend.api(`/design/workflows/definitions/${id}`, "DELETE");
    throw error;
  } finally { await backend.api("/__qualification/dashboard-live/start-clock", "POST", { instant: null }); }
}

export async function executeTrendRun(backend: Backend, definitionId: string, published: Published, version: string, startedAt: string, fault: boolean, test = false): Promise<TrendFact> {
  await backend.api("/__qualification/dashboard-live/start-clock", "POST", { instant: startedAt });
  try {
    const run = await backend.api<{ workflowExecutionId: string }>(test ? `/publishing/workflows/${version}/test-runs` : `/runtime/workflows/executables/${published.artifactId}/execute`, "POST", test ? { acknowledgeUnavailableExpressionValidation: true } : { sourceReferenceId: published.sourceReferenceId });
    expect(run.workflowExecutionId).toBeTruthy();
    const read = async () => {
      const response = await backend.response(`/runtime/workflows/instances/${run.workflowExecutionId}`);
      if (response.status() === 404) return undefined; // A normal asynchronous start has not checkpointed yet.
      expect(response.ok()).toBe(true);
      return await response.json() as { instance: { definitionId: string; status: string; runKind: string; startedAt: string } };
    };
    await expect.poll(async () => (await read())?.instance.status ?? "notStarted", { timeout: 30_000 }).toBe(fault ? "Faulted" : "Completed");
    const observed = (await read())!.instance;
    expect(observed.definitionId).toBe(definitionId);
    expect(Date.parse(observed.startedAt), "The real checkpoint must persist the controlled start, not the system instant").toBe(Date.parse(startedAt));
    expect(observed.runKind.toLowerCase()).toBe(test ? "testrun" : "publishedrun");
    return { id: run.workflowExecutionId, definitionId, status: observed.status, runKind: observed.runKind, startedAt: observed.startedAt };
  } finally { await backend.api("/__qualification/dashboard-live/start-clock", "POST", { instant: null }); }
}

function conjuncts(value?: QueryPredicate): QueryPredicate[] {
  if (!value) return [];
  expect(value.any, "The independently calculated cohort is conjunctive").toBeUndefined();
  return value.all ? value.all.flatMap(conjuncts) : [value];
}
function resolve(value: QueryValue | undefined, definition: QuestionDefinition) {
  return value?.parameter ? definition.parameterValues?.[value.parameter]?.literal ?? definition.query.parameters.find(item => item.name === value.parameter)?.default?.literal : value?.literal;
}

/** Inputs come from ordinary runtime inspections; no expected number is copied from a Dashboard row. */
export function workflowTrendOracle(definition: QuestionDefinition, result: QueryResult, seed: TrendSeed, refined: boolean) {
  const query = definition.query;
  expect(query.dataset.id).toBe("elsa.workflows.runs"); expect(query.form).toBe("aggregate");
  expect(query.groupBy).toEqual([refined ? "currentFolder" : "definitionId"]);
  expect(query.time).toMatchObject({ field: "startedAt", window: "calendarWeekToDate", timeZone: "UTC" });
  expect(query.time?.bucket ?? null).toBeNull();
  expect(query.comparison?.mode).toBe("previousCalendarWeekSameElapsed"); expect(query.metrics).toHaveLength(1);
  expect(query.metrics[0]).toMatchObject({ metric: "workflow.failure-rate", revision: "started-current-status-v1" });
  const alias = query.metrics[0].alias; const filters = conjuncts(query.filters);
  const cohort = filters.filter(item => item.field === "definitionId"); expect(cohort).toHaveLength(1);
  expect(cohort[0].operator).toBe("in");
  expect(cohort[0].values?.map(value => resolve(value, definition)).sort()).toEqual(seed.definitions.map(item => item.id).sort());
  const others = filters.filter(item => item.field !== "definitionId"); expect(others).toHaveLength(refined ? 1 : 0);
  if (refined) {
    expect(others[0].field).toBe("runKind");
    if (others[0].operator === "ne") expect(resolve(others[0].value, definition)).toBe("testRun");
    else { expect(others[0].operator).toBe("notIn"); expect(others[0].values?.map(value => resolve(value, definition))).toEqual(["testRun"]); }
  }
  const having = conjuncts(query.having); expect(having).toHaveLength(1);
  expect(having[0]).toMatchObject({ metric: `delta.${alias}`, operator: "gt" }); expect(Number(resolve(having[0].value, definition))).toBe(0);
  const current = result.periods?.find(item => item.role === "current"); const baseline = result.periods?.find(item => item.role === "baseline");
  expect(current).toBeTruthy(); expect(baseline).toBeTruthy();
  expect(Date.parse(current!.from)).toBe(Date.parse(seed.currentFrom)); expect(Date.parse(baseline!.from)).toBe(Date.parse(seed.baselineFrom));
  expect(Date.parse(current!.to) - Date.parse(current!.from)).toBe(Date.parse(baseline!.to) - Date.parse(baseline!.from));
  const column = result.columns.find(item => item.id === alias || item.id === `current.${alias}`)?.id; expect(column).toBeTruthy();
  const expected = seed.definitions.map(item => {
    const members = (period: { from: string; to: string }) => seed.facts.filter(fact => fact.definitionId === item.id && (!refined || fact.runKind.toLowerCase() !== "testrun") && Date.parse(fact.startedAt) >= Date.parse(period.from) && Date.parse(fact.startedAt) < Date.parse(period.to));
    const currentMembers = members(current!); const baselineMembers = members(baseline!);
    expect(currentMembers.length).toBeGreaterThan(0); expect(baselineMembers.length).toBeGreaterThan(0);
    const rate = (facts: TrendFact[]) => 100 * facts.filter(fact => fact.status === "Faulted").length / facts.length;
    const currentRate = rate(currentMembers); const baselineRate = rate(baselineMembers);
    return { key: refined ? item.folderPath : item.id, current: currentRate, baseline: baselineRate, delta: currentRate - baselineRate, currentIds: currentMembers.map(fact => fact.id) };
  }).filter(item => item.delta > 0);
  expect(expected).toHaveLength(1); expect(result.rows).toHaveLength(1);
  for (const item of expected) {
    const row = result.rows.find(value => value.values[query.groupBy[0]]?.value === item.key); expect(row).toBeTruthy();
    expect(Number(row!.values[column!].value)).toBeCloseTo(item.current, 10);
    expect(Number(row!.values[`baseline.${alias}`].value)).toBeCloseTo(item.baseline, 10);
    expect(Number(row!.values[`delta.${alias}`].value)).toBeCloseTo(item.delta, 10);
  }
  expect(expected[0].key).toBe(refined ? seed.definitions[0].folderPath : seed.definitions[1].id);
  return { refined, expected, observedQuery: query, source: "normal published/test runs and runtime inspections; controlled start-only clock; global IAM/evaluation clock unchanged" };
}
