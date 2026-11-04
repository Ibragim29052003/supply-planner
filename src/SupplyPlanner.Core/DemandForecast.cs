namespace SupplyPlanner.Core;

public interface IDemandForecastStrategy
{
    string Id { get; }
    DemandForecast Forecast(Product product, DateOnly startDate, int historyDays);
}

public abstract class DemandForecastStrategy : IDemandForecastStrategy
{
    public abstract string Id { get; }
    protected abstract decimal Weight(int chronologicalIndex);

    public DemandForecast Forecast(Product product, DateOnly startDate, int historyDays)
    {
        if (historyDays is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(historyDays));
        var days = product.History.ToDictionary(x => x.Date);
        decimal total = 0, weights = 0;
        var complete = 0;
        for (var i = 0; i < historyDays; i++)
        {
            var date = startDate.AddDays(i - historyDays);
            if (!days.TryGetValue(date, out var day) || !day.IsComplete) continue;
            var weight = Weight(i);
            complete++;
            total += day.Quantity * weight;
            weights += weight;
        }
        // Неизвестные дни не становятся нулями и не дают уверенную рекомендацию.
        return complete == historyDays
            ? new(total / weights, complete, historyDays, Id, null)
            : new(null, complete, historyDays, Id,
                $"Полная история: {complete} из {historyDays} дней. Рекомендация недоступна.");
    }
}

public sealed class AverageDemandStrategy : DemandForecastStrategy
{
    public override string Id => "average";
    protected override decimal Weight(int chronologicalIndex) => 1;
}

public sealed class WeightedDemandStrategy : DemandForecastStrategy
{
    public override string Id => "weighted";
    // Для 30 дней веса равны 1, 2, …, 30: последние дни важнее.
    protected override decimal Weight(int chronologicalIndex) => chronologicalIndex + 1;
}
