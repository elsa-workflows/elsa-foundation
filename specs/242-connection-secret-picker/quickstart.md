# Use an existing Secret for a connection

1. In **Secrets**, create an active **Text** Secret with a name you can recognize. Enter its value there; the stored value is never returned by the picker.
2. In **Connections**, choose **New connection**. Choose the connection type and authentication scheme, then enter its non-secret settings.
3. For each sensitive field, keep **Existing Secret** selected. Search by name or display name, select the intended Secret, and review its technical name and scope. **Refresh Secrets** updates metadata; **Retry** recovers a failed load. Results are limited to50; refine the search if needed.
4. Alternatively choose **New value** and enter a value for a connection-owned Secret. Switching source clears the inactive source rather than sending hidden credentials.
5. Create the connection and select it on the workflow activity's Connection input. The browser submitted a Secret name, not the selected stored value.
6. To rotate an existing referenced credential, rotate that Secret in **Secrets** and start a new execution of the same published workflow. Do not replace the reference or republish solely for rotation.

## Troubleshooting

- **Access denied:** the picker needs Secrets read permission. Creating/managing a connection and binding an existing Secret require the existing Connections permissions independently; the picker does not grant them.
- **No matches:** only eligible active Text Secrets are offered. Clear/refine search or create the required Secret in Secrets, then refresh.
- **Unavailable selection / server rejection:** another operator may have revoked, expired or deleted it after discovery. Select an eligible replacement. The server checks tenant-scoped metadata again when creating/updating a binding; no value is read for validation.
- **Connection test:** HTTP connection readiness does not prove a remote API authenticated the request. Run the intended activity and inspect its actual HTTP result.
- **Local development API:** scope allowed hosts to the specific loopback host and permit plain HTTP only for that local fixture. Real integrations should use HTTPS.

This feature preserves existing credentialless/interactive setup through the API. It validates each submitted source/reference rather than introducing a new global requirement that every auth field be supplied at connection creation. Durable connection storage remains tracked separately by #2854; the current in-memory connection repository does not retain definitions across host restart.

Connection-created credentials carry explicit ownership metadata. Selected existing Secrets are never assumed owned from their name. Cleanup retains credentials referenced by other stored connections. This reference check is not an atomic concurrent bind/delete guarantee. Custom durable repositories with older records should migrate proven ownership explicitly; unknown legacy records are conservatively unowned.
