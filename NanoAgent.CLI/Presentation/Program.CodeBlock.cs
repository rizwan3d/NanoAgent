using Spectre.Console;

namespace NanoAgent.CLI;

public static partial class Program
{
    private const string CodeBlockGutterMarkup = "[grey]│[/] ";
    private const string CodeBlockGutterPlain = "│ ";
    private const string CodeKeywordStyle = "deepskyblue1";
    private const string CodeStringStyle = "green";
    private const string CodeCommentStyle = "grey";
    private const string CodeNumberStyle = "aqua";
    private const string CodeFunctionStyle = "yellow";
    private const string CodeLanguageLabelStyle = "grey";

    private readonly record struct CodeLanguageSyntax(
        IReadOnlySet<string> Keywords,
        IReadOnlyList<string> LineCommentMarkers,
        bool HasBlockComments,
        bool SupportsBacktickStrings,
        bool IsMarkup = false);

    // Cross-line highlighting state for constructs that can span multiple lines
    // (C-style block comments, HTML/XML comments, and open markup tags).
    private struct CodeHighlightState
    {
        public bool InsideBlockComment;
        public bool InsideMarkupComment;
        public bool InsideTag;
    }

    // Detects a fenced code block (``` or ~~~) starting at <startIndex>.
    // Returns the info-string language, the raw code lines, and how many source
    // lines the block consumed (including the opening and closing fences).
    private static bool TryReadFencedCodeBlock(
        string[] rawLines,
        int startIndex,
        out string? language,
        out List<string> codeLines,
        out int consumedLineCount)
    {
        language = null;
        codeLines = [];
        consumedLineCount = 0;

        string opening = rawLines[startIndex];
        string trimmed = opening.TrimStart();
        int indent = opening.Length - trimmed.Length;
        if (indent > 3)
        {
            return false;
        }

        if (!TryGetCodeFenceMarker(trimmed, out char fenceChar, out int fenceLength))
        {
            return false;
        }

        string infoString = trimmed[fenceLength..].Trim();

        // A backtick fence's info string may not contain a backtick; otherwise this
        // is inline code (for example `like this`), not a fenced block.
        if (fenceChar == '`' && infoString.Contains('`', StringComparison.Ordinal))
        {
            return false;
        }

        language = ExtractCodeFenceLanguage(infoString);

        int lineIndex = startIndex + 1;
        while (lineIndex < rawLines.Length)
        {
            string candidate = rawLines[lineIndex].TrimStart();
            if (IsClosingCodeFence(candidate, fenceChar, fenceLength))
            {
                consumedLineCount = lineIndex - startIndex + 1;
                return true;
            }

            codeLines.Add(rawLines[lineIndex]);
            lineIndex++;
        }

        // Unclosed fence (for example while a response is still streaming): render
        // everything that has arrived so far as code.
        consumedLineCount = lineIndex - startIndex;
        return true;
    }

    private static bool TryGetCodeFenceMarker(
        string trimmedLine,
        out char fenceChar,
        out int fenceLength)
    {
        fenceChar = '\0';
        fenceLength = 0;

        if (trimmedLine.Length < 3)
        {
            return false;
        }

        char marker = trimmedLine[0];
        if (marker is not ('`' or '~'))
        {
            return false;
        }

        int length = 0;
        while (length < trimmedLine.Length && trimmedLine[length] == marker)
        {
            length++;
        }

        if (length < 3)
        {
            return false;
        }

        fenceChar = marker;
        fenceLength = length;
        return true;
    }

    private static bool IsClosingCodeFence(
        string trimmedLine,
        char fenceChar,
        int openingLength)
    {
        int length = 0;
        while (length < trimmedLine.Length && trimmedLine[length] == fenceChar)
        {
            length++;
        }

        return length >= openingLength &&
            trimmedLine[length..].Trim().Length == 0;
    }

    private static string? ExtractCodeFenceLanguage(string infoString)
    {
        if (string.IsNullOrWhiteSpace(infoString))
        {
            return null;
        }

        int spaceIndex = infoString.IndexOfAny([' ', '\t']);
        string language = spaceIndex < 0 ? infoString : infoString[..spaceIndex];
        return language.Length == 0 ? null : language;
    }

