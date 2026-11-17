using System.Globalization;

namespace CashHarbor.client.Components.Shared;

public sealed record ChartSeries(string Name, IReadOnlyList<long> Values, string CssClass);
public sealed record ChartAxis(decimal Minimum, decimal Maximum, decimal Step)
{
    public IEnumerable<decimal> Ticks()
    {
        for (var value = Minimum; value <= Maximum; value += Step) yield return value;
    }
    public decimal Y(decimal value) => 238m - (value - Minimum) * 204m / (Maximum - Minimum);
    public static ChartAxis Create(IEnumerable<long> values, bool includeZero)
    {
        var all = values.ToArray(); decimal min = all.Length == 0 ? 0 : all.Min(); decimal max = all.Length == 0 ? 100 : all.Max();
        if (includeZero) { min = Math.Min(0, min); max = Math.Max(0, max); }
        if (min == max) { var padding = Math.Max(100m, decimal.Abs(min) * .05m); min -= padding; max += padding; if (includeZero && all.All(v => v >= 0)) min = 0; }
        var raw = (max - min) / 4m; decimal unit = 1;
        while (unit * 10 <= raw) unit *= 10;
        while (unit > raw) unit /= 10;
        var normalized = raw / unit;
        var step = Math.Max(1m, (normalized <= 1 ? 1m : normalized <= 2 ? 2m : normalized <= 2.5m ? 2.5m : normalized <= 5 ? 5m : 10m) * unit);
        return new(decimal.Floor(min / step) * step, decimal.Ceiling(max / step) * step, step);
    }
}
public static class ChartGeometry
{
    // Calendar boundaries instead of arbitrary equally spaced days such as 5 July / 8 August.
    public static int[] DateLabels(IReadOnlyList<DateOnly> dates)
    {
        if (dates.Count <= 1) return dates.Count == 0 ? [] : [0];
        var months = Enumerable.Range(0, dates.Count).Where(i => i == 0 || dates[i].Month != dates[i - 1].Month || dates[i].Year != dates[i - 1].Year).ToArray();
        var candidates = months.Length >= 3 ? months : Enumerable.Range(0, Math.Min(4, dates.Count)).Select(i => (int)decimal.Round(i * (dates.Count - 1m) / (Math.Min(4, dates.Count) - 1))).ToArray();
        var stride = Math.Max(1, (int)Math.Ceiling(candidates.Length / 5m));
        // Keep the last label far enough from its neighbour on a phone.
        return candidates.Where((_, i) => i % stride == 0).Where(i => i == 0 || i >= dates.Count / 8).ToArray();
    }
    public const decimal Left = 98m;
    public const decimal Width = 850m;
    public static string N(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    public static decimal X(int index, int count) => count <= 1 ? Left + Width / 2 : Left + Width * index / (count - 1);
    public static string AxisEuro(decimal cents, decimal step = 100m)
    {
        var value = cents / 100m; var abs = decimal.Abs(value); var fr = CultureInfo.GetCultureInfo("fr-FR");
        return abs >= 1_000_000 && step >= 100_000m ? (value / 1_000_000m).ToString("0.###", fr) + " M€" : abs >= 10_000 && step >= 100m ? (value / 1000m).ToString("0.###", fr) + " k€" : value.ToString(cents % 100 == 0 ? "N0" : "N2", fr) + " €";
    }
    public static string Line(ChartSeries series, ChartAxis axis, bool stepped)
    {
        return string.Join(" ", series.Values.Select((value, i) => i == 0 ? $"M {N(X(i, series.Values.Count))} {N(axis.Y(value))}" : stepped ? $"H {N(X(i, series.Values.Count))} V {N(axis.Y(value))}" : $"L {N(X(i, series.Values.Count))} {N(axis.Y(value))}"));
    }
}
