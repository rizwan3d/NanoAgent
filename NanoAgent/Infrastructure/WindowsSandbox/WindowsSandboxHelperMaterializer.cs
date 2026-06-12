using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace NanoAgent.Infrastructure.WindowsSandbox;

internal static class WindowsSandboxHelperMaterializer
{
    private const string CliAssemblyFileName = "NanoAgent.CLI.dll";
    private const string CliExecutableFileName = "NanoAgent.CLI.exe";
    private const string RunnerBundlePrefix = "runner-";
    private const string ToolBundlePrefix = "tool-";
    private static readonly Lock SyncRoot = new();
    private static string? s_cachedBundleDirectory;

    public static WindowsSandboxLaunchCommand ResolveSelfLaunchCommand(string nanoAgentHome)
    {
        string processPath = Environment.ProcessPath ??
            throw new InvalidOperationException("Unable to locate the current NanoAgent executable for Windows sandbox runner.");
        string sourceDirectory = AppContext.BaseDirectory;
        WindowsSandboxResolvedLaunchTarget launchTarget = ResolveLaunchTarget(sourceDirectory, processPath);
        string bundleDirectory = EnsureRunnerBundle(
            nanoAgentHome,
            sourceDirectory,
            launchTarget.BundleIdentitySourcePath,
            [GetResourcesDirectory(sourceDirectory)]);
        if (launchTarget.UseBundledExecutable)
        {
            return new WindowsSandboxLaunchCommand(
                Path.Combine(bundleDirectory, launchTarget.BundledFileName),
                string.Empty);
        }

        return new WindowsSandboxLaunchCommand(
            launchTarget.HostFileName,
            WindowsSandboxProcessRunner.QuoteWindowsArgument(
                Path.Combine(bundleDirectory, launchTarget.BundledFileName)) + " ");
    }

    public static void RefreshHelperArtifacts(string nanoAgentHome)
    {
        string processPath = Environment.ProcessPath ??
            throw new InvalidOperationException("Unable to locate the current NanoAgent executable for Windows sandbox runner.");
        string sourceDirectory = AppContext.BaseDirectory;
        WindowsSandboxResolvedLaunchTarget launchTarget = ResolveLaunchTarget(sourceDirectory, processPath);
        string bundleDirectory = EnsureRunnerBundle(
            nanoAgentHome,
            sourceDirectory,
            launchTarget.BundleIdentitySourcePath,
            [GetResourcesDirectory(sourceDirectory)]);
        GrantSandboxReadExecute(bundleDirectory);
    }

