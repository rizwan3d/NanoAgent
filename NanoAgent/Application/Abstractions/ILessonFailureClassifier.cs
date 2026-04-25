using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface ILessonFailureClassifier
{
    Task<LessonFailureClassification?> ClassifyAsync(
        ReplSessionContext session,
        LessonFailureClassificationRequest request,
        CancellationToken cancellationToken);
}

