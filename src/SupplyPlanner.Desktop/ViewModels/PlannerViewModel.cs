using System.Collections.ObjectModel;
using System.Globalization;
using SupplyPlanner.Core;
using SupplyPlanner.Infrastructure;

namespace SupplyPlanner.Desktop.ViewModels;

public sealed class SourceRow(Product original) : ObservableObject
{
    private string _stock = original.Stock?.ToString(CultureInfo.InvariantCulture) ?? "";
    private string _history = string.Join("; ", original.History.Select(d => d.IsComplete ? d.Quantity.ToString(CultureInfo.InvariantCulture) : "?"));
    public string Id => original.Id;
    public string Name => original.Name;
    public string Article => original.Article ?? original.Id;
    public string Warehouse => PlanQuery.WarehouseName(original.Warehouse);
    public string Label => $"{Name} · {Article} · {Warehouse}";
    public string Stock { get => _stock; set => Set(ref _stock, value); }
    public string History { get => _history; set => Set(ref _history, value); }
    public string Dates => string.Join("; ", original.History.Select(d => d.Date.ToString("dd.MM")));
    public Product Read()
    {
        // Пустая история допустима: ядро вернёт «Нет оценки», а не выдуманный нулевой спрос.
        string[] parts = original.History.Count == 0 && string.IsNullOrWhiteSpace(History)
            ? [] : History.Split(';', StringSplitOptions.TrimEntries);
        if (parts.Length != original.History.Count) throw new ArgumentException($"{Article}, {Warehouse}: нужно {original.History.Count} значений истории через ;");
        decimal? stock = string.IsNullOrWhiteSpace(Stock) ? null : ReadNumber(Stock, "Остаток");
        var days = original.History.Select((d, i) => parts[i] == "?" ? d with { IsComplete = false }
            : d with { Quantity = ReadNumber(parts[i], "Спрос"), IsComplete = true }).ToArray();
        return original with { Stock = stock, History = days };
    }
    private static decimal ReadNumber(string text, string field) => decimal.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0
        ? n : throw new ArgumentException(field + ": введите целое неотрицательное число.");
}
public sealed record PlanRow(ProductPlan Value)
{
    public string Id => Value.ProductId;
    public string Name => Value.Name;
    public string Article => Value.Article ?? Id;
    public string Warehouse => PlanQuery.WarehouseName(Value.Warehouse);
    public string Stock => PlanLabels.Number(Value.Stock);
    public string Demand => PlanLabels.Number(Value.Forecast.DailyRate);
    public string Quantity => PlanLabels.Number(Value.RecommendedQuantity);
    public string Dispatch => PlanLabels.Dispatch(Value);
    public string Arrival => PlanLabels.Date(PlanOverview.RecommendedArrival(Value));
    public string Status => PlanLabels.Status(Value.Status);
    public override string ToString() => $"{Name} · {Article} · {Warehouse}";
}
public sealed record DayRow(InventoryDay Value)
{
    public string Date => PlanLabels.Date(Value.Date);
    public string Incoming => PlanLabels.Number(Value.Incoming);
    public string NewDelivery => PlanLabels.Number(Value.RecommendedIncoming);
    public string Baseline => PlanLabels.Number(Value.BaselineClosing);
    public string Planned => PlanLabels.Number(Value.PlannedClosing);
    public string Lost => PlanLabels.Number(Value.PlannedLostDemand);
}
public sealed record DeliveryRow(PlannedDelivery Value, Product Product)
{
    public string Name => $"{Product.Name} · {Product.Article ?? Product.Id}";
    public string Warehouse => PlanQuery.WarehouseName(Product.Warehouse);
    public string Date => PlanLabels.Date(Value.ArrivalDate);
    public int Quantity => Value.Quantity;
    public string Status => Value.Status switch { DeliveryStatus.Expected => "Ожидается", DeliveryStatus.Received => "Получено", _ => "Отменено" };
}
public sealed record Choice(string Id, string Label) { public override string ToString() => Label; }

