using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using SupplyPlanner.Core;
using SupplyPlanner.Infrastructure;

namespace SupplyPlanner.Tests;

public sealed class PlanViewAndExcelTests
{
    private static SupplyPlan Demo() => new Core.SupplyPlanner().Calculate(DemoData.Create());

    [Fact]
    public void QueryCombinesWordsWarehouseAndStatusWithoutChangingPlan()
    {
        var plan = Demo();
        var original = plan.Products.Select(x => x.ProductId).ToArray();
        var result = new PlanQuery("  ФУТБОЛКА   WB-1001 ", "Казань", "urgent").Apply(plan.Products);
        Assert.Equal("WB-1001-KZN", Assert.Single(result).ProductId);
        Assert.Empty(new PlanQuery("футболка", "Коледино", "urgent").Apply(plan.Products));
        Assert.Equal(original, plan.Products.Select(x => x.ProductId));
    }

    [Theory]
    [InlineData("dispatch")]
    [InlineData("quantity")]
    [InlineData("stock")]
    [InlineData("demand")]
    public void UnknownValuesSortAfterKnownZeros(string sort)
    {
        var item = Demo().Products[0];
        var knownZero = item with { ProductId = "zero", Stock = 0, RecommendedQuantity = 0,
            LatestDispatchDate = new DateOnly(2026, 10, 13), Forecast = item.Forecast with { DailyRate = 0 } };
        var unknown = item with { ProductId = "unknown", Stock = null, RecommendedQuantity = null,
            LatestDispatchDate = null, Forecast = item.Forecast with { DailyRate = null } };
        Assert.Equal(["zero", "unknown"], new PlanQuery(Sort: sort).Apply([unknown, knownZero]).Select(x => x.ProductId));
    }

    [Fact]
    public void PriorityAndQuantityHaveDeterministicOrder()
    {
        var plan = Demo();
        var priority = new PlanQuery().Apply(plan.Products);
        Assert.All(priority.Take(3), x => Assert.Equal("urgent", x.Status));
        Assert.Equal("WB-1001", new PlanQuery(Sort: "quantity").Apply(plan.Products)[0].ProductId);
        var same = plan.Products[0];
        Assert.Equal(["a", "b"], new PlanQuery().Apply([same with { ProductId = "b" }, same with { ProductId = "a" }]).Select(x => x.ProductId));
        Assert.Equal(priority, new PlanQuery(Sort: "invalid").Apply(plan.Products));
    }

    [Fact]
    public void NameSortUsesRussianAlphabet()
    {
        var item = Demo().Products[0];
        var result = new PlanQuery(Sort: "name").Apply([
            item with { ProductId = "1", Name = "Жук" }, item with { ProductId = "2", Name = "Ёж" },
            item with { ProductId = "3", Name = "Еж" }]);
        Assert.Equal(["Еж", "Ёж", "Жук"], result.Select(x => x.Name));
    }

    [Fact]
    public void ExcelContainsTypedResultsAndDailyBalancesAndValidOpenXml()
    {
        var scenario = DemoData.Create();
        var plan = Demo();
        var bytes = PlanExcelExport.Create(scenario, plan);
        using var stream = new MemoryStream(bytes);
        using (var document = SpreadsheetDocument.Open(stream, false))
            Assert.Empty(new OpenXmlValidator().Validate(document));
        using var book = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(["План поставок", "Расчёт по дням", "Параметры"], book.Worksheets.Select(x => x.Name));
        var sheet = book.Worksheet(1);
        var rows = Enumerable.Range(7, plan.Products.Count).ToDictionary(r => sheet.Cell(r, 1).GetString());
        Assert.Equal(300, sheet.Cell(rows["WB-1001"], 7).GetValue<int>());
        Assert.Equal(132, sheet.Cell(rows["WB-1001-KZN"], 7).GetValue<int>());
        Assert.Equal(XLDataType.Number, sheet.Cell(rows["WB-1001"], 7).DataType);
        Assert.Equal(XLDataType.DateTime, sheet.Cell(rows["WB-1001"], 8).DataType);
        Assert.Equal("#,##0", sheet.Cell(rows["WB-1001"], 7).Style.NumberFormat.Format);
        Assert.Equal("SupplyPlanner · План поставок", sheet.Cell(1, 1).GetString());
        Assert.Equal(XLDataType.Number, book.Worksheet(3).Cell(9, 2).DataType);
        Assert.True(sheet.Cell(rows["WB-1007"], 5).IsEmpty());
        Assert.True(sheet.Cell(rows["WB-1008"], 7).IsEmpty());
        Assert.Equal(plan.Products.Sum(x => x.Timeline.Count) + 4, book.Worksheet(2).LastRowUsed()!.RowNumber());
        Assert.Equal(6, sheet.SheetView.SplitRow);
        Assert.Equal(2, sheet.Tables.Count());
        Assert.Equal(0, sheet.SheetView.SplitColumn);
        Assert.True(sheet.Cell(rows["WB-1007"], 8).IsEmpty());
        Assert.True(sheet.Cell(rows["WB-1003"], 8).IsEmpty());
    }

    [Fact]
    public void AggregateWarehouseRemainsSeparateFromKnownWarehouses()
    {
        var result = new PlanQuery(Warehouse: PlanQuery.WarehouseName(null)).Apply(Demo().Products);
        Assert.Equal(8, result.Count);
        Assert.All(result, x => Assert.Null(x.Warehouse));
    }

    [Theory]
    [InlineData("Казань", 2, 74)]
    [InlineData("Неизвестный склад", 0, 0)]
    public void ExcelUsesTheSameScopeForRecommendationsAndDailyRows(string warehouse, int count, int days)
    {
        var bytes = PlanExcelExport.Create(DemoData.Create(), Demo(), new PlanQuery(Warehouse: warehouse, Sort: "quantity"));
        using var document = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        Assert.Empty(new OpenXmlValidator().Validate(document));
        using var book = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(count, count == 0 ? 0 : book.Worksheet(1).Tables.First().DataRange!.RowCount());
        Assert.Equal(days + 4, book.Worksheet(2).LastRowUsed()!.RowNumber());
        Assert.Contains(warehouse, book.Worksheet(1).Cell(5, 1).GetString());
        if (count > 0)
        {
            Assert.Equal("WB-1001-KZN", book.Worksheet(1).Cell(7, 1).GetString());
            Assert.Equal(132, book.Worksheet(1).Cell(7, 7).GetValue<int>());
            Assert.Equal("Казань", book.Worksheet(1).Cell(8, 4).GetString());
        }
    }

    [Fact]
    public void ExportedUserTextNeverBecomesFormula()
    {
        var scenario = DemoData.Create();
        var plan = Demo();
        plan = plan with { Products = plan.Products.Select(x => x with { Name = "=HYPERLINK(\"https://example.invalid\")", Article = "+123" }).ToArray() };
        using var book = new XLWorkbook(new MemoryStream(PlanExcelExport.Create(scenario, plan)));
        Assert.All(book.Worksheets.SelectMany(x => x.CellsUsed()), cell => Assert.False(cell.HasFormula));
        Assert.Equal(XLDataType.Text, book.Worksheet(1).Cell(7, 3).DataType);
        Assert.StartsWith("=HYPERLINK", book.Worksheet(1).Cell(7, 3).GetString());
        Assert.Equal("+123", book.Worksheet(1).Cell(7, 2).GetString());
    }
}
