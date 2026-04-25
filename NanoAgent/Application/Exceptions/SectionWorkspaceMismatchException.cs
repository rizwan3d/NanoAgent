using NanoAgent.Application.Utilities;

namespace NanoAgent.Application.Exceptions;

public sealed class SectionWorkspaceMismatchException : InvalidOperationException
{
    public const string DefaultMessage = "Working directory does not match json's dir.";

    public SectionWorkspaceMismatchException(
        string currentWorkspacePath,
        string sectionWorkspacePath)
        : base(
            SecretRedactor.Redact(
            $"{DefaultMessage}{Environment.NewLine}" +
            $"Current working directory: {currentWorkspacePath}{Environment.NewLine}" +
            $"Section working directory: {sectionWorkspacePath}"))
    {
        CurrentWorkspacePath = currentWorkspacePath;
        SectionWorkspacePath = sectionWorkspacePath;
    }

    public string CurrentWorkspacePath { get; }

    public string SectionWorkspacePath { get; }
}
