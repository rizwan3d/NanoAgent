namespace StemCode.Application.Abstractions;

public interface IStemCodeEnterpriseAuthenticator
{
    Task<string> AuthenticateAsync(
        string baseUrl,
        CancellationToken cancellationToken);
}
