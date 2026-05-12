using System.Security.Cryptography;
using System.Text.Json;

namespace NanoAgent.Infrastructure.WindowsSandbox;

internal sealed class WindowsCapabilitySids
{
    public string Workspace { get; set; } = string.Empty;

    public string Readonly { get; set; } = string.Empty;

    public Dictionary<string, string> WorkspaceByCwd { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal static class WindowsCapabilitySidStore
{
    public static WindowsCapabilitySids LoadOrCreate(string nanoAgentHome)
    {
        string path = WindowsSandboxPaths.CapSidFile(nanoAgentHome);
        if (File.Exists(path))
        {
            string text = File.ReadAllText(path).Trim();
            if (text.StartsWith('{') &&
                JsonSerializer.Deserialize(text, WindowsSandboxJsonContext.Default.WindowsCapabilitySids) is { } parsed)
            {
                if (EnsureRequiredSids(parsed))
                {
                    Persist(path, parsed);
                }

                return parsed;
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                WindowsCapabilitySids migrated = new()
                {
                    Workspace = text,
                    Readonly = CreateSidString()
                };
                Persist(path, migrated);
                return migrated;
            }
        }

        WindowsCapabilitySids created = new()
        {
            Workspace = CreateSidString(),
            Readonly = CreateSidString()
        };
        Persist(path, created);
        return created;
    }

    public static string WorkspaceSidForCwd(string nanoAgentHome, string cwd)
    {
        string path = WindowsSandboxPaths.CapSidFile(nanoAgentHome);
        WindowsCapabilitySids sids = LoadOrCreate(nanoAgentHome);
        string key = CanonicalPathKey(cwd);
        if (sids.WorkspaceByCwd.TryGetValue(key, out string? sid))
        {
            return sid;
        }

        sid = CreateSidString();
        sids.WorkspaceByCwd[key] = sid;
        Persist(path, sids);
        return sid;
    }

    public static string CanonicalPathKey(string path)
    {
        string fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        try
        {
            fullPath = Path.GetFullPath(new DirectoryInfo(fullPath).FullName);
        }
        catch (Exception) when (OperatingSystem.IsWindows())
        {
        }

        return OperatingSystem.IsWindows()
            ? fullPath.ToUpperInvariant()
            : fullPath;
    }

    private static bool EnsureRequiredSids(WindowsCapabilitySids sids)
    {
        bool changed = false;
        if (string.IsNullOrWhiteSpace(sids.Workspace))
        {
            sids.Workspace = CreateSidString();
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(sids.Readonly))
        {
            sids.Readonly = CreateSidString();
            changed = true;
        }

        return changed;
    }

    private static string CreateSidString()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        uint a = BitConverter.ToUInt32(bytes[..4]);
        uint b = BitConverter.ToUInt32(bytes.Slice(4, 4));
        uint c = BitConverter.ToUInt32(bytes.Slice(8, 4));
        uint d = BitConverter.ToUInt32(bytes.Slice(12, 4));
        return $"S-1-5-21-{a}-{b}-{c}-{d}";
    }

    private static void Persist(string path, WindowsCapabilitySids sids)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = JsonSerializer.Serialize(sids, WindowsSandboxJsonContext.Default.WindowsCapabilitySids);
        File.WriteAllText(path, json);
    }
}
