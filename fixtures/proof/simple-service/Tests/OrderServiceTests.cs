using SimpleService.App;

namespace SimpleService.Tests;

public sealed class OrderServiceTests
{
    [Fact]
    public void Cancel_RefundsPayment()
    {
        var service = new OrderService(new BillingService());
        Assert.Equal("refunded:42", service.Cancel("42"));
    }
}
