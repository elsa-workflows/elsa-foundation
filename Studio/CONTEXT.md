# Elsa Foundation Studio

Elsa Foundation Studio is the browser-based workspace for managing Elsa workflow design, operations, and supporting runtime assets.

## Language

**Secret**:
A named metadata record that lets workflows refer to sensitive material without exposing the material in Studio. A Secret never includes a current raw value in frontend-visible responses.
_Avoid_: Credential, config value

**Secret Reference**:
A workflow property value that points to a Secret by name, with optional type and scope metadata.
_Avoid_: Secret value, credential binding

**Secret Store**:
The backing source category for a Secret, such as Elsa-managed encrypted storage or host configuration lookup.
_Avoid_: Provider, vault

**Secret Type**:
The expected shape or use of a Secret, such as text, RSA key, or X.509 certificate.
_Avoid_: Value kind, credential type

**Rotation**:
Replacement of the material or configuration lookup associated with a Secret while preserving its technical name.
_Avoid_: Reveal, reset
