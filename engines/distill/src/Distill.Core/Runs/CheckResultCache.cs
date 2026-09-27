using System.Diagnostics;

using System.Reflection;

using System.Security.Cryptography;

using System.Text;

using System.Text.Json;

using Distill.Core.Diagnostics;

using Distill.Core.Planning;



namespace Distill.Core.Runs;



public static class CheckResultCache

{

    private const int CurrentSchemaVersion = 2;



    private static readonly JsonSerializerOptions SerializerOptions = new()

    {

        WriteIndented = true,

        PropertyNamingPolicy = JsonNamingPolicy.CamelCase

    };



    public static bool TryRestore(

        PlannedCheck check,

        DistillRunContext context,

        out CheckRunResult? result)

    {

        result = null;

        if (!IsCacheEligible(check) || !IsGitWorkspaceClean(context.WorkspaceRoot))

        {

            return false;

        }



        try

        {

            var cacheKey = ComputeCacheKey(check, context.WorkspaceRoot);

            var entryDirectory = RunArtifactLayout.GetCacheEntryDirectory(context.WorkspaceRoot, cacheKey);

            var metadataPath = Path.Combine(entryDirectory, "metadata.json");

            if (!File.Exists(metadataPath))

            {

                return false;

            }



            var metadata = JsonSerializer.Deserialize<CacheMetadata>(

                File.ReadAllText(metadataPath),

                SerializerOptions);

            if (metadata is null

                || metadata.SchemaVersion != CurrentSchemaVersion

                || metadata.Status != VerificationStatus.Pass.ToString()

                || !string.Equals(metadata.CacheKey, cacheKey, StringComparison.Ordinal)

                || !string.Equals(metadata.CheckId, check.Id, StringComparison.OrdinalIgnoreCase)

                || !string.Equals(metadata.Kind, check.Definition.Kind, StringComparison.OrdinalIgnoreCase))

            {

                return false;

            }



            if (!TryReconstructResult(check, metadata, out var reconstructed))

            {

                return false;

            }



            var targetDirectory = RunArtifactLayout.GetCheckDirectory(context.RunDirectory, check.Id);

            CopyCachedArtifacts(entryDirectory, targetDirectory, metadataPath);

            result = reconstructed! with

            {

                ArtifactPointer = RemapArtifactPointer(metadata.ArtifactPointer, targetDirectory)

            };



            return true;

        }

        catch (IOException)

        {

            return false;

        }

        catch (UnauthorizedAccessException)

        {

            return false;

        }

        catch (JsonException)

        {

            return false;

        }

    }



    public static void TryStore(PlannedCheck check, DistillRunContext context, CheckRunResult result)

    {

        if (result.Status != VerificationStatus.Pass

            || !IsCacheEligible(check)

            || !IsGitWorkspaceClean(context.WorkspaceRoot))

        {

            return;

        }



        try

        {

            var cacheKey = ComputeCacheKey(check, context.WorkspaceRoot);

            var entryDirectory = RunArtifactLayout.GetCacheEntryDirectory(context.WorkspaceRoot, cacheKey);

            var tempDirectory = entryDirectory + "." + Guid.NewGuid().ToString("N") + ".tmp";

            if (Directory.Exists(tempDirectory))

            {

                Directory.Delete(tempDirectory, recursive: true);

            }



            Directory.CreateDirectory(tempDirectory);

            var sourceDirectory = RunArtifactLayout.GetCheckDirectory(context.RunDirectory, check.Id);

            if (Directory.Exists(sourceDirectory))

            {

                CopyDirectory(sourceDirectory, tempDirectory);

            }



            var metadata = new CacheMetadata(

                CurrentSchemaVersion,

                cacheKey,

                check.Id,

                check.Definition.Kind,

                result.Status.ToString(),

                result.ExitCode,

                result.SourceId,

                ComputeRelativeArtifactPointer(result.ArtifactPointer, sourceDirectory),

                (int)result.Duration.TotalMilliseconds,

                result.Diagnostics.Select(DiagnosticCacheEntry.FromDiagnostic).ToList());



            File.WriteAllText(

                Path.Combine(tempDirectory, "metadata.json"),

                JsonSerializer.Serialize(metadata, SerializerOptions));



            try

            {

                Directory.Move(tempDirectory, entryDirectory);

            }

            catch (IOException) when (Directory.Exists(entryDirectory))

            {

                // 다른 작성자가 이미 이 키의 유효한 항목을 게시했다.
                // 기존 대상을 유지하고 이번 시도의 임시 디렉터리는 버린다.

                Directory.Delete(tempDirectory, recursive: true);

            }

        }

        catch (IOException)

        {

        }

        catch (UnauthorizedAccessException)

        {

        }

    }



