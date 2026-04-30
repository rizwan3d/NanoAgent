using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Utilities;
using NanoAgent.Infrastructure.Workspaces;
using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Infrastructure.CodeIntelligence;

internal sealed class LspCodeIntelligenceService : ICodeIntelligenceService
{
    private const int MaxResults = 200;
    private const int MaxHoverCharacters = 4_000;

    private readonly IWorkspaceRootProvider _workspaceRootProvider;

    public LspCodeIntelligenceService(IWorkspaceRootProvider workspaceRootProvider)
    {
        _workspaceRootProvider = workspaceRootProvider;
    }

    public async Task<CodeIntelligenceResult> QueryAsync(
        CodeIntelligenceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        string workspaceRoot = Path.GetFullPath(_workspaceRootProvider.GetWorkspaceRoot());
        string fullPath = WorkspacePath.Resolve(workspaceRoot, request.Path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"Source file '{request.Path}' does not exist.",
                request.Path);
        }

        if (WorkspaceIgnoreMatcher.Load(workspaceRoot).IsIgnored(fullPath, isDirectory: false))
        {
            throw new UnauthorizedAccessException(
                $"Source file '{WorkspacePath.ToRelativePath(workspaceRoot, fullPath)}' is excluded by .nanoagent/.nanoignore.");
        }

        LanguageServerDefinition[] servers = GetLanguageServers(fullPath).ToArray();
        if (servers.Length == 0)
        {
            throw new CodeIntelligenceUnavailableException(
                $"No supported language server is configured for '{Path.GetExtension(fullPath)}' files.",
                [GetSupportedServersText()]);
        }

        string sourceText = await File.ReadAllTextAsync(
            fullPath,
            Encoding.UTF8,
            cancellationToken);
        string relativePath = WorkspacePath.ToRelativePath(workspaceRoot, fullPath);
        string fileUri = CreateFileUri(fullPath);
        string rootUri = CreateFileUri(workspaceRoot);

