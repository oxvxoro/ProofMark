namespace WpfFixture.ViewModels;

/// <summary>평범한 CLR 객체. WPF/MVVM 기반 클래스는 필요 없다. XamlMarkupAnalyzer
/// 해소는 소스 클래스만 필요하며 ICommand 구현은 결코 필요하지 않다(plan §3.5).</summary>
public sealed class MainViewModel
{
    public string CustomerName { get; set; } = string.Empty;

    public Customer Customer { get; set; } = new();

    public void Save()
    {
        CustomerName = CustomerName.Trim();
    }

    // ICommand를 대신하는 평범한 속성. plan §3.5는 실제
    // ICommand 구현을 요구하지 않고, 일치하는 공개 속성 또는 메서드 하나만 요구한다.
    public object? SaveCommand { get; set; }
}

public sealed class Customer
{
    public string Name { get; set; } = string.Empty;
}
