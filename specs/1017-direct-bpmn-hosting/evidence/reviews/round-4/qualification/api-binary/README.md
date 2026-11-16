# Published binary API shapes after round 4 corrections

Executing products `30dfb0a9b7ee46b59ef90e18b548162fa5a96c3f` pass ten directional comparisons against eight actual published packages, with parameter-name rules enabled, resolved references and no suppressions. This covers every changed assembly with a published baseline, plus the unchanged BPMN Interchange assembly. The two new packages, `Elsa.Activities.Bpmn.Api` and `Elsa.Activities.Bpmn.Core`, have no prior entry in the refreshed publish-state record.

| Assembly | Actual baseline versions |
|---|---|
| Runtime Core | 4.0.55-preview, 4.0.56-preview |
| Runtime | 4.0.69-preview, 4.0.70-preview |
| Shared EF | 4.0.32-preview |
| BPMN | 4.0.15-preview |
| Runtime EF | 4.0.43-preview |
| Workflow Design EF | 4.0.24-preview |
| Workflow Publishing | 4.0.36-preview |
| BPMN Interchange | 4.0.16-preview |

[Comparison commands/results](all-comparisons.json), [public downloads and embedded source commits](downloads.json), the recorded earlier Core/Runtime/shared-EF downloads, restored closure inputs and [asset hashes](manifest.json) preserve the input boundaries. The latest publish-state is `f4482d87e7055c57ee21609593fa3811f50d1d13`; all five additionally downloaded nuspec commits match it. The three earlier reverse-contract controls remain in the preceding round 4 API audit; no new reverse-contract claim is made here.

Reproduction downloads the exact public package URLs and verifies their hashes, restores the compressed baseline project/lock/configuration in a fresh private directory, rebuilds the target source, then invokes Microsoft.DotNet.ApiCompat.Tool 10.0.403 with the recorded switches and the installed .NET/ASP.NET 10 reference assemblies. Substitute fresh paths for the historical command paths. No compiled package or machine-local directory is a required committed input. The first two baseline restore attempts selected the tool's NuGet.org-only configuration; those setup failures remain in the logs. The corrected public Feedz configuration restores successfully.

This proves public binary API shape, not release-package identity, version-magnitude compliance or host/state compatibility. It performs no pack, publish or version reservation. The existing owner choice under Framework §4.2/Elsa §E5 remains open; this audit neither supplies an exception nor clears T042.
