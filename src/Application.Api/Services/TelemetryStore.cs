using System.Text.Json;
using Application.Api.Models;
using IoT.Contracts;
using Microsoft.Data.Sqlite;

namespace Application.Api.Services;

public sealed class TelemetryStore : IDisposable
{
    private readonly SqliteConnection _connection;

    public TelemetryStore(string databasePath)
    {
        if (databasePath != ":memory:")
        {
            databasePath = Path.GetFullPath(databasePath);
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        }
        _connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString());
        _connection.Open();
        using var command = Command("""
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS Gateways (Key TEXT PRIMARY KEY, Data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Devices (Key TEXT PRIMARY KEY, DeviceId TEXT NOT NULL, Data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Events (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                EventId TEXT NOT NULL UNIQUE,
                DeviceId TEXT,
                AppliedToState INTEGER NOT NULL,
                Data TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Events_DeviceId_Id ON Events (DeviceId, Id);
            """);
        command.ExecuteNonQuery();
    }

    internal SqliteTransaction BeginTransaction() => _connection.BeginTransaction();

    internal bool ContainsEvent(string eventId, SqliteTransaction transaction)
    {
        using var command = Command("SELECT 1 FROM Events WHERE EventId = $id", transaction, ("$id", eventId));
        return command.ExecuteScalar() is not null;
    }

    internal GatewayStatus? GetGateway(string key, SqliteTransaction transaction) =>
        Read<GatewayStatus>("SELECT Data FROM Gateways WHERE Key = $key", transaction, ("$key", key)).SingleOrDefault();

    internal DeviceStatus? GetDevice(string key, SqliteTransaction transaction) =>
        Read<DeviceStatus>("SELECT Data FROM Devices WHERE Key = $key", transaction, ("$key", key)).SingleOrDefault();

    internal GatewayStatus[] GetGateways() => Read<GatewayStatus>("SELECT Data FROM Gateways ORDER BY Key");
    internal DeviceStatus[] GetDevices(SqliteTransaction? transaction = null) =>
        Read<DeviceStatus>("SELECT Data FROM Devices ORDER BY Key", transaction);
    internal DeviceStatus? FindDevice(string deviceId) =>
        Read<DeviceStatus>("SELECT Data FROM Devices WHERE DeviceId = $id ORDER BY Key LIMIT 1", null,
            ("$id", deviceId)).SingleOrDefault();

    internal void Save(GatewayStatus state, SqliteTransaction transaction)
    {
        using var command = Command("""
            INSERT INTO Gateways (Key, Data) VALUES ($key, $data)
            ON CONFLICT(Key) DO UPDATE SET Data = excluded.Data
            """, transaction, ("$key", state.GroupId + "/" + state.GatewayId), ("$data", JsonSerializer.Serialize(state)));
        command.ExecuteNonQuery();
    }

    internal void Save(DeviceStatus state, SqliteTransaction transaction)
    {
        using var command = Command("""
            INSERT INTO Devices (Key, DeviceId, Data) VALUES ($key, $id, $data)
            ON CONFLICT(Key) DO UPDATE SET Data = excluded.Data
            """, transaction, ("$key", state.GroupId + "/" + state.GatewayId + "/" + state.DeviceId),
            ("$id", state.DeviceId), ("$data", JsonSerializer.Serialize(state)));
        command.ExecuteNonQuery();
    }

    internal void SaveEvent(LifecycleEvent item, bool applied, SqliteTransaction transaction)
    {
        using var command = Command("""
            INSERT INTO Events (EventId, DeviceId, AppliedToState, Data) VALUES ($id, $device, $applied, $data)
            """, transaction, ("$id", item.EventId), ("$device", item.DeviceId),
            ("$applied", applied), ("$data", JsonSerializer.Serialize(item)));
        command.ExecuteNonQuery();
    }

    internal HistoryEntry[] GetHistory(string deviceId, int limit, long? before)
    {
        using var command = Command("""
            SELECT Id, AppliedToState, Data FROM Events
            WHERE DeviceId = $device AND ($before IS NULL OR Id < $before)
            ORDER BY Id DESC LIMIT $limit
            """, null, ("$device", deviceId), ("$before", before), ("$limit", limit));
        using var reader = command.ExecuteReader();
        var result = new List<HistoryEntry>();
        while (reader.Read())
            result.Add(new(reader.GetInt64(0), reader.GetBoolean(1),
                JsonSerializer.Deserialize<LifecycleEvent>(reader.GetString(2))!));
        return result.ToArray();
    }

    private T[] Read<T>(string sql, SqliteTransaction? transaction = null, params (string Name, object? Value)[] values)
    {
        using var command = Command(sql, transaction, values);
        using var reader = command.ExecuteReader();
        var result = new List<T>();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<T>(reader.GetString(0))!);
        return result.ToArray();
    }

    private SqliteCommand Command(string sql, SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] values)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    public void Dispose() => _connection.Dispose();
}
