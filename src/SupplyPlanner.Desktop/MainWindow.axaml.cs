using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using SupplyPlanner.Desktop.ViewModels;
using SupplyPlanner.Infrastructure;

namespace SupplyPlanner.Desktop;

public partial class MainWindow : Window
{
    public PlannerViewModel Model { get; } = new();
    private readonly string _data = ProjectFiles.DataDirectory;
    private bool _allowClose, _closingDialog, _busy;
    private string PreferencesPath => Path.Combine(_data, "preferences.json");
    private string RecoveryPath => Path.Combine(_data, "recovery.supply.json");
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = Model;
        try
        {
            var preferences = ProjectFiles.LoadPreferences(PreferencesPath);
            Width = preferences.Width; Height = preferences.Height;
            Application.Current!.RequestedThemeVariant = preferences.Theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark;
            if (Program.OpenPath is { } path) Model.Open(path);
            else if (File.Exists(RecoveryPath)) Model.Open(RecoveryPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        { Model.Notify("Не удалось восстановить файл. Он оставлен на диске; открыт учебный пример. " + e.Message, true); }
        this.FindControl<InventoryChart>("Chart")!.DaySelected += Model.SelectDay;
        this.FindControl<TextBox>("DeliveryDate")!.Text = Model.Workspace.Scenario.StartDate.AddDays(Model.Workspace.Scenario.Settings.LeadDays).ToString("dd.MM.yyyy");
        AddHandler(KeyDownEvent, OnShortcut, RoutingStrategies.Tunnel);
        Closing += ClosingAsync;
    }
    private void CommitEdits()
    {
        var grid = this.FindControl<DataGrid>("SourceGrid")!;
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);
    }
    private void CalculateClick(object? sender, RoutedEventArgs e) => CommitEdits();
    private async Task<bool> Confirm(string title, string message, string accept = "Продолжить")
    {
        var dialog = new Window { Title = title, Width = 500, MinWidth = 350, MaxWidth = 600, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var yes = new Button { Content = accept, Classes = { "accent" } }; var no = new Button { Content = "Отмена" };
        yes.Click += (_, _) => dialog.Close(true); no.Click += (_, _) => dialog.Close(false);
        dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = 20, Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10, Children = { no, yes } } } };
        return await dialog.ShowDialog<bool>(this);
    }
    private async Task<bool> MayReplace() => !Model.HasUnsavedChanges || await Confirm("Несохранённый проект", "Есть изменения, не сохранённые в файл проекта. Заменить их? Для сохранения нажмите «Отмена», затем «Сохранить».", "Заменить");
    private async void OpenClick(object? sender, RoutedEventArgs e) => await OpenAsync();
    private async Task OpenAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            CommitEdits();
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Открыть проект или сценарий", AllowMultiple = false, FileTypeFilter = [new("JSON / проект") { Patterns = ["*.json"] }] });
            if (files.Count == 0 || !await MayReplace()) return;
            var path = files[0].TryGetLocalPath();
            if (path is null) { Model.Notify("Выберите локальный файл.", true); return; }
            Model.Attempt(() => Model.Open(path));
        }
        catch (Exception ex) when (IsFileError(ex)) { Model.Notify("Не удалось открыть файл: " + ex.Message, true); }
        finally { _busy = false; }
    }
    private async void SaveClick(object? sender, RoutedEventArgs e) => await SaveAsync();
    private async Task SaveAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            CommitEdits();
            var file = await StorageProvider.SaveFilePickerAsync(new() { Title = "Сохранить состояние проекта", SuggestedFileName = "plan.supply.json", DefaultExtension = "json", FileTypeChoices = [new("Проект SupplyPlanner") { Patterns = ["*.supply.json"] }] });
            var path = file?.TryGetLocalPath();
            if (path is not null) Model.Attempt(() => Model.Save(path));
        }
        catch (Exception ex) when (IsFileError(ex)) { Model.Notify("Не удалось сохранить проект: " + ex.Message, true); }
        finally { _busy = false; }
    }
    private async void ExportClick(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            CommitEdits();
            if (Model.IsDirty) { Model.Notify("Перед экспортом нажмите «Рассчитать», чтобы применить правки.", true); return; }
            var file = await StorageProvider.SaveFilePickerAsync(new() { Title = "Экспорт текущего списка", SuggestedFileName = "supply-plan.xlsx", DefaultExtension = "xlsx", FileTypeChoices = [new("Excel") { Patterns = ["*.xlsx"] }] });
            var path = file?.TryGetLocalPath(); if (path is null) return;
            var workspace = Model.Workspace; var query = Model.Query;
            Model.Notify("Формируется Excel…");
            var bytes = await Task.Run(() => PlanExcelExport.Create(workspace.Scenario, workspace.Plan!, query));
            await Task.Run(() => ProjectFiles.WriteAtomic(path, bytes));
            Model.Notify("Excel сохранён. Выгружен список на момент нажатия экспорта.");
        }
        catch (Exception ex) when (IsFileError(ex) || ex is OverflowException)
        { Model.Notify("Не удалось сохранить Excel: " + ex.Message, true); }
        finally { _busy = false; }
    }
    private static bool IsFileError(Exception ex) => ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
    private void ThemeClick(object? sender, RoutedEventArgs e)
    {
        Application.Current!.RequestedThemeVariant = ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
        Model.Attempt(SavePreferences);
    }
    private void SavePreferences() => ProjectFiles.SavePreferences(PreferencesPath, new(ActualThemeVariant == ThemeVariant.Dark ? "dark" : "light", Math.Clamp(Width,960,5000),Math.Clamp(Height,640,3000)));
    private async void ExampleClick(object? sender, RoutedEventArgs e)
    { CommitEdits(); if (!_busy && await Confirm("Учебный пример", "Заменить текущий проект исходным примером? Сначала сохраните нужные изменения в файл.", "Открыть пример")) Model.Example(); }
    private void AddDeliveryClick(object? sender, RoutedEventArgs e)
    {
        CommitEdits();
        Model.Attempt(() =>
        {
            if (this.FindControl<ComboBox>("DeliveryProduct")!.SelectedItem is not SourceRow product) throw new ArgumentException("Выберите товар и склад.");
            if (!DateOnly.TryParseExact(this.FindControl<TextBox>("DeliveryDate")!.Text, "dd.MM.yyyy", out var date)) throw new ArgumentException("Дата: дд.ММ.гггг.");
            if (!int.TryParse(this.FindControl<TextBox>("DeliveryQuantity")!.Text, out var quantity)) throw new ArgumentException("Введите целое количество.");
            Model.AddDelivery(product.Id, date, quantity);
        });
    }
    private async void RemoveDeliveryClick(object? sender, RoutedEventArgs e)
    {
        CommitEdits();
        if (this.FindControl<DataGrid>("DeliveryGrid")!.SelectedItem is not DeliveryRow row) { Model.Notify("Выберите поступление для удаления.", true); return; }
        if (await Confirm("Удалить поступление", $"{row.Name}, {row.Warehouse}: {row.Quantity} шт. на {row.Date}. Удалить и пересчитать?", "Удалить")) Model.Attempt(() => Model.RemoveDelivery(row.Value.Id));
    }
    private async void HelpClick(object? sender, RoutedEventArgs e) => await HelpAsync();
    private async Task HelpAsync()
    {
        var window = new Window { Title = "Справка · SupplyPlanner", Width = 740, Height = 650, MinWidth = 450, MinHeight = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.Content = new ScrollViewer { Margin = new Thickness(24), Content = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Text = HelpText } };
        await window.ShowDialog(this);
    }
    public const string HelpText = """
        ПЛАНИРОВЩИК ПОСТАВОК

        1. Откройте сценарий JSON или используйте пример. Во вкладке «Исходные данные» измените остатки и историю двойным щелчком по ячейке. История: числа через ; по датам, ? означает неполный день. Пустой остаток означает неизвестное значение.
        2. Выберите метод и параметры. Нажмите «Рассчитать». До этого план отражает предыдущие данные. Страховой запас = прогноз спроса × число страховых дней.
        3. Выберите строку плана. График сравнивает остатки с новой поставкой и без неё. Дни выбираются мышью или ← → / Home / End. Дневная таблица находится под графиком.
        4. Во вкладке «Поступления» добавьте ожидаемый товар. Уже полученный товар должен быть включён в остаток, а не добавлен ещё раз.
        5. «Сохранить» записывает весь сценарий, журнал и фильтры. При открытии результат пересчитывается из данных. Excel содержит отфильтрованный отчёт и не пересчитывается при редактировании ячеек.

        ГОРЯЧИЕ КЛАВИШИ
        Cmd/Ctrl+O — открыть. Cmd/Ctrl+S — сохранить. Cmd/Ctrl+Enter — рассчитать. Cmd/Ctrl+F — перейти к поиску. F1 — эта справка (на Mac может понадобиться Fn+F1).

        КОМАНДНАЯ СТРОКА (из папки репозитория)
        dotnet run --project src/SupplyPlanner.Desktop -- --open examples/01-base.json
        dotnet run --project src/SupplyPlanner.Cli -- --help
        dotnet run --project src/SupplyPlanner.Cli -- calculate --input examples/01-base.json --excel plan.xlsx --state plan.supply.json
        CLI по умолчанию не заменяет существующие файлы; --force явно разрешает замену. Код ошибки 2.

        МОДЕЛЬ И ОГРАНИЧЕНИЯ
        Фиксированный пример на 13.10.2026. Приход в начале дня, расход в конце. Потерянный спрос не переносится в долг. Поздняя поставка не исправляет ранний дефицит. Рекомендация округляется до упаковки вверх. Склады независимы, общий спрос не распределяется автоматически. Параметры доставки и упаковки общие. Учебные данные, без живого WB и создания реальных поставок.

        ДАННЫЕ
        Явно сохранённый JSON переносится на другой компьютер. При закрытии последний применённый сценарий восстанавливается через локальный recovery.supply.json. Настройки темы и окна хранятся отдельно в пользовательской папке SupplyPlanner. При ошибке чтения файл не удаляется. Если копия не восстановилась, откройте её вручную или используйте пример.
        """;
    private async void OnShortcut(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1) { e.Handled = true; await HelpAsync(); return; }
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0) return;
        switch (e.Key)
        {
            case Key.O: e.Handled = true; await OpenAsync(); break;
            case Key.S: e.Handled = true; await SaveAsync(); break;
            case Key.Enter: e.Handled = true; CommitEdits(); Model.Attempt(Model.Calculate); break;
            case Key.F: e.Handled = true; this.FindControl<TabControl>("Tabs")!.SelectedIndex = 0; this.FindControl<TextBox>("PlanSearch")!.Focus(); break;
        }
    }
    private async void ClosingAsync(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_busy || _closingDialog) return;
        _closingDialog = true;
        try
        {
            CommitEdits();
            if (Model.IsDirty && !await Confirm("Есть неприменённые правки", "Закрыть окно и отбросить неприменённые правки? Последний рассчитанный сценарий будет восстановлен при следующем запуске. Чтобы сохранить правки, отмените закрытие и нажмите «Сохранить».", "Закрыть без правок")) return;
            try { ProjectFiles.Save(RecoveryPath, new(1,Model.Workspace,Model.View)); SavePreferences(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { Model.Notify("Не удалось сохранить восстановление: " + ex.Message,true); if (!await Confirm("Ошибка сохранения", "Автовосстановление не сохранено. Закрыть всё равно?", "Закрыть")) return; }
            _allowClose = true; Close();
        }
        finally { _closingDialog = false; }
    }
}
