using System.Text.Json;
using Microsoft.Data.Sqlite;
using SupplyPlanner.Core;
using SupplyPlanner.Infrastructure;

namespace SupplyPlanner.Tests;

public sealed class EventsAndStorageTests
{
    [Fact]
    public void DocumentedImportExampleCalculatesThreeHundredUnits()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "examples", "scenario.json"));
        var scenario = ScenarioJson.Import(json);
        var plan = new Core.SupplyPlanner().Calculate(scenario);
        Assert.Equal(300, Assert.Single(plan.Products).RecommendedQuantity);
    }

    [Fact]
    public void CalculationEventsContainActualShortagesAndFinalPlan()
    {
        var planner = new Core.SupplyPlanner();
        var names = new List<string>();
        SupplyPlan? calculated = null;
        planner.ShortageDetected += (_, e) => { names.Add("shortage"); Assert.Equal("p", e.ProductId); Assert.Equal(70, e.Period.LostDemand); };
        planner.PlanCalculated += (_, e) => { names.Add("plan"); calculated = e.Plan; };
        var result = planner.Calculate(CalculationTests.Scenario(stock: 0));
        Assert.Same(result, calculated);
        Assert.Equal(new[] { "shortage", "plan" }, names);
    }

    [Fact]
    public void DeliveryEventsFireOnActualValidChangesOnly()
    {
        var schedule = new DeliverySchedule();
        var scenario = CalculationTests.Scenario();
        var delivery = new PlannedDelivery("d", "p", scenario.StartDate, 10);
        var events = new List<DeliveryChangedEventArgs>();
        schedule.DeliveryChanged += (_, e) => events.Add(e);
        scenario = schedule.Upsert(scenario, delivery);
        scenario = schedule.Upsert(scenario, delivery);
        Assert.Single(events);
        Assert.Throws<ArgumentException>(() => schedule.Upsert(scenario, delivery with { Quantity = -1 }));
        Assert.Single(events);
        scenario = schedule.Upsert(scenario, delivery with { Quantity = 20 });
        Assert.Equal(10, events[1].Previous!.Quantity);
        Assert.Equal(20, events[1].Current!.Quantity);
        scenario = schedule.Remove(scenario, delivery.Id);
        Assert.Null(events[2].Current);
        Assert.Empty(scenario.Deliveries);
    }

    [Fact]
    public void DemoContainsAllRequiredSituationsAndIsDeterministic()
    {
        var demo = DemoData.Create();
        var plan = new Core.SupplyPlanner().Calculate(demo);
        Assert.Equal(11, plan.Products.Count);
        Assert.Equal(2, plan.Products.Count(x => x.Status == "unknown"));
        Assert.Contains(plan.Products, x => x.Status == "urgent");
        Assert.Contains(plan.Products, x => x.Status == "healthy");
        Assert.Contains(plan.Products, x => x.Status == "no-demand");
        Assert.Equal(JsonSerializer.Serialize(plan), JsonSerializer.Serialize(new Core.SupplyPlanner().Calculate(DemoData.Create())));
    }

    [Fact]
    public void WorkflowRecordsEventsAndDoesNotRetainSubscriptions()
    {
        var scenario = CalculationTests.Scenario();
        var workspace = new Workspace(scenario, null, []);
        workspace = PlannerWorkflow.Calculate(workspace, scenario,
            new("d", "p", scenario.StartDate, 50));
        Assert.Equal(new[] { "DeliveryChanged", "PlanCalculated" }, workspace.Journal.Select(x => x.Event));
        workspace = PlannerWorkflow.Calculate(workspace, workspace.Scenario);
        Assert.Equal(3, workspace.Journal.Count);
    }

    [Fact]
    public void ImportRoundTripsAndRejectsInvalidOrUnexpectedData()
    {
        var original = DemoData.Create();
        var json = JsonSerializer.Serialize(original, ScenarioJson.Options);
        var imported = ScenarioJson.Import(json);
        Assert.False(imported.IsDemo);
        Assert.Equal(original.Products.Count, imported.Products.Count);
        Assert.Throws<JsonException>(() => ScenarioJson.Import("{\"unexpected\":1}"));
        Assert.Throws<ArgumentException>(() => ScenarioJson.Import(json.Replace("\"packSize\": 1", "\"packSize\": 0")));
    }

    [Fact]
    public void ReplacementBacksUpPreviousScenarioAndConflictCreatesNoExtraCopy()
    {
        var directory = Path.Combine(Path.GetTempPath(), "supply-backup-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ScenarioStore(directory);
            var first = store.LoadOrCreate("alice");
            var replacement = first.Value with { Scenario = first.Value.Scenario with { Name = "Новый сценарий" } };
            store.Save("alice", replacement, first.Revision, backupCurrent: true);
            var backup = Assert.Single(store.List("alice"));
            Assert.Equal(first.Value.Scenario.Name, store.ReadCopy("alice", backup.Id).Name);
            Assert.Throws<ScenarioConflictException>(() => store.Save("alice", first.Value, first.Revision, backupCurrent: true));
            Assert.Single(store.List("alice"));
            Assert.Equal("Новый сценарий", store.LoadOrCreate("alice").Value.Scenario.Name);
            for (var i = 0; i < 22; i++) store.SaveCopy("alice", replacement.Scenario with { Name = "Копия " + i });
            Assert.Equal(20, store.List("alice").Count);
            Assert.Equal("Копия 21", store.List("alice")[0].Name);
            Assert.Equal("Копия 2", store.List("alice")[^1].Name);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void SqlitePersistsIsolatesOwnersAndRejectsLostUpdates()
    {
        var directory = Path.Combine(Path.GetTempPath(), "supply-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ScenarioStore(directory);
            var first = store.LoadOrCreate("alice");
            var modified = PlannerWorkflow.Calculate(first.Value, first.Value.Scenario with { Name = "Сохранённый пример" });
            store.Save("alice", modified, first.Revision);
            Assert.Throws<ScenarioConflictException>(() => store.Save("alice", first.Value, first.Revision));
            store.SaveCopy("alice", modified.Scenario);
            var saved = Assert.Single(store.List("alice"));
            Assert.Empty(store.List("bob"));
            Assert.Throws<ArgumentException>(() => store.ReadCopy("bob", saved.Id));
            var restarted = new ScenarioStore(directory);
            Assert.Equal("Сохранённый пример", restarted.LoadOrCreate("alice").Value.Scenario.Name);
            Assert.NotNull(restarted.LoadOrCreate("alice").Value.Plan);
            Assert.Equal("Сохранённый пример", restarted.ReadCopy("alice", saved.Id).Name);
            Assert.NotEqual("Сохранённый пример", restarted.LoadOrCreate("bob").Value.Scenario.Name);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
