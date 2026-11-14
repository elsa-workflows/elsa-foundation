using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfExecutableActivityTemplateStore
{
    public async ValueTask<RuntimeTemplateSourceSnapshot?> FindBoundedAsync(string templateId, PersistenceScope expectedScope,
        RuntimeSourceReadLimits limits, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId); ArgumentNullException.ThrowIfNull(expectedScope);
        if (RequireScope() != expectedScope.Value) throw new RuntimeSourceReadException("runtime_source_scope_mismatch");
        var identity = new TemplateIdentity(expectedScope.Value, templateId, null);
        var rowKey = new RuntimeBoundedSourceRead.Row(typeof(ExecutableActivityTemplateEntity), CreateId(expectedScope.Value, templateId));
        try
        {
            return await RuntimeBoundedSourceRead.ExecuteAsync<RuntimeTemplateSourceSnapshot?>(context, limits, [rowKey], async (lengths, token) =>
            {
                if (RequireScope() != expectedScope.Value) throw new RuntimeSourceReadException("runtime_source_scope_mismatch");
                var row = await FindRowByIdAsync(identity, token);
                return row is null ? null : new(Read(row, identity, limits), expectedScope, lengths[rowKey]);
            }, cancellationToken);
        }
        catch (Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions.RuntimeArtifactEntityFrameworkPersistenceException) { throw; }
        catch (InvalidDataException) { throw new RuntimeSourceReadException("runtime_source_stored_content_invalid"); }
        catch (Exception exception) when (exception is IOException or OperationCanceledException || Elsa.Persistence.EntityFramework.EfRelationalExceptionClassifier.IsProviderFailure(exception))
        { cancellationToken.ThrowIfCancellationRequested(); throw NormalizeProviderFailure("reading bounded template", templateId, exception); }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException)
        { throw new RuntimeSourceReadException("runtime_source_stored_content_invalid"); }
    }
}
