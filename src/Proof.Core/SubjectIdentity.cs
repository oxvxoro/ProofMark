namespace Proof.Core;

public enum IdentityMatchKind
{
    None,
    Heuristic,
    Exact
}

public sealed record SubjectIdentity(
    string? Id,
    string? DisplayName = null,
    string? FullyQualifiedName = null,
    string? Project = null,
    string? TargetFramework = null);

public static class SubjectIdentityMatcher
{
    public static string TrimSignature(string value)
    {
        var index = value.IndexOf('(');
        return index > 0 ? value[..index] : value;
    }

    public static string LastName(string value)
    {
        var trimmed = TrimSignature(value);
        var parts = trimmed.Split(['.', '+', '/'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? trimmed : parts[^1];
    }

    // 명시적 테스트 맵 신원. 잘라낸 동등성과 점 접미사, 양쪽
    // 방향이다. 짧은 이름만 있으면 접미사로 결코 일치하지 않는다.
    public static bool SymbolMatches(string? subjectId, string? displayName, string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)
            || string.IsNullOrWhiteSpace(subjectId) && string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        var symbolTrimmed = TrimSignature(symbol);
        foreach (var candidate in new[] { subjectId, displayName })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var trimmed = TrimSignature(candidate);
            if (string.Equals(trimmed, symbolTrimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (symbolTrimmed.Contains('.') && trimmed.EndsWith("." + symbolTrimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (trimmed.Contains('.') && symbolTrimmed.EndsWith("." + trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool EndsWithIdentity(string fullyQualifiedName, string? displayName, string expectedId)
    {
        foreach (var candidate in new[] { displayName, expectedId })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var trimmed = TrimSignature(candidate);
            if (!trimmed.Contains('.'))
            {
                continue;
            }

            var fqn = TrimSignature(fullyQualifiedName);
            if (fqn.EndsWith("." + trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool HeuristicNameMatch(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(needle) || string.IsNullOrWhiteSpace(haystack))
        {
            return false;
        }

        if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || needle.Contains(haystack, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var haystackTail = LastName(haystack);
        var needleTail = LastName(needle);
        return !string.IsNullOrWhiteSpace(haystackTail)
            && string.Equals(haystackTail, needleTail, StringComparison.OrdinalIgnoreCase)
            && haystackTail.Length >= 8;
    }

    public static IdentityMatchKind Match(SubjectIdentity expected, SubjectIdentity actual)
    {
        if (!string.IsNullOrWhiteSpace(expected.Project)
            && !string.IsNullOrWhiteSpace(actual.Project)
            && !string.Equals(actual.Project, expected.Project, StringComparison.OrdinalIgnoreCase))
        {
            return IdentityMatchKind.None;
        }

        if (!string.IsNullOrWhiteSpace(expected.TargetFramework)
            && (string.IsNullOrWhiteSpace(actual.TargetFramework)
                || !string.Equals(actual.TargetFramework, expected.TargetFramework, StringComparison.OrdinalIgnoreCase)))
        {
            return IdentityMatchKind.None;
        }

        if (ExactIdentity(expected, actual))
        {
            return IdentityMatchKind.Exact;
        }

        if (HeuristicIdentity(expected, actual))
        {
            return IdentityMatchKind.Heuristic;
        }

        return IdentityMatchKind.None;
    }

    private static bool ExactIdentity(SubjectIdentity expected, SubjectIdentity actual)
    {
        var expectedId = expected.Id;
        var displayName = expected.DisplayName;
        if (string.Equals(actual.Id, expectedId, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(actual.FullyQualifiedName, displayName, StringComparison.Ordinal)
            || string.Equals(actual.FullyQualifiedName, expectedId, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(actual.FullyQualifiedName)
            && !string.IsNullOrWhiteSpace(displayName)
            && string.Equals(
                TrimSignature(actual.FullyQualifiedName),
                TrimSignature(displayName),
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(actual.FullyQualifiedName)
            && EndsWithIdentity(actual.FullyQualifiedName, displayName, expectedId ?? string.Empty))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(actual.DisplayName)
            && string.Equals(
                TrimSignature(actual.DisplayName),
                TrimSignature(displayName ?? expectedId ?? string.Empty),
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool HeuristicIdentity(SubjectIdentity expected, SubjectIdentity actual)
    {
        foreach (var needle in new[] { expected.Id, expected.DisplayName, expected.FullyQualifiedName })
        {
            if (string.IsNullOrWhiteSpace(needle))
            {
                continue;
            }

            foreach (var haystack in new[] { actual.Id, actual.DisplayName, actual.FullyQualifiedName })
            {
                if (string.IsNullOrWhiteSpace(haystack))
                {
                    continue;
                }

                if (HeuristicNameMatch(haystack, needle))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
