using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NanoAgent.VS.ToolWindows
{
    public enum DiffLineType { Add, Del, Ctx, Meta }

    public sealed record DiffLine(DiffLineType Type, string Text);

    public sealed class FileDiff
    {
        public string Path { get; init; } = string.Empty;
        public List<DiffLine> Lines { get; } = new();

        public int Added
        {
            get { int n = 0; foreach (var l in Lines) if (l.Type == DiffLineType.Add) n++; return n; }
        }

        public int Removed
        {
            get { int n = 0; foreach (var l in Lines) if (l.Type == DiffLineType.Del) n++; return n; }
        }

        /// <summary>Reconstructs a unified-diff-ish text for the copy button.</summary>
        public string ToClipboardText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("--- " + Path);
            foreach (DiffLine line in Lines)
            {
                char prefix = line.Type switch
                {
                    DiffLineType.Add => '+',
                    DiffLineType.Del => '-',
                    DiffLineType.Meta => ' ',
                    _ => ' '
                };
                sb.AppendLine(line.Type == DiffLineType.Meta ? line.Text : prefix + line.Text);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Parses a NanoAgent tool-call's raw input into renderable file diffs.
    /// C# port of the VS Code extension's diffModel.ts.
    /// ponytail: naive per-line classifier, no intra-line word diff. Add LCS only if asked.
    /// </summary>
    public static class DiffModel
    {
        private static readonly Regex FileHeader = new(@"^\*\*\* (Add|Update|Delete) File: (.+)$", RegexOptions.Compiled);
        private static readonly Regex BeginEnd = new(@"^\*\*\* (Begin|End) Patch", RegexOptions.Compiled);

        /// <summary>Returns parsed diffs, or null if the input is not an edit/patch.</summary>
        public static List<FileDiff>? Build(string? kind, string? title, string? rawInputJson)
        {
            if (string.IsNullOrWhiteSpace(rawInputJson))
            {
                return null;
            }

            string raw = rawInputJson!;
            JsonElement obj = default;
            bool isObject = false;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    obj = doc.RootElement.Clone();
                    isObject = true;
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.String)
                {
                    raw = doc.RootElement.GetString() ?? raw;
                }
            }
            catch (JsonException) { /* treat as plain text below */ }

            // 1. apply_patch: a "*** Begin Patch" block.
            string? patch = null;
            if (raw.Contains("*** "))
            {
                patch = raw;
            }
            else if (isObject)
            {
                foreach (string key in new[] { "patch", "diff", "patch_text", "patchText" })
                {
                    string? v = GetString(obj, key);
                    if (v != null && v.Contains("*** ")) { patch = v; break; }
                }
                // also accept a raw unified diff string in patch/diff fields
                if (patch == null)
                {
                    foreach (string key in new[] { "patch", "diff" })
                    {
                        string? v = GetString(obj, key);
                        if (v != null && (v.Contains("@@") || v.StartsWith("--- "))) { patch = v; break; }
                    }
                }
            }

            if (patch != null)
            {
                List<FileDiff> filesFromPatch = ParsePatch(patch);
                if (filesFromPatch.Count > 0) return filesFromPatch;
            }

            // 2. file_write style: { path, content } — show whole file as added.
            bool isEdit = kind == "edit"
                || Regex.IsMatch(title ?? string.Empty, @"apply_?patch|file_?write|\bwrite\b|\bedit\b", RegexOptions.IgnoreCase);
            if (isEdit && isObject)
            {
                string? path = FirstString(obj, "path", "file_path", "filePath", "file");
                string? content = FirstString(obj, "content", "text", "new_text", "newText", "contents");
                if (path != null && content != null)
                {
                    var fd = new FileDiff { Path = path };
                    foreach (string line in content.Replace("\r\n", "\n").Split('\n'))
                    {
                        fd.Lines.Add(new DiffLine(DiffLineType.Add, line));
                    }
                    return new List<FileDiff> { fd };
                }
            }

            return null;
        }

        /// <summary>Parses raw unified/apply-patch diff text (e.g. from a ```diff fenced block).</summary>
        public static List<FileDiff> ParseDiffText(string diffText) => ParsePatch(diffText ?? string.Empty);

        private static List<FileDiff> ParsePatch(string patch)
        {
            var files = new List<FileDiff>();
            FileDiff? current = null;

            foreach (string raw in patch.Replace("\r\n", "\n").Split('\n'))
            {
                if (BeginEnd.IsMatch(raw)) continue;

                Match header = FileHeader.Match(raw);
                if (header.Success)
                {
                    current = new FileDiff { Path = header.Groups[2].Value.Trim() };
                    files.Add(current);
                    continue;
                }

                // Standard unified diff headers (git diff / ---/+++ / diff --git).
                if (raw.StartsWith("diff --git") || raw.StartsWith("--- ") || raw.StartsWith("+++ "))
                {
                    if (raw.StartsWith("+++ "))
                    {
                        string p = raw.Substring(4).Trim();
                        if (p.StartsWith("b/")) p = p.Substring(2);
                        current = new FileDiff { Path = p };
                        files.Add(current);
                    }
                    continue;
                }

                if (current == null)
                {
                    if (raw.StartsWith("@@"))
                    {
                        // diff without a file header; create an anonymous file
                        current = new FileDiff { Path = "(diff)" };
                        files.Add(current);
                    }
                    else continue;
                }

                if (raw.StartsWith("@@"))
                    current!.Lines.Add(new DiffLine(DiffLineType.Meta, raw));
                else if (raw.StartsWith("+"))
                    current!.Lines.Add(new DiffLine(DiffLineType.Add, raw.Substring(1)));
                else if (raw.StartsWith("-"))
                    current!.Lines.Add(new DiffLine(DiffLineType.Del, raw.Substring(1)));
                else
                    current!.Lines.Add(new DiffLine(DiffLineType.Ctx, raw.StartsWith(" ") ? raw.Substring(1) : raw));
            }

            return files;
        }

        private static string? GetString(JsonElement obj, string key)
            => obj.ValueKind == JsonValueKind.Object
               && obj.TryGetProperty(key, out JsonElement v)
               && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

        private static string? FirstString(JsonElement obj, params string[] keys)
        {
            foreach (string key in keys)
            {
                string? v = GetString(obj, key);
                if (v != null) return v;
            }
            return null;
        }
    }
}
