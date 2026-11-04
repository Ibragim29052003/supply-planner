using System.Globalization;
using SupplyPlanner.Core;

namespace SupplyPlanner.Infrastructure;

public static class PlanLabels
{
    public static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");
    public static string Number(decimal? value) => value?.ToString("0.########", Russian) ?? "—";
    public static string Date(DateOnly? value) => value?.ToString("dd.MM.yyyy") ?? "—";
    public static string Status(string status) => status switch
    {
        "urgent" => "Нужна ранняя доставка", "replenish" => "Нужно пополнить",
        "unknown" => "Нет оценки", "healthy" => "Запаса достаточно", _ => "Нет спроса"
    };
    public static string Dispatch(ProductPlan item) => item.RecommendedQuantity > 0 ? Date(item.LatestDispatchDate) : "—";
}
