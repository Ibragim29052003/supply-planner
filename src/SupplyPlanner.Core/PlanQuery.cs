using System.Globalization;

namespace SupplyPlanner.Core;

// Представление плана не меняет сам расчёт. Неизвестные числа/даты всегда в конце.
public sealed record PlanQuery(string? Search = null, string? Warehouse = null,
    string? Status = null, string Sort = "priority")
{
    public IReadOnlyList<ProductPlan> Apply(IEnumerable<ProductPlan> products)
    {
        var words = (Search ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var filtered = products.Where(item =>
            (string.IsNullOrEmpty(Warehouse) || Warehouse == WarehouseName(item.Warehouse)) &&
            (string.IsNullOrEmpty(Status) || Status == item.Status) &&
            words.All(word => $"{item.Name} {item.ProductId} {item.Article} {WarehouseName(item.Warehouse)}"
                .Contains(word, StringComparison.OrdinalIgnoreCase)));
        var ordered = Sort switch
        {
            "dispatch" => filtered.OrderBy(x => x.LatestDispatchDate is null).ThenBy(x => x.LatestDispatchDate),
            "quantity" => filtered.OrderBy(x => x.RecommendedQuantity is null).ThenByDescending(x => x.RecommendedQuantity),
            "stock" => filtered.OrderBy(x => x.Stock is null).ThenBy(x => x.Stock),
            "demand" => filtered.OrderBy(x => x.Forecast.DailyRate is null).ThenByDescending(x => x.Forecast.DailyRate),
            "name" => filtered.OrderBy(x => x.Name, StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"), true)),
            _ => filtered.OrderBy(x => Priority(x.Status)).ThenBy(x => x.LatestDispatchDate is null)
                .ThenBy(x => x.LatestDispatchDate)
        };
        return ordered.ThenBy(x => x.ProductId, StringComparer.Ordinal).ToArray();
    }

    public static string WarehouseName(string? warehouse) => warehouse ?? "Без разбивки по складам";
    private static int Priority(string status) => status switch
    {
        "urgent" => 0, "replenish" => 1, "unknown" => 2, "healthy" => 3, _ => 4
    };
}
