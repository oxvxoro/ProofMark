namespace CoverageService.Lib;

public interface IPriceRule
{
    decimal Apply(decimal subtotal);
}
