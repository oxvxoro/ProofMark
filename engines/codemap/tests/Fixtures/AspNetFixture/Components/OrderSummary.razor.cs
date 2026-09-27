using Microsoft.AspNetCore.Components;

namespace AspNetFixture.Components;

public partial class OrderSummary : ComponentBase
{
    [Parameter]
    public decimal Total { get; set; }

    // 컴포넌트 매개변수가 아니다. 특성 바인딩에는 실제
    // [Parameter] 심볼이 필요하고, 이름만 유일한 속성 일치로는 부족함을 단언한다(plan §7).
    public string InternalNote { get; set; } = string.Empty;
}
