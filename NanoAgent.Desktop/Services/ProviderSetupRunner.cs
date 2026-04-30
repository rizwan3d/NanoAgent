using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Backend;
using NanoAgent.Application.Models;
using NanoAgent.Desktop.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Plugin.GitHub;
using DesktopChatMessage = NanoAgent.Desktop.Models.ChatMessage;

namespace NanoAgent.Desktop.Services;

public sealed class ProviderSetupRunner : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<string>? _currentActivity;

    public event EventHandler<DesktopChatMessage>? ConversationMessageReceived;

    public event EventHandler<DesktopSelectionPrompt?>? SelectionPromptChanged;

    public event EventHandler<DesktopTextPrompt?>? TextPromptChanged;

    public async Task<ProviderSetupRunResult> RunAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<string> activity = [];
            _currentActivity = activity;

            DesktopUiBridge bridge = new(
                AddBridgeActivity,
                AddBridgeConversationMessage,
                SetSelectionPrompt,
                SetTextPrompt);

            IHost? host = null;
            try
            {
                host = NanoAgentHostFactory.Create(
                    bridge,
                    [],
                    static services => services.AddGitHubPlugin());
                IProviderSetupService setupService = host.Services.GetRequiredService<IProviderSetupService>();
                ProviderSetupResult setupResult = await setupService.EnsureConfiguredAsync(cancellationToken);

                string providerName = setupResult.OnboardingResult.Profile.ProviderKind.ToDisplayName();
                string modelId = setupResult.ModelDiscoveryResult.SelectedModelId;
                activity.Add($"Provider ready: {providerName} / {modelId}");

                return new ProviderSetupRunResult(providerName, modelId, activity);
            }
            finally
            {
                if (host is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else
                {
                    host?.Dispose();
                }
            }
        }
        finally
        {
            _currentActivity = null;
            SetSelectionPrompt(null);
            SetTextPrompt(null);
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private void AddBridgeActivity(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            _currentActivity?.Add(message.Trim());
        }
    }

    private void AddBridgeConversationMessage(string role, string message)
    {
        if (!string.IsNullOrWhiteSpace(role) &&
            !string.IsNullOrWhiteSpace(message))
        {
            ConversationMessageReceived?.Invoke(
                this,
                new DesktopChatMessage(
                    role.Trim(),
                    message.Trim(),
                    statusNote: null,
                    workspacePath: null));
        }
    }

    private void SetSelectionPrompt(DesktopSelectionPrompt? prompt)
    {
        SelectionPromptChanged?.Invoke(this, prompt);
    }

    private void SetTextPrompt(DesktopTextPrompt? prompt)
    {
        TextPromptChanged?.Invoke(this, prompt);
    }
}

public sealed record ProviderSetupRunResult(
    string ProviderName,
    string ModelId,
    IReadOnlyList<string> Activity);
