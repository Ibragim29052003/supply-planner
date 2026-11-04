using SupplyPlanner.Core;

namespace SupplyPlanner.Infrastructure;

public static class DemoData
{
    public static PlanningScenario Create()
    {
        var start = new DateOnly(2026, 10, 13);
        Product Make(string id, string name, decimal? stock, Func<int, decimal> demand,
            bool complete = true) => new(id, name, stock, Enumerable.Range(0, 60)
                .Select(i => new DailyDemand(start.AddDays(i - 60), demand(i), complete || i != 58)).ToArray());
        return new("Учебный пример · октябрь", "Вымышленные данные: неотменённые заказы, шт.", true,
            start, new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.FromHours(3)), new(),
            [
                Make("WB-1001", "Футболка базовая", 100, _ => 10) with { Warehouse = "Коледино", Article = "WB-1001" },
                Make("WB-1001-KZN", "Футболка базовая", 20, _ => 4) with { Warehouse = "Казань", Article = "WB-1001" },
                Make("WB-1002", "Худи · растущий спрос", 80, i => i < 46 ? 3 : 12),
                Make("WB-1003", "Носки · большой запас", 900, _ => 4),
                Make("WB-1004", "Рюкзак · позднее поступление", 20, _ => 8),
                Make("WB-1005", "Шапка · поставка вовремя", 45, _ => 5) with { Warehouse = "Казань", Article = "WB-1005" },
                Make("WB-1006", "Ремень · нет спроса", 60, _ => 0),
                Make("WB-1007", "Сумка · неизвестный остаток", null, _ => 2),
                Make("WB-1008", "Куртка · неполная история", 25, _ => 4, false),
                Make("WB-1009", "Кроссовки · запас закончился", 0, _ => 3),
                Make("WB-1010", "Перчатки · редкие заказы", 12, i => i % 3 == 0 ? 1 : 0)
            ],
            [new("demo-late", "WB-1004", start.AddDays(14), 200),
             new("demo-early", "WB-1005", start.AddDays(5), 160)]);
    }
}
