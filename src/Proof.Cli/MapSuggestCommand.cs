using System.CommandLine;
using System.Text.Json;
using Proof.Adapters.Distill;
using Proof.Adapters.Git;
using Proof.Core;
using Proof.Engine;

namespace Proof.Cli;

public static class MapSuggestCommand
{
    public static Command Create()
    {
        var suggest = new Command("suggest", "List open P005 symbols with heuristic test-map suggestions (advice only, never evidence).");
        suggest.SetAction(async (parseResult, cancellationToken) => await ExecuteAsync(cancellationToken).ConfigureAwait(false));

        var symbolOption = new Option<string>("--symbol")
        {
            Description = "Map key symbol. TestMapMatching understands dotted names.",
            Required = true
        };
        var testOption = new Option<string[]>("--test")
        {
            Description = "Test fully-qualified name (repeatable).",
            Required = true,
            AllowMultipleArgumentsPerToken = true
        };
        var acceptOption = new Option<bool>("--accept")
        {
            Description = "Write the entry to proof.yml. Without it the command is a dry run.",
            DefaultValueFactory = _ => false
        };
        var configOption = new Option<string?>("--config")
        {
            Description = "Path to proof.yml. Defaults to the workspace configuration.",
            DefaultValueFactory = _ => null
        };
        var add = new Command("add", "Write an explicit test-map entry into policy.testMaps (authoring only; never evidence).")
        {
            symbolOption,
            testOption,
            acceptOption,
            configOption
        };
        add.SetAction((parseResult, cancellationToken) => Task.FromResult(ExecuteAdd(
            parseResult.GetValue(symbolOption)!,
            parseResult.GetValue(testOption) ?? [],
            parseResult.GetValue(acceptOption),
            parseResult.GetValue(configOption))));

        var coverageOption = new Option<string[]>("--coverage")
        {
            Description = "Cobertura XML path (repeatable). Defaults to **/coverage.cobertura.xml in the workspace.",
            AllowMultipleArgumentsPerToken = true
        };
        var certificateOption = new Option<string?>("--certificate")
        {
            Description = "Certificate JSON path. Defaults to the latest .proof/certificates entry.",
            DefaultValueFactory = _ => null
        };
        var writeOption = new Option<bool>("--write")
        {
            Description = "Write --test entries for hit P005 symbols. Requires --test.",
            DefaultValueFactory = _ => false
        };
        var fromCoverageAccept = new Option<bool>("--accept")
        {
            Description = "Write changes. Required with --write; without it the command is a dry run.",
            DefaultValueFactory = _ => false
        };
        var fromCoverageTest = new Option<string[]>("--test")
        {
            Description = "Explicit test FQN (repeatable). Required with --write; cobertura never names the executing test.",
            AllowMultipleArgumentsPerToken = true
        };
        var fromCoverage = new Command("from-coverage", "Match cobertura hits against open P005 symbols and author explicit maps.")
        {
            coverageOption,
            certificateOption,
            writeOption,
            fromCoverageAccept,
            fromCoverageTest
        };
        fromCoverage.SetAction(async (parseResult, cancellationToken) => await ExecuteFromCoverageAsync(
            parseResult.GetValue(coverageOption) ?? [],
            parseResult.GetValue(certificateOption),
            parseResult.GetValue(writeOption),
            parseResult.GetValue(fromCoverageAccept),
            parseResult.GetValue(fromCoverageTest) ?? [],
            cancellationToken).ConfigureAwait(false));

        var map = new Command("map", "Test-map authoring helpers.");
        map.Subcommands.Add(suggest);
        map.Subcommands.Add(add);
        map.Subcommands.Add(fromCoverage);
        return map;
    }

    internal static object BuildResult(IReadOnlyList<MapSuggestion> suggestions)
        => new
        {
            MustReview = true,
            OpenP005 = suggestions.Select(item => new
            {
                item.Symbol.SubjectId,
                item.Symbol.DisplayName,
                item.Symbol.File,
                item.Symbol.Project,
                item.Reasons,
                SuggestedTests = item.Tests
            }),
            YamlSnippet = MapSuggester.ToYamlSnippet(suggestions),
            Warning = "Suggestions are heuristic and must be reviewed before being added to policy.testMaps. They are never evidence."
        };

