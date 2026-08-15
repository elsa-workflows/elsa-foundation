# Secrets HTTP Compatibility Contract

All routes are shell-relative and may receive a host-configured route prefix. The current method and route templates are authoritative.

| Method | Route | Binding | Permission | Success | Explicit legacy outcomes |
|---|---|---|---|---|---|
| GET | `/secrets` | `SecretQuery` from query | `secrets:read` | `200 ListSecretsResponse` | Missing tenant `403` |
| POST | `/secrets` | `CreateSecretRequest` JSON | `secrets:write` | `201 SecretMetadata` | Missing tenant `403`; captured validation/conflict ProblemDetails |
| GET | `/secrets/descriptors` | none | `secrets:read` | `200 SecretDescriptorsResponse` | No tenant requirement |
| POST | `/secrets/picker` | `SecretPickerRequest` JSON | `secrets:read` | `200 SecretPickerResponse` | Missing tenant `403` |
| GET | `/secrets/{name}` | route name | `secrets:read` | `200 SecretMetadata` | Missing tenant `403`; invisible/missing `404` |
| PUT | `/secrets/{name}` | route name plus metadata JSON | `secrets:write` | `200 SecretMetadata` | Missing tenant `403`; captured missing/conflict behavior |
| POST | `/secrets/{name}/rotate` | route name plus rotation JSON | `secrets:update-value` | `200 SecretMetadata` | Missing tenant `403`; captured missing/validation behavior |
| POST | `/secrets/{name}/revoke` | route name | `secrets:delete` | `200 SecretMetadata` | Missing tenant `403`; missing `404` |
| DELETE | `/secrets/{name}` | route name | `secrets:delete` | `204` empty | Missing tenant `403`; missing `404` |
| POST | `/secrets/{name}/test` | route name | `secrets:test` | `200 SecretTestResult` | Missing tenant `403`; safe failures remain `200` |

## Binding and JSON

- Use ASP.NET web JSON property naming, dictionary-key naming, and string-enum representation matching the legacy host.
- Route `name` and normalized principal tenant are authoritative.
- Empty/malformed JSON, repeated query inputs, singular/plural filters, enum binding, and paging boundaries retain captured behavior.
- No new `Location`, ETag, precondition, or custom headers are introduced.

## Response safety

- Metadata, list, picker, and lifecycle responses never contain raw values, configuration keys, protected payloads, or provider-private metadata.
- Descriptor responses expose only safe capability descriptions.
- Test responses contain only success, safe code, and safe message.
- Problem responses and headers must not echo submitted sensitive markers or unsafe provider exceptions.

## Compatibility rule

The immutable FastEndpoints observations and actual consumed OpenAPI projection are the before authority. The replacement must produce zero unapproved differences. Any approval is exact to endpoint, method, facet, expected/actual value, owner, reason, and follow-up.
