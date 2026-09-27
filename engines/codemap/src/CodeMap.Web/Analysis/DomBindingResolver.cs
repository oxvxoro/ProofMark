namespace CodeMap.Web;

/// <summary>HTML/스크립트 바인딩 단계가 공유하는 DOM 선택자 정규화.</summary>
public static class DomBindingResolver
{
    public static string NormalizeSelector(string selector) =>
        string.IsNullOrWhiteSpace(selector) ? string.Empty : selector.Trim();
}
