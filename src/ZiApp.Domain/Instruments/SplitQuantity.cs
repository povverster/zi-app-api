namespace ZiApp.Domain.Instruments;

public static class SplitQuantity
{
    public static decimal Apply(decimal quantity, decimal numerator, decimal denominator)
    {
        // Multiply before division: 3 units in a 1-for-3 reverse split must be 1,
        // not 3 multiplied by a pre-rounded decimal approximation of one third.
        decimal result = quantity * numerator / denominator;
        if (quantity > 0m && result == 0m) { throw new OverflowException("Split quantity is below decimal precision."); }
        return result;
    }
}