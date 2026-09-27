namespace Distill.Core.Planning;

public static class DotnetCommandParser
{
    public sealed record ParsedDotnetCommand(
        string Verb,
        string? Target,
        IReadOnlyList<string> Arguments);

    public static ParsedDotnetCommand Parse(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("Command must not be empty.", nameof(command));
        }

        var tokens = SplitCommandLine(command);
        if (tokens.Count == 0)
        {
            throw new ArgumentException("Command must contain at least one token.", nameof(command));
        }

        var index = 0;
        if (string.Equals(tokens[index], "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            index++;
        }

        if (index >= tokens.Count)
        {
            throw new ArgumentException("Command must include a dotnet verb.", nameof(command));
        }

        var verb = tokens[index++];
        string? target = null;
        var arguments = new List<string>();

        if (index < tokens.Count && !tokens[index].StartsWith("-", StringComparison.Ordinal))
        {
            target = tokens[index++];
        }

        for (; index < tokens.Count; index++)
        {
            arguments.Add(tokens[index]);
        }

        return new ParsedDotnetCommand(verb, target, arguments);
    }

    public static IReadOnlyList<string> ToArgumentList(ParsedDotnetCommand parsed)
    {
        var arguments = new List<string> { parsed.Verb };
        if (!string.IsNullOrWhiteSpace(parsed.Target))
        {
            arguments.Add(parsed.Target);
        }

        arguments.AddRange(parsed.Arguments);
        return arguments;
    }

    // kind: process 명령처럼 dotnet 동사가 없는 명령줄을 같은 따옴표 규칙으로 나눈다.
    public static IReadOnlyList<string> Tokenize(string command)
        => string.IsNullOrWhiteSpace(command) ? [] : SplitCommandLine(command);

    private static List<string> SplitCommandLine(string command)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < command.Length; i++)
        {
            var ch = command[i];
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}
