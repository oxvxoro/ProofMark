namespace CoverageService.Lib;

public sealed class PriceCalculator
{
    public decimal Total(int quantity, IPriceRule rule) => rule.Apply(quantity * 3m);
}
