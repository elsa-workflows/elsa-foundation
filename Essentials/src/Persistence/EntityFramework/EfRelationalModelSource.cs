using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Elsa.Persistence.EntityFramework;

/// <summary>Completes relational metadata before EF publishes a model to other contexts.</summary>
internal sealed class EfRelationalModelSource(ModelSourceDependencies dependencies) : ModelSource(dependencies)
{
    // EF 10.0.10's GetRelationalModel reads the model and factory separately, then removes the factory.
    // A concurrent first reader can therefore see neither. ModelSource holds its creation lock here;
    // building relational metadata before cache publication avoids that race without disabling drift checks.
    [SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "The model creation hook is the EF seam before shared model cache publication.")]
    public override IModel CreateModel(DbContext context, ModelCreationDependencies modelCreationDependencies, bool designTime)
    {
        var model = base.CreateModel(context, modelCreationDependencies, designTime);
        _ = model.GetRelationalModel();
        return model;
    }
}
