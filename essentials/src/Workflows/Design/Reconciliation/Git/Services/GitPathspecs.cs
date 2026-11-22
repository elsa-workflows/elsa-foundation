namespace Elsa.Workflows.Design.Reconciliation.Git.Services;

/// <summary>
/// The global option every git command that takes a path under the workflows path runs with (#2197). The workflows path
/// is configuration and a definition id is data, so either could hold pathspec magic (<c>:/</c>, <c>:(top)</c>) or a
/// wildcard (<c>*</c>), which would aim <c>clean -f -d</c> and <c>restore</c> at the whole clone. Read literally, a path
/// names only itself.
/// </summary>
internal static class GitPathspecs
{
    public const string Literal = "--literal-pathspecs";
}