    internal static async Task<CheckRunResult?> TryRestoreAsync(

        PlannedCheck check,

        DistillRunContext context,

        CancellationToken cancellationToken)

    {

        if (!IsCacheEligible(check)

            || !await IsGitWorkspaceCleanAsync(context.WorkspaceRoot, cancellationToken).ConfigureAwait(false))

        {

            return null;

        }



        try

        {

            var gitHead = await TryGetGitHeadAsync(context.WorkspaceRoot, cancellationToken).ConfigureAwait(false);

            var sdkVersion = await TryGetDotnetSdkVersionAsync(cancellationToken).ConfigureAwait(false);

            var cacheKey = ComputeCacheKey(check, gitHead, sdkVersion);

            var entryDirectory = RunArtifactLayout.GetCacheEntryDirectory(context.WorkspaceRoot, cacheKey);

            var metadataPath = Path.Combine(entryDirectory, "metadata.json");

            if (!File.Exists(metadataPath))

            {

                return null;

            }



            var metadata = JsonSerializer.Deserialize<CacheMetadata>(

                File.ReadAllText(metadataPath),

                SerializerOptions);

            if (metadata is null

                || metadata.SchemaVersion != CurrentSchemaVersion

                || metadata.Status != VerificationStatus.Pass.ToString()

                || !string.Equals(metadata.CacheKey, cacheKey, StringComparison.Ordinal)

                || !string.Equals(metadata.CheckId, check.Id, StringComparison.OrdinalIgnoreCase)

                || !string.Equals(metadata.Kind, check.Definition.Kind, StringComparison.OrdinalIgnoreCase))

            {

                return null;

            }



            if (!TryReconstructResult(check, metadata, out var reconstructed))

            {

                return null;

            }



            var targetDirectory = RunArtifactLayout.GetCheckDirectory(context.RunDirectory, check.Id);

            CopyCachedArtifacts(entryDirectory, targetDirectory, metadataPath);

            return reconstructed! with

            {

                ArtifactPointer = RemapArtifactPointer(metadata.ArtifactPointer, targetDirectory)

            };

        }

        catch (IOException)

        {

            return null;

        }

        catch (UnauthorizedAccessException)

        {

            return null;

        }

        catch (JsonException)

        {

            return null;

        }

    }



    internal static async Task TryStoreAsync(

        PlannedCheck check,

        DistillRunContext context,

        CheckRunResult result,

        CancellationToken cancellationToken)

    {

        if (result.Status != VerificationStatus.Pass

            || !IsCacheEligible(check)

            || !await IsGitWorkspaceCleanAsync(context.WorkspaceRoot, cancellationToken).ConfigureAwait(false))

        {

            return;

        }



        try

        {

            var gitHead = await TryGetGitHeadAsync(context.WorkspaceRoot, cancellationToken).ConfigureAwait(false);

            var sdkVersion = await TryGetDotnetSdkVersionAsync(cancellationToken).ConfigureAwait(false);

            var cacheKey = ComputeCacheKey(check, gitHead, sdkVersion);

            var entryDirectory = RunArtifactLayout.GetCacheEntryDirectory(context.WorkspaceRoot, cacheKey);

            var tempDirectory = entryDirectory + "." + Guid.NewGuid().ToString("N") + ".tmp";

            if (Directory.Exists(tempDirectory))

            {

                Directory.Delete(tempDirectory, recursive: true);

            }



            Directory.CreateDirectory(tempDirectory);

            var sourceDirectory = RunArtifactLayout.GetCheckDirectory(context.RunDirectory, check.Id);

            if (Directory.Exists(sourceDirectory))

            {

                CopyDirectory(sourceDirectory, tempDirectory);

            }



            var metadata = new CacheMetadata(

                CurrentSchemaVersion,

                cacheKey,

                check.Id,

                check.Definition.Kind,

                result.Status.ToString(),

                result.ExitCode,

                result.SourceId,

                ComputeRelativeArtifactPointer(result.ArtifactPointer, sourceDirectory),

                (int)result.Duration.TotalMilliseconds,

                result.Diagnostics.Select(DiagnosticCacheEntry.FromDiagnostic).ToList());



            File.WriteAllText(

                Path.Combine(tempDirectory, "metadata.json"),

                JsonSerializer.Serialize(metadata, SerializerOptions));



            try

            {

                Directory.Move(tempDirectory, entryDirectory);

            }

            catch (IOException) when (Directory.Exists(entryDirectory))

            {

                Directory.Delete(tempDirectory, recursive: true);

            }

        }

        catch (IOException)

        {

        }

        catch (UnauthorizedAccessException)

        {

        }

    }



