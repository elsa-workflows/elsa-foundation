# Feature Specification: HTTP Workflows That Work Out of the Box

**Feature Branch**: `agent/codex/1177-http-usability`
**Created**: 2026-10-16
**Status**: Implemented; prepared for PR review
**Issue**: #1177
**Program-goal state**: none/free-flow (one customer-reported HTTP usability improvement).

## User scenarios and testing

### 1. Receive the response authored in a new workflow (P1)

An author creates an HTTP endpoint followed by Write HTTP Response. Publishing without an explicitly authored response mode returns the workflow's status and body. Output captures are optional.

Acceptance: a real POST against a newly published workflow without a response-mode input returns 200 and the authored JSON. Explicit Async still returns 202. Existing stored trigger/bookmark metadata without a mode still means Async until republished. Enum numeric values remain stable.

### 2. See and understand response behavior (P1)

The endpoint offers a dropdown labelled in ordinary language: "Wait for workflow response" and "Accept and run in background". Its graph node shows the effective choice. An Async endpoint with a reachable Write HTTP Response offers a non-blocking explanation and an explicit action to switch to Sync. Editing the graph never silently changes mode.

### 3. Start with a working HTTP example and diagnose the result (P1)

The create-workflow dialog offers an HTTP request/response starter when the necessary catalog activities exist. It authors a start endpoint and JSON response with no required captures. An explicit test-request action sends a real request to the configured backend and displays status, content type and body. A 202 includes an explanation grounded in the actual response contract, including the difference between background acceptance and a synchronous exchange that produced no response.

## Requirements and boundaries

- Keep runtime HTTP knowledge in HTTP code and Studio HTTP-specific presentation in the owning Workflows module; generic property editors consume descriptor metadata.
- Preserve existing published metadata interpretation. New publication records the effective mode explicitly. Republish of an unauthored draft deliberately adopts Sync; document this transition.
- A valid background workflow remains publishable. Hints must not imply every Write HTTP Response reaches every endpoint or that background execution failed.
- No automatic request on opening a dialog, no arbitrary external request target, and no auth-token/storage inspection. Use the configured backend and canonical HTTP client.
- Use current catalog version IDs for the starter, and fail clearly when required activities are unavailable. No fixed content-addressed IDs.
- No new packages, persistence migrations, remote deployment, or recording changes.

## Success criteria

A new author can create the HTTP starter, publish it and receive JSON without entering enum strings or binding outputs. A surprising 202 has an actionable explanation. Existing already-published endpoints retain their response behavior.
