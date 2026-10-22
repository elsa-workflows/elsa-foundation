# Rules Workflows (Design)

`RulesWorkflowsDesign` contributes an `IExecutableCompilationSource` for `RunRules`. It accepts only a bounded literal finite Rules program, compiles the canonical artifact, and embeds it with the verified installed implementation identity in node metadata. Runtime reads that frozen pin without a dependency on this Design module or a later deployment lookup.
