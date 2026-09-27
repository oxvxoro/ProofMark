namespace WpfFixture.Views;

/// <summary>MainWindow.xaml의 x:Class 특성이 가리키는 코드비하인드 클래스. 이 픽스처는
/// Windows 전용 WindowsDesktop SDK를 의도적으로 피하므로 WPF SDK
/// 기반 클래스(Window)를 여기선 참조하지 않는다. XamlMarkupAnalyzer는 상속이 아니라
/// 소스 그래프에 대한 한정 이름 조회로 x:Class를 해소한다.</summary>
public sealed class MainWindow
{
    public void Save_Click(object sender, object e)
    {
    }
}
