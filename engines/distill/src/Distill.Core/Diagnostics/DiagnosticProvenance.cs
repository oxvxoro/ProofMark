namespace Distill.Core.Diagnostics;

public enum DiagnosticProvenance
{
    MsBuildBinaryLog,
    VSTestLoggerEvent,
    Trx,
    MtpStructuredReport,
    Sarif,
    KnownTextParser,
    GenericTextParser,
    RawFallback
}
