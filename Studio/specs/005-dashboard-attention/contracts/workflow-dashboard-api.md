# Workflow Dashboard API Contract

## Workflow Portfolio

```http
GET /_elsa/workflows/dashboard/definitions
```

```json
{
  "status": "ready",
  "generatedAt": "2026-07-13T10:00:00Z",
  "activeDefinitionCount": 42,
  "publishedDefinitionCount": 30,
  "unpublishedDraftCount": 14,
  "invalidDraftCount": 3
}
```

Counters overlap. Published means at least one live Published executable source
reference. Invalid means the current draft derives one or more validation errors.
Deleted definitions are excluded.

Unsupported third-party persistence returns an explicit `unavailable` snapshot
with a safe reason; official providers must return `ready` exact counts.

## Workflow Run Health

```http
GET /_elsa/workflows/dashboard/runs?from=2026-07-06T00:00:00Z&to=2026-07-13T00:00:00Z&timeZone=Europe%2FAmsterdam&bucket=day&includeTestRuns=false
```

`from` is inclusive and `to` exclusive. `timeZone` is an IANA identifier.
`bucket` is `hour` or `day`.

```json
{
  "status": "ready",
  "generatedAt": "2026-07-13T10:00:00Z",
  "from": "2026-07-06T00:00:00Z",
  "to": "2026-07-13T00:00:00Z",
  "timeZone": "Europe/Amsterdam",
  "bucket": "day",
  "includeTestRuns": false,
  "startedCount": 120,
  "succeededCount": 105,
  "failedCount": 8,
  "cancelledCount": 2,
  "incompleteCount": 5,
  "incidentBearingRunCount": 11,
  "incidentCount": 16,
  "runningCount": 4,
  "failurePercentage": 6.67,
  "incidentBearingPercentage": 9.17,
  "buckets": [],
  "highestFailureDefinitions": []
}
```

Outcome and incident-bearing counts are independent. Running count is current
state and is not forced into terminal range outcomes. Test runs are excluded by
default. Buckets use local calendar boundaries and preserve absolute start/end
instants across daylight-saving transitions.

Validation errors:

- `400`: missing/invalid range, `from >= to`, unsupported bucket, invalid time
  zone, or range beyond Host Policy cap.
- `401/403`: normal auth/permission failure.
- `200 unavailable`: unsupported third-party aggregate provider only.
