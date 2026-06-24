using Elsa.Primitives.Entities;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

internal static class GroundworkEntityTimestamps
{
    public static void StampAdded(Entity entity, DateTimeOffset now)
    {
        entity.CreatedAt = now;
        entity.LastModifiedAt = now;
    }

    public static void StampModified(Entity entity, DateTimeOffset now)
    {
        if (entity.CreatedAt == default)
            entity.CreatedAt = now;

        entity.LastModifiedAt = now;
    }

    public static void StampSaved(Entity entity, Entity? existing, DateTimeOffset now)
    {
        if (existing is null)
        {
            StampAdded(entity, now);
            return;
        }

        entity.CreatedAt = existing.CreatedAt == default ? now : existing.CreatedAt;
        entity.LastModifiedAt = now;
    }
}
