namespace Carimbo.Validation;

/// <summary>
/// Decimal arithmetic whose overflow is reported instead of thrown. A model-controlled amount that fits a
/// decimal can still overflow when multiplied or added, and a validator must answer with a finding, not an
/// exception. Each <c>catch</c> is scoped to the single operation, never around a whole rule.
/// </summary>
internal static class SafeMath
{
    public static bool TryMultiply(decimal left, decimal right, out decimal result)
    {
        try
        {
            result = left * right;
            return true;
        }
        catch (OverflowException)
        {
            result = 0m;
            return false;
        }
    }

    public static bool TryAdd(decimal left, decimal right, out decimal result)
    {
        try
        {
            result = left + right;
            return true;
        }
        catch (OverflowException)
        {
            result = 0m;
            return false;
        }
    }

    public static bool TrySubtract(decimal left, decimal right, out decimal result)
    {
        try
        {
            result = left - right;
            return true;
        }
        catch (OverflowException)
        {
            result = 0m;
            return false;
        }
    }

    /// <summary>
    /// The inclusive comparison <c>|computed - printed| &lt;= tolerance</c>. False when the difference itself
    /// overflows (so <paramref name="within"/> must not be read).
    /// </summary>
    public static bool TryWithin(decimal computed, decimal printed, decimal tolerance, out bool within)
    {
        if (!TrySubtract(computed, printed, out var difference))
        {
            within = false;
            return false;
        }

        within = Math.Abs(difference) <= tolerance;
        return true;
    }
}
