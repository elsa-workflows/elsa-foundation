namespace Elsa.Integrations.Connectors.Microsoft365;

internal static class Microsoft365Paths
{
    public static string UserRoot(string? userId) =>
        string.IsNullOrWhiteSpace(userId) ? "me" : $"users/{Uri.EscapeDataString(userId.Trim())}";

    public static string DriveRoot(string? driveId, string? userId)
    {
        if (!string.IsNullOrWhiteSpace(driveId))
            return $"drives/{Uri.EscapeDataString(driveId.Trim())}";
        return $"{UserRoot(userId)}/drive";
    }

    public static string DriveItemByPath(string? driveId, string? userId, string path) =>
        $"{DriveRoot(driveId, userId)}/{EncodeRootPath(path)}";

    public static string DriveItemById(string? driveId, string? userId, string itemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        if (!string.IsNullOrWhiteSpace(driveId))
            return $"drives/{Uri.EscapeDataString(driveId.Trim())}/items/{Uri.EscapeDataString(itemId.Trim())}";
        return $"{UserRoot(userId)}/drive/items/{Uri.EscapeDataString(itemId.Trim())}";
    }

    public static string EncodeRootPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var trimmed = path.Trim().Trim('/');
        if (string.IsNullOrEmpty(trimmed))
            throw new Microsoft365ApiException(Microsoft365ErrorCodes.InvalidInput, "Item path is required.");

        var encoded = string.Join('/', trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        return $"root:/{encoded}:";
    }

    public static string FileNameFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "file";
        var trimmed = path.Trim().TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }
}
