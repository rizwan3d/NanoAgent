namespace StemCode.Application.Models;

public sealed record InputValidationResult(
    bool IsValid,
    string? NormalizedValue,
    string? ErrorMessage)
{
    public static InputValidationResult Success(string normalizedValue)
    {
        return new(true, normalizedValue, null);
    }

    public static InputValidationResult Failure(string errorMessage)
    {
        return new(false, null, errorMessage);
    }
}
