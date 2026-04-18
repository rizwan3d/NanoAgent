namespace FinalAgent.Domain.Models;

public readonly record struct GreetingContext(
    string OperatorName,
    string TargetName,
    DateTimeOffset OccurredAt);
