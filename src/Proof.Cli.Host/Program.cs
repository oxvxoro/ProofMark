using System.CommandLine;
using Proof.Cli;

var root = new RootCommand("Proof — deterministic change verification");
root.Subcommands.Add(VerifyCommand.Create());
root.Subcommands.Add(PlanCommand.Create());
root.Subcommands.Add(MapSuggestCommand.Create());
root.Subcommands.Add(ReviewCommand.Create());
root.Subcommands.Add(InitCommand.Create());
root.Subcommands.Add(ConfigValidateCommand.Create());
root.Subcommands.Add(CertificateVerifyCommand.Create());
root.Subcommands.Add(ExplainCommand.Create());
root.Subcommands.Add(ObligationsCommand.Create());
root.Subcommands.Add(SummaryCommand.Create());
root.Subcommands.Add(HistoryCommand.Create());
root.Subcommands.Add(ExceptionCommand.Create());
return await root.Parse(args).InvokeAsync();
