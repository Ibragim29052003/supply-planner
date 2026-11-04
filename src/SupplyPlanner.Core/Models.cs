namespace SupplyPlanner.Core;

public sealed record DailyDemand(DateOnly Date, decimal Quantity, bool IsComplete = true);
public enum DeliveryStatus { Expected, Received, Cancelled }
public sealed record PlannedDelivery(string Id, string ProductId, DateOnly ArrivalDate,
    int Quantity, DeliveryStatus Status = DeliveryStatus.Expected);

// Id идентифицирует позицию. Один артикул может иметь разные позиции по складам.
public sealed record Product(string Id, string Name, decimal? Stock,
    IReadOnlyList<DailyDemand> History, string? Warehouse = null, string? Article = null);

public sealed record PlanningSettings(int LeadDays = 7, int TargetDays = 30,
    int SafetyDays = 3, int PackSize = 1, int HistoryDays = 30, string Strategy = "average")
{
    public void Validate()
    {
        if (LeadDays is < 0 or > 90 || TargetDays is < 1 or > 180 ||
            SafetyDays is < 0 or > 90 || PackSize is < 1 or > 10000 ||
            HistoryDays is < 1 or > 60 || Strategy is not ("average" or "weighted"))
            throw new ArgumentException("Проверьте параметры: доставка 0–90 дней, запас 1–180, " +
                "страховой запас 0–90, упаковка 1–10 000, история 1–60 дней.");
    }
}

// Снимок на начало StartDate. Изменения через with создают новую версию сценария.
public sealed record PlanningScenario(string Name, string Source, bool IsDemo,
    DateOnly StartDate, DateTimeOffset? SnapshotAt, PlanningSettings Settings,
    IReadOnlyList<Product> Products, IReadOnlyList<PlannedDelivery> Deliveries)
{
    public void Validate()
    {
        if (Settings is null) throw new ArgumentException("Не заданы настройки расчёта.");
        Settings.Validate();
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 120 ||
            string.IsNullOrWhiteSpace(Source) || Source.Length > 200 ||
            StartDate.Year is < 2000 or > 2100 || Products is null || Deliveries is null ||
            Products.Count is < 1 or > 100 || Deliveries.Count > 500)
            throw new ArgumentException("Некорректный сценарий: нужны название, источник и 1–100 товаров.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var positions = new Dictionary<string, HashSet<string>>();
        foreach (var product in Products)
        {
            if (product is null || string.IsNullOrWhiteSpace(product.Id) || product.Id.Length > 60 ||
                !ids.Add(product.Id) || string.IsNullOrWhiteSpace(product.Name) || product.Name.Length > 160 ||
                product.Stock is < 0 or > 1_000_000_000 ||
                (product.Stock.HasValue && decimal.Truncate(product.Stock.Value) != product.Stock.Value) ||
                product.History is null || product.History.Count > 366)
                throw new ArgumentException("Проверьте товары: уникальный ID позиции, название и целый неотрицательный остаток.");
            if (product.Warehouse is { Length: > 120 } || product.Article is { Length: > 60 } ||
                (product.Warehouse is not null && string.IsNullOrWhiteSpace(product.Warehouse)) ||
                (product.Article is not null && string.IsNullOrWhiteSpace(product.Article)))
                throw new ArgumentException("Проверьте название склада и артикул товара.");
            var article = product.Article?.Trim() ?? product.Id;
            var warehouse = product.Warehouse?.Trim().ToUpperInvariant() ?? "";
            if (!positions.TryGetValue(article, out var warehouses)) positions[article] = warehouses = [];
            if (warehouses.Contains(warehouse) ||
                (warehouses.Count > 0 && (warehouse == "" || warehouses.Contains(""))))
                throw new ArgumentException($"Артикул {article}: нельзя дублировать склад или смешивать общий остаток с данными по складам.");
            warehouses.Add(warehouse);
            var dates = new HashSet<DateOnly>();
            foreach (var day in product.History)
                if (day is null || day.Date >= StartDate || !dates.Add(day.Date) ||
                    day.Quantity is < 0 or > 1_000_000 || decimal.Truncate(day.Quantity) != day.Quantity)
                    throw new ArgumentException($"Товар {product.Id}: история должна содержать уникальные прошлые даты и неотрицательный спрос.");
        }
        var deliveryIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var delivery in Deliveries)
            if (delivery is null || string.IsNullOrWhiteSpace(delivery.Id) || delivery.Id.Length > 60 ||
                !deliveryIds.Add(delivery.Id) || !ids.Contains(delivery.ProductId) ||
                delivery.Quantity is < 1 or > 1_000_000_000 || !Enum.IsDefined(delivery.Status) ||
                (delivery.Status == DeliveryStatus.Expected &&
                    (delivery.ArrivalDate < StartDate || delivery.ArrivalDate > StartDate.AddDays(365))))
                throw new ArgumentException("Проверьте поступления: товар, уникальный ID, положительное количество и будущая дата.");
    }
}

public sealed record DemandForecast(decimal? DailyRate, int CompleteDays, int RequiredDays,
    string Method, string? Warning);
public sealed record InventoryDay(DateOnly Date, decimal Incoming, decimal RecommendedIncoming,
    decimal BaselineClosing, decimal PlannedClosing, decimal BaselineLostDemand, decimal PlannedLostDemand);
public sealed record ShortagePeriod(DateOnly Start, DateOnly End, decimal LostDemand);
public sealed record ProductPlan(string ProductId, string Name, decimal? Stock,
    DemandForecast Forecast, decimal? CoverDays, decimal? SafetyStock,
    int? RecommendedQuantity, decimal? StockAtArrival, DateOnly ArrivalDate,
    DateOnly? StockoutDate, DateOnly? LatestDispatchDate, string Status,
    IReadOnlyList<InventoryDay> Timeline, IReadOnlyList<ShortagePeriod> Shortages,
    IReadOnlyList<string> Warnings, string? Warehouse = null, string? Article = null);
public sealed record SupplyPlan(DateOnly StartDate, PlanningSettings Settings,
    IReadOnlyList<ProductPlan> Products);
