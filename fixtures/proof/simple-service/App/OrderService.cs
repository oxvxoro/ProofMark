namespace SimpleService.App;

public sealed class OrderService
{
    private readonly BillingService _billing;

    public OrderService(BillingService billing) => _billing = billing;

    public string Cancel(string orderId) => _billing.Refund(orderId);
}
