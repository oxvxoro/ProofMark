namespace CodeMap.Cli;

/// <summary>명령 처리기와 테스트가 공유하는 안정적인 프로세스 종료 의미.</summary>
public static class CliExitCodes
{
    public const int Success = 0;
    public const int Error = 1;
    public const int NoMatchOrAmbiguous = 2;
    public const int Cancelled = 130;
}
