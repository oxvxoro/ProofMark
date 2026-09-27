using Distill.Cli.Commands;
using System.CommandLine;

var root = new RootCommand("Distill — .NET AI-friendly verification orchestrator");
root.Subcommands.Add(InitCommand.Create());
root.Subcommands.Add(VerifyCommand.Create());
root.Subcommands.Add(DoctorCommand.Create());
root.Subcommands.Add(ReportCommand.Create());
root.Subcommands.Add(DiagnosticsCommand.Create());
root.Subcommands.Add(RawCommand.Create());
root.Subcommands.Add(ArtifactsCommand.Create());
root.Subcommands.Add(BuildSpikeCommand.Create());
root.Subcommands.Add(TestSpikeCommand.Create());

return await root.Parse(args).InvokeAsync();
