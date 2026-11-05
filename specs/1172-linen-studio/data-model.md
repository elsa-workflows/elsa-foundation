# Presentation model

No persisted server entities change. AppearancePreferences retains its fields and existing keys; missing workspace style resolves to host default/Linen, an explicit preset sentinel resolves to null. Host defaults never overwrite stored choices. Theme inventory adds Linen with light/dark/dim/highContrast roles.

Studio public runtime fields contain branding strings/asset and link URLs plus appearance defaults. Only these named fields are serialized.

LoginPageOptions holds brand/copy/image/layout/palette/focal position and one optional renderer callback. LoginPageContext carries sanitized return URL, antiforgery token and error state; the endpoint remains responsible for security and POST handling.
