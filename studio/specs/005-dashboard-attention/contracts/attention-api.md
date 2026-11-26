# Attention API Contract

## Query

```http
GET /_elsa/attention/items
GET /_elsa/attention/items?contributorId=workflows.runtime&contributorId=secrets
```

Repeated contributor IDs filter evaluation. An unfiltered request discovers and
evaluates all authorized contributors. Studio and backend hosts may expose the
same contract independently.

Studio queries its own host endpoint only when browser runtime configuration
sets `Studio:Attention:HostApiEnabled` to `true`; this prevents an unconfigured
host capability from producing a noisy fallback request. The backend endpoint
remains required. When both base URLs are the same, the client makes one request.

## Response

```json
{
  "generatedAt": "2026-07-13T10:00:00Z",
  "contributors": [
    {
      "contributorId": "workflows.runtime",
      "displayName": "Workflow runtime",
      "status": "ready",
      "evaluatedAt": "2026-07-13T10:00:00Z",
      "totalCount": 7,
      "isTruncated": true,
      "items": []
    },
    {
      "contributorId": "secrets",
      "displayName": "Secrets",
      "status": "timedOut",
      "evaluatedAt": "2026-07-13T10:00:05Z",
      "errorCode": "CONTRIBUTOR_TIMEOUT",
      "detail": "Secrets attention could not be evaluated in time."
    }
  ]
}
```

Contributor status is one of `ready`, `forbidden`, `unavailable`, `timedOut`,
or `failed`. Individual failures return HTTP 200 with partial envelopes.
Authentication failure, malformed filters, and aggregation-service failure use
normal request-level errors.

Unfiltered discovery omits unauthorized contributors. An explicitly requested
known contributor may return `forbidden` without domain data.

## Attention Item

```json
{
  "id": "incident:inc-123",
  "generation": "3",
  "severity": "critical",
  "title": "Workflow run has a blocking incident",
  "summary": "Order fulfillment failed during payment capture.",
  "occurredAt": "2026-07-13T09:45:00Z",
  "lastObservedAt": "2026-07-13T10:00:00Z",
  "count": 1,
  "destination": {
    "path": "/workflows/instances/wf-123",
    "label": "Inspect run"
  },
  "correlations": [
    { "kind": "workflowExecution", "value": "wf-123" },
    { "kind": "incident", "value": "inc-123" }
  ],
  "sensitivity": "metadata"
}
```

Titles/summaries are sanitized metadata. Restricted summary text may be omitted
by Host Policy. Items never carry mutation commands or protected payload data.

## Contributor Budgets

- Default timeout: 5 seconds.
- Maximum returned items: 5 before clustering.
- Maximum downstream service calls: 2 unless Host Policy raises the budget.
- Complete authorized evaluation required; paged samples cannot claim all-clear.
- Dynamic unload cancels evaluation and removes results.
