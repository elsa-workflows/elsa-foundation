using Xunit;

// Each case owns a real Workbench process and kills it; two at once would double the host cost and blur the timing cuts.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
