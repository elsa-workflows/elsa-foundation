namespace Elsa3.Mapping.ActivityMap;

/// <summary>The named node-rewrite strategy a map entry uses for conversion logic that is not data.</summary>
public enum Elsa3NodeRewriteKind
{
    /// <summary>Switch/FlowSwitch: synthesize <c>Value</c> returning the label of the first true case (MatchFirst).</summary>
    SwitchValueSynthesis,

    /// <summary>SetVariable: an Elsa 3 variable becomes a variable reference with its computed declaring scope.</summary>
    VariableReference,

    /// <summary>SetOutput: in a reusable workflow a declared output becomes a write to its synthesized variable.</summary>
    ReusableOutputWrite,

    /// <summary>Complete/End: finish in a plain workflow's root, a Complete node in a reusable workflow.</summary>
    TerminalPlacement,

    /// <summary>ExecuteWorkflow: DispatchWorkflow with WaitForCompletion.</summary>
    ExecuteWorkflowAsDispatch,

    /// <summary>Parallel/Fork: generated branch names and the join threshold.</summary>
    ParallelBranchNames,

    /// <summary>For/ForEach: the loop's scoped variables for lowering.</summary>
    LoopVariableReferences,

    /// <summary>HttpEndpoint: ResponseMode Sync and the request projections.</summary>
    HttpTriggerResponseMode
}
