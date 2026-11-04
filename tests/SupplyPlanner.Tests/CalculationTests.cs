using SupplyPlanner.Core;

namespace SupplyPlanner.Tests;

public sealed class CalculationTests
{
    private static readonly DateOnly Start = new(2026, 10, 13);
    internal static PlanningScenario Scenario(decimal stock = 100, decimal rate = 10,
        PlanningSettings? settings = null, params PlannedDelivery[] deliveries) => new(
            "Тест", "Полная тестовая история", true, Start, null, settings ?? new(),
            [new("p", "Товар", stock, Enumerable.Range(1, 60).Select(i => new DailyDemand(Start.AddDays(-i), rate)).ToArray())], deliveries);
    private static ProductPlan Calculate(PlanningScenario scenario) => new Core.SupplyPlanner().Calculate(scenario).Products.Single();

    [Fact]
    public void SameArticleIsCalculatedIndependentlyForDifferentWarehouses()
    {
        var scenario = Scenario();
        var first = scenario.Products[0] with { Warehouse = "Коледино", Article = "WB-1001" };
        var second = first with { Id = "p-kazan", Warehouse = "Казань", Stock = 20,
            History = first.History.Select(x => x with { Quantity = 4 }).ToArray() };
        scenario = scenario with { Products = [first, second], Deliveries = [new("d", "p", Start.AddDays(7), 50)] };
        var result = new Core.SupplyPlanner().Calculate(scenario);
        Assert.Equal(250, result.Products[0].RecommendedQuantity);
        Assert.Equal(132, result.Products[1].RecommendedQuantity);
        Assert.Equal("Казань", result.Products[1].Warehouse);
        Assert.Equal("WB-1001", result.Products[1].Article);
        Assert.Equal(0, result.Products[1].Timeline[7].Incoming);
    }

    [Theory]
    [InlineData("Коледино")]
    [InlineData(null)]
    public void DuplicateWarehouseOrMixedAggregateCannotDoubleCountAnArticle(string? secondWarehouse)
    {
        var scenario = Scenario();
        var first = scenario.Products[0] with { Warehouse = "Коледино", Article = "WB-1001" };
        scenario = scenario with { Products = [first, first with { Id = "second", Warehouse = secondWarehouse }] };
        Assert.Throws<ArgumentException>(() => new Core.SupplyPlanner().Calculate(scenario));
    }

    [Theory]
    [InlineData(1, 300)]
    [InlineData(12, 300)]
    [InlineData(16, 304)]
    public void MatchesHandCalculatedExample(int pack, int expected)
    {
        var result = Calculate(Scenario(settings: new(PackSize: pack)));
        Assert.Equal(expected, result.RecommendedQuantity);
        Assert.Equal(30, result.StockAtArrival);
        Assert.Equal(30, result.SafetyStock);
        Assert.Equal(10, result.CoverDays);
        Assert.All(result.Timeline.Skip(7), x => Assert.True(x.PlannedClosing >= 30));
    }

    [Fact]
    public void ArrivalDayDeliveryIsCountedExactlyOnce()
    {
        var result = Calculate(Scenario(deliveries: [new("d", "p", Start.AddDays(7), 50)]));
        Assert.Equal(80, result.StockAtArrival);
        Assert.Equal(250, result.RecommendedQuantity);
        Assert.Equal(50, result.Timeline[7].Incoming);
        Assert.Equal(320, result.Timeline[7].PlannedClosing);
    }

    [Fact]
    public void LateDeliveryCannotCoverEarlierShortage()
    {
        var result = Calculate(Scenario(stock: 0, settings: new(LeadDays: 0, TargetDays: 10, SafetyDays: 0),
            deliveries: [new("late", "p", Start.AddDays(5), 100)]));
        Assert.Equal(50, result.RecommendedQuantity);
        Assert.All(result.Timeline, x => Assert.Equal(0, x.PlannedLostDemand));
        Assert.Equal(0, result.Timeline[4].PlannedClosing);
        Assert.Equal(90, result.Timeline[5].PlannedClosing);
    }

    [Fact]
    public void EarlyLostSalesDoNotBecomeBackorders()
    {
        var result = Calculate(Scenario(stock: 0, settings: new(LeadDays: 7, TargetDays: 3, SafetyDays: 0)));
        Assert.Equal(30, result.RecommendedQuantity);
        var period = Assert.Single(result.Shortages);
        Assert.Equal(Start, period.Start);
        Assert.Equal(Start.AddDays(6), period.End);
        Assert.Equal(70, period.LostDemand);
        Assert.Equal("urgent", result.Status);
        Assert.Equal(20, result.Timeline[7].PlannedClosing);
    }

    [Fact]
    public void NoDemandHasNoDivisionByZeroOrFalseRecommendation()
    {
        var result = Calculate(Scenario(rate: 0));
        Assert.Equal(0, result.RecommendedQuantity);
        Assert.Null(result.CoverDays);
        Assert.Null(result.StockoutDate);
        Assert.Null(result.LatestDispatchDate);
        Assert.Equal("no-demand", result.Status);
    }

