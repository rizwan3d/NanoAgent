using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface ILessonFailureClassifier
{
    Task<LessonFailureClassification?> ClassifyAsync(
        ReplSessionContext session,
        LessonFailureClassificationRequest request,
        CancellationToken cancellationToken);
}

