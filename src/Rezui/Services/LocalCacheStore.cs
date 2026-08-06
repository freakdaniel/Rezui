using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Rezui.Models;

namespace Rezui.Services;

public enum CacheArea
{
    Account,
    MediaMetadata,
    Comments,
    Covers,
    Library
}

public sealed class LocalCacheStore : IDisposable
{
    private const long MaximumCacheBytes = 512L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly object SqliteInitializationLock = new();
    private static bool _sqliteInitialized;

    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly string _connectionString;
    private bool _initialized;
    private bool _disposed;

    public LocalCacheStore(string? directory = null)
    {
        DirectoryPath = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rezui");
        DatabasePath = Path.Combine(DirectoryPath, "Cache.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    public string DirectoryPath { get; }

    public string DatabasePath { get; }

    public async Task<T?> GetJsonAsync<T>(
        CacheArea area,
        string key,
        CancellationToken cancellationToken = default)
    {
        var bytes = await GetBytesAsync(area, key, cancellationToken);
        return bytes is null ? default : JsonSerializer.Deserialize<T>(bytes, JsonOptions);
    }

    public Task SetJsonAsync<T>(
        CacheArea area,
        string key,
        T value,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default) =>
        SetBytesAsync(
            area,
            key,
            JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions),
            lifetime,
            cancellationToken);

    public async Task<byte[]?> GetBytesAsync(
        CacheArea area,
        string key,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload, compression
            FROM cache_entries
            WHERE area = $area AND cache_key = $key
              AND (expires_utc IS NULL OR expires_utc > $now);
            """;
        command.Parameters.AddWithValue("$area", area.ToString());
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        byte[] result;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var payload = (byte[])reader[0];
            var compression = reader.GetInt32(1);
            result = compression == 1 ? Decompress(payload) : payload;
        }

        await using var touch = connection.CreateCommand();
        touch.CommandText = """
            UPDATE cache_entries SET last_access_utc = $now
            WHERE area = $area AND cache_key = $key;
            """;
        touch.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        touch.Parameters.AddWithValue("$area", area.ToString());
        touch.Parameters.AddWithValue("$key", key);
        await touch.ExecuteNonQueryAsync(cancellationToken);
        return result;
    }

    public async Task SetBytesAsync(
        CacheArea area,
        string key,
        ReadOnlyMemory<byte> value,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureInitializedAsync(cancellationToken);
        var compressed = Compress(value.Span);
        var useCompression = compressed.Length < value.Length;
        var payload = useCompression ? compressed : value.ToArray();
        var now = DateTimeOffset.UtcNow;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO cache_entries (
                area, cache_key, payload, compression, created_utc,
                expires_utc, last_access_utc, payload_size)
            VALUES ($area, $key, $payload, $compression, $created, $expires, $accessed, $size)
            ON CONFLICT(area, cache_key) DO UPDATE SET
                payload = excluded.payload,
                compression = excluded.compression,
                created_utc = excluded.created_utc,
                expires_utc = excluded.expires_utc,
                last_access_utc = excluded.last_access_utc,
                payload_size = excluded.payload_size;
            """;
        command.Parameters.AddWithValue("$area", area.ToString());
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$payload", payload);
        command.Parameters.AddWithValue("$compression", useCompression ? 1 : 0);
        command.Parameters.AddWithValue("$created", now.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$expires", now.Add(lifetime).ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$accessed", now.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$size", payload.Length);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await PruneAsync(connection, cancellationToken);
        ApplyDatabasePermissions();
    }

    public async Task<IReadOnlyList<RecentMedia>> GetRecentAsync(
        string scope,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var result = new List<RecentMedia>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT title, url, image_url, category, opened_utc
            FROM recent_media
            WHERE scope = $scope
            ORDER BY opened_utc DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$scope", scope);
        command.Parameters.AddWithValue("$limit", Math.Max(1, limit));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new RecentMedia(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(4))));
        }

