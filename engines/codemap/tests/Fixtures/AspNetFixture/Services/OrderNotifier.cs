namespace AspNetFixture.Services;

/// <summary>자체 수명으로 등록된 두 번째의 무관한 서비스(인터페이스 분리 없음).
/// 단일 타입 인자 AddScoped/AddTransient 오버로드를 검증한다.</summary>
public sealed class OrderNotifier
{
    public void Notify(string message) => System.Console.WriteLine(message);
}
