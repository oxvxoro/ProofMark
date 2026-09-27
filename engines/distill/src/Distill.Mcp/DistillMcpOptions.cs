namespace Distill.Mcp;

public sealed record DistillMcpOptions(string? Root)
{
    internal const string RootFlag = "--root";

    internal static DistillMcpOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? root = null;
        for (var index = 0; index < args.Count; index++)
        {
            if (args[index] == RootFlag)
            {
                if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    throw new ArgumentException($"{RootFlag} requires a directory argument.");
                }

                root = args[++index];
            }
        }

        return new DistillMcpOptions(root);
    }
}

public sealed class DistillMcpHostContext
{
    internal DistillMcpHostContext(DistillMcpOptions options)
    {
        WorkspacePolicy = new McpWorkspacePolicy(options.Root, Directory.GetCurrentDirectory());
    }

    internal McpWorkspacePolicy WorkspacePolicy { get; }
}