    private static void AddCodeBlockLines(
        List<ConversationLine> lines,
        string? language,
        IReadOnlyList<string> codeLines,
        ref bool firstLine,
        string roleName,
        string roleColor,
        int contentWidth)
    {
        if (!string.IsNullOrWhiteSpace(language))
        {
            AddConversationContentLine(
                lines,
                $"[{CodeLanguageLabelStyle}]{Markup.Escape(language!)}[/]",
                language!,
                ref firstLine,
                roleName,
                roleColor);
        }

        foreach (List<MarkdownFragment> fragments in HighlightCodeLines(codeLines, language))
        {
            AddWrappedFragmentLine(
                lines,
                fragments,
                CodeBlockGutterMarkup,
                CodeBlockGutterPlain,
                CodeBlockGutterMarkup,
                CodeBlockGutterPlain,
                ref firstLine,
                roleName,
                roleColor,
                contentWidth);
        }
    }

    // Wraps pre-built (already styled) fragments to the content width and emits one
    // conversation line per visual row, with a prefix on the first row and a
    // continuation prefix on subsequent rows.
    private static void AddWrappedFragmentLine(
        List<ConversationLine> lines,
        IReadOnlyList<MarkdownFragment> fragments,
        string firstPrefixMarkup,
        string firstPrefixPlain,
        string continuationPrefixMarkup,
        string continuationPrefixPlain,
        ref bool firstLine,
        string roleName,
        string roleColor,
        int contentWidth)
    {
        int firstMessagePrefixLength = firstLine ? roleName.Length + 2 : 5;
        int firstLineLength = Math.Max(
            1,
            contentWidth - firstMessagePrefixLength - firstPrefixPlain.Length);
        int continuationLineLength = Math.Max(
            1,
            contentWidth - 5 - continuationPrefixPlain.Length);

        List<List<MarkdownFragment>> wrappedLines = WrapMarkdownFragments(
            fragments,
            firstLineLength,
            continuationLineLength);

        for (int index = 0; index < wrappedLines.Count; index++)
        {
            InlineRenderResult renderResult = RenderMarkdownFragments(wrappedLines[index], string.Empty);
            bool isFirstWrappedLine = index == 0;
            AddConversationContentLine(
                lines,
                (isFirstWrappedLine ? firstPrefixMarkup : continuationPrefixMarkup) + renderResult.Markup,
                (isFirstWrappedLine ? firstPrefixPlain : continuationPrefixPlain) + renderResult.Plain,
                ref firstLine,
                roleName,
                roleColor);
        }
    }

    private static List<List<MarkdownFragment>> HighlightCodeLines(
        IReadOnlyList<string> codeLines,
        string? language)
    {
        CodeLanguageSyntax syntax = GetCodeLanguageSyntax(language);
        List<List<MarkdownFragment>> highlightedLines = [];
        CodeHighlightState state = default;

        foreach (string line in codeLines)
        {
            List<MarkdownFragment> fragments = [];
            HighlightLine(line, syntax, fragments, ref state);
            highlightedLines.Add(fragments);
        }

        return highlightedLines;
    }

    private static void HighlightLine(
        string line,
        CodeLanguageSyntax syntax,
        List<MarkdownFragment> fragments,
        ref CodeHighlightState state)
    {
        if (syntax.IsMarkup)
        {
            HighlightMarkupLine(line, fragments, ref state);
            return;
        }

        HighlightCodeLine(line, syntax, fragments, ref state);
    }

