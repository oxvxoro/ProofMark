namespace SimpleService.App;

public sealed class BillingService
{
    public string Refund(string orderId) => $"refunded:{orderId}";
}
