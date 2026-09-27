using System.CommandLine;
using System.Text.Json;
using Proof.Core;
using Proof.Engine;

namespace Proof.Cli;

public static class SummaryCommand
{
    public static Command Create()
    {
        var certificateOption = new Option<string?>("--certificate")
        {
            Description = "Certificate JSON path. Defaults to the latest .proof/certificates entry.",
            DefaultValueFactory = _ => null
        };
        var formatOption = new Option<string>("--format")
        {
            Description = "Output format: markdown or json.",
            DefaultValueFactory = _ => "markdown"
        };
        var command = new Command("summary", "Render the certificate summary sidecar without re-running the engine.")
        {
            certificateOption,
            formatOption
        };
        command.SetAction((parseResult, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Execute(
                parseResult.GetValue(certificateOption),
                parseResult.GetValue(formatOption)!));
        });
        return command;
    }

    internal static int Execute(string? certificatePath, string format)
    {
        var resolved = string.IsNullOrWhiteSpace(certificatePath)
            ? CertificateExplainer.LatestCertificate(Directory.GetCurrentDirectory())
            : certificatePath;
        if (string.IsNullOrWhiteSpace(resolved) || !File.Exists(resolved))
        {
            Console.Error.WriteLine($"Certificate not found: {resolved ?? "(none)"}");
            return 2;
        }

        CertificateSummary summary;
        if (resolved.EndsWith(".summary.json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                summary = JsonSerializer.Deserialize<CertificateSummary>(File.ReadAllText(resolved), ProofJson.WireOptions)
                          ?? throw new JsonException("Summary deserialized to null.");
            }
            catch (JsonException exception)
            {
                Console.Error.WriteLine($"Invalid summary JSON: {exception.Message}");
                return 2;
            }
        }
        else
        {
            var certificate = CertificateExplainer.LoadCertificate(Directory.GetCurrentDirectory(), resolved, out var error);
            if (certificate is null)
            {
                Console.Error.WriteLine(error);
                return 2;
            }

            summary = ChangeCertificateBuilder.ToSummary(certificate);
        }
        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(summary, ProofJson.WireOptions));
            return 0;
        }

        Console.WriteLine(SummaryMarkdown.Render(summary));
        return 0;
    }
}
