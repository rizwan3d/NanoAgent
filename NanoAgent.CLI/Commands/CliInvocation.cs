namespace NanoAgent.CLI;

internal enum CliMode
{
    Acp,
    Interactive,
    SingleTurn
}

internal sealed record CliInvocation(
    CliMode Mode,
    string[] BackendArgs,
    string? ProviderAuthKey,
    string? Prompt,
    bool ShowHelp)
{
    private static readonly string[] BackendOptionsWithValues =
    [
        "--section",
        "--session",
        "--profile",
        "--thinking"
    ];

    public static CliInvocation Help { get; } = new(
        CliMode.Interactive,
        [],
        ProviderAuthKey: null,
        Prompt: null,
        ShowHelp: true);

    public static CliInvocation Parse(
        IReadOnlyList<string> args,
        bool stdinRedirected,
        Func<string> readStandardInput)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(readStandardInput);

        List<string> backendArgs = [];
        List<string> promptParts = [];
        string? providerAuthKey = null;
        bool forceAcp = false;
        bool forceInteractive = false;
        bool readPromptFromStandardInput = false;

        for (int index = 0; index < args.Count; index++)
        {
            string arg = args[index];

            if (string.Equals(arg, "--", StringComparison.Ordinal))
            {
                promptParts.AddRange(args.Skip(index + 1));
                break;
            }

            if (IsHelpOption(arg))
            {
                return Help;
            }

            if (IsAcpOption(arg))
            {
                forceAcp = true;
                continue;
            }

            if (IsInteractiveOption(arg))
            {
                forceInteractive = true;
                continue;
            }

            if (TryConsumeBackendOption(args, ref index, backendArgs))
            {
                continue;
            }

            if (TryConsumeProviderAuthKeyOption(args, ref index, out string? authKey))
            {
                providerAuthKey = authKey;
                continue;
            }

            if (TryConsumePromptOption(args, ref index, promptParts))
            {
                continue;
            }

            if (string.Equals(arg, "--stdin", StringComparison.OrdinalIgnoreCase))
            {
                readPromptFromStandardInput = true;
                continue;
            }

            if (arg.StartsWith("-", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unknown option '{arg}'.");
            }

            promptParts.Add(arg);
        }

        if (forceAcp)
        {
            if (forceInteractive)
            {
                throw new ArgumentException("--acp cannot be combined with --interactive.");
            }

            if (readPromptFromStandardInput || promptParts.Count > 0)
            {
                throw new ArgumentException("--acp uses stdin for Agent Client Protocol messages and cannot accept a one-shot prompt.");
            }

            return new CliInvocation(
                CliMode.Acp,
                backendArgs.ToArray(),
                providerAuthKey,
                Prompt: null,
                ShowHelp: false);
        }

        if (forceInteractive)
        {
            if (stdinRedirected)
            {
                throw new ArgumentException("--interactive requires terminal input.");
            }

            return new CliInvocation(
                CliMode.Interactive,
                backendArgs.ToArray(),
                providerAuthKey,
                Prompt: null,
                ShowHelp: false);
        }

        if (readPromptFromStandardInput)
        {
            if (!stdinRedirected)
            {
                throw new ArgumentException("--stdin requires redirected standard input.");
            }

            promptParts.Add(readStandardInput());
        }
        else if (promptParts.Count == 0 && stdinRedirected)
        {
            promptParts.Add(readStandardInput());
        }

        string prompt = string.Join(' ', promptParts).Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            if (stdinRedirected)
            {
                throw new ArgumentException("No prompt was provided.");
            }

            return new CliInvocation(
                CliMode.Interactive,
                backendArgs.ToArray(),
                providerAuthKey,
                Prompt: null,
                ShowHelp: false);
        }

        return new CliInvocation(
            CliMode.SingleTurn,
            backendArgs.ToArray(),
            providerAuthKey,
            prompt,
            ShowHelp: false);
    }

    private static bool IsHelpOption(string arg)
    {
        return string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAcpOption(string arg)
    {
        return string.Equals(arg, "--acp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInteractiveOption(string arg)
    {
        return string.Equals(arg, "--interactive", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryConsumePromptOption(
        IReadOnlyList<string> args,
        ref int index,
        List<string> promptParts)
    {
        string arg = args[index];

        if (TryReadOptionValue(args, ref index, "--prompt", out string? prompt) ||
            TryReadOptionValue(args, ref index, "-p", out prompt))
        {
            promptParts.Add(prompt!);
            return true;
        }

        return false;
    }

    private static bool TryConsumeProviderAuthKeyOption(
        IReadOnlyList<string> args,
        ref int index,
        out string? providerAuthKey)
    {
        return TryReadOptionValue(args, ref index, "--provider-auth-key", out providerAuthKey);
    }

    private static bool TryConsumeBackendOption(
        IReadOnlyList<string> args,
        ref int index,
        List<string> backendArgs)
    {
        foreach (string optionName in BackendOptionsWithValues)
        {
            int originalIndex = index;
            if (!TryReadOptionValue(args, ref index, optionName, out _))
            {
                index = originalIndex;
                continue;
            }

            if (index == originalIndex)
            {
                backendArgs.Add(args[index]);
            }
            else
            {
                backendArgs.Add(args[originalIndex]);
                backendArgs.Add(args[index]);
            }

            return true;
        }

        return false;
    }

    private static bool TryReadOptionValue(
        IReadOnlyList<string> args,
        ref int index,
        string optionName,
        out string? value)
    {
        string arg = args[index];
        value = null;

        if (string.Equals(arg, optionName, StringComparison.OrdinalIgnoreCase))
        {
            int valueIndex = index + 1;
            if (valueIndex >= args.Count || string.IsNullOrWhiteSpace(args[valueIndex]))
            {
                throw new ArgumentException($"Missing value for {optionName}.");
            }

            value = args[valueIndex].Trim();
            index = valueIndex;
            return true;
        }

        string prefix = optionName + "=";
        if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        value = arg[prefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Missing value for {optionName}.");
        }

        return true;
    }
}
