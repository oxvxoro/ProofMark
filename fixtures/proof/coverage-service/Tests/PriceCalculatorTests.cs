using CoverageService.Lib;

namespace CoverageService.Tests;

public sealed class PriceCalculatorTests
{
    [Fact]
    public void Total_AppliesBulkDiscount()
    {
        Assert.Equal(5m, new PriceCalculator().Total(2, new BulkDiscount()));
    }
}