public sealed class PlannerViewModel : ObservableObject
{
    private Workspace _workspace;
    private bool _loading;
    private bool _dirty;
    public bool HasUnsavedChanges { get; private set; }
    private string _message = "Учебный пример загружен. Измените данные или откройте свой JSON.";
    private bool _error;
    private string _search = "", _warehouse = "", _status = "", _name = "", _lead = "7", _target = "30", _safety = "3", _pack = "1", _history = "3";
    private Choice _strategy = new("average", "Средний спрос");
    private Choice _sort = new("priority", "Сначала срочные");
    private Choice _statusChoice = new("", "Все оценки");
    private PlanRow? _selected;
    private int _day;
    public ObservableCollection<SourceRow> Sources { get; } = [];
    public ObservableCollection<PlanRow> Results { get; } = [];
    public ObservableCollection<DayRow> Days { get; } = [];
    public ObservableCollection<DeliveryRow> Deliveries { get; } = [];
    public ObservableCollection<JournalEntry> Journal { get; } = [];
    public ObservableCollection<string> Warehouses { get; } = [];
    public IReadOnlyList<Choice> Strategies { get; } = [new("average", "Средний спрос"), new("weighted", "С учётом последних дней")];
    public IReadOnlyList<Choice> Sorts { get; } = [new("priority", "Сначала срочные"), new("quantity", "Количество ↓"), new("dispatch", "Срок отправки"), new("demand", "Спрос ↓"), new("name", "Название")];
    public IReadOnlyList<Choice> Statuses { get; } = [new("", "Все оценки"), new("urgent", "Ранняя доставка"), new("replenish", "Пополнить"), new("unknown", "Нет оценки"), new("healthy", "Достаточно"), new("no-demand", "Нет спроса")];
    public ActionCommand CalculateCommand { get; }
    public ActionCommand ResetFiltersCommand { get; }
    public PlannerViewModel()
    {
        var scenario = ProjectFiles.Example();
        _workspace = PlannerWorkflow.Calculate(new(scenario, null, []), scenario);
        CalculateCommand = new(() => Attempt(Calculate));
        ResetFiltersCommand = new(() => { Search = ""; Warehouse = "Все склады"; StatusChoice = Statuses[0]; Sort = Sorts[0]; });
        Populate(new());
    }
    public Workspace Workspace => _workspace;
    public string Message { get => _message; private set => Set(ref _message, value); }
    public bool IsError { get => _error; private set => Set(ref _error, value); }
    public bool IsDirty { get => _dirty; private set { Set(ref _dirty, value); Raise(nameof(DraftNotice)); } }
    public string DraftNotice => IsDirty ? "Есть изменения. План показывает предыдущий расчёт. Нажмите «Рассчитать»." : "Показан актуальный рассчитанный план.";
    public string Name { get => _name; set { if (Set(ref _name, value)) Dirty(); } }
    public string Lead { get => _lead; set { if (Set(ref _lead, value)) Dirty(); } }
    public string Target { get => _target; set { if (Set(ref _target, value)) Dirty(); } }
    public string Safety { get => _safety; set { if (Set(ref _safety, value)) Dirty(); } }
    public string Pack { get => _pack; set { if (Set(ref _pack, value)) Dirty(); } }
    public string History { get => _history; set { if (Set(ref _history, value)) Dirty(); } }
    public Choice Strategy { get => _strategy; set { if (value is not null && Set(ref _strategy, value)) Dirty(); } }
    public string Search { get => _search; set { if (Set(ref _search, value)) Refresh(); } }
    public string Warehouse { get => _warehouse; set { if (value is not null && Set(ref _warehouse, value)) Refresh(); } }
    public Choice StatusChoice { get => _statusChoice; set { if (value is not null && Set(ref _statusChoice, value)) { _status = value.Id; Refresh(); } } }
    public Choice Sort { get => _sort; set { if (value is not null && Set(ref _sort, value)) Refresh(); } }
    public PlanRow? Selected { get => _selected; set { if (Set(ref _selected, value)) { _day = 0; UpdateDetails(); } } }
    public ProductPlan? SelectedPlan => Selected?.Value;
    public string DetailTitle => Selected is null ? "Выберите позицию" : Selected.ToString();
    public string DetailNote => Selected is null ? "Нажмите строку плана, чтобы увидеть прогноз." :
        $"Остаток: {Selected.Stock} шт. · спрос: {Selected.Demand} шт./день · страховой запас: {PlanLabels.Number(Selected.Value.SafetyStock)} шт.\n" + PlanOverview.Action(Selected.Value) + "\n" + string.Join(" ", Selected.Value.Warnings);
    public string DayInfo => Selected?.Value.Timeline.ElementAtOrDefault(_day) is { } d
        ? $"{d.Date:dd.MM.yyyy}: с новой {PlanLabels.Number(d.PlannedClosing)} шт. · без новой {PlanLabels.Number(d.BaselineClosing)} шт. · ожидается {PlanLabels.Number(d.Incoming)} шт. · новая поставка {PlanLabels.Number(d.RecommendedIncoming)} шт. · потеряно {PlanLabels.Number(d.PlannedLostDemand)} шт."
        : "Нет дневного прогноза: уточните исходные данные.";
    public string ModelInfo => $"Начало модели: {_workspace.Scenario.StartDate:dd.MM.yyyy} · учебный снимок; реальные WB-кабинеты не подключены";
    public string Summary => $"Позиций: {Results.Count} из {_workspace.Scenario.Products.Count}   ·   К поставке: {Results.Sum(r => (long)(r.Value.RecommendedQuantity ?? 0))} шт.   ·   Без оценки: {Results.Count(r => r.Value.RecommendedQuantity is null)}";
    public string WarehouseSummary => string.Join("   |   ", PlanOverview.ByWarehouse(Results.Select(r => r.Value)).Select(s => $"{s.Warehouse}: {PlanLabels.Number(s.Quantity)} шт."));
    public ViewState View => new(Search, Warehouse == "Все склады" ? "" : Warehouse, _status, Sort.Id, Selected?.Id);
    public PlanQuery Query => new(View.Search, View.Warehouse, View.Status, View.Sort);
    private void Dirty() { if (!_loading) { IsDirty = true; HasUnsavedChanges = true; } }
    public void Notify(string message, bool error = false) { IsError = error; Message = message; }
    public bool Attempt(Action action)
    {
        try { action(); return true; }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or OverflowException or FormatException)
        { Notify(e.Message, true); return false; }
    }
    public void Calculate()
    {
        var next = ReadDraft();
        _workspace = PlannerWorkflow.Calculate(_workspace, next);
        HasUnsavedChanges = true;
        IsDirty = false;
        Refresh(); SyncJournal();
        Notify("План рассчитан. Сохраните проект, чтобы вернуться к нему позже.");
    }
    public PlanningScenario ReadDraft()
    {
        int Number(string text) => int.TryParse(text, out var n) ? n : throw new ArgumentException("Параметры должны быть целыми числами.");
        var scenario = _workspace.Scenario with { Name = Name.Trim(), Products = Sources.Select(r => r.Read()).ToArray(),
            Settings = new(Number(Lead), Number(Target), Number(Safety), Number(Pack), Number(History), Strategy.Id) };
        scenario.Validate(); return scenario;
    }
    public void Open(string path)
    {
        var document = ProjectFiles.Read(path); // До успешной проверки текущие данные не трогаем.
        _workspace = document.Workspace;
        Populate(document.View);
        HasUnsavedChanges = false;
        Notify("Открыто: " + Path.GetFileName(path));
    }
    public void Example()
    {
        var scenario = ProjectFiles.Example();
        _workspace = PlannerWorkflow.Calculate(new(scenario, null, []), scenario);
        Populate(new()); Notify("Исходный пример восстановлен: 692 шт. к поставке.");
        HasUnsavedChanges = true;
    }
    public void Save(string path)
    {
        if (IsDirty) Calculate();
        ProjectFiles.Save(path, new(1, _workspace, View));
        HasUnsavedChanges = false;
        Notify("Проект сохранён: " + Path.GetFileName(path));
    }
    public void Export(string path)
    {
        if (IsDirty) throw new ArgumentException("Сначала рассчитайте изменённые данные, затем экспортируйте актуальный план.");
        ProjectFiles.WriteAtomic(path, PlanExcelExport.Create(_workspace.Scenario, _workspace.Plan!, Query));
        Notify("Excel сохранён. Отчёт содержит текущий отфильтрованный список.");
    }
    public void AddDelivery(string productId, DateOnly date, int quantity)
    {
        var scenario = ReadDraft();
        _workspace = PlannerWorkflow.Calculate(_workspace, scenario, new(Guid.NewGuid().ToString("N"), productId, date, quantity));
        Populate(View); Notify("Поступление добавлено, план пересчитан.");
        HasUnsavedChanges = true;
    }
    public void RemoveDelivery(string id)
    {
        var scenario = ReadDraft();
        _workspace = PlannerWorkflow.Calculate(_workspace, scenario, removeId: id);
        Populate(View); Notify("Поступление удалено, план пересчитан.");
        HasUnsavedChanges = true;
    }
    public void SelectDay(int index)
    { _day = Math.Clamp(index, 0, Math.Max(0, (Selected?.Value.Timeline.Count ?? 0) - 1)); Raise(nameof(DayInfo)); }
    private void Populate(ViewState view)
    {
        _loading = true;
        var scenario = _workspace.Scenario;
        Name = scenario.Name; Lead = scenario.Settings.LeadDays.ToString(); Target = scenario.Settings.TargetDays.ToString();
        Safety = scenario.Settings.SafetyDays.ToString(); Pack = scenario.Settings.PackSize.ToString(); History = scenario.Settings.HistoryDays.ToString();
        Strategy = Strategies.First(s => s.Id == scenario.Settings.Strategy);
        Sources.Clear();
        foreach (var product in scenario.Products)
        { var row = new SourceRow(product); row.PropertyChanged += (_, _) => Dirty(); Sources.Add(row); }
        Warehouses.Clear(); Warehouses.Add("Все склады");
        foreach (var name in scenario.Products.Select(p => PlanQuery.WarehouseName(p.Warehouse)).Distinct().Order()) Warehouses.Add(name);
        _search = view.Search; _warehouse = Warehouses.Contains(view.Warehouse) ? view.Warehouse : "Все склады";
        _statusChoice = Statuses.FirstOrDefault(s => s.Id == view.Status) ?? Statuses[0]; _status = _statusChoice.Id;
        _sort = Sorts.FirstOrDefault(s => s.Id == view.Sort) ?? Sorts[0];
        Raise(nameof(Search)); Raise(nameof(Warehouse)); Raise(nameof(StatusChoice)); Raise(nameof(Sort));
        Deliveries.Clear(); foreach (var d in scenario.Deliveries.OrderBy(d => d.ArrivalDate)) Deliveries.Add(new(d, scenario.Products.First(p => p.Id == d.ProductId)));
        Refresh(view.Selected); SyncJournal();
        IsDirty = false; _loading = false; Raise(nameof(ModelInfo));
    }
    private void Refresh(string? selected = null)
    {
        if (_workspace.Plan is null) return;
        var id = selected ?? Selected?.Id;
        var rows = Query.Apply(_workspace.Plan.Products).Select(p => new PlanRow(p)).ToArray();
        Results.Clear(); foreach (var row in rows) Results.Add(row);
        Selected = Results.FirstOrDefault(r => r.Id == id) ?? Results.FirstOrDefault();
        Raise(nameof(Summary)); Raise(nameof(WarehouseSummary));
    }
    private void SyncJournal() { Journal.Clear(); foreach (var entry in _workspace.Journal.Reverse()) Journal.Add(entry); }
    private void UpdateDetails()
    {
        Days.Clear(); if (Selected is not null) foreach (var d in Selected.Value.Timeline) Days.Add(new(d));
        Raise(nameof(SelectedPlan)); Raise(nameof(DetailTitle)); Raise(nameof(DetailNote)); Raise(nameof(DayInfo));
    }
}
