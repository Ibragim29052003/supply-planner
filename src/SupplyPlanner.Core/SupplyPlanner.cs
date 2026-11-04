namespace SupplyPlanner.Core;

public sealed class SupplyPlanner
{
    // Только погрешность decimal при делении целочисленного спроса (например, 2 / 3).
    private const decimal Epsilon = 0.000000001m;
    private static decimal ZeroDust(decimal value) => Math.Abs(value) < Epsilon ? 0 : value;
    private readonly IReadOnlyDictionary<string, IDemandForecastStrategy> _strategies;
    public SupplyPlanner(IEnumerable<IDemandForecastStrategy>? strategies = null)
    {
        _strategies = (strategies ?? [new AverageDemandStrategy(), new WeightedDemandStrategy()])
            .ToDictionary(x => x.Id, StringComparer.Ordinal);
    }

    public event EventHandler<PlanCalculatedEventArgs>? PlanCalculated;
    public event EventHandler<ShortageDetectedEventArgs>? ShortageDetected;

    public SupplyPlan Calculate(PlanningScenario scenario)
    {
        scenario.Validate();
        if (!_strategies.TryGetValue(scenario.Settings.Strategy, out var strategy))
            throw new ArgumentException("Метод прогноза не зарегистрирован.");
        var products = scenario.Products.Select(product => CalculateProduct(scenario, product, strategy)).ToArray();
        var plan = new SupplyPlan(scenario.StartDate, scenario.Settings, products);
        foreach (var product in products)
            foreach (var shortage in product.Shortages)
                ShortageDetected?.Invoke(this, new(product.ProductId, shortage));
        PlanCalculated?.Invoke(this, new(plan));
        return plan;
    }

    private static ProductPlan CalculateProduct(PlanningScenario scenario, Product product,
        IDemandForecastStrategy strategy)
    {
        var settings = scenario.Settings;
        var forecast = strategy.Forecast(product, scenario.StartDate, settings.HistoryDays);
        var arrival = scenario.StartDate.AddDays(settings.LeadDays);
        var warnings = new List<string>();
        if (product.Stock is null) warnings.Add("Доступный остаток неизвестен.");
        if (forecast.Warning is not null) warnings.Add(forecast.Warning);
        // Неизвестный остаток или неполная история дают отсутствие оценки, а не ложный ноль.
        if (warnings.Count > 0)
            return new(product.Id, product.Name, product.Stock, forecast, null, null, null, null,
                arrival, null, null, "unknown", [], [], warnings, product.Warehouse, product.Article);

        var rate = forecast.DailyRate!.Value;
        var initial = product.Stock!.Value;
        var safety = rate * settings.SafetyDays;
        var horizon = settings.LeadDays + settings.TargetDays;
        var incoming = scenario.Deliveries
            .Where(x => x.ProductId == product.Id && x.Status == DeliveryStatus.Expected)
            .GroupBy(x => x.ArrivalDate).ToDictionary(g => g.Key, g => g.Sum(x => (decimal)x.Quantity));
        decimal At(int day) => incoming.GetValueOrDefault(scenario.StartDate.AddDays(day));

        // До прибытия новой поставки расходуем только уже имеющийся и ожидаемый товар.
        var atArrival = initial;
        for (var day = 0; day < settings.LeadDays; day++)
            atArrival = ZeroDust(Math.Max(0, atArrival + At(day) - rate));
        atArrival += At(settings.LeadDays);

        // Максимум по префиксам: позднее поступление не оплачивает ранний расход.
        var required = Math.Max(0, safety - atArrival);
        decimal futureIncoming = 0;
        for (var k = 1; k <= settings.TargetDays; k++)
        {
            if (k > 1) futureIncoming += At(settings.LeadDays + k - 1);
            required = Math.Max(required, rate * k + safety - atArrival - futureIncoming);
        }
        // Округляем один раз, после расчёта всей потребности: до целого числа упаковок вверх.
        var packages = Math.Max(0, required) / settings.PackSize;
        var nearest = decimal.Round(packages, 0, MidpointRounding.ToEven);
        if (Math.Abs(required - nearest * settings.PackSize) < Epsilon) packages = nearest;
        var quantity = checked((int)(decimal.Ceiling(packages) * settings.PackSize));
        decimal baseline = initial, planned = initial;
        DateOnly? stockout = null, dispatch = null;
        var timeline = new List<InventoryDay>();
        for (var day = 0; day < horizon; day++)
        {
            var date = scenario.StartDate.AddDays(day);
            var existing = At(day);
            var added = day == settings.LeadDays ? quantity : 0;
            baseline += existing;
            planned += existing + added;
            var baselineLost = ZeroDust(Math.Max(0, rate - baseline));
            var plannedLost = ZeroDust(Math.Max(0, rate - planned));
            baseline = ZeroDust(Math.Max(0, baseline - rate));
            planned = ZeroDust(Math.Max(0, planned - rate));
            if (rate > 0 && baseline == 0) stockout ??= date;
            if (rate > 0 && (baseline + Epsilon < safety || baseline == 0))
                dispatch ??= date.AddDays(-settings.LeadDays);
            timeline.Add(new(date, existing, added, baseline, planned, baselineLost, plannedLost));
        }
        var shortages = BuildShortages(timeline);
        if (shortages.Count > 0)
            warnings.Add("Дефицит до прибытия: новая поставка не исправляет этот период. Нужна более ранняя доставка.");
        if (rate == 0) warnings.Add("За полный выбранный период спрос отсутствует.");
        if (dispatch < scenario.StartDate && rate > 0)
            warnings.Add("Расчётная дата отправки уже прошла. Проверьте ускоренную доставку.");
        return new(product.Id, product.Name, initial, forecast, rate > 0 ? initial / rate : null,
            safety, quantity, atArrival, arrival, stockout, dispatch,
            rate == 0 ? "no-demand" : shortages.Count > 0 ? "urgent" : quantity > 0 ? "replenish" : "healthy",
            timeline, shortages, warnings, product.Warehouse, product.Article);
    }

    private static IReadOnlyList<ShortagePeriod> BuildShortages(IEnumerable<InventoryDay> timeline)
    {
        var result = new List<ShortagePeriod>();
        ShortagePeriod? current = null;
        foreach (var day in timeline)
        {
            if (day.PlannedLostDemand > 0)
                current = current is null ? new(day.Date, day.Date, day.PlannedLostDemand)
                    : current with { End = day.Date, LostDemand = current.LostDemand + day.PlannedLostDemand };
            else if (current is not null) { result.Add(current); current = null; }
        }
        if (current is not null) result.Add(current);
        return result;
    }
}
