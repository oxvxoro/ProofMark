namespace AspNetFixture.Services;

// DI에 결코 등록되지 않는 두 번째 IOrderService 구현(Program.cs는 OrderService만
// 등록한다). DI를 아는 흐름 좁히기를 검증한다(plan §5). IOrderService에서 시작하는
// 흐름 질의는 이 미등록 구현을 DI가 고른 OrderService와 같은 경로로
// 도달 가능하다고 취급해서는 안 된다.
public sealed class LegacyOrderService : IOrderService
{
    public string GetOrder(int id) => $"legacy-order-{id}";
}
