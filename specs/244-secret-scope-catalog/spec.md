# Secret scope catalogue and picker

**Feature Branch**: `agent/codex/2976-secret-scope-picker`
**Created**: 2026-10-18
**Status**: Implemented
**Input**: Approved issue #2976 and the owner-requested Secret editor polish. Program bucket: none/free-flow.

The owner approved a discoverable scope picker with module contributions. An operator creating a Secret starts with **No scope**, can choose a suggested scope by its friendly name and description, or reveal **Custom…** to enter an arbitrary qualifier. Scope constrains matching when a reference supplies one; it does not grant permissions.

Acceptance:
- Enabled modules can contribute static, non-sensitive suggestions through a Secrets-owned provider contract. Contributions have stable names, friendly labels, optional descriptions and source context.
- An empty catalogue is valid. Duplicate names are diagnosed deterministically, case-insensitively, without choosing a registration-order winner.
- Metadata discovery remains tenant-independent and uses the existing authorized descriptors endpoint. It never enumerates stored Secrets or their values.
- No scope remains the default. Loading, retry, empty results and missing provider metadata preserve the authored selection and custom text. Existing arbitrary and differently-cased values retain their meaning.
- The picker works by keyboard and in narrow layouts. Search helps navigate a larger catalogue.
- The editor uses an explicit Edit action and heading, prioritizes editable fields, and progressively discloses secondary metadata. Both create and edit views explain Scope using the same matching semantics; existing scope remains read-only.
- Scope remains an open string in APIs and headless clients. Creation, rotation and runtime matching do not consult the catalogue. Removing a contributor does not invalidate stored Secrets or references.

Non-goals: changing a stored scope, rotation semantics, authorization, tenant-specific discovery, new first-party scopes, or a generic option-provider framework.