        return result;
    }

    public async Task SaveRecentAsync(
        string scope,
        RecentMedia item,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO recent_media (scope, url, title, image_url, category, opened_utc)
                VALUES ($scope, $url, $title, $image, $category, $opened)
                ON CONFLICT(scope, url) DO UPDATE SET
                    title = excluded.title,
                    image_url = excluded.image_url,
                    category = excluded.category,
                    opened_utc = excluded.opened_utc;
                """;
            command.Parameters.AddWithValue("$scope", scope);
            command.Parameters.AddWithValue("$url", item.Url);
            command.Parameters.AddWithValue("$title", item.Title);
            command.Parameters.AddWithValue("$image", item.ImageUrl);
            command.Parameters.AddWithValue("$category", item.Category);
            command.Parameters.AddWithValue("$opened", item.OpenedAt.ToUnixTimeSeconds());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var trim = connection.CreateCommand())
        {
            trim.Transaction = transaction;
            trim.CommandText = """
                DELETE FROM recent_media
                WHERE scope = $scope AND url NOT IN (
                    SELECT url FROM recent_media
                    WHERE scope = $scope
                    ORDER BY opened_utc DESC LIMIT 20
                );
                """;
            trim.Parameters.AddWithValue("$scope", scope);
            await trim.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        ApplyDatabasePermissions();
    }

    public async Task ImportRecentAsync(
        string scope,
        IEnumerable<RecentMedia> items,
        CancellationToken cancellationToken = default)
    {
        foreach (var item in items.OrderBy(item => item.OpenedAt))
        {
            await SaveRecentAsync(scope, item, cancellationToken);
        }
    }

    public async Task RemoveAreaAsync(
        CacheArea area,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM cache_entries WHERE area = $area;";
        command.Parameters.AddWithValue("$area", area.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            InitializeSqlite();
            Directory.CreateDirectory(DirectoryPath);
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                PRAGMA temp_store = MEMORY;
                PRAGMA busy_timeout = 5000;

                CREATE TABLE IF NOT EXISTS cache_entries (
                    area TEXT NOT NULL,
                    cache_key TEXT NOT NULL,
                    payload BLOB NOT NULL,
                    compression INTEGER NOT NULL,
                    created_utc INTEGER NOT NULL,
                    expires_utc INTEGER,
                    last_access_utc INTEGER NOT NULL,
                    payload_size INTEGER NOT NULL,
                    PRIMARY KEY (area, cache_key)
                ) WITHOUT ROWID;
                CREATE INDEX IF NOT EXISTS ix_cache_expiry
                    ON cache_entries(expires_utc);
                CREATE INDEX IF NOT EXISTS ix_cache_lru
                    ON cache_entries(last_access_utc);

                CREATE TABLE IF NOT EXISTS recent_media (
                    scope TEXT NOT NULL,
                    url TEXT NOT NULL,
                    title TEXT NOT NULL,
                    image_url TEXT NOT NULL,
                    category TEXT NOT NULL,
                    opened_utc INTEGER NOT NULL,
                    PRIMARY KEY (scope, url)
                ) WITHOUT ROWID;

                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await MigrateRecentMediaScopeAsync(connection, cancellationToken);
            await PruneAsync(connection, cancellationToken);
            ApplyDatabasePermissions();
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    /// <summary>
    /// Rebuilds the pre-scoping <c>recent_media</c> table (url-only primary key)
    /// into the per-account layout, carrying old rows over into the shared
    /// empty scope so upgrades do not lose history.
    /// </summary>
    private static async Task MigrateRecentMediaScopeAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var probe = connection.CreateCommand();
        probe.CommandText = """
            SELECT COUNT(*) FROM pragma_table_info('recent_media')
            WHERE name = 'scope';
            """;
        var hasScope = Convert.ToInt64(
            await probe.ExecuteScalarAsync(cancellationToken)) > 0;
        if (hasScope)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE recent_media_scoped (
                scope TEXT NOT NULL,
                url TEXT NOT NULL,
                title TEXT NOT NULL,
                image_url TEXT NOT NULL,
                category TEXT NOT NULL,
                opened_utc INTEGER NOT NULL,
                PRIMARY KEY (scope, url)
            ) WITHOUT ROWID;

            INSERT INTO recent_media_scoped (
                scope, url, title, image_url, category, opened_utc)
            SELECT '', url, title, image_url, category, opened_utc
            FROM recent_media;

            DROP TABLE recent_media;
            ALTER TABLE recent_media_scoped RENAME TO recent_media;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task PruneAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM cache_entries
            WHERE expires_utc IS NOT NULL AND expires_utc <= unixepoch();

            DELETE FROM cache_entries
            WHERE (area, cache_key) IN (
                SELECT area, cache_key
                FROM (
                    SELECT
                        area,
                        cache_key,
                        SUM(payload_size) OVER (
                            ORDER BY last_access_utc DESC, created_utc DESC
                        ) AS retained_bytes
                    FROM cache_entries
                )
                WHERE retained_bytes > $maximum
            );
            """;
        command.Parameters.AddWithValue("$maximum", MaximumCacheBytes);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static byte[] Compress(ReadOnlySpan<byte> value)
    {
        using var output = new MemoryStream();
        using (var compressor = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            compressor.Write(value);
        }

        return output.ToArray();
    }

    private static byte[] Decompress(byte[] value)
    {
        using var input = new MemoryStream(value);
        using var decompressor = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        decompressor.CopyTo(output);
        return output.ToArray();
    }

    private static void InitializeSqlite()
    {
        lock (SqliteInitializationLock)
        {
            if (_sqliteInitialized)
            {
                return;
            }

            SQLitePCL.Batteries_V2.Init();
            _sqliteInitialized = true;
        }
    }

    private static void ApplyOwnerOnlyPermissions(string path)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (PlatformNotSupportedException)
        {
            // The cache remains usable on filesystems without Unix modes.
        }
    }

    private void ApplyDatabasePermissions()
    {
        ApplyOwnerOnlyPermissions(DatabasePath);
        ApplyOwnerOnlyPermissions(DatabasePath + "-wal");
        ApplyOwnerOnlyPermissions(DatabasePath + "-shm");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _initializationLock.Dispose();
        // Microsoft.Data.Sqlite keeps pooled connections alive after the
        // application closes them, which holds the database file open on
        // Windows and prevents the temp directory from being deleted in tests.
        // Clearing the pool for this store's connection string releases those
        // handles so the file can be removed.
        if (_initialized || File.Exists(DatabasePath))
        {
            try
            {
                SqliteConnection.ClearPool(new SqliteConnection(_connectionString));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Clearing the pool is best-effort during shutdown; a failure
                // here must not mask the original dispose path.
            }
        }
    }
}
