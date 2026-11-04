using System.Text.Json;
using SupplyPlanner.Core;
using SupplyPlanner.Infrastructure;

return CliRunner.Run(args, Console.Out, Console.Error);

public static class CliRunner
{
    public const string Help = """
        SupplyPlanner / .NET 10
        demo --output scenario.json [--force]
        calculate --input scenario.json|project.supply.json
          [--output plan.json] [--excel plan.xlsx] [--state project.supply.json]
          [--warehouse Казань] [--search WB-1001] [--strategy average|weighted]
          [--pack-size 16] [--force]
        --help
        Без выходных файлов выводится сводка. --force разрешает замену существующих файлов.
        Фильтры применяются к отчёту и сводке; состояние сохраняет весь сценарий.
        Ошибки: код 2; успех: 0. Desktop: dotnet run --project src/SupplyPlanner.Desktop -- [--open путь] [--help]
        """;
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Length == 0 || args is ["--help"] or ["-h"]) { output.WriteLine(Help); return 0; }
            var command = args[0];
            if (command is not ("demo" or "calculate")) throw new ArgumentException("Неизвестная команда. Используйте --help.");
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 1; i < args.Length; i++)
            {
                var key = args[i];
                var allowed = command == "demo" ? new[] { "--output", "--force" } : new[] { "--input", "--output", "--excel", "--state", "--warehouse", "--search", "--strategy", "--pack-size", "--force" };
                if (!allowed.Contains(key) || options.ContainsKey(key)) throw new ArgumentException("Некорректный или повторный параметр: " + key);
                if (key == "--force") { options[key] = "true"; continue; }
                if (++i >= args.Length || args[i].StartsWith("--")) throw new ArgumentException("Нужно значение: " + key);
                options[key] = args[i];
            }
            bool force = options.ContainsKey("--force");
            string Required(string key) => options.GetValueOrDefault(key) ?? throw new ArgumentException("Нужен " + key);
            if (command == "demo")
            { ProjectFiles.WriteAtomic(Required("--output"), JsonSerializer.Serialize(ProjectFiles.Example(), ScenarioJson.Options), force); output.WriteLine("Пример сохранён."); return 0; }
            var input = Path.GetFullPath(Required("--input"));
            var destinations = new[] { "--output", "--excel", "--state" }.Where(options.ContainsKey).Select(k => Path.GetFullPath(options[k])).ToArray();
            // macOS/Windows обычно не различают регистр; ссылка тоже может вести на исходный файл.
            var comparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            static string Identity(string path) => File.Exists(path) ? new FileInfo(path).ResolveLinkTarget(true)?.FullName ?? path : path;
            var identities = destinations.Select(Identity).ToArray();
            if (identities.Distinct(comparer).Count() != identities.Length || identities.Contains(Identity(input), comparer))
                throw new ArgumentException("Входной файл и выходные пути должны различаться.");
            if (!force && destinations.Any(File.Exists)) throw new ArgumentException("Выходной файл уже существует. Укажите другой путь или --force.");
            var document = ProjectFiles.Read(input);
            var scenario = document.Workspace.Scenario;
            var settings = scenario.Settings;
            if (options.TryGetValue("--strategy", out var strategy)) settings = settings with { Strategy = strategy };
            if (options.TryGetValue("--pack-size", out var pack)) settings = settings with { PackSize = int.Parse(pack) };
            scenario = scenario with { Settings = settings };
            var workspace = PlannerWorkflow.Calculate(document.Workspace, scenario);
            var plan = workspace.Plan!;
            var query = new PlanQuery(options.GetValueOrDefault("--search"), options.GetValueOrDefault("--warehouse"));
            var products = query.Apply(plan.Products);
            // Сначала формируем все отчёты; ошибки расчёта/Excel не оставляют частичный JSON.
            var report = JsonSerializer.Serialize(new { plan.StartDate, plan.Settings, Products = products }, ScenarioJson.Options);
            var excel = options.ContainsKey("--excel") ? PlanExcelExport.Create(scenario, plan, query) : null;
            if (options.TryGetValue("--output", out var result)) ProjectFiles.WriteAtomic(result, report, force);
            if (options.TryGetValue("--excel", out var xlsx)) ProjectFiles.WriteAtomic(xlsx, excel!, force);
            if (options.TryGetValue("--state", out var state)) ProjectFiles.Save(state, new(1, workspace, new(query.Search ?? "", query.Warehouse ?? "")), force);
            foreach (var item in products) output.WriteLine($"{item.Article ?? item.ProductId} | {PlanQuery.WarehouseName(item.Warehouse)} | {PlanLabels.Number(item.RecommendedQuantity)} шт. | {PlanLabels.Status(item.Status)}");
            output.WriteLine($"Позиций: {products.Count}. К поставке: {products.Sum(p => (long)(p.RecommendedQuantity ?? 0))}. Без оценки: {products.Count(p => p.RecommendedQuantity is null)}.");
            return 0;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or JsonException or OverflowException or FormatException)
        { error.WriteLine("Ошибка: " + e.Message); return 2; }
    }
}