    public static string MaterializeExternalExecutable(string nanoAgentHome, string sourceExecutablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nanoAgentHome);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceExecutablePath);

        string fullSourcePath = Path.GetFullPath(sourceExecutablePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException("Unable to find the external executable to materialize.", fullSourcePath);
        }

        string sourceDirectory = Path.GetDirectoryName(fullSourcePath) ??
                                 throw new InvalidOperationException("Unable to resolve the external executable directory.");
        string bundleDirectory = Path.Combine(
            WindowsSandboxPaths.SandboxRuntimeDir(nanoAgentHome),
            "tool-cache",
            ToolBundlePrefix + Path.GetFileNameWithoutExtension(fullSourcePath) + "-" + CreateBundleSuffix(fullSourcePath));

        lock (SyncRoot)
        {
            Directory.CreateDirectory(bundleDirectory);
            foreach (string file in Directory.EnumerateFiles(sourceDirectory))
            {
                CopyFileIfNeeded(file, Path.Combine(bundleDirectory, Path.GetFileName(file)));
            }

            string runtimesDirectory = Path.Combine(sourceDirectory, "runtimes");
            if (Directory.Exists(runtimesDirectory))
            {
                CopyDirectoryRecursive(runtimesDirectory, Path.Combine(bundleDirectory, "runtimes"));
            }

            GrantSandboxReadExecute(bundleDirectory);
            return Path.Combine(bundleDirectory, Path.GetFileName(fullSourcePath));
        }
    }

    internal static string EnsureRunnerBundle(
        string nanoAgentHome,
        string sourceDirectory,
        string bundleIdentitySourcePath,
        IEnumerable<string?> additionalDirectories)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nanoAgentHome);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleIdentitySourcePath);

        WindowsSandboxPaths.EnsureStateDirectories(nanoAgentHome);
        string[] additionalDirectoryList = additionalDirectories
            .Where(static directory => !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory!))
            .Select(static directory => directory!)
            .ToArray();
        string bundleDirectory = Path.Combine(
            WindowsSandboxPaths.SandboxBinDir(nanoAgentHome),
            RunnerBundlePrefix + CreateRunnerBundleSuffix(
                sourceDirectory,
                bundleIdentitySourcePath,
                additionalDirectoryList));

        lock (SyncRoot)
        {
            if (string.Equals(s_cachedBundleDirectory, bundleDirectory, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(bundleDirectory))
            {
                return bundleDirectory;
            }

            Directory.CreateDirectory(bundleDirectory);
            CopyTopLevelArtifacts(sourceDirectory, bundleDirectory);
            foreach (string directory in additionalDirectoryList)
            {
                CopyDirectoryRecursive(directory, Path.Combine(bundleDirectory, Path.GetFileName(directory)));
            }

            GrantSandboxReadExecute(bundleDirectory);
            s_cachedBundleDirectory = bundleDirectory;
            return bundleDirectory;
        }
    }

    internal static string CreateBundleSuffix(string bundleIdentitySourcePath)
    {
        FileInfo info = new(bundleIdentitySourcePath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("Unable to find the Windows sandbox helper source path.", bundleIdentitySourcePath);
        }

        return $"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}";
    }

    private static string CreateRunnerBundleSuffix(
        string sourceDirectory,
        string bundleIdentitySourcePath,
        IReadOnlyList<string> additionalDirectories)
    {
        List<string> paths =
        [
            .. Directory.EnumerateFiles(sourceDirectory)
                .Where(static file =>
                {
                    string extension = Path.GetExtension(file);
                    return string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(extension, ".pdb", StringComparison.OrdinalIgnoreCase);
                })
        ];

        string runtimesDirectory = Path.Combine(sourceDirectory, "runtimes");
        if (Directory.Exists(runtimesDirectory))
        {
            paths.AddRange(Directory.EnumerateFiles(runtimesDirectory, "*", SearchOption.AllDirectories));
        }

        foreach (string directory in additionalDirectories)
        {
            paths.AddRange(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories));
        }

        if (!paths.Contains(bundleIdentitySourcePath, StringComparer.OrdinalIgnoreCase))
        {
            paths.Add(bundleIdentitySourcePath);
        }

        paths.Sort(StringComparer.OrdinalIgnoreCase);

        using SHA256 sha256 = SHA256.Create();
        foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            FileInfo info = new(path);
            if (!info.Exists)
            {
                continue;
            }

            string stamp = string.Join(
                "|",
                Path.GetRelativePath(sourceDirectory, path),
                info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(stamp);
            _ = sha256.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }

        _ = sha256.TransformFinalBlock([], 0, 0);
        string hash = Convert.ToHexString(sha256.Hash!).ToLowerInvariant()[..12];
        return $"{CreateBundleSuffix(bundleIdentitySourcePath)}-{hash}";
    }

    private static WindowsSandboxResolvedLaunchTarget ResolveLaunchTarget(string sourceDirectory, string processPath)
    {
        string bundledCliExecutable = Path.Combine(sourceDirectory, CliExecutableFileName);
        if (File.Exists(bundledCliExecutable))
        {
            return new WindowsSandboxResolvedLaunchTarget(
                UseBundledExecutable: true,
                HostFileName: bundledCliExecutable,
                BundledFileName: CliExecutableFileName,
                BundleIdentitySourcePath: bundledCliExecutable);
        }

        string processName = Path.GetFileNameWithoutExtension(processPath);
        string? preferredBundledDll = ResolvePreferredManagedEntryAssemblyPath(sourceDirectory);
        if (string.Equals(processName, "dotnet", StringComparison.OrdinalIgnoreCase) &&
            preferredBundledDll is not null)
        {
            return new WindowsSandboxResolvedLaunchTarget(
                UseBundledExecutable: false,
                HostFileName: processPath,
                BundledFileName: Path.GetFileName(preferredBundledDll),
                BundleIdentitySourcePath: preferredBundledDll);
        }

        if (!string.Equals(processName, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return new WindowsSandboxResolvedLaunchTarget(
                UseBundledExecutable: true,
                HostFileName: processPath,
                BundledFileName: Path.GetFileName(processPath),
                BundleIdentitySourcePath: processPath);
        }

        throw new InvalidOperationException("Unable to locate the current NanoAgent assembly for Windows sandbox runner.");
    }

    private static string? ResolvePreferredManagedEntryAssemblyPath(string sourceDirectory)
    {
        string preferredCliAssemblyPath = Path.Combine(sourceDirectory, CliAssemblyFileName);
        if (File.Exists(preferredCliAssemblyPath))
        {
            return preferredCliAssemblyPath;
        }

        string? entryAssemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
        if (string.IsNullOrWhiteSpace(entryAssemblyName))
        {
            return null;
        }

        string entryAssemblyPath = Path.Combine(sourceDirectory, entryAssemblyName + ".dll");
        return File.Exists(entryAssemblyPath)
            ? entryAssemblyPath
            : null;
    }

    private static string? GetResourcesDirectory(string sourceDirectory)
    {
        string candidate = Path.Combine(sourceDirectory, "codex-resources");
        return Directory.Exists(candidate) ? candidate : null;
    }

    private static void CopyTopLevelArtifacts(string sourceDirectory, string bundleDirectory)
    {
        foreach (string file in Directory.EnumerateFiles(sourceDirectory))
        {
            string extension = Path.GetExtension(file);
            if (!string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".pdb", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            CopyFileIfNeeded(file, Path.Combine(bundleDirectory, Path.GetFileName(file)));
        }

        string runtimesDirectory = Path.Combine(sourceDirectory, "runtimes");
        if (Directory.Exists(runtimesDirectory))
        {
            CopyDirectoryRecursive(runtimesDirectory, Path.Combine(bundleDirectory, "runtimes"));
        }
    }

    private static void CopyDirectoryRecursive(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (string file in Directory.EnumerateFiles(sourceDirectory))
        {
            CopyFileIfNeeded(file, Path.Combine(destinationDirectory, Path.GetFileName(file)));
        }

        foreach (string childDirectory in Directory.EnumerateDirectories(sourceDirectory))
        {
            CopyDirectoryRecursive(childDirectory, Path.Combine(destinationDirectory, Path.GetFileName(childDirectory)));
        }
    }

    private static void CopyFileIfNeeded(string sourcePath, string destinationPath)
    {
        FileInfo sourceInfo = new(sourcePath);
        FileInfo destinationInfo = new(destinationPath);
        if (destinationInfo.Exists &&
            destinationInfo.Length == sourceInfo.Length &&
            destinationInfo.LastWriteTimeUtc == sourceInfo.LastWriteTimeUtc)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, overwrite: true);
        File.SetLastWriteTimeUtc(destinationPath, sourceInfo.LastWriteTimeUtc);
    }

    private static void GrantSandboxReadExecute(string bundleDirectory)
    {
        FileSystemRights directoryRights = FileSystemRights.ReadAndExecute | FileSystemRights.ListDirectory;
        FileSystemRights fileRights = FileSystemRights.ReadAndExecute;
        foreach (string sid in EnumerateSandboxSubjectSids())
        {
            WindowsSandboxAcl.AddAllowAce(bundleDirectory, sid, directoryRights);
            foreach (string directory in Directory.EnumerateDirectories(bundleDirectory, "*", SearchOption.AllDirectories))
            {
                WindowsSandboxAcl.AddAllowAce(directory, sid, directoryRights);
            }

            foreach (string file in Directory.EnumerateFiles(bundleDirectory, "*", SearchOption.AllDirectories))
            {
                WindowsSandboxAcl.AddAllowAce(file, sid, fileRights);
            }
        }
    }

    private static IEnumerable<string> EnumerateSandboxSubjectSids()
    {
        foreach (string groupName in WindowsSandboxPaths.SandboxGroupNames())
        {
            yield return ResolveSid(groupName);
        }

        yield return ResolveSid(WindowsSandboxPaths.OfflineUsername);
        yield return ResolveSid(WindowsSandboxPaths.OnlineUsername);
    }

    private static string ResolveSid(string accountName)
    {
        NTAccount account = new(Environment.MachineName, accountName);
        return ((SecurityIdentifier)account.Translate(typeof(SecurityIdentifier))).Value;
    }
}

internal sealed record WindowsSandboxLaunchCommand(
    string FileName,
    string ParametersPrefix);

internal sealed record WindowsSandboxResolvedLaunchTarget(
    bool UseBundledExecutable,
    string HostFileName,
    string BundledFileName,
    string BundleIdentitySourcePath);
