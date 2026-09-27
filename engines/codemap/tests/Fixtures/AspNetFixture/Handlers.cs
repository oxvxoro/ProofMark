using Microsoft.AspNetCore.Http;
using AspNetFixture.Services;

namespace AspNetFixture;

public static class Handlers
{
    public static string GetOrderHandler(IOrderService orders, int id) => orders.GetOrder(id);

    public static IResult CreateOrderHandler(IOrderService orders) => Results.Ok(orders.GetOrder(0));

    // 오버로드된 메서드 그룹 처리기: 소스 메서드 두 개가 "PingHandler"라는 이름을 공유한다.
    // Roslyn은 매개변수 없는 오버로드를 경로 대리자 대상으로 고른다. RoutesTo
    // 엣지는 심볼 동일성으로 바로 그 오버로드에 해소되어야 하며, 다른 쪽에 해서는 안 된다.
    public static string PingHandler() => "pong";

    public static string PingHandler(IOrderService orders) => orders.GetOrder(0);
}
