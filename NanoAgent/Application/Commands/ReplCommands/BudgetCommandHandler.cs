using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Commands;

internal sealed class BudgetCommandHandler : IReplCommandHandler
{
    private readonly IBudgetControlsConfigurationStore _configurationStore;
    private readonly IBudgetControlsSecretStore _secretStore;
    private readonly IBudgetControlsUsageService _usageService;
    private readonly ISelectionPrompt _selectionPrompt;
    private readonly ITextPrompt _textPrompt;
    private readonly ISecretPrompt _secretPrompt;

    public BudgetCommandHandler(
        IBudgetControlsConfigurationStore configurationStore,
        IBudgetControlsSecretStore secretStore,
        IBudgetControlsUsageService usageService,
        ISelectionPrompt selectionPrompt,
        ITextPrompt textPrompt,
        ISecretPrompt secretPrompt)
    {
        _configurationStore = configurationStore;
        _secretStore = secretStore;
        _usageService = usageService;
        _selectionPrompt = selectionPrompt;
        _textPrompt = textPrompt;
        _secretPrompt = secretPrompt;
    }

    public string CommandName => "budget";

    public string Description => "Show or configure budget controls from local or cloud settings.";

    public string Usage => "/budget [status|local [path]|cloud]";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Arguments.Count == 0)
        {
            return await PromptAndExecuteAsync(context, cancellationToken);
        }

        string action = context.Arguments[0].Trim();
        return action.ToLowerInvariant() switch
        {
            "status" or "show" => await ShowStatusAsync(context, cancellationToken),
            "local" => await ConfigureLocalAsync(context, cancellationToken),
            "cloud" => await ConfigureCloudAsync(context, cancellationToken),
            "help" or "-h" or "--help" => ReplCommandResult.Continue(FormatHelp()),
            _ => ReplCommandResult.Continue(
                $"Usage: {Usage}",
                ReplFeedbackKind.Error)
        };
    }

    private async Task<ReplCommandResult> PromptAndExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        BudgetControlsSettings current = await LoadSettingsAsync(cancellationToken);
        BudgetCommandAction action = await _selectionPrompt.PromptAsync(
            new SelectionPromptRequest<BudgetCommandAction>(
                "Budget controls",
                [
                    new SelectionPromptOption<BudgetCommandAction>(
                        "Local",
                        BudgetCommandAction.Local,
                        "Create or use a workspace-local budget controls file."),
                    new SelectionPromptOption<BudgetCommandAction>(
                        "Cloud",
                        BudgetCommandAction.Cloud,
                        "Connect to a cloud budget controls API with an auth key."),
                    new SelectionPromptOption<BudgetCommandAction>(
                        "Status",
                        BudgetCommandAction.Status,
                        "Show the current budget controls configuration.")
                ],
                FormatCurrentSummary(current),
                DefaultIndex: string.Equals(
                    current.Source,
                    BudgetControlsSettings.CloudSource,
                    StringComparison.OrdinalIgnoreCase)
                        ? 1
                        : 0),
            cancellationToken);

        return action switch
        {
            BudgetCommandAction.Local => await ConfigureLocalAsync(context, cancellationToken),
            BudgetCommandAction.Cloud => await ConfigureCloudAsync(context, cancellationToken),
            _ => await ShowStatusAsync(context, cancellationToken)
        };
    }

    private async Task<ReplCommandResult> ShowStatusAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            BudgetControlsStatus status = await _usageService.GetStatusAsync(
                context.Session,
                cancellationToken);

            return ReplCommandResult.Continue(FormatStatus(status));
        }
        catch (HttpRequestException exception)
        {
            return ReplCommandResult.Continue(
                $"Budget controls status could not be loaded: {exception.Message}",
                ReplFeedbackKind.Warning);
        }
    }

    private async Task<ReplCommandResult> ConfigureLocalAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        if (context.Arguments.Count > 2)
        {
            return ReplCommandResult.Continue(
                "Usage: /budget local [path]",
                ReplFeedbackKind.Error);
        }

        string localPath = context.Arguments.Count == 2
            ? context.Arguments[1]
            : BudgetControlsSettings.DefaultLocalPath;

        decimal? monthlyBudgetUsd = await PromptOptionalMoneyAsync(
            "Monthly budget USD",
            "Optional monthly budget in USD. Leave empty for no fixed budget.",
            cancellationToken);
        int alertThresholdPercent = await PromptAlertThresholdAsync(cancellationToken);
        BudgetControlsPricing pricing = new(
            await PromptPriceAsync(
                "Input price",
                "USD price per 1M non-cached input tokens.",
                cancellationToken),
            await PromptPriceAsync(
                "Cached input price",
                "USD price per 1M cached input tokens.",
                cancellationToken),
            await PromptPriceAsync(
                "Output price",
                "USD price per 1M output tokens.",
                cancellationToken));

        BudgetControlsLocalOptions options = new(
            pricing,
            monthlyBudgetUsd,
            alertThresholdPercent);

        try
        {
            await _usageService.ConfigureLocalAsync(
                context.Session,
                localPath,
                options,
                cancellationToken);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return ReplCommandResult.Continue(
                exception.Message,
                ReplFeedbackKind.Error);
        }
        catch (InvalidOperationException exception)
        {
            return ReplCommandResult.Continue(
                exception.Message,
                ReplFeedbackKind.Error);
        }

        BudgetControlsStatus status = await _usageService.GetStatusAsync(
            context.Session,
            cancellationToken);

        return ReplCommandResult.Continue(
            "Budget controls now use local tracking.\n\n" +
            FormatStatus(status));
    }

    private async Task<ReplCommandResult> ConfigureCloudAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        if (context.Arguments.Count > 1)
        {
            return ReplCommandResult.Continue(
                "Usage: /budget cloud",
                ReplFeedbackKind.Error);
        }

        BudgetControlsSettings current = await LoadSettingsAsync(cancellationToken);
        string apiUrl = await _textPrompt.PromptAsync(
            new TextPromptRequest(
                "Budget API URL",
                "Enter the cloud budget controls API URL.",
                current.CloudApiUrl),
            cancellationToken);

        if (!TryNormalizeCloudApiUrl(apiUrl, out string normalizedApiUrl, out string? apiUrlError))
        {
            return ReplCommandResult.Continue(
                apiUrlError ?? "Budget API URL is invalid.",
                ReplFeedbackKind.Error);
        }

        string authKey = await _secretPrompt.PromptAsync(
            new SecretPromptRequest(
                "Budget auth key",
                "Paste the auth key for the cloud budget controls API."),
            cancellationToken);

        if (string.IsNullOrWhiteSpace(authKey))
        {
            return ReplCommandResult.Continue(
                "Budget auth key cannot be empty.",
                ReplFeedbackKind.Error);
        }

        await _secretStore.SaveCloudAuthKeyAsync(authKey, cancellationToken);

        BudgetControlsSettings settings = BudgetControlsSettings.Cloud(
            normalizedApiUrl,
            hasCloudAuthKey: true);
        await _configurationStore.SaveAsync(settings, cancellationToken);

        try
        {
            BudgetControlsStatus status = await _usageService.GetStatusAsync(
                context.Session,
                cancellationToken);

            return ReplCommandResult.Continue(
                "Budget controls now use the configured cloud API.\n\n" +
                FormatStatus(status));
        }
        catch (HttpRequestException exception)
        {
            return ReplCommandResult.Continue(
                "Budget controls now use the configured cloud API, but the status request failed: " +
                exception.Message,
                ReplFeedbackKind.Warning);
        }
    }

    private async Task<BudgetControlsSettings> LoadSettingsAsync(CancellationToken cancellationToken)
    {
        return BudgetControlsSettings.NormalizeOrDefault(
            await _configurationStore.LoadAsync(cancellationToken));
    }

    private static bool TryNormalizeCloudApiUrl(
        string value,
        out string normalizedApiUrl,
        out string? errorMessage)
    {
        normalizedApiUrl = string.Empty;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errorMessage = "Budget API URL must be an absolute HTTP or HTTPS URL.";
            return false;
        }

        normalizedApiUrl = uri.ToString();
        return true;
    }

    private static string FormatCurrentSummary(BudgetControlsSettings settings)
    {
        return string.Equals(settings.Source, BudgetControlsSettings.CloudSource, StringComparison.OrdinalIgnoreCase)
            ? $"Current source: cloud ({settings.CloudApiUrl ?? "API URL not set"})."
            : $"Current source: local ({settings.LocalPath ?? BudgetControlsSettings.DefaultLocalPath}).";
    }

    private static string FormatHelp()
    {
        return "Budget controls commands:\n" +
            "/budget - Choose local, cloud, or status with the picker.\n" +
            "/budget status - Show the current budget controls source.\n" +
            "/budget local [path] - Ask for monthly budget, alert threshold, and token prices, then enable local usage tracking.\n" +
            "/budget cloud - Ask for the cloud API URL and auth key.";
    }

    private async Task<decimal> PromptPriceAsync(
        string label,
        string description,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            string rawValue = await _textPrompt.PromptAsync(
                new TextPromptRequest(
                    label,
                    description,
                    DefaultValue: "0"),
                cancellationToken);

            if (decimal.TryParse(
                    rawValue,
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out decimal price) &&
                price >= 0m)
            {
                return price;
            }
        }
    }

    private async Task<decimal?> PromptOptionalMoneyAsync(
        string label,
        string description,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            string rawValue = await _textPrompt.PromptAsync(
                new TextPromptRequest(
                    label,
                    description),
                cancellationToken);

            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return null;
            }

            if (decimal.TryParse(
                    rawValue,
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out decimal value) &&
                value >= 0m)
            {
                return value;
            }
        }
    }

    private async Task<int> PromptAlertThresholdAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            string rawValue = await _textPrompt.PromptAsync(
                new TextPromptRequest(
                    "Alert threshold",
                    "Alert threshold as a percent from 1 to 100.",
                    DefaultValue: "80"),
                cancellationToken);

            if (int.TryParse(
                    rawValue,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int threshold) &&
                threshold is >= 1 and <= 100)
            {
                return threshold;
            }
        }
    }

    private static string FormatStatus(BudgetControlsStatus status)
    {
        if (string.Equals(status.Source, BudgetControlsSettings.CloudSource, StringComparison.OrdinalIgnoreCase))
        {
            return "Budget controls:\n" +
                "Source: Cloud\n" +
                $"API URL: {status.CloudApiUrl ?? "(not configured)"}\n" +
                $"Auth key: {(status.HasCloudAuthKey ? "stored" : "missing")}\n" +
                $"Monthly budget USD: {FormatMoney(status.MonthlyBudgetUsd)}\n" +
                $"Spent USD: {FormatMoney(status.SpentUsd)}\n" +
                $"Alert threshold: {status.AlertThresholdPercent}%";
        }

        return "Budget controls:\n" +
            "Source: Local\n" +
            $"Local path: {status.LocalPath ?? BudgetControlsSettings.DefaultLocalPath}\n" +
            $"Monthly budget USD: {FormatMoney(status.MonthlyBudgetUsd)}\n" +
            $"Spent USD: {FormatMoney(status.SpentUsd)}\n" +
            $"Alert threshold: {status.AlertThresholdPercent}%";
    }

    private static string FormatMoney(decimal? value)
    {
        return value is null
            ? "(not configured)"
            : value.Value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    }

    private enum BudgetCommandAction
    {
        Local,
        Cloud,
        Status
    }
}