        List<string> attempts = [];
        foreach (LanguageServerDefinition server in servers)
        {
            using CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));

            try
            {
                return await QueryServerAsync(
                    server,
                    request,
                    workspaceRoot,
                    rootUri,
                    fileUri,
                    relativePath,
                    sourceText,
                    attempts,
                    timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                attempts.Add($"{server.Name}: timed out after {request.TimeoutSeconds} seconds.");
            }
            catch (Win32Exception)
            {
                attempts.Add($"{server.Name}: command '{server.Command}' was not found.");
            }
            catch (Exception exception) when (IsRecoverableServerException(exception))
            {
                attempts.Add($"{server.Name}: {exception.Message}");
            }
        }

        string commands = string.Join(
            ", ",
            servers
                .Select(static server => server.Command)
                .Distinct(StringComparer.Ordinal));
        throw new CodeIntelligenceUnavailableException(
            $"No language server could complete '{request.Action}' for '{request.Path}'. Install one of: {commands}.",
            attempts);
    }

    private async Task<CodeIntelligenceResult> QueryServerAsync(
        LanguageServerDefinition server,
        CodeIntelligenceRequest request,
        string workspaceRoot,
        string rootUri,
        string fileUri,
        string relativePath,
        string sourceText,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken)
    {
        using Process process = CreateProcess(server, workspaceRoot);
        if (!process.Start())
        {
            throw new InvalidOperationException($"Language server '{server.Name}' did not start.");
        }

        _ = ConsumeStandardErrorAsync(process);
        await using LspConnection connection = new(process, rootUri, Path.GetFileName(workspaceRoot));

        await connection.InitializeAsync(workspaceRoot, cancellationToken);
        await connection.DidOpenAsync(
            fileUri,
            server.LanguageId,
            sourceText,
            cancellationToken);

        JsonElement result = request.Action switch
        {
            "document_symbols" => await connection.SendRequestAsync(
                "textDocument/documentSymbol",
                writer => WriteTextDocumentParams(writer, fileUri),
                cancellationToken),
            "definition" => await connection.SendRequestAsync(
                "textDocument/definition",
                writer => WriteTextDocumentPositionParams(writer, fileUri, request.Line, request.Character),
                cancellationToken),
            "references" => await connection.SendRequestAsync(
                "textDocument/references",
                writer => WriteReferenceParams(writer, fileUri, request.Line, request.Character, request.IncludeDeclaration),
                cancellationToken),
            "hover" => await connection.SendRequestAsync(
                "textDocument/hover",
                writer => WriteTextDocumentPositionParams(writer, fileUri, request.Line, request.Character),
                cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported code intelligence action '{request.Action}'.")
        };

        return request.Action switch
        {
            "document_symbols" => CreateSymbolResult(
                request,
                server,
                relativePath,
                workspaceRoot,
                result,
                warnings),
            "hover" => CreateHoverResult(
                request,
                server,
                relativePath,
                result,
                warnings),
            _ => CreateLocationResult(
                request,
                server,
                relativePath,
                workspaceRoot,
                result,
                warnings)
        };
    }

    private static Process CreateProcess(
        LanguageServerDefinition server,
        string workspaceRoot)
    {
        ProcessStartInfo startInfo = new(server.Command)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = workspaceRoot
        };

        foreach (string argument in server.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return new Process
        {
            StartInfo = startInfo
        };
    }

    private static async Task ConsumeStandardErrorAsync(Process process)
    {
        try
        {
            await process.StandardError.ReadToEndAsync();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
        }
    }

    private static CodeIntelligenceResult CreateSymbolResult(
        CodeIntelligenceRequest request,
        LanguageServerDefinition server,
        string relativePath,
        string workspaceRoot,
        JsonElement result,
        IReadOnlyList<string> warnings)
    {
        List<CodeIntelligenceItem> items = [];
        if (result.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement symbol in result.EnumerateArray())
            {
                if (symbol.TryGetProperty("location", out JsonElement location))
                {
                    TryAddLocationItem(
                        items,
                        "Symbol",
                        GetString(symbol, "name"),
                        GetString(symbol, "detail"),
                        GetString(symbol, "containerName"),
                        workspaceRoot,
                        location);
                }
                else
                {
                    AddDocumentSymbol(items, symbol, relativePath, parentName: null);
                }

                if (items.Count >= MaxResults)
                {
                    break;
                }
            }
        }

        return new CodeIntelligenceResult(
            request.Action,
            relativePath,
            server.LanguageId,
            server.Name,
            items.Take(MaxResults).ToArray(),
            HoverText: null,
            warnings.ToArray());
    }

    private static CodeIntelligenceResult CreateLocationResult(
        CodeIntelligenceRequest request,
        LanguageServerDefinition server,
        string relativePath,
        string workspaceRoot,
        JsonElement result,
        IReadOnlyList<string> warnings)
    {
        List<CodeIntelligenceItem> items = [];
        string kind = string.Equals(request.Action, "definition", StringComparison.Ordinal)
            ? "Definition"
            : "Reference";

        if (result.ValueKind == JsonValueKind.Object)
        {
            TryAddLocationOrLinkItem(items, kind, workspaceRoot, result);
        }
        else if (result.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement location in result.EnumerateArray())
            {
                TryAddLocationOrLinkItem(items, kind, workspaceRoot, location);
                if (items.Count >= MaxResults)
                {
                    break;
                }
            }
        }

        return new CodeIntelligenceResult(
            request.Action,
            relativePath,
            server.LanguageId,
            server.Name,
            items.Take(MaxResults).ToArray(),
            HoverText: null,
            warnings.ToArray());
    }

    private static CodeIntelligenceResult CreateHoverResult(
        CodeIntelligenceRequest request,
        LanguageServerDefinition server,
        string relativePath,
        JsonElement result,
        IReadOnlyList<string> warnings)
    {
        string? hoverText = ExtractHoverText(result);
        return new CodeIntelligenceResult(
            request.Action,
            relativePath,
            server.LanguageId,
            server.Name,
            [],
            hoverText,
            warnings.ToArray());
    }

    private static void AddDocumentSymbol(
        List<CodeIntelligenceItem> items,
        JsonElement symbol,
        string relativePath,
        string? parentName)
    {
        string? name = GetString(symbol, "name");
        string kind = GetSymbolKind(GetInt32(symbol, "kind"));
        string? detail = GetString(symbol, "detail");
        if (TryGetSymbolRange(symbol, out LspRange range))
        {
            items.Add(new CodeIntelligenceItem(
                kind,
                name,
                detail,
                relativePath,
                range.Start.Line + 1,
                range.Start.Character + 1,
                range.End.Line + 1,
                range.End.Character + 1,
                parentName));
        }

        if (items.Count >= MaxResults ||
            !symbol.TryGetProperty("children", out JsonElement children) ||
            children.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement child in children.EnumerateArray())
        {
            AddDocumentSymbol(items, child, relativePath, name ?? parentName);
            if (items.Count >= MaxResults)
            {
                return;
            }
        }
    }

    private static void TryAddLocationOrLinkItem(
        List<CodeIntelligenceItem> items,
        string kind,
        string workspaceRoot,
        JsonElement value)
    {
        if (value.TryGetProperty("targetUri", out JsonElement targetUri))
        {
            TryAddLocationLinkItem(items, kind, workspaceRoot, value, targetUri);
            return;
        }

        TryAddLocationItem(
            items,
            kind,
            name: null,
            detail: null,
            containerName: null,
            workspaceRoot,
            value);
    }

    private static void TryAddLocationLinkItem(
        List<CodeIntelligenceItem> items,
        string kind,
        string workspaceRoot,
        JsonElement value,
        JsonElement targetUri)
    {
        if (targetUri.ValueKind != JsonValueKind.String)
        {
            return;
        }

        JsonElement rangeElement = value.TryGetProperty("targetSelectionRange", out JsonElement selectionRange)
            ? selectionRange
            : value.TryGetProperty("targetRange", out JsonElement targetRange)
                ? targetRange
                : default;

        if (rangeElement.ValueKind != JsonValueKind.Object ||
            !TryGetRange(rangeElement, out LspRange range))
        {
            return;
        }

        items.Add(CreateLocationItem(
            kind,
            name: null,
            detail: null,
            containerName: null,
            workspaceRoot,
            targetUri.GetString(),
            range));
    }

    private static void TryAddLocationItem(
        List<CodeIntelligenceItem> items,
        string kind,
        string? name,
        string? detail,
        string? containerName,
        string workspaceRoot,
        JsonElement location)
    {
        if (!location.TryGetProperty("uri", out JsonElement uri) ||
            uri.ValueKind != JsonValueKind.String ||
            !location.TryGetProperty("range", out JsonElement rangeElement) ||
            !TryGetRange(rangeElement, out LspRange range))
        {
            return;
        }

        items.Add(CreateLocationItem(
            kind,
            name,
            detail,
            containerName,
            workspaceRoot,
            uri.GetString(),
            range));
    }

    private static CodeIntelligenceItem CreateLocationItem(
        string kind,
        string? name,
        string? detail,
        string? containerName,
        string workspaceRoot,
        string? uri,
        LspRange range)
    {
        return new CodeIntelligenceItem(
            kind,
            name,
            detail,
            ToWorkspacePathFromUri(workspaceRoot, uri),
            range.Start.Line + 1,
            range.Start.Character + 1,
            range.End.Line + 1,
            range.End.Character + 1,
            containerName);
    }

    private static bool TryGetSymbolRange(
        JsonElement symbol,
        out LspRange range)
    {
        range = default;
        JsonElement rangeElement = symbol.TryGetProperty("selectionRange", out JsonElement selectionRange)
            ? selectionRange
            : symbol.TryGetProperty("range", out JsonElement fullRange)
                ? fullRange
                : default;

        return rangeElement.ValueKind == JsonValueKind.Object &&
               TryGetRange(rangeElement, out range);
    }

    private static bool TryGetRange(
        JsonElement value,
        out LspRange range)
    {
        range = default;
        if (!value.TryGetProperty("start", out JsonElement start) ||
            !value.TryGetProperty("end", out JsonElement end) ||
            !TryGetPosition(start, out LspPosition startPosition) ||
            !TryGetPosition(end, out LspPosition endPosition))
        {
            return false;
        }

        range = new LspRange(startPosition, endPosition);
        return true;
    }

    private static bool TryGetPosition(
        JsonElement value,
        out LspPosition position)
    {
        position = default;
        if (!value.TryGetProperty("line", out JsonElement line) ||
            !line.TryGetInt32(out int lineNumber) ||
            !value.TryGetProperty("character", out JsonElement character) ||
            !character.TryGetInt32(out int characterNumber))
        {
            return false;
        }

        position = new LspPosition(lineNumber, characterNumber);
        return true;
    }

    private static string? ExtractHoverText(JsonElement hover)
    {
        if (hover.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
            !hover.TryGetProperty("contents", out JsonElement contents))
        {
            return null;
        }

        return ExtractMarkupText(contents);
    }

    private static string? ExtractMarkupText(JsonElement value)
    {
        string? text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Object when value.TryGetProperty("value", out JsonElement textValue) &&
                                      textValue.ValueKind == JsonValueKind.String => textValue.GetString(),
            JsonValueKind.Array => string.Join(
                Environment.NewLine + Environment.NewLine,
                value.EnumerateArray()
                    .Select(ExtractMarkupText)
                    .Where(static item => !string.IsNullOrWhiteSpace(item))),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        return normalized.Length <= MaxHoverCharacters
            ? normalized
            : normalized[..Math.Max(0, MaxHoverCharacters - 3)].TrimEnd() + "...";
    }

    private static string? GetString(
        JsonElement value,
        string propertyName)
    {
        return value.TryGetProperty(propertyName, out JsonElement property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int GetInt32(
        JsonElement value,
        string propertyName)
    {
        return value.TryGetProperty(propertyName, out JsonElement property) &&
               property.TryGetInt32(out int result)
            ? result
            : 0;
    }

    private static string ToWorkspacePathFromUri(
        string workspaceRoot,
        string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri) ||
            !Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsedUri) ||
            !parsedUri.IsFile)
        {
            return uri ?? "(unknown)";
        }

        string localPath = Path.GetFullPath(parsedUri.LocalPath);
        return WorkspacePath.IsSamePathOrDescendant(workspaceRoot, localPath)
            ? WorkspacePath.ToRelativePath(workspaceRoot, localPath)
            : "(outside workspace)";
    }

    private static string GetSymbolKind(int kind)
    {
        return kind switch
        {
            1 => "File",
            2 => "Module",
            3 => "Namespace",
            4 => "Package",
            5 => "Class",
            6 => "Method",
            7 => "Property",
            8 => "Field",
            9 => "Constructor",
            10 => "Enum",
            11 => "Interface",
            12 => "Function",
            13 => "Variable",
            14 => "Constant",
            15 => "String",
            16 => "Number",
            17 => "Boolean",
            18 => "Array",
            19 => "Object",
            20 => "Key",
            21 => "Null",
            22 => "EnumMember",
            23 => "Struct",
            24 => "Event",
            25 => "Operator",
            26 => "TypeParameter",
            _ => "Symbol"
        };
    }

    private static IReadOnlyList<LanguageServerDefinition> GetLanguageServers(string fullPath)
    {
        string extension = Path.GetExtension(fullPath).ToLowerInvariant();
        return extension switch
        {
            ".ts" or ".mts" or ".cts" => [new LanguageServerDefinition("TypeScript language server", "typescript-language-server", ["--stdio"], "typescript")],
            ".tsx" => [new LanguageServerDefinition("TypeScript language server", "typescript-language-server", ["--stdio"], "typescriptreact")],
            ".js" or ".mjs" or ".cjs" => [new LanguageServerDefinition("TypeScript language server", "typescript-language-server", ["--stdio"], "javascript")],
            ".jsx" => [new LanguageServerDefinition("TypeScript language server", "typescript-language-server", ["--stdio"], "javascriptreact")],
            ".cs" => [new LanguageServerDefinition("C# language server", "csharp-ls", [], "csharp")],
            ".py" =>
            [
                new LanguageServerDefinition("Python LSP server", "pylsp", [], "python"),
                new LanguageServerDefinition("Pyright language server", "pyright-langserver", ["--stdio"], "python")
            ],
            ".rs" => [new LanguageServerDefinition("Rust analyzer", "rust-analyzer", [], "rust")],
            ".go" => [new LanguageServerDefinition("Go language server", "gopls", [], "go")],
            ".c" or ".h" => [new LanguageServerDefinition("Clangd", "clangd", [], "c")],
            ".cc" or ".cpp" or ".cxx" or ".hh" or ".hpp" or ".hxx" => [new LanguageServerDefinition("Clangd", "clangd", [], "cpp")],
            _ => []
        };
    }

    private static string GetSupportedServersText()
    {
        return "Supported server commands: typescript-language-server, csharp-ls, pylsp, pyright-langserver, rust-analyzer, gopls, clangd.";
    }

    private static bool IsRecoverableServerException(Exception exception)
    {
        return exception is IOException or JsonException or InvalidOperationException;
    }

    private static string CreateFileUri(string path)
    {
        return new Uri(Path.GetFullPath(path)).AbsoluteUri;
    }

    private static void WriteTextDocumentParams(
        Utf8JsonWriter writer,
        string fileUri)
    {
        writer.WriteStartObject();
        WriteTextDocument(writer, fileUri);
        writer.WriteEndObject();
    }

    private static void WriteTextDocumentPositionParams(
        Utf8JsonWriter writer,
        string fileUri,
        int? line,
        int? character)
    {
        writer.WriteStartObject();
        WriteTextDocument(writer, fileUri);
        WritePosition(writer, line, character);
        writer.WriteEndObject();
    }

    private static void WriteReferenceParams(
        Utf8JsonWriter writer,
        string fileUri,
        int? line,
        int? character,
        bool includeDeclaration)
    {
        writer.WriteStartObject();
        WriteTextDocument(writer, fileUri);
        WritePosition(writer, line, character);
        writer.WritePropertyName("context");
        writer.WriteStartObject();
        writer.WriteBoolean("includeDeclaration", includeDeclaration);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteTextDocument(
        Utf8JsonWriter writer,
        string fileUri)
    {
        writer.WritePropertyName("textDocument");
        writer.WriteStartObject();
        writer.WriteString("uri", fileUri);
        writer.WriteEndObject();
    }

    private static void WritePosition(
        Utf8JsonWriter writer,
        int? line,
        int? character)
    {
        writer.WritePropertyName("position");
        writer.WriteStartObject();
        writer.WriteNumber("line", Math.Max(0, line.GetValueOrDefault(1) - 1));
        writer.WriteNumber("character", Math.Max(0, character.GetValueOrDefault(1) - 1));
        writer.WriteEndObject();
    }

    private sealed class LspConnection : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly string _rootUri;
        private readonly string _rootName;
        private int _nextId;
        private bool _initialized;
        private bool _isDisposed;

        public LspConnection(
            Process process,
            string rootUri,
            string rootName)
        {
            _process = process;
            _rootUri = rootUri;
            _rootName = string.IsNullOrWhiteSpace(rootName)
                ? "workspace"
                : rootName;
        }

        public async Task InitializeAsync(
            string workspaceRoot,
            CancellationToken cancellationToken)
        {
            await SendRequestAsync(
                "initialize",
                writer => WriteInitializeParams(writer, workspaceRoot),
                cancellationToken);
            await SendNotificationAsync(
                "initialized",
                static writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteEndObject();
                },
                cancellationToken);
            _initialized = true;
        }

        public Task DidOpenAsync(
            string fileUri,
            string languageId,
            string text,
            CancellationToken cancellationToken)
        {
            return SendNotificationAsync(
                "textDocument/didOpen",
                writer =>
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("textDocument");
                    writer.WriteStartObject();
                    writer.WriteString("uri", fileUri);
                    writer.WriteString("languageId", languageId);
                    writer.WriteNumber("version", 1);
                    writer.WriteString("text", text);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                },
                cancellationToken);
        }

        public async Task<JsonElement> SendRequestAsync(
            string method,
            Action<Utf8JsonWriter>? writeParams,
            CancellationToken cancellationToken)
        {
            int requestId = Interlocked.Increment(ref _nextId);
            await WriteRequestAsync(requestId, method, writeParams, cancellationToken);

            while (true)
            {
                JsonElement message = await ReadMessageAsync(
                    _process.StandardOutput.BaseStream,
                    cancellationToken);

                if (!message.TryGetProperty("id", out JsonElement id))
                {
                    continue;
                }

                if (IsMatchingId(id, requestId) &&
                    !message.TryGetProperty("method", out _))
                {
                    return ReadResponseResult(message);
                }

                if (message.TryGetProperty("method", out JsonElement serverMethod) &&
                    serverMethod.ValueKind == JsonValueKind.String)
                {
                    await RespondToServerRequestAsync(message, cancellationToken);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            if (!_process.HasExited && _initialized)
            {
                try
                {
                    using CancellationTokenSource shutdown = new(TimeSpan.FromSeconds(2));
                    await SendRequestAsync("shutdown", writeParams: null, shutdown.Token);
                    await SendNotificationAsync("exit", writeParams: null, shutdown.Token);
                }
                catch (Exception)
                {
                }
            }

            if (!_process.HasExited)
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                }
            }

            try
            {
                await _process.WaitForExitAsync();
            }
            catch (Exception)
            {
            }
        }

        private void WriteInitializeParams(
            Utf8JsonWriter writer,
            string workspaceRoot)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("processId");
            writer.WriteNullValue();
            writer.WriteString("rootPath", workspaceRoot);
            writer.WriteString("rootUri", _rootUri);
            writer.WritePropertyName("capabilities");
            writer.WriteStartObject();
            writer.WritePropertyName("textDocument");
            writer.WriteStartObject();
            writer.WritePropertyName("definition");
            writer.WriteStartObject();
            writer.WriteBoolean("linkSupport", true);
            writer.WriteEndObject();
            writer.WritePropertyName("documentSymbol");
            writer.WriteStartObject();
            writer.WriteBoolean("hierarchicalDocumentSymbolSupport", true);
            writer.WriteEndObject();
            writer.WritePropertyName("hover");
            writer.WriteStartObject();
            writer.WritePropertyName("contentFormat");
            writer.WriteStartArray();
            writer.WriteStringValue("markdown");
            writer.WriteStringValue("plaintext");
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WritePropertyName("references");
            writer.WriteStartObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WritePropertyName("workspace");
            writer.WriteStartObject();
            writer.WriteBoolean("configuration", false);
            writer.WriteBoolean("workspaceFolders", true);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WritePropertyName("clientInfo");
            writer.WriteStartObject();
            writer.WriteString("name", "NanoAgent");
            writer.WriteString("version", "1.0");
            writer.WriteEndObject();
            writer.WriteString("trace", "off");
            WriteWorkspaceFolders(writer);
            writer.WriteEndObject();
        }

        private async Task SendNotificationAsync(
            string method,
            Action<Utf8JsonWriter>? writeParams,
            CancellationToken cancellationToken)
        {
            await WriteMessageAsync(
                writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteString("jsonrpc", "2.0");
                    writer.WriteString("method", method);
                    if (writeParams is not null)
                    {
                        writer.WritePropertyName("params");
                        writeParams(writer);
                    }

                    writer.WriteEndObject();
                },
                cancellationToken);
        }

        private async Task WriteRequestAsync(
            int id,
            string method,
            Action<Utf8JsonWriter>? writeParams,
            CancellationToken cancellationToken)
        {
            await WriteMessageAsync(
                writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteString("jsonrpc", "2.0");
                    writer.WriteNumber("id", id);
                    writer.WriteString("method", method);
                    if (writeParams is not null)
                    {
                        writer.WritePropertyName("params");
                        writeParams(writer);
                    }

                    writer.WriteEndObject();
                },
                cancellationToken);
        }

        private async Task RespondToServerRequestAsync(
            JsonElement request,
            CancellationToken cancellationToken)
        {
            JsonElement id = request.GetProperty("id");
            string method = request.GetProperty("method").GetString() ?? string.Empty;
            await WriteMessageAsync(
                writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteString("jsonrpc", "2.0");
                    writer.WritePropertyName("id");
                    id.WriteTo(writer);
                    writer.WritePropertyName("result");
                    WriteServerRequestResult(writer, method, request);
                    writer.WriteEndObject();
                },
                cancellationToken);
        }

        private void WriteServerRequestResult(
            Utf8JsonWriter writer,
            string method,
            JsonElement request)
        {
            switch (method)
            {
                case "workspace/configuration":
                    int itemCount = 0;
                    if (request.TryGetProperty("params", out JsonElement configurationParams) &&
                        configurationParams.TryGetProperty("items", out JsonElement items) &&
                        items.ValueKind == JsonValueKind.Array)
                    {
                        itemCount = items.GetArrayLength();
                    }

                    writer.WriteStartArray();
                    for (int index = 0; index < itemCount; index++)
                    {
                        writer.WriteNullValue();
                    }

                    writer.WriteEndArray();
                    break;

                case "workspace/workspaceFolders":
                    WriteWorkspaceFolderArray(writer);
                    break;

                default:
                    writer.WriteNullValue();
                    break;
            }
        }

        private void WriteWorkspaceFolders(Utf8JsonWriter writer)
        {
            writer.WritePropertyName("workspaceFolders");
            WriteWorkspaceFolderArray(writer);
        }

        private void WriteWorkspaceFolderArray(Utf8JsonWriter writer)
        {
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("uri", _rootUri);
            writer.WriteString("name", _rootName);
            writer.WriteEndObject();
            writer.WriteEndArray();
        }

        private static JsonElement ReadResponseResult(JsonElement response)
        {
            if (response.TryGetProperty("error", out JsonElement error))
            {
                string message = error.TryGetProperty("message", out JsonElement messageValue) &&
                                 messageValue.ValueKind == JsonValueKind.String
                    ? messageValue.GetString() ?? "Language server returned an error."
                    : "Language server returned an error.";
                throw new InvalidOperationException(message);
            }

            return response.TryGetProperty("result", out JsonElement result)
                ? result.Clone()
                : default;
        }

        private static bool IsMatchingId(
            JsonElement id,
            int expectedId)
        {
            return id.ValueKind == JsonValueKind.Number &&
                   id.TryGetInt32(out int value) &&
                   value == expectedId;
        }

        private async Task WriteMessageAsync(
            Action<Utf8JsonWriter> writeMessage,
            CancellationToken cancellationToken)
        {
            ArrayBufferWriter<byte> body = new();
            using (Utf8JsonWriter writer = new(body))
            {
                writeMessage(writer);
            }

            byte[] header = Encoding.ASCII.GetBytes(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Content-Length: {body.WrittenCount}\r\n\r\n"));
            Stream input = _process.StandardInput.BaseStream;
            await input.WriteAsync(header, cancellationToken);
            await input.WriteAsync(body.WrittenMemory, cancellationToken);
            await input.FlushAsync(cancellationToken);
        }

        private static async Task<JsonElement> ReadMessageAsync(
            Stream output,
            CancellationToken cancellationToken)
        {
            List<byte> headerBytes = [];
            byte[] buffer = new byte[1];
            byte[] separator = [13, 10, 13, 10];
            int matchedSeparatorBytes = 0;

            while (true)
            {
                int read = await output.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
                if (read == 0)
                {
                    throw new IOException("Language server closed the output stream.");
                }

                byte currentByte = buffer[0];
                headerBytes.Add(currentByte);
                if (currentByte == separator[matchedSeparatorBytes])
                {
                    matchedSeparatorBytes++;
                    if (matchedSeparatorBytes == separator.Length)
                    {
                        break;
                    }
                }
                else
                {
                    matchedSeparatorBytes = currentByte == separator[0] ? 1 : 0;
                }

                if (headerBytes.Count > 8_192)
                {
                    throw new InvalidOperationException("Language server sent an oversized message header.");
                }
            }

            string headers = Encoding.ASCII.GetString(headerBytes.ToArray());
            int contentLength = ParseContentLength(headers);
            byte[] body = new byte[contentLength];
            await ReadExactAsync(output, body, cancellationToken);

            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }

        private static int ParseContentLength(string headers)
        {
            foreach (string line in headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string value = line["Content-Length:".Length..].Trim();
                if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int length) &&
                    length >= 0)
                {
                    return length;
                }
            }

            throw new InvalidOperationException("Language server message was missing Content-Length.");
        }

        private static async Task ReadExactAsync(
            Stream stream,
            byte[] buffer,
            CancellationToken cancellationToken)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await stream.ReadAsync(
                    buffer.AsMemory(offset, buffer.Length - offset),
                    cancellationToken);
                if (read == 0)
                {
                    throw new IOException("Language server closed the output stream.");
                }

                offset += read;
            }
        }
    }

    private sealed record LanguageServerDefinition(
        string Name,
        string Command,
        IReadOnlyList<string> Arguments,
        string LanguageId);

    private readonly record struct LspPosition(
        int Line,
        int Character);

    private readonly record struct LspRange(
        LspPosition Start,
        LspPosition End);
}
