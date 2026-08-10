using StemCode.Application.Abstractions;
using StemCode.Application.Models;
using StemCode.Domain.Services;

namespace StemCode.Application.Services;

internal sealed class OnboardingInputValidator : IOnboardingInputValidator
{
    public InputValidationResult ValidateApiKey(string? value)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return InputValidationResult.Failure("API key cannot be empty.");
        }

        return InputValidationResult.Success(normalized);
    }

    public InputValidationResult ValidateBaseUrl(string? value)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return InputValidationResult.Failure("Base URL cannot be empty.");
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri))
        {
            return InputValidationResult.Failure("Base URL must be an absolute URL.");
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return InputValidationResult.Failure("Base URL must use http or https.");
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return InputValidationResult.Failure("Base URL cannot contain a query string or fragment.");
        }

        string canonicalValue = CompatibleProviderBaseUrlNormalizer.Normalize(uri);
        return InputValidationResult.Success(canonicalValue);
    }
}
