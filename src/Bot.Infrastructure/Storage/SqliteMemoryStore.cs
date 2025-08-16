using Bot.Application.Interfaces;
using Bot.Domain;
using Bot.Domain.Models;
using Microsoft.Data.Sqlite;

namespace Bot.Infrastructure.Storage;

public class SqliteMemoryStore : IMemoryStore
{
    private readonly string _connectionString;

    public SqliteMemoryStore(string connectionString)
    {
        _connectionString = Normalize(connectionString);
        EnsureDbDirectory();
        EnsureSchema();
    }
    public async Task Append(ThreadKey threadKey, ConversationMessage message, CancellationToken cancellationToken)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = @"
        INSERT INTO messages(chat_id, thread_id, role, content, ts)
        VALUES ($chat, $thread, $role, $content, $ts)";
        command.Parameters.AddWithValue("$chat", threadKey.ChatId);
        command.Parameters.AddWithValue("$thread", threadKey.ThreadId);
        command.Parameters.AddWithValue("$role", message.Role);
        command.Parameters.AddWithValue("$content", message.Content);
        command.Parameters.AddWithValue("$ts", message.Ts.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> GetSummary(ThreadKey threadKey, CancellationToken cancellationToken)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT summary FROM thread_summaries WHERE chat_id = $chat AND thread_id = $thread";
        command.Parameters.AddWithValue("@chat", threadKey.ChatId);
        command.Parameters.AddWithValue("@thread", threadKey.ThreadId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    public async Task<IReadOnlyList<ConversationMessage>> LoadRecent(ThreadKey threadKey, int maxItems, CancellationToken cancellationToken)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = @"
        SELECT role, content, ts FROM messages
        WHERE chat_id = $chat AND thread_id = $thread
        ORDER BY ts DESC
        LIMIT $limit;";
        command.Parameters.AddWithValue("$chat", threadKey.ChatId);
        command.Parameters.AddWithValue("$thread", threadKey.ThreadId);
        command.Parameters.AddWithValue("$limit", maxItems);

        var list = new List<ConversationMessage>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var roleStr = reader.GetString(0);
            var content = reader.GetString(1);
            var ts = DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc);
            var role = Enum.Parse<ConversationRole>(roleStr);
            list.Add(new ConversationMessage(role, content, ts));
        }

        list.Reverse();
        return list;

    }

    public async Task UpsertSummary(ThreadKey threadKey, string summary, CancellationToken cancellationToken)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = @"
        INSERT INTO thread_summaries(chat_id, thread_id, summary)
        VALUES ($chat, $thread, $summary)
        ON CONFLICT(chat_id, thread_id) DO UPDATE SET summary = excluded.summary;";
        command.Parameters.AddWithValue("@chat", threadKey.ChatId);
        command.Parameters.AddWithValue("@thread", threadKey.ThreadId);
        command.Parameters.AddWithValue("@summary", summary);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string Normalize(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);

        if (string.IsNullOrWhiteSpace(builder.DataSource))
            builder.DataSource = "/data/memory.db";

        if (!(builder.Mode == default)) builder.Mode = SqliteOpenMode.ReadWriteCreate;

        builder.Cache = SqliteCacheMode.Shared;

        return builder.ToString();
    }

    private void EnsureDbDirectory()
    {
        var builder = new SqliteConnectionStringBuilder(_connectionString);
        var path = builder.DataSource;

        if (string.Equals(path, ":memory:", StringComparison.OrdinalIgnoreCase))
            return;

        if (!Path.IsPathRooted(path))
            path = Path.GetFullPath(path, AppContext.BaseDirectory);

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = @"
        PRAGMA journal_mode = WAL;
        CREATE TABLE IF NOT EXIST messages (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            chat_id INTEGER NOT NULL,
            thread_id INTEGER NOT NULL,
            role TEXT NOT NULL,
            content TEXT NOT NULL,
            ts DATETIME NOT NULL,
        );
        CREATE INDEX IF NOT EXISTS ix_messages_c_t_ts ON messages(chat_id, thread_id, ts);

        CREATE TABLE IF NOT EXISTS thread_summaries (
            chat_id INTEGER NOT NULL,
            thread_id INTEGER NOT NULL,
            summary TEXT NOT NULL,
            PRIMARY KEY(chat_id, thread_id)
        );";
        command.ExecuteNonQuery();
    }
}