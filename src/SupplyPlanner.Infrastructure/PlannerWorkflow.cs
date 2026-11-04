using SupplyPlanner.Core;

namespace SupplyPlanner.Infrastructure;

/// <summary>Подписчики существуют только в пределах одного изменения сценария.</summary>
public static class PlannerWorkflow
{
    public static Workspace Calculate(Workspace workspace, PlanningScenario scenario,
        PlannedDelivery? delivery = null, string? removeId = null)
    {
        var journal = workspace.Journal.ToList();
        void Log(string name, string message) => journal.Add(new(DateTimeOffset.UtcNow, name, message));
        var schedule = new DeliverySchedule();
        schedule.DeliveryChanged += (_, e) => Log("DeliveryChanged", e.Current is null
            ? $"Удалено поступление {e.Previous!.ProductId}."
            : $"{e.Current.ProductId}: {e.Current.Quantity} шт., прибытие {e.Current.ArrivalDate:dd.MM.yyyy}.");
        if (delivery is not null) scenario = schedule.Upsert(scenario, delivery);
        if (removeId is not null) scenario = schedule.Remove(scenario, removeId);
        var planner = new Core.SupplyPlanner();
        planner.ShortageDetected += (_, e) => Log("ShortageDetected",
            $"{e.ProductId}: дефицит {e.Period.Start:dd.MM}–{e.Period.End:dd.MM}, неудовлетворённый спрос {e.Period.LostDemand:0.##} шт.");
        planner.PlanCalculated += (_, e) => Log("PlanCalculated",
            $"Рассчитано товаров: {e.Plan.Products.Count}; метод: {scenario.Settings.Strategy}.");
        var plan = planner.Calculate(scenario);
        return new(scenario, plan, journal.TakeLast(100).ToArray());
    }
}
