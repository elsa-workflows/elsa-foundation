using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IBookmarkResumeResolver
{
    BookmarkResumeResolution Resolve(BookmarkResumeRequest request);
}
