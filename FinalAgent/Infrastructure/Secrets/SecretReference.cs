namespace FinalAgent.Infrastructure.Secrets;

internal sealed record SecretReference(
    string ServiceName,
    string AccountName,
    string DisplayLabel);
