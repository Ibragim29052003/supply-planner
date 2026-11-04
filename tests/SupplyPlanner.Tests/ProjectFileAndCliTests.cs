using System.Text.Json;
using SupplyPlanner.Core;
using SupplyPlanner.Infrastructure;

namespace SupplyPlanner.Tests;

public sealed class ProjectFileAndCliTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "planner-files-" + Guid.NewGuid().ToString("N"));
    public ProjectFileAndCliTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);
    private string FilePath(string name) => Path.Combine(_dir, name);
    [Fact]
    public void StateRoundTripRestoresScenarioJournalViewAndRecalculatesResult()
    {
        var scenario = ProjectFiles.Example();
        var workspace = PlannerWorkflow.Calculate(new(scenario, null, []), scenario);
        var view = new ViewState("WB-1001", "Казань", "urgent", "quantity", "SHIRT-KZN");
        var path = FilePath("project.json");
        ProjectFiles.Save(path, new(1, workspace, view));
        var text = File.ReadAllText(path);
        Assert.Contains("\"plan\": null", text);
        var loaded = ProjectFiles.Read(path);
        Assert.Equal(view, loaded.View);
        Assert.Equal(workspace.Journal, loaded.Workspace.Journal);
        Assert.Equal(692, loaded.Workspace.Plan!.Products.Sum(x => x.RecommendedQuantity));
        Assert.Equal(JsonSerializer.Serialize(scenario), JsonSerializer.Serialize(loaded.Workspace.Scenario));
    }
    [Fact]
    public void UnsupportedInvalidOrOversizedProjectsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => ProjectFiles.Parse("{\"formatVersion\":2,\"workspace\":null,\"view\":null}"));
        Assert.ThrowsAny<JsonException>(() => ProjectFiles.Parse("not-json"));
        Assert.Throws<ArgumentException>(() => ProjectFiles.Parse(new string(' ', ProjectFiles.MaxBytes+1)));
        Assert.Throws<JsonException>(() => ProjectFiles.Parse("{\"extra\":true}"));
    }
    [Fact]
    public void AtomicSaveWithoutOverwriteKeepsExistingData()
    {
        var path = FilePath("existing.txt"); File.WriteAllText(path,"keep");
        Assert.Throws<IOException>(() => ProjectFiles.WriteAtomic(path,"replace",false));
        Assert.Equal("keep", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_dir));
    }
    [Fact]
    public void CliAndLibraryProduceIdenticalFilteredResultsAndRejectOverwrite()
    {
        var input = FilePath("source.json"); var result = FilePath("result.json"); var state = FilePath("state.json"); var excel = FilePath("report.xlsx");
        Assert.Equal(0, CliRunner.Run(["demo","--output",input],TextWriter.Null,TextWriter.Null));
        var output = new StringWriter();
        Assert.Equal(0,CliRunner.Run(["calculate","--input",input,"--output",result,"--state",state,"--excel",excel,"--warehouse","Казань"],output,TextWriter.Null));
        using var json = JsonDocument.Parse(File.ReadAllText(result));
        var rows = json.RootElement.GetProperty("products").EnumerateArray().ToArray();
        Assert.Equal(2,rows.Length); Assert.Equal(132,rows.Sum(r=>r.GetProperty("recommendedQuantity").GetInt32()));
        Assert.Equal(5,ProjectFiles.Read(state).Workspace.Scenario.Products.Count);
        Assert.Contains("К поставке: 132",output.ToString()); Assert.True(new FileInfo(excel).Length>1000);
        Assert.Equal(2,CliRunner.Run(["demo","--output",input],TextWriter.Null,TextWriter.Null));
        Assert.Equal(2,CliRunner.Run(["calculate","--input",input,"--output",input,"--force"],TextWriter.Null,TextWriter.Null));
    }
    [Fact]
    public void CliValidationDoesNotWriteAnyOutputOnInvalidSettings()
    {
        var input=FilePath("input.json"); var output=FilePath("output.json");
        CliRunner.Run(["demo","--output",input],TextWriter.Null,TextWriter.Null);
        Assert.Equal(2,CliRunner.Run(["calculate","--input",input,"--pack-size","0","--output",output],TextWriter.Null,TextWriter.Null));
        Assert.False(File.Exists(output));
        Assert.Equal(2,CliRunner.Run(["calculate","--input",input,"--wat"],TextWriter.Null,TextWriter.Null));
    }
    [Theory]
    [InlineData("01-base.json", 692, 132)]
    [InlineData("02-late-delivery.json", 779, 152)]
    [InlineData("03-early-incoming.json", 749, 152)]
    public void DemonstrationFilesRoundTripThroughCliStateAndFilteredExcel(string file, int total, int kazan)
    {
        var input = Path.Combine(AppContext.BaseDirectory, "examples", file);
        var state = FilePath("state.supply.json"); var excel = FilePath("kazan.xlsx");
        Assert.Equal(0, CliRunner.Run(["calculate", "--input", input, "--state", state, "--warehouse", "Казань", "--excel", excel], TextWriter.Null, TextWriter.Null));
        var loaded = ProjectFiles.Read(state);
        Assert.Equal(total, loaded.Workspace.Plan!.Products.Sum(p => p.RecommendedQuantity));
        Assert.Equal(5, loaded.Workspace.Scenario.Products.Count);
        using var book = new ClosedXML.Excel.XLWorkbook(excel);
        Assert.Equal(kazan, book.Worksheet(1).Table("WarehouseTotals").DataRange!.Cell(1, 2).GetValue<int>());
        var second = new StringWriter();
        Assert.Equal(0, CliRunner.Run(["calculate", "--input", state], second, TextWriter.Null));
        Assert.Contains($"К поставке: {total}.", second.ToString());
    }
    [Fact]
    public void InvalidDemonstrationFileIsRejectedWithoutCreatingOutput()
    {
        var input = Path.Combine(AppContext.BaseDirectory, "examples", "04-invalid-duplicate.json");
        var output = FilePath("invalid.xlsx");
        Assert.Throws<ArgumentException>(() => ProjectFiles.Read(input));
        Assert.Equal(2, CliRunner.Run(["calculate", "--input", input, "--excel", output], TextWriter.Null, TextWriter.Null));
        Assert.False(File.Exists(output));
    }
    [Fact]
    public void ForceCannotOverwriteInputThroughSymbolicLink()
    {
        if (OperatingSystem.IsWindows()) return; // Создание ссылок в Windows может требовать отдельного права.
        var input = FilePath("input.json");
        CliRunner.Run(["demo", "--output", input], TextWriter.Null, TextWriter.Null);
        var before = File.ReadAllText(input);
        var alias = FilePath("alias.json"); File.CreateSymbolicLink(alias, input);
        Assert.Equal(2, CliRunner.Run(["calculate", "--input", alias, "--output", input, "--force"], TextWriter.Null, TextWriter.Null));
        Assert.Equal(before, File.ReadAllText(input));
    }
    [Fact]
    public void ForceCannotOverwriteInputWithDifferentFilenameCaseOnMacAndWindows()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows()) return;
        var input = FilePath("input.json");
        CliRunner.Run(["demo", "--output", input], TextWriter.Null, TextWriter.Null);
        var before = File.ReadAllText(input);
        Assert.Equal(2, CliRunner.Run(["calculate", "--input", input, "--output", FilePath("INPUT.JSON"), "--force"], TextWriter.Null, TextWriter.Null));
        Assert.Equal(before, File.ReadAllText(input));
    }
    [Fact]
    public void SettingsPersistAndInvalidThemeIsRejected()
    {
        var path=FilePath("settings.json"); var value=new UserPreferences("light",1100,760);
        ProjectFiles.SavePreferences(path,value); Assert.Equal(value,ProjectFiles.LoadPreferences(path));
        File.WriteAllText(path,"{\"theme\":\"broken\"}");
        Assert.Throws<ArgumentException>(()=>ProjectFiles.LoadPreferences(path));
    }
}