    private static void HighlightCodeLine(
        string line,
        CodeLanguageSyntax syntax,
        List<MarkdownFragment> fragments,
        ref CodeHighlightState state)
    {
        int length = line.Length;
        int index = 0;
        int plainStart = 0;

        while (index < length)
        {
            if (state.InsideBlockComment)
            {
                int closeIndex = line.IndexOf("*/", index, StringComparison.Ordinal);
                if (closeIndex < 0)
                {
                    AddMarkdownFragment(fragments, line[plainStart..], CodeCommentStyle);
                    return;
                }

                AddMarkdownFragment(fragments, line[plainStart..(closeIndex + 2)], CodeCommentStyle);
                index = closeIndex + 2;
                plainStart = index;
                state.InsideBlockComment = false;
                continue;
            }

            char character = line[index];

            if (syntax.HasBlockComments &&
                character == '/' &&
                index + 1 < length &&
                line[index + 1] == '*')
            {
                AddMarkdownFragment(fragments, line[plainStart..index], string.Empty);
                int closeIndex = line.IndexOf("*/", index + 2, StringComparison.Ordinal);
                if (closeIndex < 0)
                {
                    AddMarkdownFragment(fragments, line[index..], CodeCommentStyle);
                    state.InsideBlockComment = true;
                    return;
                }

                AddMarkdownFragment(fragments, line[index..(closeIndex + 2)], CodeCommentStyle);
                index = closeIndex + 2;
                plainStart = index;
                continue;
            }

            if (MatchesLineComment(line, index, syntax.LineCommentMarkers))
            {
                AddMarkdownFragment(fragments, line[plainStart..index], string.Empty);
                AddMarkdownFragment(fragments, line[index..], CodeCommentStyle);
                return;
            }

            if (character is '"' or '\'' ||
                (syntax.SupportsBacktickStrings && character == '`'))
            {
                AddMarkdownFragment(fragments, line[plainStart..index], string.Empty);
                int stringEnd = ScanCodeString(line, index, character);
                AddMarkdownFragment(fragments, line[index..stringEnd], CodeStringStyle);
                index = stringEnd;
                plainStart = index;
                continue;
            }

            if (char.IsDigit(character) &&
                (index == 0 || !IsCodeIdentifierChar(line[index - 1])))
            {
                AddMarkdownFragment(fragments, line[plainStart..index], string.Empty);
                int numberEnd = ScanCodeNumber(line, index);
                AddMarkdownFragment(fragments, line[index..numberEnd], CodeNumberStyle);
                index = numberEnd;
                plainStart = index;
                continue;
            }

            if (IsCodeIdentifierStart(character))
            {
                int wordEnd = index + 1;
                while (wordEnd < length && IsCodeIdentifierChar(line[wordEnd]))
                {
                    wordEnd++;
                }

                string word = line[index..wordEnd];
                if (syntax.Keywords.Contains(word))
                {
                    AddMarkdownFragment(fragments, line[plainStart..index], string.Empty);
                    AddMarkdownFragment(fragments, word, CodeKeywordStyle);
                    index = wordEnd;
                    plainStart = index;
                    continue;
                }

                int callIndex = wordEnd;
                while (callIndex < length && line[callIndex] == ' ')
                {
                    callIndex++;
                }

                if (callIndex < length && line[callIndex] == '(')
                {
                    AddMarkdownFragment(fragments, line[plainStart..index], string.Empty);
                    AddMarkdownFragment(fragments, word, CodeFunctionStyle);
                    index = wordEnd;
                    plainStart = index;
                    continue;
                }

                index = wordEnd;
                continue;
            }

            index++;
        }

        if (plainStart < length)
        {
            AddMarkdownFragment(fragments, line[plainStart..], string.Empty);
        }
    }

