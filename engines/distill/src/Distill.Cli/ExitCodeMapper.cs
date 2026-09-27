using Distill.Core.Runs;

namespace Distill.Cli;

public static class ExitCodeMapper
{
    public static int FromStatus(VerificationStatus status)
        => status switch
        {
            VerificationStatus.Pass => 0,
            VerificationStatus.Fail => 1,
            VerificationStatus.Uncertain => 4,
            VerificationStatus.InfraError => 3,
            _ => 3
        };

    public static int ConfigError => 2;

    public static int Canceled => 130;
}
