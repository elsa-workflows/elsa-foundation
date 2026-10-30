using Elsa.Primitives.Exceptions;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

internal sealed class EfWorkflowTemplateStore(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access,
    IDesignAtomicWriter atomic, IPayloadSerializer serializer) : IWorkflowTemplateStore
{
    private IQueryable<WorkflowTemplateEntity> Query() => EfDesignSupport.InScope(db.Templates, access, row => row.TenantId);

    public async Task<IReadOnlyList<WorkflowTemplate>> ListAsync(CancellationToken cancellationToken = default)
    {
        // Bound authoring inventory; fail visibly rather than silently omit templates.
        var rows = await EfDesignSupport.ReadAsync("listing workflow templates", () => Query().OrderBy(row => row.Id).Take(1001).ToArrayAsync(cancellationToken));
        if (rows.Length > 1000) throw new InvalidOperationException("The template catalog exceeds its limit of 1000 templates.");
        return rows.Select(row => Read(row)).OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray();
    }

    public async Task<WorkflowTemplate?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        var row = await EfDesignSupport.ReadAsync("reading workflow template", () => Query().SingleOrDefaultAsync(row => row.Id == id, cancellationToken));
        return row is null ? null : Read(row);
    }

    public async Task<WorkflowTemplate> SaveAsync(DesignOperationKey operationKey, string? id, string name, string? description,
        WorkflowTemplateContent content, string? expectedRevision, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(content.State);
        if (name.Length > 2048 || description?.Length > 2048) throw new ArgumentException("Template name or description exceeds 2048 characters.");
        if (id is not null) { ValidateId(id); ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision); }
        var json = serializer.Serialize(content);
        if (json.Length > 4 * 1024 * 1024) throw new ArgumentException("The template document exceeds 4 MiB.");
        var templateId = id ?? "template-" + Guid.NewGuid().ToString("N");
        try
        {
            return await atomic.ExecuteAsync(operationKey, "workflow.template.save.v1",
                new { id, name, description, json, expectedRevision }, ["workflow-templates"], async token =>
                {
                    var row = id is null ? null : await Query().SingleOrDefaultAsync(candidate => candidate.Id == id, token)
                        ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowTemplate), id!);
                    if (row is not null && row.Revision != expectedRevision) throw new WorkflowTemplateConflictException();
                    var now = DateTimeOffset.UtcNow;
                    if (row is null)
                    {
                        row = new WorkflowTemplateEntity { Id = templateId, TenantId = access.Current.RequireScope().Value, CreatedAt = now };
                        db.Templates.Add(row);
                    }
                    row.Name = name.Trim(); row.Description = description; row.ContentJson = json;
                    row.Revision = Guid.NewGuid().ToString("N"); row.LastModifiedAt = now;
                    return Read(row, verifyScope: false);
                }, cancellationToken);
        }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException || exception.InnerException is DbUpdateConcurrencyException)
        { throw new WorkflowTemplateConflictException(); }
    }

    public async Task DeleteAsync(DesignOperationKey operationKey, string id, string expectedRevision, CancellationToken cancellationToken = default)
    {
        ValidateId(id); ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);
        try
        {
            await atomic.ExecuteAsync(operationKey, "workflow.template.delete.v1", new { id, expectedRevision }, ["workflow-templates"], async token =>
            {
                var row = await Query().SingleOrDefaultAsync(candidate => candidate.Id == id, token)
                    ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowTemplate), id);
                if (row.Revision != expectedRevision) throw new WorkflowTemplateConflictException();
                db.Templates.Remove(row);
                return id;
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException || exception.InnerException is DbUpdateConcurrencyException)
        { throw new WorkflowTemplateConflictException(); }
    }

    private WorkflowTemplate Read(WorkflowTemplateEntity row, bool verifyScope = true)
    {
        if (verifyScope) EfDesignSupport.EnsurePhysicalScope(db, row, "workflow template read");
        return new(row.Id, row.Name, row.Description, row.Revision, row.CreatedAt, row.LastModifiedAt,
            serializer.Deserialize<WorkflowTemplateContent>(row.ContentJson));
    }

    private static void ValidateId(string id)
    {
        if (!id.StartsWith("template-", StringComparison.Ordinal) || !Guid.TryParseExact(id[9..], "N", out _))
            throw new ArgumentException("Only stored custom template identities can be changed or read through this endpoint.");
    }
}
