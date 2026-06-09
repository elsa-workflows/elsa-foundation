using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Workflows.Design.Persistence.EFCore.Constants;

public static class LockKeys
{
    public const string DraftLockKeyFormat = "workflow-draft:{0}";

    public static string DraftKey(string draftId) => string.Format(DraftLockKeyFormat, draftId);
}
