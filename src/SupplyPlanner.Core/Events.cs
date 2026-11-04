namespace SupplyPlanner.Core;

public sealed class PlanCalculatedEventArgs(SupplyPlan plan) : EventArgs
{
    public SupplyPlan Plan { get; } = plan;
}
public sealed class ShortageDetectedEventArgs(string productId, ShortagePeriod period) : EventArgs
{
    public string ProductId { get; } = productId;
    public ShortagePeriod Period { get; } = period;
}
public sealed class DeliveryChangedEventArgs(PlannedDelivery? previous, PlannedDelivery? current) : EventArgs
{
    public PlannedDelivery? Previous { get; } = previous;
    public PlannedDelivery? Current { get; } = current;
}

public sealed class DeliverySchedule
{
    public event EventHandler<DeliveryChangedEventArgs>? DeliveryChanged;

    public PlanningScenario Upsert(PlanningScenario scenario, PlannedDelivery delivery)
    {
        var previous = scenario.Deliveries.FirstOrDefault(x => x.Id == delivery.Id);
        var next = scenario with
        {
            Deliveries = scenario.Deliveries.Where(x => x.Id != delivery.Id).Append(delivery).ToArray()
        };
        next.Validate();
        if (previous != delivery) DeliveryChanged?.Invoke(this, new(previous, delivery));
        return next;
    }

    public PlanningScenario Remove(PlanningScenario scenario, string id)
    {
        var previous = scenario.Deliveries.FirstOrDefault(x => x.Id == id)
            ?? throw new ArgumentException("Поступление не найдено.");
        var next = scenario with { Deliveries = scenario.Deliveries.Where(x => x.Id != id).ToArray() };
        next.Validate();
        DeliveryChanged?.Invoke(this, new(previous, null));
        return next;
    }
}
