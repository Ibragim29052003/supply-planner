using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SupplyPlanner.Desktop;
using SupplyPlanner.Desktop.ViewModels;
using SupplyPlanner.Infrastructure;

[assembly: AvaloniaTestApplication(typeof(SupplyPlanner.Desktop.Tests.TestAppBuilder))]

namespace SupplyPlanner.Desktop.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        Environment.SetEnvironmentVariable("SUPPLY_PLANNER_DATA", Path.Combine(Path.GetTempPath(), "planner-ui-"+Guid.NewGuid().ToString("N")));
        return AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new() { UseHeadlessDrawing = false });
    }
}

public sealed class DesktopTests
{
    [Fact]
    public void SourceChangesRecalculateOnlyAffectedWarehouseAndInvalidDraftKeepsOldPlan()
    {
        var vm=new PlannerViewModel();
        Assert.Contains("692",vm.Summary);
        vm.Sources.First(s=>s.Id=="SHIRT-KOL").Stock="150";
        Assert.True(vm.IsDirty); Assert.Contains("692",vm.Summary);
        vm.CalculateCommand.Execute(null);
        Assert.False(vm.IsDirty); Assert.True(vm.HasUnsavedChanges);
        Assert.Equal(250,vm.Results.Single(r=>r.Id=="SHIRT-KOL").Value.RecommendedQuantity);
        Assert.Equal(132,vm.Results.Single(r=>r.Id=="SHIRT-KZN").Value.RecommendedQuantity);
        vm.Sources[0].Stock="-1"; vm.CalculateCommand.Execute(null);
        Assert.True(vm.IsError); Assert.True(vm.IsDirty);
        Assert.Equal(250,vm.Results.Single(r=>r.Id=="SHIRT-KOL").Value.RecommendedQuantity);
    }
    [Fact]
    public void ViewAndJournalRoundTripAndFailedOpenPreservesCurrentWorkspace()
    {
        var dir=Path.Combine(Path.GetTempPath(),"planner-vm-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var vm=new PlannerViewModel(); vm.Search="WB-1001"; vm.Warehouse="Казань";
            var path=Path.Combine(dir,"plan.json"); vm.Save(path); Assert.False(vm.HasUnsavedChanges);
            var next=new PlannerViewModel(); next.Open(path);
            Assert.Single(next.Results); Assert.Equal("SHIRT-KZN",next.Selected!.Id); Assert.Contains("132",next.Summary);
            var before=next.Workspace; File.WriteAllText(path,"bad"); Assert.False(next.Attempt(()=>next.Open(path))); Assert.Same(before,next.Workspace);
        }
        finally { Directory.Delete(dir,true); }
    }
    [Fact]
    public void DeliveryEventsAndStrategiesUseSameCore()
    {
        var vm=new PlannerViewModel(); vm.Lead="12"; vm.Calculate();
        Assert.Equal(330,vm.Results.Single(r=>r.Id=="SHIRT-KOL").Value.RecommendedQuantity);
        vm.AddDelivery("SHIRT-KOL",new(2026,10,18),50);
        Assert.Equal(300,vm.Results.Single(r=>r.Id=="SHIRT-KOL").Value.RecommendedQuantity);
        Assert.Contains(vm.Journal,e=>e.Event=="DeliveryChanged");
        vm.Example(); vm.Strategy=vm.Strategies[1]; vm.Calculate();
        Assert.Equal(300,vm.Results.Single(r=>r.Id=="HOODIE-KOL").Value.RecommendedQuantity);
    }
    [AvaloniaFact]
    public void WindowRendersBothThemesAndMinimumSizeWithWorkingBindings()
    {
        var window=new MainWindow(); window.Show();
        var grid=window.FindControl<DataGrid>("PlanGrid")!;
        Assert.Equal(5,grid.ItemsSource.Cast<PlanRow>().Count());
        grid.SelectedItem=window.Model.Results.Single(r=>r.Id=="SHIRT-KOL");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Коледино",window.Model.DetailTitle);
        window.FindControl<TextBox>("PlanSearch")!.Text="WB-1001";
        Dispatcher.UIThread.RunJobs(); Assert.Equal(2,window.Model.Results.Count);
        window.Model.ResetFiltersCommand.Execute(null);
        var artifact=Environment.GetEnvironmentVariable("PLANNER_QA_OUTPUT");
        foreach (var (width,height,theme,label) in new[] { (1320,900,ThemeVariant.Dark,"dark"),(960,640,ThemeVariant.Light,"light-min") })
        {
            window.Width=width; window.Height=height; Application.Current!.RequestedThemeVariant=theme;
            Dispatcher.UIThread.RunJobs();
            using var frame=window.CaptureRenderedFrame(); Assert.NotNull(frame);
            if (artifact is not null) { Directory.CreateDirectory(artifact); frame.Save(Path.Combine(artifact,"desktop-"+label+".png")); }
            foreach(var button in window.GetVisualDescendants().OfType<Button>().Where(b=>b.IsVisible && b.Content is string s && s is "Открыть…" or "Сохранить…" or "Справка · F1"))
            { var point=button.TranslatePoint(default,window)!.Value; Assert.InRange(point.X,0,width-button.Bounds.Width+1); Assert.True(button.Bounds.Height>=36); }
        }
        window.Close();
    }
    [AvaloniaFact]
    public void HelpShortcutAndSourceTabAreAvailableAndChartRenders()
    {
        var window=new MainWindow { Width=1320, Height=900 }; window.Show(); window.Model.Example();
        Application.Current!.RequestedThemeVariant=ThemeVariant.Dark;
        var tabs=window.FindControl<TabControl>("Tabs")!; tabs.SelectedIndex=1;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(5,window.FindControl<DataGrid>("SourceGrid")!.ItemsSource.Cast<SourceRow>().Count());
        window.KeyPressQwerty(PhysicalKey.F1,RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var help=Assert.Single(window.OwnedWindows);
        Assert.Contains("Справка",help.Title);
        Assert.Contains("--input",help.GetVisualDescendants().OfType<TextBlock>().Single().Text);
        help.Close();
        tabs.SelectedIndex=0; window.Model.Selected=window.Model.Results.Single(r=>r.Id=="SHIRT-KOL");
        window.FindControl<ScrollViewer>("PlanScroll")!.Offset=new Vector(0,450);
        Dispatcher.UIThread.RunJobs();
        using var frame=window.CaptureRenderedFrame(); Assert.NotNull(frame);
        var artifact=Environment.GetEnvironmentVariable("PLANNER_QA_OUTPUT");
        if (artifact is not null) frame.Save(Path.Combine(artifact,"desktop-chart.png"));
        window.Close();
    }
    [AvaloniaFact]
    public void ChartHoverAndClickUpdateInlineDayDetailsWithoutPopup()
    {
        var window = new MainWindow { Width = 1320, Height = 900 };
        window.Show(); window.Model.Example();
        window.Model.Selected = window.Model.Results.Single(r => r.Id == "SHIRT-KOL");
        window.FindControl<ScrollViewer>("PlanScroll")!.Offset = new Vector(0, 450);
        Dispatcher.UIThread.RunJobs();
        var chart = window.FindControl<InventoryChart>("Chart")!;
        var end = chart.TranslatePoint(new Point(chart.Bounds.Width - 14, 70), window)!.Value;
        window.MouseMove(end);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("18.11.2026", window.Model.DayInfo);
        Assert.Contains("ожидается", window.Model.DayInfo);
        Assert.Contains("новая поставка", window.Model.DayInfo);
        Assert.Null(ToolTip.GetTip(chart));
        var start = chart.TranslatePoint(new Point(42, 70), window)!.Value;
        window.MouseDown(start, MouseButton.Left);
        window.MouseUp(start, MouseButton.Left);
        Assert.Contains("13.10.2026", window.Model.DayInfo);
        Assert.Null(ToolTip.GetTip(chart));
        window.Close();
    }
    [AvaloniaFact]
    public async Task FileButtonsSaveReloadExportCancelAndKeepWorkspaceOnPickerFailure()
    {
        var directory = Path.Combine(Path.GetTempPath(), "planner-buttons-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var files = new TestFileDialogs { SavePath = Path.Combine(directory, "project.supply.json"), ExcelPath = Path.Combine(directory, "report.xlsx") };
        var window = new MainWindow(files); window.Show(); window.Model.Example();
        void Click(string label) => window.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Content?.ToString() == label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            window.Model.Sources.Single(s => s.Id == "SHIRT-KOL").Stock = "150";
            Click("Сохранить…");
            Assert.True(File.Exists(files.SavePath)); Assert.False(window.Model.HasUnsavedChanges);
            Assert.Equal(250, window.Model.Results.Single(r => r.Id == "SHIRT-KOL").Value.RecommendedQuantity);
            files.OpenPath = files.SavePath; Click("Открыть…");
            Assert.Contains("642", window.Model.Summary);
            window.Model.Warehouse = "Казань";
            Click("Excel…");
            var timeout = DateTime.UtcNow.AddSeconds(10);
            while (!window.Model.Message.StartsWith("Excel сохранён") && !window.Model.IsError && DateTime.UtcNow < timeout) await Task.Delay(20);
            Assert.StartsWith("Excel сохранён", window.Model.Message);
            Assert.True(new FileInfo(files.ExcelPath).Length > 1000);
            var original = window.Model.Workspace;
            files.OpenPath = null; Click("Открыть…"); Assert.Same(original, window.Model.Workspace);
            files.Fail = true; Click("Открыть…");
            Assert.True(window.Model.IsError); Assert.Same(original, window.Model.Workspace);
            Assert.Contains("Доступ", window.Model.Message);
            files.Fail = false;
            var scenario = original.Scenario with { StartDate = new(2026, 11, 1), Deliveries = [] };
            var alternate = Path.Combine(directory, "november.json");
            ProjectFiles.WriteAtomic(alternate, System.Text.Json.JsonSerializer.Serialize(scenario, ScenarioJson.Options));
            files.OpenPath = alternate; Click("Открыть…");
            Assert.Equal("08.11.2026", window.FindControl<TextBox>("DeliveryDate")!.Text);
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    }
    private sealed class TestFileDialogs : IPlannerFileDialogs
    {
        public string? OpenPath { get; set; }
        public string? SavePath { get; init; }
        public string? ExcelPath { get; init; }
        public bool Fail { get; set; }
        public Task<string?> OpenProjectAsync() => Fail ? Task.FromException<string?>(new UnauthorizedAccessException("Доступ к файлу запрещён.")) : Task.FromResult(OpenPath);
        public Task<string?> SaveProjectAsync() => Task.FromResult(SavePath);
        public Task<string?> SaveExcelAsync() => Task.FromResult(ExcelPath);
    }
    [AvaloniaFact]
    public void CalculateCommandAndChartKeyboardOperateInWindow()
    {
        var window=new MainWindow(); window.Show();
        window.Model.Example(); window.Model.Pack="16";
        var calculate=window.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()=="Рассчитать →");
        calculate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        // Command execution is covered through the same bound command used by the button.
        calculate.Command!.Execute(null);
        Assert.Equal(304,window.Model.Results.Single(r=>r.Id=="SHIRT-KOL").Value.RecommendedQuantity);
        window.Model.Selected=window.Model.Results.Single(r=>r.Id=="SHIRT-KOL");
        var chart=window.FindControl<InventoryChart>("Chart")!; chart.Focus();
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Assert.Contains("14.10.2026",window.Model.DayInfo);
        window.Model.Example(); window.Close();
    }
}
