import type { ActivityCatalogItem } from "../workflowTypes";
import { compareSemVer, parseSemVer } from "../semver";
import { createActivityNode } from "../workflowAdapter";
import { flowchartStructureKind } from "../flowchartStartNode";

const starterActivityTypes = [
  "Elsa.Activities.Flowchart.Activities.Flowchart",
  "Elsa.Activities.Http.Activities.HttpEndpoint",
  "Elsa.Activities.Http.Activities.WriteHttpResponse"
] as const;
const starterActivityNames = ["Flowchart", "HTTP Endpoint", "Write HTTP Response"] as const;

export function httpStarterActivities(catalog: ActivityCatalogItem[]) {
  return starterActivityTypes.map(type => {
    const matching = catalog.filter(activity => activity.activityTypeKey === type && activity.available !== false);
    const versioned = matching.flatMap(activity => {
      const version = parseSemVer(activity.version);
      return version ? [{ activity, version }] : [];
    });
    if (!versioned.length) return matching[0];
    return versioned.reduce((latest, candidate) => compareSemVer(candidate.version, latest.version) > 0 ? candidate : latest).activity;
  });
}

export function getHttpStarterAvailabilityReason(catalog: ActivityCatalogItem[]) {
  const available = httpStarterActivities(catalog);
  const unavailable = starterActivityTypes.flatMap((type, index) => {
    if (available[index]) return [];

    const name = starterActivityNames[index];
    const matches = catalog.filter(activity => activity.activityTypeKey === type);
    const suppliedReason = matches.find(activity => activity.available === false && activity.availabilityReason?.trim())?.availabilityReason?.trim();
    if (suppliedReason) return [`${name}: ${suppliedReason}`];
    if (matches.length > 0) return [`${name} is listed but unavailable in the activity catalog.`];
    return [`${name} is not present in the activity catalog.`];
  });

  return unavailable.length > 0
    ? `The HTTP request and response starter is unavailable. ${unavailable.join(" ")}`
    : undefined;
}

export function createHttpStarterState(catalog: ActivityCatalogItem[]) {
  const [flowchart, endpoint, response] = httpStarterActivities(catalog);
  if (!flowchart || !endpoint || !response) throw new Error("The HTTP starter needs Flowchart, HTTP Endpoint and Write HTTP Response activities on this server.");

  const suffix = crypto.randomUUID();
  const endpointNode = {
    ...createActivityNode(endpoint, `http-endpoint-${suffix}`),
    inputs: [
      { referenceKey: "Path", value: { expressionType: "Literal", value: "/hello" } },
      { referenceKey: "SupportedMethods", value: { expressionType: "Object", value: '["POST"]' } },
      { referenceKey: "CanStartWorkflow", value: { expressionType: "Literal", value: "true" } }
    ],
    outputs: []
  };
  const responseNode = {
    ...createActivityNode(response, `http-response-${suffix}`),
    inputs: [
      { referenceKey: "Body", value: { expressionType: "Literal", value: '{"message":"Hello from Elsa"}' } },
      { referenceKey: "ContentType", value: { expressionType: "Literal", value: "application/json" } },
      { referenceKey: "StatusCode", value: { expressionType: "Literal", value: "200" } }
    ],
    outputs: []
  };
  const rootActivity = createActivityNode(flowchart, `flowchart-${suffix}`);
  return {
    variables: [],
    inputs: [],
    outputs: [],
    rootActivity: {
      ...rootActivity,
      structure: {
        kind: flowchartStructureKind,
        schemaVersion: "1.0.0",
        payload: {
          activities: [endpointNode, responseNode],
          connections: [{
            id: `http-response-${suffix}`,
            source: { nodeId: endpointNode.nodeId, port: "Done" },
            target: { nodeId: responseNode.nodeId }
          }],
          startNodeId: endpointNode.nodeId,
          nodeMetadata: {},
          connectionMetadata: {}
        }
      }
    }
  };
}
