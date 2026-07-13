# Workflows.Publishing extension points moved

The authoritative Publishing-domain catalog is maintained beside the composition root:
[the Publishing API extension-point catalog](../Api/EXTENSION_POINTS.md).

Contracts continue to live in `Elsa.Workflows.Publishing.Core`; this pointer avoids maintaining a second copy of
their defaults and obligations.
