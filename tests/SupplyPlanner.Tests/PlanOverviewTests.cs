using ClosedXML.Excel;
using SupplyPlanner.Core;
using SupplyPlanner.Infrastructure;

namespace SupplyPlanner.Tests;

public sealed class PlanOverviewTests
{
    [Fact]
    public void WarehousesKeepUnknownsSeparateAndUseTheSelectedScope()
    {
        var plan = new Core.SupplyPlanner().Calculate(DemoData.Create());
        var scoped = new PlanQuery(Warehouse: "Казань").Apply(plan.Products);
        var summary = Assert.Single(PlanOverview.ByWarehouse(scoped));
        Assert.Equal("Казань", summary.Warehouse);
        Assert.Equal(132, summary.Quantity);
        Assert.Equal(2, summary.Positions);
        Assert.Equal(1, summary.ToReplenish);
        Assert.Equal(1, summary.Urgent);
        Assert.Equal(0, summary.Unknown);
        var unknowns = PlanOverview.ByWarehouse(new PlanQuery(Status: "unknown").Apply(plan.Products));
        Assert.All(unknowns, item => { Assert.Null(item.Quantity); Assert.Null(item.Arrival); Assert.Null(item.DispatchBy); });
        Assert.Equal(2, unknowns.Sum(item => item.Unknown));
        Assert.Empty(PlanOverview.ByWarehouse([]));
    }

    [Fact]
    public void NoRecommendationDoesNotInventAnArrivalAndUrgencyExplainsTheDate()
    {
        var plan = new Core.SupplyPlanner().Calculate(DemoData.Create());
        Assert.Null(PlanOverview.RecommendedArrival(plan.Products.Single(item => item.ProductId == "WB-1005")));
        Assert.Null(PlanOverview.RecommendedArrival(plan.Products.Single(item => item.ProductId == "WB-1007")));
        var urgent = plan.Products.Single(item => item.ProductId == "WB-1001-KZN");
        Assert.Equal(new DateOnly(2026, 10, 20), PlanOverview.RecommendedArrival(urgent));
        Assert.Equal("Срок до начала модели", PlanOverview.DispatchNote(urgent, plan.StartDate));
        Assert.Contains("18.10.2026", PlanOverview.Action(urgent));
    }

    [Fact]
    public void ExcelWarehouseSummaryMatchesTheFilteredRecommendationTable()
    {
        var scenario = DemoData.Create();
        var plan = new Core.SupplyPlanner().Calculate(scenario);
        using var book = new XLWorkbook(new MemoryStream(PlanExcelExport.Create(scenario, plan, new PlanQuery(Warehouse: "Казань"))));
        var summary = book.Worksheet(1).Table("WarehouseTotals").DataRange!;
        Assert.Equal("Казань", summary.Cell(1, 1).GetString());
        Assert.Equal(132, summary.Cell(1, 2).GetValue<int>());
        Assert.Equal(2, summary.Cell(1, 3).GetValue<int>());
        Assert.Equal(0, summary.Cell(1, 5).GetValue<int>());
        Assert.Contains("ранняя доставка", summary.Cell(1, 8).GetString());
    }

    [Fact]
    public void LateExistingSupplyCanNeedExpeditingEvenWhenExtraQuantityIsZero()
    {
        var scenario = DemoData.Create();
        var product = scenario.Products.First() with { Stock = 0 };
        scenario = scenario with { Products = [product], Deliveries = [new("late", product.Id, scenario.StartDate.AddDays(7), 400)] };
        var plan = new Core.SupplyPlanner().Calculate(scenario);
        var item = Assert.Single(plan.Products);
        Assert.Equal(0, item.RecommendedQuantity);
        Assert.Equal("urgent", item.Status);
        Assert.Null(PlanOverview.RecommendedArrival(item));
        Assert.Equal("Нужна более ранняя доставка", PlanOverview.DispatchNote(item, scenario.StartDate));
        var summary = Assert.Single(PlanOverview.ByWarehouse(plan.Products));
        Assert.Equal(0, summary.Quantity);
        Assert.Equal(1, summary.Urgent);
    }
}
