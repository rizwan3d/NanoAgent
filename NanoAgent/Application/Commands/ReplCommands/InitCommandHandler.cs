using NanoAgent.Application.Models;
using NanoAgent.Application.Utilities;
using System.Text;

namespace NanoAgent.Application.Commands;

internal sealed class InitCommandHandler : IReplCommandHandler
{
    private const string WorkspaceDirectoryName = ".nanoagent";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public string CommandName => "init";

    public string Description => "Initialize workspace-local NanoAgent configuration files.";

    public string Usage => "/init";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(context.ArgumentText))
        {
            return ReplCommandResult.Continue(
                "Usage: /init",
                ReplFeedbackKind.Error);
        }

        string workspaceRoot = Path.GetFullPath(context.Session.WorkspacePath);
        string workspaceDirectory = Path.Combine(workspaceRoot, WorkspaceDirectoryName);
        InitSummary summary = new(workspaceRoot);

        try
        {
            EnsureDirectory(workspaceRoot, workspaceDirectory, summary);
            EnsureDirectory(workspaceRoot, Path.Combine(workspaceDirectory, "agents"), summary);
            EnsureDirectory(workspaceRoot, Path.Combine(workspaceDirectory, "skills"), summary);
            EnsureDirectory(workspaceRoot, Path.Combine(workspaceDirectory, "skills", "dotnet"), summary);
            EnsureDirectory(workspaceRoot, Path.Combine(workspaceDirectory, "memory"), summary);
            EnsureDirectory(workspaceRoot, Path.Combine(workspaceDirectory, "logs"), summary);

            await EnsureFileAsync(
                workspaceRoot,
                Path.Combine(workspaceDirectory, "agent-profile.json"),
                AgentProfileTemplate,
                summary,
                cancellationToken);
            await EnsureFileAsync(
                workspaceRoot,
                Path.Combine(workspaceDirectory, "README.md"),
                ReadmeTemplate,
                summary,
                cancellationToken);
            await EnsureFileAsync(
                workspaceRoot,
                Path.Combine(workspaceDirectory, ".gitignore"),
                GitIgnoreTemplate,
                summary,
                cancellationToken);
            await EnsureFileAsync(
                workspaceRoot,
                Path.Combine(workspaceDirectory, ".nanoignore"),
                NanoIgnoreTemplate,
                summary,
                cancellationToken);
            await EnsureFileAsync(
                workspaceRoot,
                Path.Combine(workspaceDirectory, "agents", "code-reviewer.md.template"),
                AgentTemplate,
                summary,
                cancellationToken);
            await EnsureFileAsync(
                workspaceRoot,
                Path.Combine(workspaceDirectory, "skills", "dotnet", "SKILL.md.template"),
                SkillTemplate,
                summary,
                cancellationToken);
            foreach (RepoMemoryDocumentDefinition document in RepoMemoryDocuments.All)
            {
                await EnsureFileAsync(
                    workspaceRoot,
                    Path.Combine(workspaceDirectory, "memory", document.FileName),
                    RepoMemoryDocuments.CreateTemplate(document),
                    summary,
                    cancellationToken);
            }

            await EnsureFileAsync(
                workspaceRoot,
                Path.Combine(workspaceDirectory, "memory", "lessons.jsonl"),
                string.Empty,
                summary,
                cancellationToken);
            await EnsureFileAsync(
                workspaceRoot,
                Path.Combine(workspaceDirectory, "logs", ".gitkeep"),
                string.Empty,
                summary,
                cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ReplCommandResult.Continue(
                $"Could not initialize {WorkspaceDirectoryName}: {exception.Message}",
                ReplFeedbackKind.Error);
        }

        return ReplCommandResult.Continue(
            summary.Format(),
            ReplFeedbackKind.Info);
    }

    private static void EnsureDirectory(
        string workspaceRoot,
        string directoryPath,
        InitSummary summary)
    {
        if (Directory.Exists(directoryPath))
        {
            summary.Existing.Add(ToRelativePath(workspaceRoot, directoryPath) + "/");
            return;
        }

        Directory.CreateDirectory(directoryPath);
        summary.Created.Add(ToRelativePath(workspaceRoot, directoryPath) + "/");
    }

    private static async Task EnsureFileAsync(
        string workspaceRoot,
        string filePath,
        string content,
        InitSummary summary,
        CancellationToken cancellationToken)
    {
        if (File.Exists(filePath))
        {
            summary.Existing.Add(ToRelativePath(workspaceRoot, filePath));
            return;
        }

        string? parentDirectory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        await File.WriteAllTextAsync(
            filePath,
            content,
            Utf8NoBom,
            cancellationToken);
        summary.Created.Add(ToRelativePath(workspaceRoot, filePath));
    }

    private static string ToRelativePath(
        string workspaceRoot,
        string path)
    {
        return WorkspacePath.ToRelativePath(workspaceRoot, path);
    }

    private sealed class InitSummary
    {
        public InitSummary(string workspaceRoot)
        {
            WorkspaceRoot = workspaceRoot;
        }

        public List<string> Created { get; } = [];

        public List<string> Existing { get; } = [];

        public string WorkspaceRoot { get; }

        public string Format()
        {
            StringBuilder builder = new();
            builder.AppendLine($"Initialized NanoAgent workspace files in {WorkspaceDirectoryName}.");
            builder.AppendLine($"Workspace: {WorkspaceRoot}");

            AppendSection(builder, "Created", Created);
            AppendSection(builder, "Already existed", Existing);

            builder.AppendLine();
            builder.AppendLine("Next steps:");
            builder.AppendLine("- Edit .nanoagent/agent-profile.json for workspace memory, audit, MCP, and custom tool settings.");
            builder.AppendLine("- Review .nanoagent/memory/*.md for repo-scoped team memory your team can inspect, diff, and version-control.");
            builder.AppendLine("- Rename .nanoagent/agents/code-reviewer.md.template to .md when you want to enable that custom agent.");
            builder.AppendLine("- Rename .nanoagent/skills/dotnet/SKILL.md.template to SKILL.md when you want to enable that workspace skill.");
            builder.AppendLine("- Add a root AGENTS.md file for persistent workspace instructions.");

            return builder.ToString().Trim();
        }

        private static void AppendSection(
            StringBuilder builder,
            string title,
            IReadOnlyList<string> items)
        {
            if (items.Count == 0)
            {
                return;
            }

            builder.AppendLine();
            builder.AppendLine(title + ":");
            foreach (string item in items.Order(StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine("- " + item);
            }
        }
    }

    private const string AgentProfileTemplate =
        """
        {
          "memory": {
            "requireApprovalForWrites": true,
            "allowAutoFailureObservation": true,
            "allowAutoManualLessons": false,
            "redactSecrets": true,
            "maxEntries": 500,
            "maxPromptChars": 12000,
            "disabled": false
          },
          "toolAudit": {
            "enabled": false,
            "redactSecrets": true,
            "maxArgumentsChars": 12000,
            "maxResultChars": 12000
          },
          "customTools": {},
          "mcpServers": {}
        }
        """;

    private const string ReadmeTemplate =
        """
        # NanoAgent Workspace Files

        This directory stores workspace-local NanoAgent configuration.

        - `agent-profile.json`: workspace memory, audit, custom tools, and MCP server settings.
        - `.nanoignore`: workspace paths excluded from NanoAgent file tools.
        - `agents/*.md`: custom agents. Files ending in `.template` are inactive until renamed to `.md`.
        - `skills/**/SKILL.md`: workspace skills. Template files are inactive until renamed to `SKILL.md`.
        - `memory/*.md`: repo-scoped team memory that can be inspected, diffed, and version-controlled.
        - `memory/lessons.jsonl`: reusable local lessons about mistakes, failures, and fixes.
        - `logs/tool-audit.jsonl`: optional tool audit log when enabled in `agent-profile.json`.

        Memory writes require approval by default. Keep team memory focused on durable architecture, convention, decision, known-issue, and test-strategy notes.

        Root-level `AGENTS.md` files are loaded as persistent workspace instructions.
        """;

    private const string GitIgnoreTemplate =
        """
        logs/*.jsonl
        memory/*.jsonl
        *.local.json
        """;

    private const string NanoIgnoreTemplate =
        """
        # Local secrets and environment files
        .env
        .env.*
        *.env
        *.local
        *.secret
        *.secrets
        secrets.*
        secret.*
        credentials.*
        credential.*
        appsettings.*.local.json
        appsettings.*.secrets.json
        appsettings.Production.json
        appsettings.Staging.json

        # Credential and signing material
        *.pem
        *.key
        *.p12
        *.pfx
        *.cer
        *.crt
        *.der
        *.keystore
        *.jks
        *.publishsettings
        *.pubxml
        PublishScripts/

        # User and IDE state
        .vs/
        .vscode/*.local.json
        *.suo
        *.user
        *.userosscache
        *.rsuser
        *.userprefs
        *.DotSettings.user
        .localhistory/

        # Build, test, and generated output
        [Bb]in/
        [Oo]bj/
        [Dd]ebug/
        [Rr]elease/
        [Rr]eleases/
        artifacts/
        publish/
        TestResults/
        [Tt]est[Rr]esult*/
        BenchmarkDotNet.Artifacts/
        coverage/
        coverage*.json
        coverage*.xml
        coverage*.info
        *.coverage
        *.coveragexml
        *.binlog
        *.log

        # Package and dependency caches
        node_modules/
        packages/
        **/[Pp]ackages/*
        *.nupkg
        *.snupkg
        .nuget/

        # NanoAgent local runtime data
        .nanoagent/sessions/
        .nanoagent/logs/
        .nanoagent/cache/
        .nanoagent/tmp/
        .nanoagent/temp/
        .nanoagent/memory/*.jsonl

        # VCS and OS metadata
        .git/
        .DS_Store
        Thumbs.db
        desktop.ini
        """;

    private const string AgentTemplate =
        """
        ---
        name: code-reviewer
        mode: subagent
        description: Read-only reviewer for bugs, regressions, edge cases, and missing tests.
        editMode: readOnly
        shellMode: safeInspectionOnly
        tools:
          - code_intelligence
          - directory_list
          - file_read
          - search_files
          - shell_command
          - text_search
        ---
        Review the requested code or change set with a findings-first posture.
        """;

    private const string SkillTemplate =
        """
        ---
        name: dotnet
        description: Use for .NET build, test, package, and project-file work.
        ---
        Prefer repo-native build and test commands.
        Inspect the relevant `.csproj` before changing package references.
        Keep package and target framework changes narrowly scoped.
        """;

}
