# Studio Preferences Authorization Contract

## Ownership

- Permission catalog owner: `elsa.studio.preferences`.
- Read permission: `studio.preferences.read`.
- Write permission: `studio.preferences.write`.
- Existing implication: write implies read.
- Administrative wildcard: `*` remains an explicit grant only and is never cataloged.

## Endpoint declarations

- GET declares one canonical `Any(*, studio.preferences.read)` Foundation policy.
- PUT declares one canonical `Any(*, studio.preferences.write)` Foundation policy.
- Each declaration contributes standard ASP.NET Core authorization metadata and one typed permission security disposition.

## Required outcomes

- No authenticated principal: challenge (`401`).
- Authenticated normalized principal without a satisfying grant: forbid (`403`).
- Exact action grant: allow.
- Write grant on GET: allow through catalog implication.
- Explicit wildcard grant: allow.
- Resource-handler denial: deny even when claim evaluation succeeds.

Endpoint handlers do not inspect permission claims. Provider claim mapping and normalized-principal trust remain outside this module.
