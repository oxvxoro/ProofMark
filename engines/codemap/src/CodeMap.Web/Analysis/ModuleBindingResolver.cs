namespace CodeMap.Web;

/// <summary>언어 분석기가 모듈 지정자를 해소하기 전에 정규화한다.</summary>
public static class ModuleBindingResolver
{
    public static string Normalize(string specifier) =>
        string.IsNullOrWhiteSpace(specifier) ? string.Empty : specifier.Trim().Replace('\\', '/');
}
