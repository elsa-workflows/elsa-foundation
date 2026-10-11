namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;

/// <summary>A row whose revision is its optimistic-concurrency token, so a revision-checked save can compare and advance it.</summary>
internal interface IRevisionedIdentityEntity
{
    long Revision { get; set; }
}
