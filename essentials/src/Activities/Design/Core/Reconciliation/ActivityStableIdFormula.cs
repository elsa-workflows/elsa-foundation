using System;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Activities.Design.Core.Reconciliation;

/// <summary>
/// The one implementation of the activity catalog stable id formula. Persisted catalogs and generated
/// activity call facades already hold these ids, so the output must stay byte-for-byte identical.
/// </summary>
/// <remarks>
/// This file is also compiled into the netstandard2.0 <c>Elsa.Workflows.Design.CodeGeneration</c> source
/// generator, which cannot reference this assembly. Keep it free of APIs that netstandard2.0 lacks.
/// </remarks>
internal static class ActivityStableIdFormula
{
    private const string DefinitionIdPrefix = "actdef";
    private const string VersionIdPrefix = "actver";

    public static string DefinitionId(string activityTypeKey) => StableId(DefinitionIdPrefix, activityTypeKey);

    public static string VersionId(string activityTypeKey, string semVerSortKey) => StableId(VersionIdPrefix, activityTypeKey, semVerSortKey);

    private static string StableId(string prefix, params string[] parts)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join("\u001f", parts));
        byte[] hash;
        using (var algorithm = SHA256.Create())
            hash = algorithm.ComputeHash(bytes);
        var encoded = Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return prefix + "_" + encoded;
    }
}
