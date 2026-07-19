namespace Elsa.Persistence.Core;

/// <summary>Supplies the immutable persistence access selected for the current dependency-injection scope.</summary>
public interface IPersistenceAccessContextAccessor
{
    PersistenceAccessContext Current { get; }
}
