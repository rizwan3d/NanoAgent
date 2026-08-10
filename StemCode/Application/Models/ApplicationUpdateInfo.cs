namespace StemCode.Application.Models;

public sealed record ApplicationUpdateInfo(
    string CurrentVersion,
    string LatestVersion,
    Uri ReleaseUri,
    bool IsUpdateAvailable);
