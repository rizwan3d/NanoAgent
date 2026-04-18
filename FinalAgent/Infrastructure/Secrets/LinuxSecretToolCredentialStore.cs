using System.ComponentModel;

namespace FinalAgent.Infrastructure.Secrets;

internal sealed class LinuxSecretToolCredentialStore : IPlatformCredentialStore
{
    private readonly IProcessRunner _processRunner;

    public LinuxSecretToolCredentialStore(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<string?> LoadAsync(SecretReference secretReference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secretReference);

        ProcessExecutionResult result = await RunSecretToolAsync(
            new ProcessExecutionRequest(
                "secret-tool",
                [
                    "lookup",
                    "service", secretReference.ServiceName,
                    "account", secretReference.AccountName
                ]),
            "read the API key from the Linux Secret Service",
            cancellationToken);

        if (result.ExitCode == 0)
        {
            string normalized = result.StandardOutput.TrimEnd('\r', '\n');
            return string.IsNullOrWhiteSpace(normalized)
                ? null
                : normalized;
        }

        if (string.IsNullOrWhiteSpace(result.StandardError))
        {
            return null;
        }

        throw SecretStorageException.ForOperation(
            "Linux Secret Service",
            "load",
            result.StandardError);
    }

    public async Task SaveAsync(
        SecretReference secretReference,
        string secretValue,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secretReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretValue);

        ProcessExecutionResult clearResult = await RunSecretToolAsync(
            new ProcessExecutionRequest(
                "secret-tool",
                [
                    "clear",
                    "service", secretReference.ServiceName,
                    "account", secretReference.AccountName
                ]),
            "clear an existing API key from the Linux Secret Service",
            cancellationToken);

        if (clearResult.ExitCode != 0 && !string.IsNullOrWhiteSpace(clearResult.StandardError))
        {
            throw SecretStorageException.ForOperation(
                "Linux Secret Service",
                "clear",
                clearResult.StandardError);
        }

        ProcessExecutionResult storeResult = await RunSecretToolAsync(
            new ProcessExecutionRequest(
                "secret-tool",
                [
                    "store",
                    $"--label={secretReference.DisplayLabel}",
                    "service", secretReference.ServiceName,
                    "account", secretReference.AccountName
                ],
                secretValue),
            "store the API key in the Linux Secret Service",
            cancellationToken);

        if (storeResult.ExitCode != 0)
        {
            throw SecretStorageException.ForOperation(
                "Linux Secret Service",
                "save",
                storeResult.StandardError);
        }
    }

    private async Task<ProcessExecutionResult> RunSecretToolAsync(
        ProcessExecutionRequest request,
        string actionDescription,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _processRunner.RunAsync(request, cancellationToken);
        }
        catch (Win32Exception exception)
        {
            throw new SecretStorageException(
                $"Unable to {actionDescription}. Install 'secret-tool' from libsecret-tools and ensure a Secret Service is running.",
                exception);
        }
    }
}
