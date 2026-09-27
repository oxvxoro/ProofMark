namespace CoverageService.Lib;

// 테스트는 IPriceRule로만 이 구현을 실행한다. 정적 호출 그래프에는
// 테스트에서 Apply로 가는 Calls 엣지가 없어서 P005는 커버리지로만 닫힌다.
public sealed class BulkDiscount : IPriceRule
{
    public decimal Apply(decimal subtotal) => subtotal >= 6m ? subtotal - 1m : subtotal;
}
