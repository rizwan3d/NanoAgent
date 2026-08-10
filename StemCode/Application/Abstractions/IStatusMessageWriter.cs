namespace StemCode.Application.Abstractions;

public interface IStatusMessageWriter
{
    Task ShowInfoAsync(string message, CancellationToken cancellationToken);

    Task ShowSuccessAsync(string message, CancellationToken cancellationToken);

    Task ShowErrorAsync(string message, CancellationToken cancellationToken);
}
