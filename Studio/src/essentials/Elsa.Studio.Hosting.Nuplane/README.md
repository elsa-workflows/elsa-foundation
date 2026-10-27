# Elsa.Studio.Hosting.Nuplane

Optional Studio package and feature administration. The base Elsa.Studio.Hosting package remains independent
of Nuplane and this addon. Public composition, installed configuration choices and supported store profiles
are implemented and qualified under [spec1010](https://github.com/elsa-workflows/elsa-foundation/tree/main/specs/1010-studio-optional-admin).

This package is under implementation in Program #3340. It is not a release/publication request.

The optional routes use the installed selected shell, a frozen effective directory feed and the shared
configuration mutation lease. Feed edits take effect on restart. A cache-only directory can receive uploads
without declaring new desired packages; reconciliation/loading outcomes remain authoritative. Source-only
directories that do not resolve packages cannot be selected as the upload destination.

Installed metadata/assets require the coordinated immutable extracted-store profile. Nuplane owns admission,
identity/link checks and guarded bytes; the HTTP adapter adds preflight size and response limits. Ordinary,
unenrolled, archive and custom mutable installs receive explicit refusal/diagnostics. Host-owned nonphysical
static providers remain trusted immutable content; physical static-provider aliases into the install store
take the admitted path. No automatic enrollment, local prune algorithm or arbitrary external-writer guarantee.
