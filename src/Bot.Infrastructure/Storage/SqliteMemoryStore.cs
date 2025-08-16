using Bot.Application.Interfaces;
using Bot.Domain;
using Bot.Domain.Models;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Bot.Infrastructure.Storage;

public sealed class SqliteMemoryStore : IMemoryStore
{
    private readonly string _connString;

    static SqliteMemoryStore()
    {
        Batteries_V2.Init();
    }

    public SqliteMemoryStore(string connectionString)
    {
        _connString = Normalize(connectionString);
        EnsureDbDirectory();    // создаём каталог, если надо
        EnsureSchema();         // открываем соединение и создаём таблицы
    }

    private static string Normalize(string cs)
    {
        var b = new SqliteConnectionStringBuilder(cs);

        // БД по умолчанию в /data (папка примонтирована volume'ом)
        if (string.IsNullOrWhiteSpace(b.DataSource))
            b.DataSource = "/data/memory.db";

        // Абсолютный путь (если не :memory:)
        if (!string.Equals(b.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase)
            && !Path.IsPathRooted(b.DataSource))
        {
            b.DataSource = Path.GetFullPath(b.DataSource, AppContext.BaseDirectory);
        }

        // Явно разрешаем создание файла, если не задано
        if (b.Mode == 0)
            b.Mode = SqliteOpenMode.ReadWriteCreate;

        b.Cache = SqliteCacheMode.Shared;

        return b.ToString();
    }

    private void EnsureDbDirectory()
    {
        var b = new SqliteConnectionStringBuilder(_connString);
        var ds = b.DataSource;

        if (string.Equals(ds, ":memory:", StringComparison.OrdinalIgnoreCase))
            return;

        var dir = Path.GetDirectoryName(ds);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir!);
    }

    private void EnsureSchema()
    {
        using var conn = new SqliteConnection(_connString);

        try
        {
            conn.Open();
        }
        catch (Exception ex)
        {
            var ds = new SqliteConnectionStringBuilder(_connString).DataSource;
            throw new InvalidOperationException(
                $"Failed to open SQLite at '{ds}'. Check that the directory exists and is writable, and Mode=ReadWriteCreate.",
                ex);
        }

        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL;";
            _ = pragma.ExecuteScalar();
        }

        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;

            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS messages (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  chat_id    INTEGER NOT NULL,
  thread_id  INTEGER NOT NULL,
  role       TEXT    NOT NULL,
  content    TEXT    NOT NULL,
  ts         DATETIME NOT NULL
);";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"CREATE INDEX IF NOT EXISTS ix_messages_c_t_ts ON messages(chat_id, thread_id, ts);";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS thread_summaries (
  chat_id   INTEGER NOT NULL,
  thread_id INTEGER NOT NULL,
  summary   TEXT    NOT NULL,
  PRIMARY KEY(chat_id, thread_id)
);";
            cmd.ExecuteNonQuery();

            tx.Commit();
        }
    }
    public async Task Append(ThreadKey key, ConversationMessage message, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO messages(chat_id, thread_id, role, content, ts)
VALUES ($chat, $thread, $role, $content, $ts);";
        cmd.Parameters.AddWithValue("$chat", key.ChatId);
        cmd.Parameters.AddWithValue("$thread", key.ThreadId);
        cmd.Parameters.AddWithValue("$role", message.Role.ToString());
        cmd.Parameters.AddWithValue("$content", message.Content);
        cmd.Parameters.AddWithValue("$ts", message.Ts.UtcDateTime);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ConversationMessage>> LoadRecent(ThreadKey key, int maxItems, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT role, content, ts FROM messages
WHERE chat_id = $chat AND thread_id = $thread
ORDER BY ts DESC
LIMIT $limit;";
        cmd.Parameters.AddWithValue("$chat", key.ChatId);
        cmd.Parameters.AddWithValue("$thread", key.ThreadId);
        cmd.Parameters.AddWithValue("$limit", maxItems);

        var list = new List<ConversationMessage>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var role = Enum.Parse<ConversationRole>(reader.GetString(0));
            var content = reader.GetString(1);
            var ts = DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc);
            list.Add(new ConversationMessage(role, content, ts));
        }
        list.Reverse();
        return list;
    }

    public async Task UpsertSummary(ThreadKey key, string summary, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO thread_summaries(chat_id, thread_id, summary)
VALUES ($chat, $thread, $summary)
ON CONFLICT(chat_id, thread_id) DO UPDATE SET summary = excluded.summary;";
        cmd.Parameters.AddWithValue("$chat", key.ChatId);
        cmd.Parameters.AddWithValue("$thread", key.ThreadId);
        cmd.Parameters.AddWithValue("$summary", summary);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<string?> GetSummary(ThreadKey key, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT summary FROM thread_summaries WHERE chat_id = $chat AND thread_id = $thread;";
        cmd.Parameters.AddWithValue("$chat", key.ChatId);
        cmd.Parameters.AddWithValue("$thread", key.ThreadId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }
}