    // Highlights one line of HTML/XML-style markup: <!-- comments -->, tag names,
    // attribute names, and quoted attribute values. Comments and open tags can span
    // lines, so that carries across calls via CodeHighlightState.
    private static void HighlightMarkupLine(
        string line,
        List<MarkdownFragment> fragments,
        ref CodeHighlightState state)
    {
        int length = line.Length;
        int index = 0;
        int plainStart = 0;

        while (index < length)
        {
            if (state.InsideMarkupComment)
            {
                int closeIndex = line.IndexOf("-->", index, StringComparison.Ordinal);
                if (closeIndex < 0)
                {
                    AddMarkdownFragment(fragments, line[plainStart..], CodeCommentStyle);
                    return;
                }

                AddMarkdownFragment(fragments, line[plainStart..(closeIndex + 3)], CodeCommentStyle);
                index = closeIndex + 3;
                plainStart = index;
                state.InsideMarkupComment = false;
                continue;
            }

            char character = line[index];

            if (state.InsideTag)
            {
                if (character is '"' or '\'')
                {
                    AddMarkdownFragment(fragments, line[plainStart..index], string.Empty);
                    int stringEnd = ScanCodeString(line, index, character);
                    AddMarkdownFragment(fragments, line[index..stringEnd], CodeStringStyle);
                    index = stringEnd;
                    plainStart = index;
                    continue;
                }

                if (character == '>')
                {
                    state.InsideTag = false;
                    index++;
                    continue;
                }

                if (IsMarkupNameStart(character))
                {
                    AddMarkdownFragment(fragments, line[plainStart..index], string.Empty);
                    int nameEnd = index + 1;
                    while (nameEnd < length && IsMarkupNameChar(line[nameEnd]))
                    {
                        nameEnd++;
                    }

                    AddMarkdownFragment(fragments, line[index..nameEnd], CodeNumberStyle);
                    index = nameEnd;
                    plainStart = index;
                    continue;
                }

                index++;
                continue;
            }

            if (character == '<')
            {
                if (string.CompareOrdinal(line, index, "<!--", 0, 4) == 0)
                {
                    AddMarkdownFragment(fragments, line[plainStart..index], string.Empty);
                    int closeIndex = line.IndexOf("-->", index + 4, StringComparison.Ordinal);
                    if (closeIndex < 0)
                    {
                        AddMarkdownFragment(fragments, line[index..], CodeCommentStyle);
                        state.InsideMarkupComment = true;
                        return;
                    }

                    AddMarkdownFragment(fragments, line[index..(closeIndex + 3)], CodeCommentStyle);
                    index = closeIndex + 3;
                    plainStart = index;
                    continue;
                }

                char next = index + 1 < length ? line[index + 1] : '\0';
                if (next is '/' or '!' or '?' || IsMarkupNameStart(next))
                {
                    AddMarkdownFragment(fragments, line[plainStart..index], string.Empty);

                    int delimiterEnd = index + 1;
                    if (delimiterEnd < length && line[delimiterEnd] is '/' or '!' or '?')
                    {
                        delimiterEnd++;
                    }

                    AddMarkdownFragment(fragments, line[index..delimiterEnd], string.Empty);

                    int nameEnd = delimiterEnd;
                    while (nameEnd < length && IsMarkupNameChar(line[nameEnd]))
                    {
                        nameEnd++;
                    }

                    if (nameEnd > delimiterEnd)
                    {
                        AddMarkdownFragment(fragments, line[delimiterEnd..nameEnd], CodeKeywordStyle);
                    }

                    index = nameEnd;
                    plainStart = index;
                    state.InsideTag = true;
                    continue;
                }
            }

            index++;
        }

        if (plainStart < length)
        {
            AddMarkdownFragment(fragments, line[plainStart..], string.Empty);
        }
    }

    private static bool IsMarkupNameStart(char character)
    {
        return char.IsLetter(character) || character is '_' or ':';
    }

    private static bool IsMarkupNameChar(char character)
    {
        return char.IsLetterOrDigit(character) || character is '-' or '_' or ':' or '.';
    }

    private static int ScanCodeString(string line, int startIndex, char quote)
    {
        int index = startIndex + 1;
        while (index < line.Length)
        {
            if (line[index] == '\\' && index + 1 < line.Length)
            {
                index += 2;
                continue;
            }

            if (line[index] == quote)
            {
                return index + 1;
            }

            index++;
        }

        return line.Length;
    }

    private static int ScanCodeNumber(string line, int startIndex)
    {
        int index = startIndex;
        while (index < line.Length &&
            (char.IsLetterOrDigit(line[index]) || line[index] is '.' or '_'))
        {
            index++;
        }

        return index;
    }

