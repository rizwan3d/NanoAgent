using StemCode.Application.Abstractions;

namespace StemCode.Infrastructure.Tools;

internal sealed class CurrentDirectoryWorkspaceRootProvider : IWorkspaceRootProvider
{
    public string GetWorkspaceRoot()
    {
        return Directory.GetCurrentDirectory();
    }
}