    internal static string ComputeCacheKey(PlannedCheck check, string workspaceRoot)

    {

        return ComputeCacheKey(check, TryGetGitHead(workspaceRoot), TryGetDotnetSdkVersion());

    }



    internal static string ComputeCacheKey(PlannedCheck check, string gitHead, string sdkVersion)

    {

        var builder = new StringBuilder();

        builder.Append(check.Id).Append('\n');

        builder.Append(check.Definition.Kind).Append('\n');

        builder.Append(check.Definition.Command).Append('\n');

        builder.Append(check.Definition.Source).Append('\n');

        builder.Append(check.Definition.Timeout).Append('\n');

        builder.Append(string.Join(',', check.Definition.DependsOn)).Append('\n');

        builder.Append(string.Join(',', check.DependsOn)).Append('\n');

        builder.Append(gitHead).Append('\n');

        builder.Append(sdkVersion).Append('\n');

        builder.Append(Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown").Append('\n');

        builder.Append(System.Runtime.InteropServices.RuntimeInformation.OSDescription).Append('\n');

        builder.Append(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture).Append('\n');

        builder.Append(System.Environment.Version).Append('\n');



        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));

        return Convert.ToHexString(hash).ToLowerInvariant();

    }



    private static bool IsCacheEligible(PlannedCheck check)

    {

        if (string.Equals(check.Definition.Source, "mtp", StringComparison.OrdinalIgnoreCase)

            || string.Equals(check.Definition.Source, "mtp-report", StringComparison.OrdinalIgnoreCase))

        {

            return false;

        }



        if (string.Equals(check.Definition.Kind, "test", StringComparison.OrdinalIgnoreCase)

            && string.Equals(check.Definition.Source, "auto", StringComparison.OrdinalIgnoreCase))

        {

            return false;

        }



        return true;

    }



    private static bool TryReconstructResult(

        PlannedCheck check,

        CacheMetadata metadata,

        out CheckRunResult? result)

    {

        result = null;

        if (!Enum.TryParse<VerificationStatus>(metadata.Status, out var status))

        {

            return false;

        }



        var diagnostics = new List<DistillDiagnostic>();

        foreach (var entry in metadata.Diagnostics ?? [])

        {

            if (!entry.TryToDiagnostic(check.Id, out var diagnostic))

            {

                return false;

            }



            diagnostics.Add(diagnostic);

        }



        result = new CheckRunResult(

            check.Id,

            metadata.Kind,

            status,

            metadata.ExitCode,

            diagnostics,

            metadata.SourceId,

            metadata.ArtifactPointer,

            TimeSpan.FromMilliseconds(metadata.DurationMs));

        return true;

    }



    private static string? ComputeRelativeArtifactPointer(string? artifactPointer, string sourceDirectory)

    {

        if (string.IsNullOrWhiteSpace(artifactPointer))

        {

            return artifactPointer;

        }



        if (string.Equals(
                Path.GetFullPath(artifactPointer).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }



        var fullPointer = Path.GetFullPath(artifactPointer);

        var fullSource = Path.GetFullPath(sourceDirectory);

        if (!fullPointer.StartsWith(fullSource + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))

        {

            // 포인터는 검사 디렉터리 밖에 있다. 상대 경로는 의미가 없다.

            return null;

        }



        return Path.GetRelativePath(fullSource, fullPointer);

    }



    private static string RemapArtifactPointer(string? relativeArtifactPointer, string targetDirectory)

    {

        if (relativeArtifactPointer is null)

        {

            return targetDirectory;

        }



        if (relativeArtifactPointer.Length == 0)

        {

            return targetDirectory;

        }



        return Path.Combine(targetDirectory, relativeArtifactPointer);

    }



    private static void CopyCachedArtifacts(string entryDirectory, string targetDirectory, string metadataPath)

    {

        var stagingDirectory = targetDirectory + "." + Guid.NewGuid().ToString("N") + ".restage";

        if (Directory.Exists(stagingDirectory))

        {

            Directory.Delete(stagingDirectory, recursive: true);

        }



        try

        {

            CopyDirectoryRecursive(entryDirectory, stagingDirectory, metadataPath);



            if (Directory.Exists(targetDirectory))

            {

                Directory.Delete(targetDirectory, recursive: true);

            }



            Directory.Move(stagingDirectory, targetDirectory);

        }

        catch

        {

            if (Directory.Exists(stagingDirectory))

            {

                Directory.Delete(stagingDirectory, recursive: true);

            }



            throw;

        }

    }



    private static void CopyDirectory(string sourceDirectory, string targetDirectory)

        => CopyDirectoryRecursive(sourceDirectory, targetDirectory, excludePath: null);



    private static void CopyDirectoryRecursive(string sourceDirectory, string targetDirectory, string? excludePath)

    {

        Directory.CreateDirectory(targetDirectory);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))

        {

            if (excludePath is not null && string.Equals(file, excludePath, StringComparison.OrdinalIgnoreCase))

            {

                continue;

            }



            File.Copy(file, Path.Combine(targetDirectory, Path.GetFileName(file)), overwrite: true);

        }



        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))

        {

            CopyDirectoryRecursive(

                directory,

                Path.Combine(targetDirectory, Path.GetFileName(directory)),

                excludePath);

        }

    }



    private static bool IsGitWorkspaceClean(string workspaceRoot)

    {

        try

        {

            using var process = Process.Start(new ProcessStartInfo

            {

                FileName = "git",

                Arguments = "status --porcelain",

                WorkingDirectory = workspaceRoot,

                RedirectStandardOutput = true,

                RedirectStandardError = true,

                UseShellExecute = false,

                CreateNoWindow = true

            });



            if (process is null)

            {

                return false;

            }



            var output = process.StandardOutput.ReadToEnd();

            process.WaitForExit();

            return process.ExitCode == 0 && string.IsNullOrWhiteSpace(output);

        }

        catch

        {

            return false;

        }

    }



    private static string TryGetGitHead(string workspaceRoot)

    {

        try

        {

            using var process = Process.Start(new ProcessStartInfo

            {

                FileName = "git",

                Arguments = "rev-parse HEAD",

                WorkingDirectory = workspaceRoot,

                RedirectStandardOutput = true,

                RedirectStandardError = true,

                UseShellExecute = false,

                CreateNoWindow = true

            });



            if (process is null)

            {

                return string.Empty;

            }



            var output = process.StandardOutput.ReadToEnd().Trim();

            process.WaitForExit();

            return process.ExitCode == 0 ? output : string.Empty;

        }

        catch

        {

            return string.Empty;

        }

    }



    private static string TryGetDotnetSdkVersion()

    {

        try

        {

            using var process = Process.Start(new ProcessStartInfo

            {

                FileName = "dotnet",

                Arguments = "--version",

                RedirectStandardOutput = true,

                RedirectStandardError = true,

                UseShellExecute = false,

                CreateNoWindow = true

            });



            if (process is null)

            {

                return string.Empty;

            }



            var output = process.StandardOutput.ReadToEnd().Trim();

            process.WaitForExit();

            return process.ExitCode == 0 ? output : string.Empty;

        }

        catch

        {

            return string.Empty;

        }

    }



    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);



    private static async Task<bool> IsGitWorkspaceCleanAsync(string workspaceRoot, CancellationToken cancellationToken)

    {

        var probe = await RunProbeAsync(

            "git",

            ["status", "--porcelain"],

            workspaceRoot,

            cancellationToken).ConfigureAwait(false);

        return probe.Succeeded && probe.ExitCode == 0 && string.IsNullOrWhiteSpace(probe.Output);

    }



    private static async Task<string> TryGetGitHeadAsync(string workspaceRoot, CancellationToken cancellationToken)

    {

        var probe = await RunProbeAsync(

            "git",

            ["rev-parse", "HEAD"],

            workspaceRoot,

            cancellationToken).ConfigureAwait(false);

        return probe.Succeeded && probe.ExitCode == 0 ? probe.Output.Trim() : string.Empty;

    }



    private static async Task<string> TryGetDotnetSdkVersionAsync(CancellationToken cancellationToken)

    {

        var probe = await RunProbeAsync(

            "dotnet",

            ["--version"],

            workingDirectory: null,

            cancellationToken).ConfigureAwait(false);

        return probe.Succeeded && probe.ExitCode == 0 ? probe.Output.Trim() : string.Empty;

    }



    private sealed record ProbeResult(bool Succeeded, int? ExitCode, string Output);



    private static async Task<ProbeResult> RunProbeAsync(

        string fileName,

        IReadOnlyList<string> arguments,

        string? workingDirectory,

        CancellationToken cancellationToken)

    {

        cancellationToken.ThrowIfCancellationRequested();



        using var process = new Process

        {

            StartInfo = new ProcessStartInfo

            {

                FileName = fileName,

                UseShellExecute = false,

                RedirectStandardOutput = true,

                RedirectStandardError = true,

                CreateNoWindow = true

            },

            EnableRaisingEvents = true

        };



        if (!string.IsNullOrEmpty(workingDirectory))

        {

            process.StartInfo.WorkingDirectory = workingDirectory;

        }



        foreach (var argument in arguments)

        {

            process.StartInfo.ArgumentList.Add(argument);

        }



        try

        {

            if (!process.Start())

            {

                return new ProbeResult(false, null, string.Empty);

            }

        }

        catch

        {

            return new ProbeResult(false, null, string.Empty);

        }



        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);



        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        linkedCts.CancelAfter(ProbeTimeout);



        try

        {

            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);

            await process.WaitForExitAsync().ConfigureAwait(false);

            var output = await stdoutTask.ConfigureAwait(false);

            _ = await stderrTask.ConfigureAwait(false);

            return new ProbeResult(true, process.ExitCode, output);

        }

        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)

        {

            KillProcessTree(process);

            throw;

        }

        catch (OperationCanceledException)

        {

            KillProcessTree(process);

            return new ProbeResult(false, null, string.Empty);

        }

    }



    private static void KillProcessTree(Process process)

    {

        try

        {

            if (!process.HasExited)

            {

                process.Kill(entireProcessTree: true);

            }

        }

        catch

        {

        }

    }



    private sealed record CacheMetadata(

        int SchemaVersion,

        string CacheKey,

        string CheckId,

        string Kind,

        string Status,

        int? ExitCode,

        string? SourceId,

        string? ArtifactPointer,

        int DurationMs,

        IReadOnlyList<DiagnosticCacheEntry>? Diagnostics);



    private sealed record ExceptionCacheEntry(string Type, string Message)

    {

        public static ExceptionCacheEntry? FromException(ExceptionEvidence? exception)

            => exception is null ? null : new ExceptionCacheEntry(exception.Type, exception.Message);



        public ExceptionEvidence? ToException()

            => new(Type, Message);

    }



    private sealed record StackFrameCacheEntry(string? File, int Line, string? Method, bool IsFramework)

    {

        public static StackFrameCacheEntry FromFrame(StackFrameEvidence frame)

            => new(frame.File, frame.Line, frame.Method, frame.IsFramework);



        public StackFrameEvidence ToFrame()

            => new(File, Line, Method, IsFramework);

    }



    private sealed record DiagnosticCacheEntry(

        string Id,

        string Kind,

        string Severity,

        string Source,

        string Code,

        string Message,

        string? TestName,

        string? File,

        int? Line,

        int? Column,

        string? Project,

        ExceptionCacheEntry? Exception,

        IReadOnlyList<StackFrameCacheEntry>? Frames,

        Dictionary<string, string>? Properties,

        string Provenance,

        double Confidence)

    {

        public static DiagnosticCacheEntry FromDiagnostic(DistillDiagnostic diagnostic)

            => new(

                diagnostic.Id,

                diagnostic.Kind.ToString(),

                diagnostic.Severity.ToString(),

                diagnostic.Source,

                diagnostic.Code ?? string.Empty,

                diagnostic.Message,

                diagnostic.TestName,

                diagnostic.Location?.File,

                diagnostic.Location?.Line,

                diagnostic.Location?.Column,

                diagnostic.Project,

                ExceptionCacheEntry.FromException(diagnostic.Exception),

                diagnostic.Frames.Select(StackFrameCacheEntry.FromFrame).ToList(),

                diagnostic.Properties?.ToDictionary(

                    pair => pair.Key,

                    pair => pair.Value,

                    StringComparer.Ordinal),

                diagnostic.Provenance.ToString(),

                diagnostic.Confidence);



        public bool TryToDiagnostic(string checkId, out DistillDiagnostic diagnostic)

        {

            diagnostic = null!;

            if (!Enum.TryParse<DiagnosticKind>(Kind, out var kind)

                || !Enum.TryParse<DiagnosticSeverity>(Severity, out var severity)

                || !Enum.TryParse<DiagnosticProvenance>(Provenance, out var provenance))

            {

                return false;

            }



            diagnostic = DistillDiagnostic.Create(

                id: Id,

                kind: kind,

                severity: severity,

                source: Source,

                code: Code,

                message: Message,

                location: File is null ? null : new SourceLocation(File, Line ?? 0, Column ?? 0),

                project: Project,

                testName: TestName,

                exception: Exception?.ToException(),

                frames: Frames?.Select(frame => frame.ToFrame()).ToList() ?? [],

                provenance: provenance,

                confidence: Confidence,

                properties: Properties);

            return true;

        }

    }

}