    private static bool MatchesLineComment(
        string line,
        int index,
        IReadOnlyList<string> markers)
    {
        foreach (string marker in markers)
        {
            if (string.CompareOrdinal(line, index, marker, 0, marker.Length) == 0 &&
                index + marker.Length <= line.Length)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCodeIdentifierStart(char character)
    {
        return char.IsLetter(character) || character is '_' or '$';
    }

    private static bool IsCodeIdentifierChar(char character)
    {
        return char.IsLetterOrDigit(character) || character is '_' or '$';
    }

    private static CodeLanguageSyntax GetCodeLanguageSyntax(string? language)
    {
        string normalized = (language ?? string.Empty).Trim().ToLowerInvariant();

        return normalized switch
        {
            "cs" or "csharp" or "c#" or "dotnet" =>
                new CodeLanguageSyntax(CSharpKeywords, ["//"], HasBlockComments: true, SupportsBacktickStrings: false),
            "c" or "cpp" or "c++" or "h" or "hpp" or "cc" or "objc" =>
                new CodeLanguageSyntax(CFamilyKeywords, ["//"], HasBlockComments: true, SupportsBacktickStrings: false),
            "java" or "kotlin" or "kt" or "scala" or "groovy" or "swift" =>
                new CodeLanguageSyntax(JavaKeywords, ["//"], HasBlockComments: true, SupportsBacktickStrings: false),
            "js" or "javascript" or "jsx" or "mjs" or "cjs" or "ts" or "typescript" or "tsx" =>
                new CodeLanguageSyntax(JavaScriptKeywords, ["//"], HasBlockComments: true, SupportsBacktickStrings: true),
            "go" or "golang" =>
                new CodeLanguageSyntax(GoKeywords, ["//"], HasBlockComments: true, SupportsBacktickStrings: true),
            "rust" or "rs" =>
                new CodeLanguageSyntax(RustKeywords, ["//"], HasBlockComments: true, SupportsBacktickStrings: false),
            "py" or "python" =>
                new CodeLanguageSyntax(PythonKeywords, ["#"], HasBlockComments: false, SupportsBacktickStrings: false),
            "rb" or "ruby" =>
                new CodeLanguageSyntax(RubyKeywords, ["#"], HasBlockComments: false, SupportsBacktickStrings: false),
            "php" =>
                new CodeLanguageSyntax(PhpKeywords, ["//", "#"], HasBlockComments: true, SupportsBacktickStrings: false),
            "sh" or "bash" or "zsh" or "shell" or "ps1" or "powershell" =>
                new CodeLanguageSyntax(ShellKeywords, ["#"], HasBlockComments: false, SupportsBacktickStrings: true),
            "sql" =>
                new CodeLanguageSyntax(SqlKeywords, ["--"], HasBlockComments: true, SupportsBacktickStrings: true),
            "json" =>
                new CodeLanguageSyntax(JsonKeywords, [], HasBlockComments: false, SupportsBacktickStrings: false),
            "yaml" or "yml" or "toml" or "ini" =>
                new CodeLanguageSyntax(ConfigKeywords, ["#"], HasBlockComments: false, SupportsBacktickStrings: false),
            "html" or "htm" or "xhtml" or "xml" or "svg" or "xaml" or "axaml" or "vue" or "rss" or "atom" or "plist" =>
                new CodeLanguageSyntax(EmptyKeywords, [], HasBlockComments: false, SupportsBacktickStrings: false, IsMarkup: true),
            _ =>
                new CodeLanguageSyntax(CommonKeywords, ["//", "#"], HasBlockComments: true, SupportsBacktickStrings: true),
        };
    }

    private static readonly IReadOnlySet<string> EmptyKeywords =
        new HashSet<string>(StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> CSharpKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "abstract", "as", "async", "await", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
        "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "get", "goto", "if",
        "implicit", "in", "init", "int", "interface", "internal", "is", "lock", "long", "namespace", "new",
        "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly",
        "record", "ref", "return", "sbyte", "sealed", "set", "short", "sizeof", "stackalloc", "static", "string",
        "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe",
        "ushort", "using", "var", "virtual", "void", "volatile", "while", "yield",
    };

    private static readonly IReadOnlySet<string> CFamilyKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "auto", "bool", "break", "case", "char", "class", "const", "constexpr", "continue", "default", "delete",
        "do", "double", "else", "enum", "extern", "false", "float", "for", "friend", "goto", "if", "inline",
        "int", "long", "namespace", "new", "nullptr", "operator", "private", "protected", "public", "register",
        "return", "short", "signed", "sizeof", "static", "struct", "switch", "template", "this", "throw", "true",
        "try", "typedef", "typename", "union", "unsigned", "using", "virtual", "void", "volatile", "while",
    };

    private static readonly IReadOnlySet<string> JavaKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "abstract", "as", "boolean", "break", "byte", "case", "catch", "char", "class", "const", "continue",
        "data", "def", "default", "do", "double", "else", "enum", "extends", "false", "final", "finally", "float",
        "for", "fun", "if", "implements", "import", "in", "instanceof", "int", "interface", "is", "long", "native",
        "new", "null", "object", "open", "override", "package", "private", "protected", "public", "return",
        "sealed", "short", "static", "super", "switch", "synchronized", "this", "throw", "throws", "transient",
        "true", "try", "val", "var", "void", "volatile", "when", "while",
    };

