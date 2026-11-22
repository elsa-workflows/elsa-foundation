# Release transport and semantic contracts

The lightweight target contracts contain no compiler, catalog or activation dependency. Transport parsing and semantic
verification have separate verdicts. The indexed semantic content reader cannot resolve omitted members from another store.
The authoritative default, exact-key strategy and replacement obligations are in the
[engine catalog](../Elsa.Workflows.Publishing.Releases/EXTENSION_POINTS.md).

`IReleaseBundleBuilder` constructs a manifest from actual owner facts; it grants no durable creation or target authority.
`IReleaseCaptureService`, exact-kind `IReleaseCaptureSource` strategies and `IReleaseSourceMemberContributor` fan-in are described by the optional
[source capture catalog](../Capture/Elsa.Workflows.Publishing.Releases.Capture/EXTENSION_POINTS.md). Those contracts live in the optional Capture.Core package; the lightweight target Core keeps no source or Events dependency.
