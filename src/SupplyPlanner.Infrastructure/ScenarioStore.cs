using System.Text.Json;
using Microsoft.Data.Sqlite;
using SupplyPlanner.Core;

namespace SupplyPlanner.Infrastructure;

public sealed record JournalEntry(DateTimeOffset At, string Event, string Message);
public sealed record Workspace(PlanningScenario Scenario, SupplyPlan? Plan, IReadOnlyList<JournalEntry> Journal);
public sealed record StoredWorkspace(Workspace Value, long Revision);
public sealed record SavedScenario(string Id, string Name, DateTimeOffset SavedAt);
public sealed class ScenarioConflictException : Exception
{
    public ScenarioConflictException() : base("Сценарий изменён в другой вкладке. Обновите страницу перед повторением действия.") { }
}

public static class ScenarioJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 24
    };

    public static PlanningScenario Import(string json)
    {
        var scenario = JsonSerializer.Deserialize<PlanningScenario>(json, Options)
            ?? throw new ArgumentException("Файл не содержит сценарий.");
        if (scenario.Settings is null) throw new ArgumentException("В файле отсутствуют настройки.");
        scenario.Validate();
        return scenario with { IsDemo = false };
    }
}

public sealed class ScenarioStore
{
    private readonly string _connectionString;
    public ScenarioStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "planner.db"), DefaultTimeout = 10
        }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS workspaces (
                owner TEXT PRIMARY KEY, payload TEXT NOT NULL, revision INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS scenarios (
                id TEXT PRIMARY KEY, owner TEXT NOT NULL, name TEXT NOT NULL,
                saved_at TEXT NOT NULL, payload TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS scenarios_owner ON scenarios(owner);
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public void CheckHealth()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM workspaces";
        command.ExecuteScalar();
    }

    public StoredWorkspace LoadOrCreate(string owner)
    {
        using var connection = Open();
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT OR IGNORE INTO workspaces(owner,payload,revision) VALUES($owner,$payload,0)";
            insert.Parameters.AddWithValue("$owner", owner);
            insert.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(
                new Workspace(DemoData.Create(), null, []), ScenarioJson.Options));
            insert.ExecuteNonQuery();
        }
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload,revision FROM workspaces WHERE owner=$owner";
        command.Parameters.AddWithValue("$owner", owner);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("Сценарий не найден.");
        return new(JsonSerializer.Deserialize<Workspace>(reader.GetString(0), ScenarioJson.Options)!, reader.GetInt64(1));
    }

    public void Save(string owner, Workspace value, long expectedRevision, bool backupCurrent = false)
    {
        using var connection = Open();
        // Проверка версии, резервная копия и замена — одна транзакция, без потери изменений другой вкладки.
        using var transaction = connection.BeginTransaction();
        if (backupCurrent)
        {
            using var read = connection.CreateCommand();
            read.Transaction = transaction;
            read.CommandText = "SELECT payload FROM workspaces WHERE owner=$owner AND revision=$revision";
            read.Parameters.AddWithValue("$owner", owner);
            read.Parameters.AddWithValue("$revision", expectedRevision);
            if (read.ExecuteScalar() is not string previous) throw new ScenarioConflictException();
            InsertCopy(connection, transaction, owner,
                JsonSerializer.Deserialize<Workspace>(previous, ScenarioJson.Options)!.Scenario);
        }
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE workspaces SET payload=$payload,revision=revision+1 WHERE owner=$owner AND revision=$revision";
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(value, ScenarioJson.Options));
        command.Parameters.AddWithValue("$owner", owner);
        command.Parameters.AddWithValue("$revision", expectedRevision);
        if (command.ExecuteNonQuery() != 1) throw new ScenarioConflictException();
        transaction.Commit();
    }

    public void SaveCopy(string owner, PlanningScenario scenario)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        InsertCopy(connection, transaction, owner, scenario);
        transaction.Commit();
    }

    private static void InsertCopy(SqliteConnection connection, SqliteTransaction transaction,
        string owner, PlanningScenario scenario)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO scenarios(id,owner,name,saved_at,payload) VALUES($id,$owner,$name,$at,$payload);
            DELETE FROM scenarios WHERE owner=$owner AND id NOT IN
                (SELECT id FROM scenarios WHERE owner=$owner ORDER BY saved_at DESC,rowid DESC LIMIT 20);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$owner", owner);
        command.Parameters.AddWithValue("$name", scenario.Name);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(scenario, ScenarioJson.Options));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<SavedScenario> List(string owner)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,name,saved_at FROM scenarios WHERE owner=$owner ORDER BY saved_at DESC,rowid DESC";
        command.Parameters.AddWithValue("$owner", owner);
        using var reader = command.ExecuteReader();
        var result = new List<SavedScenario>();
        while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2))));
        return result;
    }

    public PlanningScenario ReadCopy(string owner, string id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM scenarios WHERE owner=$owner AND id=$id";
        command.Parameters.AddWithValue("$owner", owner);
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteScalar() is string json
            ? JsonSerializer.Deserialize<PlanningScenario>(json, ScenarioJson.Options)!
            : throw new ArgumentException("Сохранённый сценарий не найден.");
    }
}