    [Fact]
    public void UnknownStockIsNotZero()
    {
        var scenario = Scenario();
        scenario = scenario with { Products = [scenario.Products[0] with { Stock = null }] };
        var result = Calculate(scenario);
        Assert.Equal("unknown", result.Status);
        Assert.Null(result.RecommendedQuantity);
        Assert.Empty(result.Timeline);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingOrIncompleteDayPreventsRecommendation(bool remove)
    {
        var scenario = Scenario();
        var product = scenario.Products[0];
        var history = product.History.ToList();
        if (remove) history.RemoveAt(0); else history[0] = history[0] with { IsComplete = false };
        var result = Calculate(scenario with { Products = [product with { History = history }] });
        Assert.Equal(29, result.Forecast.CompleteDays);
        Assert.Null(result.Forecast.DailyRate);
        Assert.Null(result.RecommendedQuantity);
    }

    [Fact]
    public void WeightedForecastUsesChronologicalWeights()
    {
        var history = new[] { new DailyDemand(Start.AddDays(-3), 2),
            new DailyDemand(Start.AddDays(-1), 8), new DailyDemand(Start.AddDays(-2), 2) };
        var product = new Product("p", "Товар", 100, history);
        Assert.Equal(4, new AverageDemandStrategy().Forecast(product, Start, 3).DailyRate);
        Assert.Equal(5, new WeightedDemandStrategy().Forecast(product, Start, 3).DailyRate);
    }

    [Fact]
    public void CancelledAndReceivedDeliveriesDoNotIncreaseStock()
    {
        var result = Calculate(Scenario(deliveries: [
            new("cancelled", "p", Start, 1000, DeliveryStatus.Cancelled),
            new("received", "p", Start.AddDays(-1), 1000, DeliveryStatus.Received)]));
        Assert.Equal(300, result.RecommendedQuantity);
    }

    [Fact]
    public void DispatchDateHasDefinedEndOfDayBoundary()
    {
        // Остаток 30 достигнут на конец дня 6; нарушение запаса будет на день 7.
        // Поставка должна поступить в начале дня 7: последний день отправки — день 0.
        var result = Calculate(Scenario());
        Assert.Equal(Start, result.LatestDispatchDate);
        Assert.Equal(Start.AddDays(9), result.StockoutDate);
    }

    [Fact]
    public void InvalidDataIsRejectedBeforeEvents()
    {
        var planner = new Core.SupplyPlanner();
        var events = 0;
        planner.PlanCalculated += (_, _) => events++;
        Assert.Throws<ArgumentException>(() => planner.Calculate(Scenario(stock: -1)));
        Assert.Throws<ArgumentException>(() => planner.Calculate(Scenario(settings: new(PackSize: 0))));
        var scenario = Scenario();
        Assert.Throws<ArgumentException>(() => planner.Calculate(scenario with { Products = [scenario.Products[0], scenario.Products[0]] }));
        Assert.Equal(0, events);
    }

    [Fact]
    public void QuantityIsMinimalAgainstIndependentDaySimulation()
    {
        // Перебор небольших сценариев проверяет минимальность, а не повторяет формулу.
        foreach (var stock in new[] { 0, 4, 20 })
        foreach (var rate in new[] { 1, 3 })
        foreach (var lead in new[] { 0, 2 })
        foreach (var safetyDays in new[] { 0, 2 })
        foreach (var pack in new[] { 1, 4 })
        foreach (var deliveryDay in new[] { 0, 3, 6 })
        {
            var settings = new PlanningSettings(lead, 6, safetyDays, pack);
            var scenario = Scenario(stock, rate, settings, new PlannedDelivery("d", "p", Start.AddDays(deliveryDay), 8));
            var result = Calculate(scenario);
            bool Satisfies(int quantity)
            {
                decimal balance = stock;
                for (var day = 0; day < lead + 6; day++)
                {
                    if (day == deliveryDay) balance += 8;
                    if (day == lead) balance += quantity;
                    if (day >= lead && (balance < rate || balance - rate < rate * safetyDays)) return false;
                    balance = Math.Max(0, balance - rate);
                }
                return true;
            }
            var expected = Enumerable.Range(0, 100).Select(i => i * pack).First(Satisfies);
            Assert.Equal(expected, result.RecommendedQuantity);
        }
    }

    [Fact]
    public void FractionalDemandRemainsDecimalAndRoundsToPackaging()
    {
        var scenario = Scenario(stock: 0, rate: 0,
            settings: new(LeadDays: 0, TargetDays: 3, SafetyDays: 1, PackSize: 3, HistoryDays: 4));
        var history = scenario.Products[0].History.Select(x => x.Date == Start.AddDays(-1)
            ? x with { Quantity = 1 } : x).ToArray();
        var result = Calculate(scenario with { Products = [scenario.Products[0] with { History = history }] });
        Assert.Equal(3, result.RecommendedQuantity);
        Assert.Equal(0.25m, result.SafetyStock);
    }

    [Fact]
    public void RepeatingDecimalDoesNotCreateAnExtraUnit()
    {
        var scenario = Scenario(stock: 0, rate: 0,
            settings: new(LeadDays: 0, TargetDays: 3, SafetyDays: 0, HistoryDays: 3));
        var history = scenario.Products[0].History.Select(x => x.Date == Start.AddDays(-1)
            ? x with { Quantity = 2 } : x).ToArray();
        var result = Calculate(scenario with { Products = [scenario.Products[0] with { History = history }] });
        Assert.Equal(2, result.RecommendedQuantity);
        Assert.Equal(0, result.Timeline[^1].PlannedClosing);
        Assert.Empty(result.Shortages);
    }
}