    internal static async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var workspace = await ProofWorkspace.PlanAsync(baseRevision: null, profile: null, cancellationToken).ConfigureAwait(false);
            var suggestions = MapSuggester.Suggest(workspace.Impact, workspace.Plan);
            Console.WriteLine(JsonSerializer.Serialize(BuildResult(suggestions), ProofJson.WireOptions));
            return 0;
        }
        catch (ProofConfigException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    internal static int ExecuteAdd(string symbol, string[] tests, bool accept, string? configPath)
    {
        try
        {
            var workspaceRoot = Directory.GetCurrentDirectory();
            var proofPath = configPath is not null ? Path.GetFullPath(configPath) : ProofConfig.ResolveConfigPath(workspaceRoot);
            var config = File.Exists(proofPath)
                ? ProofConfig.LoadedFromFile(proofPath, workspaceRoot)
                : new ProofConfig();
            var merged = MergeTestMaps(config.Policy.TestMaps, symbol, tests);
            ValidateMerged(merged);

            if (!accept)
            {
                // Dry run. 병합된 항목(기존 테스트와 새 테스트의 합집합)을
                // 출력하고 파일은 그대로 둔다.
                var entry = merged.FirstOrDefault(item => EntryMatches(item.Symbol, symbol))
                           ?? new TestMapEntrySection { Symbol = symbol, Tests = [.. tests] };
                Console.WriteLine(TestMapYamlEditor.RenderEntry(4, entry.Symbol, entry.Tests));
                return 0;
            }

            var original = File.Exists(proofPath) ? File.ReadAllText(proofPath) : string.Empty;
            string updated;
            try
            {
                updated = TestMapYamlEditor.Upsert(original, symbol, tests);
                File.WriteAllText(proofPath, updated);
            }
            catch (Exception exception) when (exception is not ProofConfigException)
            {
                // 파일은 변환이 성공한 뒤에만 교체된다. 변환이
                // 실패하면 원래 바이트가 디스크에 남는다.
                Console.Error.WriteLine($"Failed to update {proofPath}: {exception.Message}");
                return 2;
            }

            try
            {
                // 실제로 쓴 파일을 다시 파싱한다. 기본 설정 경로이면
                // 워크스페이스 proof.yml이고, --config이면 그
                // 명시적 파일이다. 그래서 깨진 쓰기는 항상 롤백된다.
                ProofConfig.LoadedFromFile(proofPath, workspaceRoot);
                ValidateMerged(merged);
            }
            catch (ProofConfigException exception)
            {
                File.WriteAllText(proofPath, original);
                Console.Error.WriteLine($"proof.yml failed to re-parse; restored original: {exception.Message}");
                return 2;
            }

            Console.WriteLine($"Updated {proofPath}");
            return 0;
        }
        catch (ProofConfigException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    internal static async Task<int> ExecuteFromCoverageAsync(
        string[] coveragePaths,
        string? certificatePath,
        bool write,
        bool accept,
        string[] tests,
        CancellationToken cancellationToken)
    {
        try
        {
            var workspaceRoot = Directory.GetCurrentDirectory();
            var coverageFiles = ResolveCoverageFiles(workspaceRoot, coveragePaths);
            if (coverageFiles.Count == 0)
            {
                Console.Error.WriteLine("No cobertura coverage files found (--coverage or **/coverage.cobertura.xml).");
                return 2;
            }

            var hits = coverageFiles
                .SelectMany(file => CoberturaCoverageParser.Parse(File.ReadAllText(file)))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var (obligations, possibleTestFilters) = await ResolveP005TargetsAsync(certificatePath, cancellationToken).ConfigureAwait(false);
            var matched = new List<(ProofObligation Obligation, string CoverageSymbol)>();
            foreach (var obligation in obligations)
            {
                foreach (var hit in hits)
                {
                    if (TestMapMatching.SymbolMatches(obligation.SubjectId, obligation.Subject?.DisplayName, hit))
                    {
                        matched.Add((obligation, hit));
                        break;
                    }
                }
            }

            if (write)
            {
                if (tests.Length == 0)
                {
                    // Cobertura는 실행된 제품 메서드만 보고한다. 실행한
                    // 테스트 이름은 결코 내지 않으므로, 명시적 --test가 필수다.
                    Console.Error.WriteLine("--write requires --test: cobertura does not name the executing test.");
                    return 2;
                }

                if (matched.Count == 0)
                {
                    Console.Error.WriteLine("No cobertura hit matched an open P005 symbol; nothing to write.");
                    return 0;
                }

                return WriteMapsForHits(matched, tests, accept);
            }

            Console.WriteLine(JsonSerializer.Serialize(BuildFromCoverageAdvice(matched, possibleTestFilters), ProofJson.WireOptions));
            return 0;
        }
        catch (ProofConfigException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    internal static object BuildFromCoverageAdvice(
        IReadOnlyList<(ProofObligation Obligation, string CoverageSymbol)> matched,
        IReadOnlyList<string> possibleTestFilters)
        => new
        {
            MustReview = true,
            Hits = matched.Select(item => new
            {
                SubjectId = item.Obligation.SubjectId,
                DisplayName = item.Obligation.Subject?.DisplayName ?? item.Obligation.SubjectId,
                item.CoverageSymbol
            }),
            MissingTestsReason = "cobertura does not name the executing test",
            SuggestedAddCommands = matched.Select(item =>
                $"proof map add --symbol \"{item.Obligation.Subject?.DisplayName ?? item.Obligation.SubjectId}\" --test <YOU MUST FILL> --accept"),
            PossibleTestFilters = possibleTestFilters
        };

    private static int WriteMapsForHits(
        IReadOnlyList<(ProofObligation Obligation, string CoverageSymbol)> matched,
        string[] tests,
        bool accept)
    {
        var workspaceRoot = Directory.GetCurrentDirectory();
        var proofPath = ProofConfig.ResolveConfigPath(workspaceRoot);
        var config = ProofConfig.Load(workspaceRoot);
        var merged = config.Policy.TestMaps
            .Select(entry => new TestMapEntrySection { Symbol = entry.Symbol, Tests = [.. entry.Tests] })
            .ToList();
        foreach (var (obligation, _) in matched)
        {
            merged = [.. MergeTestMaps(merged, obligation.Subject?.DisplayName ?? obligation.SubjectId, tests)];
        }

        ValidateMerged(merged);

        if (!accept)
        {
            Console.WriteLine("--write is a dry run without --accept; file not changed.");
            return 0;
        }

        var original = File.Exists(proofPath) ? File.ReadAllText(proofPath) : string.Empty;
        try
        {
            var updated = original;
            foreach (var (obligation, _) in matched)
            {
                updated = TestMapYamlEditor.Upsert(updated, obligation.Subject?.DisplayName ?? obligation.SubjectId, tests);
            }

            File.WriteAllText(proofPath, updated);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Failed to update {proofPath}: {exception.Message}");
            return 2;
        }

        try
        {
            ProofConfig.Load(workspaceRoot);
        }
        catch (ProofConfigException exception)
        {
            File.WriteAllText(proofPath, original);
            Console.Error.WriteLine($"proof.yml failed to re-parse; restored original: {exception.Message}");
            return 2;
        }

        Console.WriteLine($"Updated {proofPath} for {matched.Count} symbol(s).");
        return 0;
    }

    private static async Task<(IReadOnlyList<ProofObligation> Obligations, IReadOnlyList<string> PossibleTestFilters)> ResolveP005TargetsAsync(
        string? certificatePath,
        CancellationToken cancellationToken)
    {
        var workspaceRoot = Directory.GetCurrentDirectory();
        var resolved = string.IsNullOrWhiteSpace(certificatePath)
            ? CertificateExplainer.LatestCertificate(workspaceRoot)
            : certificatePath;
        ChangeCertificate? certificate = null;
        if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
        {
            certificate = CertificateExplainer.LoadCertificate(workspaceRoot, resolved, out _);
        }

        if (certificate is not null)
        {
            var filters = certificate.VerificationPlan?.Checks
                .Select(check => check.TestFilter)
                .Where(filter => !string.IsNullOrWhiteSpace(filter))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .OrderBy(filter => filter, StringComparer.Ordinal)
                .ToArray() ?? [];
            return (certificate.Plan.Obligations.Where(item => item.RuleId == "P005").ToArray(), filters);
        }

        var workspace = await ProofWorkspace.PlanAsync(baseRevision: null, profile: null, cancellationToken).ConfigureAwait(false);
        var plannedFilters = workspace.VerificationPlan?.Checks
            .Select(check => check.TestFilter)
            .Where(filter => !string.IsNullOrWhiteSpace(filter))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(filter => filter, StringComparer.Ordinal)
            .ToArray() ?? [];
        return (workspace.Plan.Obligations.Where(item => item.RuleId == "P005").ToArray(), plannedFilters);
    }

    private static void ValidateMerged(IReadOnlyList<TestMapEntrySection> merged)
        => ConfigValidateCommand.ValidateTestMaps(new ProofConfig
        {
            Policy = new PolicySection { TestMaps = [.. merged] }
        });

    private static IReadOnlyList<string> ResolveCoverageFiles(string workspaceRoot, string[] coveragePaths)
    {
        if (coveragePaths is { Length: > 0 })
        {
            return coveragePaths
                .Select(path => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(workspaceRoot, path)))
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        return Directory
            .EnumerateFiles(workspaceRoot, "coverage.cobertura.xml", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(workspaceRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(relative => Path.Combine(workspaceRoot, relative))
            .ToArray();
    }

    internal static IReadOnlyList<TestMapEntrySection> MergeTestMaps(
        IReadOnlyList<TestMapEntrySection> existing,
        string symbol,
        string[] tests)
    {
        var merged = existing
            .Select(entry => new TestMapEntrySection { Symbol = entry.Symbol, Tests = [.. entry.Tests] })
            .ToList();
        var index = merged.FindIndex(entry => EntryMatches(entry.Symbol, symbol));
        if (index < 0)
        {
            merged.Add(new TestMapEntrySection
            {
                Symbol = symbol,
                Tests = [.. tests.Where(test => !string.IsNullOrWhiteSpace(test)).Distinct(StringComparer.Ordinal)]
            });
            return merged;
        }

        foreach (var test in tests)
        {
            if (!merged[index].Tests.Contains(test, StringComparer.Ordinal))
            {
                merged[index].Tests.Add(test);
            }
        }

        return merged;
    }

    private static IReadOnlyList<TestMapEntrySection> AsSections(IReadOnlyList<TestMapEntrySection> entries) => entries;

    private static bool EntryMatches(string existingSymbol, string symbol)
        => string.Equals(existingSymbol, symbol, StringComparison.OrdinalIgnoreCase)
           || TestMapMatching.SymbolMatches(symbol, symbol, existingSymbol);
}

/// <summary>
/// CLI 전용 제안 도우미. 열린 P005 의무에 대해 휴리스틱 테스트 맵 후보를
/// 만든다. 같은 파일의 테스트 심볼, 또는 표시 이름 토큰
/// 일치다. 출력은 policy.testMaps용 작성 조언이다. 엔진이나 평가기가
/// 증거로 결코 소비하지 않는다.
/// </summary>
internal static class MapSuggester
{
    public static IReadOnlyList<MapSuggestion> Suggest(ChangeImpact impact, ProofPlan plan)
    {
        var testSymbols = impact.ImpactedSymbols.Where(symbol => symbol.IsTest).ToArray();
        var suggestions = new List<MapSuggestion>();

        foreach (var obligation in plan.Obligations.Where(item => item.RuleId == "P005"))
        {
            var symbol = new SuggestedSymbol(
                obligation.SubjectId,
                obligation.Subject?.DisplayName ?? obligation.SubjectId,
                obligation.Subject?.Project,
                obligation.Subject?.File);

            var tests = new List<string>();
            var reasons = new List<string>();

            if (!string.IsNullOrWhiteSpace(symbol.File))
            {
                var sameFile = testSymbols.Where(test =>
                    string.Equals(test.File, symbol.File, StringComparison.OrdinalIgnoreCase));
                foreach (var test in sameFile)
                {
                    tests.Add(test.DisplayName);
                    reasons.Add("test symbol in the same file");
                }
            }

            foreach (var token in NameTokens(symbol.DisplayName))
            {
                var matched = testSymbols.Where(test => test.DisplayName.Contains(token, StringComparison.Ordinal));
                foreach (var test in matched)
                {
                    if (!tests.Contains(test.DisplayName))
                    {
                        tests.Add(test.DisplayName);
                        reasons.Add($"display name contains '{token}'");
                    }
                }
            }

            if (tests.Count == 0)
            {
                reasons.Add("no heuristic candidate found; author the map manually");
            }

            suggestions.Add(new MapSuggestion(symbol, reasons.Distinct().ToArray(), tests.Distinct().ToArray()));
        }

        return suggestions;
    }

    public static string ToYamlSnippet(IReadOnlyList<MapSuggestion> suggestions)
    {
        var lines = new List<string> { "policy:", "  testMaps:" };
        foreach (var suggestion in suggestions)
        {
            lines.Add($"    - symbol: \"{suggestion.Symbol.DisplayName}\"");
            if (suggestion.Tests.Count == 0)
            {
                lines.Add("      tests: []");
            }
            else
            {
                lines.Add("      tests:");
                foreach (var test in suggestion.Tests)
                {
                    lines.Add($"        - {test}");
                }
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static IEnumerable<string> NameTokens(string displayName)
    {
        var parts = displayName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            // 짧은 메서드 이름만 있는 키는 의도적으로 일치에서 뺀다
            // (TestMapMatching의 "no short bare names" 규칙을 따른다).
            if (part.Length >= 4)
            {
                yield return part;
            }
        }
    }
}

internal sealed record SuggestedSymbol(
    string SubjectId,
    string DisplayName,
    string? Project,
    string? File);

internal sealed record MapSuggestion(
    SuggestedSymbol Symbol,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Tests);
