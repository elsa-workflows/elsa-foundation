export const defaultHttpTestRequest = {
  routeValues: {},
  query: {},
  headers: {},
  body: ""
};

export const defaultHttpTestRequestDraft = JSON.stringify(defaultHttpTestRequest, null, 2);

export function defaultStimulusDraft(
  option?: { driverId?: string | null } | null,
  mode?: { modeId?: string | null } | null
) {
  if (option?.driverId === "Elsa.HttpEndpoint" && mode?.modeId === "http-request") {
    return defaultHttpTestRequestDraft;
  }
  return "{}";
}
