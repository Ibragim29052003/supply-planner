using System.Globalization;

namespace SupplyPlanner.Core;

public sealed record WarehousePlanSummary(string Warehouse, long? Quantity, int Positions,
    int ToReplenish, int Urgent, int Unknown, DateOnly? DispatchBy, DateOnly? Arrival);

// Сводка строится по уже отфильтрованному плану и не меняет рекомендации.
public static class PlanOverview
{
    public static IReadOnlyList<WarehousePlanSummary> ByWarehouse(IEnumerable<ProductPlan> products) => products
        .GroupBy(item => PlanQuery.WarehouseName(item.Warehouse))
        .OrderBy(group => group.Key, StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"), true))
        .Select(group =>
        {
            var items = group.ToArray();
            var needed = items.Where(item => item.RecommendedQuantity > 0).ToArray();
            return new WarehousePlanSummary(group.Key,
                items.Any(item => item.RecommendedQuantity.HasValue) ? items.Sum(item => (long)(item.RecommendedQuantity ?? 0)) : null,
                items.Length, needed.Length, items.Count(item => item.Status == "urgent"),
                items.Count(item => !item.RecommendedQuantity.HasValue),
                needed.Select(item => item.LatestDispatchDate).Min(),
                needed.Select(item => (DateOnly?)item.ArrivalDate).Min());
        }).ToArray();

    public static DateOnly? RecommendedArrival(ProductPlan item) => item.RecommendedQuantity > 0 ? item.ArrivalDate : null;
    public static string DispatchNote(ProductPlan item, DateOnly start) => item.RecommendedQuantity is null
        ? "Нужны исходные данные" : item.RecommendedQuantity == 0 ? item.Status == "urgent" ? "Нужна более ранняя доставка" : "Новая поставка не нужна"
        : item.LatestDispatchDate < start ? "Срок до начала модели" : "";
    public static string Action(ProductPlan item) => item.Status switch
    {
        "unknown" => "Уточнить остаток и историю спроса",
        "urgent" => $"Ускорить доставку: дефицит с {item.Shortages.FirstOrDefault()?.Start:dd.MM.yyyy}",
        "healthy" or "no-demand" => "Новая поставка не требуется",
        _ => "Запланировать поставку"
    };
}
