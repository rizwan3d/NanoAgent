using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface IOnboardingInputValidator
{
    InputValidationResult ValidateApiKey(string? value);

    InputValidationResult ValidateBaseUrl(string? value);
}
