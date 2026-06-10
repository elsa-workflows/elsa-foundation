using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Resolvers;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeBookmarkResumeContractTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly WorkflowExecutableIdentity _identity;

    public RuntimeBookmarkResumeContractTests()
    {
        _identity = new WorkflowExecutableIdentity(
            ArtifactId: "artifact-1",
            DefinitionId: "orders",
            DefinitionVersionId: "orders-v7",
            ArtifactVersion: "7.0.0",
            ArtifactHash: "sha256:artifact");
    }

    [Fact]
    public void BookmarkState_StoresResumeTargetInsteadOfCallbackMethod()
    {
        var bookmark = NewBookmark();
        var durablePropertyNames = typeof(BookmarkState)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal("resume-target:delivery", bookmark.ResumeTargetId);
        Assert.Equal("delivery-status", bookmark.StimulusType);
        Assert.Equal("sha256:delivery-status:order-123", bookmark.StimulusHash);
        Assert.DoesNotContain(durablePropertyNames, name => name.Contains("Callback", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(durablePropertyNames, name => name.Contains("Method", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(durablePropertyNames, name => name.Contains("Delegate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResumeTargetAttribute_DeclaresStableTargetIdForActivityAuthors()
    {
        var method = typeof(ActivityWithResumeTarget).GetMethod(nameof(ActivityWithResumeTarget.OnDeliveryStatusReceived))!;
        var attribute = Assert.Single(method.GetCustomAttributes<ResumeTargetAttribute>());

        Assert.Equal("resume-target:delivery", attribute.ResumeTargetId);
        Assert.Throws<ArgumentException>(() => new ResumeTargetAttribute(" "));
    }

    [Fact]
    public void BookmarkResumeResolver_ResolvesThroughPinnedExecutableArtifact()
    {
        var executable = NewExecutable();
        var request = new BookmarkResumeRequest(
            WorkflowExecution: NewWorkflowExecution(),
            Executable: executable,
            Bookmark: NewBookmark(),
            Input: Json("""{"status":"delivered"}"""));
        var resolver = new BookmarkResumeResolver();

        var resolution = resolver.Resolve(request);

        Assert.Equal("bookmark-1", resolution.Bookmark.BookmarkId);
        Assert.Equal("node-delivery", resolution.ExecutableNode.ExecutableNodeId);
        Assert.Equal("resume-target:delivery", resolution.ResumeTarget.ResumeTargetId);
        Assert.Equal("delivery-handler", resolution.ResumeTarget.HandlerKey);
        Assert.Equal("delivered", resolution.Input!.Value.GetProperty("status").GetString());
    }

    [Fact]
    public void WorkflowExecutable_DerivesNodeLookupFromExecutableNodes()
    {
        var node = NewDeliveryNode();
        var executable = NewExecutable(nodes: [node]);

        Assert.Same(node, executable.NodesById["node-delivery"]);
        Assert.Same(node, Assert.Single(executable.Nodes));
        Assert.Throws<ArgumentException>(() => NewExecutable(nodes: [NewDeliveryNode(), NewDeliveryNode()]));
    }

    [Fact]
    public void BookmarkResumeResolver_IgnoresSourceReferenceWhenCheckingPinnedExecutableSnapshot()
    {
        var workflowExecution = NewWorkflowExecution(_identity with
        {
            Source = new WorkflowExecutableSourceReference("WorkflowDefinitionVersion", "orders-v7", "7.0.0")
        });
        var executable = NewExecutable(_identity with { Source = null });
        var request = new BookmarkResumeRequest(
            WorkflowExecution: workflowExecution,
            Executable: executable,
            Bookmark: NewBookmark(),
            Input: null);
        var resolver = new BookmarkResumeResolver();

        var resolution = resolver.Resolve(request);

        Assert.Equal("node-delivery", resolution.ExecutableNode.ExecutableNodeId);
    }

    [Fact]
    public void BookmarkResumeResolver_RejectsBookmarkForDifferentWorkflowExecution()
    {
        var request = NewRequest(NewExecutable(), bookmark: NewBookmark() with { WorkflowExecutionId = "wfexec-other" });
        var resolver = new BookmarkResumeResolver();

        var exception = Assert.Throws<BookmarkResumeResolutionException>(() => resolver.Resolve(request));

        Assert.Equal(BookmarkResumeResolutionFailureReason.WorkflowExecutionMismatch, exception.Reason);
        Assert.Equal("wfexec-other", exception.WorkflowExecutionId);
    }

    [Fact]
    public void BookmarkResumeResolver_RejectsExecutableThatIsNotPinned()
    {
        var executable = NewExecutable(_identity with { DefinitionVersionId = "orders-v8", ArtifactHash = "sha256:other" });
        var request = NewRequest(executable);
        var resolver = new BookmarkResumeResolver();

        var exception = Assert.Throws<BookmarkResumeResolutionException>(() => resolver.Resolve(request));

        Assert.Equal(BookmarkResumeResolutionFailureReason.ExecutableArtifactNotPinned, exception.Reason);
        Assert.Equal("bookmark-1", exception.BookmarkId);
        Assert.Equal("resume-target:delivery", exception.ResumeTargetId);
        Assert.Contains("sha256:other", exception.Message);
        Assert.Contains("sha256:artifact", exception.Message);
        Assert.Contains("orders-v8", exception.Message);
        Assert.Contains("orders-v7", exception.Message);
    }

    [Fact]
    public void BookmarkResumeResolver_RejectsMissingArtifactResumeTarget()
    {
        var executable = NewExecutable(resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>());
        var request = NewRequest(executable);
        var resolver = new BookmarkResumeResolver();

        var exception = Assert.Throws<BookmarkResumeResolutionException>(() => resolver.Resolve(request));

        Assert.Equal(BookmarkResumeResolutionFailureReason.ResumeTargetMissing, exception.Reason);
        Assert.Contains("Resume target 'resume-target:delivery'", exception.Message);
        Assert.Contains("sha256:artifact", exception.Message);
    }

    [Fact]
    public void BookmarkResumeResolver_RejectsResumeTargetForDifferentExecutableNode()
    {
        var executable = NewExecutable(resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>
        {
            ["resume-target:delivery"] = new(
                ResumeTargetId: "resume-target:delivery",
                ExecutableNodeId: "node-other",
                HandlerKey: "delivery-handler",
                Metadata: new Dictionary<string, string>())
        });
        var request = NewRequest(executable);
        var resolver = new BookmarkResumeResolver();

        var exception = Assert.Throws<BookmarkResumeResolutionException>(() => resolver.Resolve(request));

        Assert.Equal(BookmarkResumeResolutionFailureReason.ResumeTargetNodeMismatch, exception.Reason);
    }

    [Fact]
    public void BookmarkResumeResolver_RejectsResumeTargetWhoseIdDoesNotMatchLookupKey()
    {
        var executable = NewExecutable(resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>
        {
            ["resume-target:delivery"] = new(
                ResumeTargetId: "resume-target:other",
                ExecutableNodeId: "node-delivery",
                HandlerKey: "delivery-handler",
                Metadata: new Dictionary<string, string>())
        });
        var request = NewRequest(executable);
        var resolver = new BookmarkResumeResolver();

        var exception = Assert.Throws<BookmarkResumeResolutionException>(() => resolver.Resolve(request));

        Assert.Equal(BookmarkResumeResolutionFailureReason.ResumeTargetIdentityMismatch, exception.Reason);
        Assert.Contains("resume-target:other", exception.Message);
    }

    [Fact]
    public void BookmarkResumeResolver_RejectsMissingExecutableNode()
    {
        var executable = NewExecutable(nodes: []);
        var request = NewRequest(executable);
        var resolver = new BookmarkResumeResolver();

        var exception = Assert.Throws<BookmarkResumeResolutionException>(() => resolver.Resolve(request));

        Assert.Equal(BookmarkResumeResolutionFailureReason.ExecutableNodeMissing, exception.Reason);
        Assert.Contains("sha256:artifact", exception.Message);
    }

    private BookmarkResumeRequest NewRequest(WorkflowExecutable executable, BookmarkState? bookmark = null) =>
        new(
            WorkflowExecution: NewWorkflowExecution(),
            Executable: executable,
            Bookmark: bookmark ?? NewBookmark(),
            Input: null);

    private WorkflowExecutionState NewWorkflowExecution(WorkflowExecutableIdentity? pinnedExecutable = null) =>
        new(
            WorkflowExecutionId: "wfexec-1",
            PinnedExecutable: pinnedExecutable ?? _identity,
            Status: WorkflowExecutionStatus.Suspended,
            SubStatus: null,
            CreatedAt: _now,
            StartedAt: _now,
            UpdatedAt: _now,
            CompletedAt: null,
            CorrelationId: "order-123",
            ParentWorkflowExecutionId: null,
            TenantId: "tenant-a",
            SystemMetadata: new Dictionary<string, string>());

    private BookmarkState NewBookmark() =>
        new(
            BookmarkId: "bookmark-1",
            WorkflowExecutionId: "wfexec-1",
            ActivityExecutionId: "actexec-1",
            ExecutableNodeId: "node-delivery",
            ResumeTargetId: "resume-target:delivery",
            StimulusType: "delivery-status",
            StimulusHash: "sha256:delivery-status:order-123",
            Payload: Json("""{"orderId":"order-123"}"""),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: _now,
            ExpiresAt: _now.AddDays(1));

    private WorkflowExecutable NewExecutable(
        WorkflowExecutableIdentity? identity = null,
        IReadOnlyCollection<ExecutableNode>? nodes = null,
        IReadOnlyDictionary<string, WorkflowExecutableResumeTarget>? resumeTargets = null)
    {
        var executableNodes = nodes ?? [NewDeliveryNode()];

        return new(
            identity: identity ?? _identity,
            nodes: executableNodes,
            resumeTargets: resumeTargets ?? new Dictionary<string, WorkflowExecutableResumeTarget>
            {
                ["resume-target:delivery"] = new(
                    ResumeTargetId: "resume-target:delivery",
                    ExecutableNodeId: "node-delivery",
                    HandlerKey: "delivery-handler",
                    Metadata: new Dictionary<string, string>())
            },
            createdAt: _now,
            publishedAt: _now,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private ExecutableNode NewDeliveryNode() =>
        new(
            ExecutableNodeId: "node-delivery",
            AuthoredActivityId: "activity-delivery",
            ActivityType: "Elsa.WaitForDeliveryStatus",
            ActivityTypeVersion: "1.0.0",
            DescriptorType: "test",
            DescriptorPayload: Json("{}"),
            InputBindings: new Dictionary<string, JsonElement>(),
            OutputCaptures: new Dictionary<string, JsonElement>(),
            Metadata: new Dictionary<string, string>());

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class ActivityWithResumeTarget
    {
        [ResumeTarget("resume-target:delivery")]
        public void OnDeliveryStatusReceived()
        {
        }
    }
}
