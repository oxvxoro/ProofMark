using System.Text.RegularExpressions;
using Distill.Core.Diagnostics;

namespace Distill.Core.Redaction;

public sealed class SecretRedactor
{
    private static readonly string[] DefaultKeywords =
    [
        "password",
        "passwd",
        "token",
        "apikey",
        "api_key",
        "authorization",
        "bearer",
        "connectionstring",
        "secret"
    ];

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private readonly IReadOnlyList<Regex> _keyPatterns;
    private readonly IReadOnlyList<Regex> _customPatterns;
    private static readonly Regex BearerPattern = new(
        @"\bBearer\s+\S+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        MatchTimeout);

    public SecretRedactor(IEnumerable<string>? customPatterns = null)
    {
        _keyPatterns = DefaultKeywords
            .Select(keyword => new Regex(
                $@"(?<key>\b{Regex.Escape(keyword)}\b)(?<separator>\s*[:=]\s*|\s+)(?<value>""[^""]*""|'[^']*'|[^\s,;]+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                MatchTimeout))
            .ToList();

        _customPatterns = (customPatterns ?? Array.Empty<string>())
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(pattern => new Regex(
                pattern,
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                MatchTimeout))
            .ToList();
    }

    public string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var redacted = TryReplace(BearerPattern, value, "Bearer ***");
        foreach (var pattern in _keyPatterns)
        {
            redacted = TryReplace(
                pattern,
                redacted,
                match => $"{match.Groups["key"].Value}{match.Groups["separator"].Value}***");
        }

        foreach (var pattern in _customPatterns)
        {
            redacted = TryReplace(pattern, redacted, "***");
        }

        return redacted;
    }

    private static string TryReplace(Regex pattern, string input, string replacement)
    {
        try
        {
            return pattern.Replace(input, replacement);
        }
        catch (RegexMatchTimeoutException)
        {
            return "***";
        }
    }

    private static string TryReplace(Regex pattern, string input, MatchEvaluator evaluator)
    {
        try
        {
            return pattern.Replace(input, evaluator);
        }
        catch (RegexMatchTimeoutException)
        {
            return "***";
        }
    }

    public DistillDiagnostic Redact(DistillDiagnostic diagnostic)
    {
        var properties = diagnostic.Properties?
            .ToDictionary(pair => pair.Key, pair => Redact(pair.Value), StringComparer.Ordinal);

        return diagnostic with
        {
            Message = Redact(diagnostic.Message),
            Exception = diagnostic.Exception is null
                ? null
                : diagnostic.Exception with
                {
                    Type = Redact(diagnostic.Exception.Type),
                    Message = Redact(diagnostic.Exception.Message)
                },
            Properties = properties
        };
    }
}
