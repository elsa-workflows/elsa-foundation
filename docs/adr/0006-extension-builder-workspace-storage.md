# 0006. Extension Builder workspace storage

## Status

Accepted

## Context

Extension Builder workspaces are Git repository checkouts with physical source files. If workspaces are stored under the `Elsa.Server` project source tree, generated `.cs` files can be picked up by SDK-style project globs and compiled into the server application. Runtime authoring state also becomes tangled with application source.

## Decision

Extension Builder stores workspace repositories outside the application source root by default. The workspace root is configurable, for example through `Elsa:ExtensionBuilder:WorkspaceRoot`, so administrators can choose an appropriate app-data or mounted storage location.

## Consequences

- Generated repository source files cannot accidentally become part of the server application build.
- Local development and production deployments get a clearer boundary between app code and authored extension code.
- Backup, cleanup, disk quota, and access-control policies can target Extension Builder workspace storage explicitly.
- Existing implementations that store under the app source tree need migration or compatibility handling.