    private static readonly IReadOnlySet<string> JavaScriptKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "abstract", "any", "as", "async", "await", "boolean", "break", "case", "catch", "class", "const",
        "continue", "debugger", "declare", "default", "delete", "do", "else", "enum", "export", "extends",
        "false", "finally", "for", "from", "function", "get", "if", "implements", "import", "in", "instanceof",
        "interface", "let", "namespace", "new", "null", "number", "of", "private", "protected", "public",
        "readonly", "return", "set", "static", "string", "super", "switch", "this", "throw", "true", "try",
        "type", "typeof", "undefined", "var", "void", "while", "yield",
    };

    private static readonly IReadOnlySet<string> GoKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "break", "case", "chan", "const", "continue", "default", "defer", "else", "fallthrough", "false", "for",
        "func", "go", "goto", "if", "import", "interface", "map", "nil", "package", "range", "return", "select",
        "struct", "switch", "true", "type", "var",
    };

    private static readonly IReadOnlySet<string> RustKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "as", "async", "await", "break", "const", "continue", "crate", "dyn", "else", "enum", "extern", "false",
        "fn", "for", "if", "impl", "in", "let", "loop", "match", "mod", "move", "mut", "pub", "ref", "return",
        "self", "Self", "static", "struct", "super", "trait", "true", "type", "unsafe", "use", "where", "while",
    };

    private static readonly IReadOnlySet<string> PythonKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif", "else",
        "except", "False", "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "None",
        "nonlocal", "not", "or", "pass", "raise", "return", "True", "try", "while", "with", "yield",
    };

    private static readonly IReadOnlySet<string> RubyKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "alias", "and", "begin", "break", "case", "class", "def", "defined?", "do", "else", "elsif", "end",
        "ensure", "false", "for", "if", "in", "module", "next", "nil", "not", "or", "redo", "rescue", "retry",
        "return", "self", "super", "then", "true", "unless", "until", "when", "while", "yield",
    };

    private static readonly IReadOnlySet<string> PhpKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "abstract", "and", "array", "as", "break", "callable", "case", "catch", "class", "clone", "const",
        "continue", "declare", "default", "do", "echo", "else", "elseif", "enum", "extends", "false", "final",
        "finally", "fn", "for", "foreach", "function", "global", "if", "implements", "include", "instanceof",
        "interface", "namespace", "new", "null", "or", "print", "private", "protected", "public", "require",
        "return", "static", "switch", "throw", "trait", "true", "try", "use", "var", "while", "yield",
    };

    private static readonly IReadOnlySet<string> ShellKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "case", "do", "done", "elif", "else", "esac", "exit", "export", "fi", "for", "function", "if", "in",
        "local", "param", "return", "select", "set", "then", "until", "while",
    };

    private static readonly IReadOnlySet<string> SqlKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "add", "all", "alter", "and", "as", "asc", "between", "by", "case", "column", "create", "delete", "desc",
        "distinct", "drop", "else", "end", "exists", "from", "group", "having", "in", "index", "inner", "insert",
        "into", "is", "join", "left", "like", "limit", "not", "null", "on", "or", "order", "outer", "primary",
        "right", "select", "set", "table", "then", "union", "update", "values", "view", "where",
    };

    private static readonly IReadOnlySet<string> JsonKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "true", "false", "null",
    };

    private static readonly IReadOnlySet<string> ConfigKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "true", "false", "null", "yes", "no", "on", "off",
    };

    private static readonly IReadOnlySet<string> CommonKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "and", "as", "async", "await", "break", "case", "class", "const", "continue", "def", "default", "do",
        "else", "enum", "export", "extends", "false", "final", "finally", "for", "from", "func", "function",
        "if", "import", "in", "interface", "is", "let", "namespace", "new", "nil", "not", "null", "or", "package",
        "private", "protected", "public", "return", "static", "struct", "switch", "this", "throw", "true", "try",
        "type", "use", "using", "var", "void", "while", "yield",
    };
}
