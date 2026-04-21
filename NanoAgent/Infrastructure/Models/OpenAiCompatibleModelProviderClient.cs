using System.Net.Http.Headers;
using System.Text.Json;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.OpenAi;
using Microsoft.Extensions.Logging;

namespace NanoAgent.Infrastructure.Models;

internal sealed class OpenAiCompatibleModelProviderClient : IModelProviderClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAiCompatibleModelProviderClient> _logger;

    public OpenAiCompatibleModelProviderClient(
        HttpClient httpClient,
        ILogger<OpenAiCompatibleModelProviderClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AvailableModel>> GetAvailableModelsAsync(
        AgentProviderProfile providerProfile,
        string apiKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providerProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        Uri baseUri = OpenAiBaseUriResolver.Resolve(providerProfile);
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(baseUri, "models"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        LogDebugApiRequest(request.Method, request.RequestUri);

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        LogDebugApiResponse(response.StatusCode, responseBody);

        if (!response.IsSuccessStatusCode)
        {
            string detail = string.IsNullOrWhiteSpace(responseBody)
                ? $"Provider returned HTTP {(int)response.StatusCode}."
                : $"Provider returned HTTP {(int)response.StatusCode}: {Truncate(responseBody.Trim(), 200)}";

            throw new ModelProviderException(
                $"Unable to fetch models from the configured provider. {detail}");
        }

        ModelListResponse? payload = JsonSerializer.Deserialize(
            responseBody,
            ModelApiJsonContext.Default.ModelListResponse);

        if (payload?.Data is null)
        {
            throw new ModelProviderException(
                "The configured provider returned an invalid models response.");
        }

        return payload.Data
            .Select(item => new AvailableModel(item.Id))
            .ToArray();
    }
    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..Math.Max(0, maxLength - 3)] + "...";
    }

    private void LogDebugApiRequest(
        HttpMethod method,
        Uri? requestUri)
    {
#if DEBUG
        _logger.LogInformation(
            "OpenAI-compatible models API request {Method} {RequestUri}",
            method,
            requestUri);
#endif
    }

    private void LogDebugApiResponse(
        System.Net.HttpStatusCode statusCode,
        string responseBody)
    {
#if DEBUG
        _logger.LogInformation(
            "OpenAI-compatible models API response {StatusCode}: {ResponseBody}",
            (int)statusCode,
            responseBody);
#endif
    }
}
