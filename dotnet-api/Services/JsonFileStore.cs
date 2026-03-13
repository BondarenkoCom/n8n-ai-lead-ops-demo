using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using N8nAiLeadOps.DemoApi.Infrastructure;

namespace N8nAiLeadOps.DemoApi.Services;

public sealed class JsonFileStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);

    public async Task<T> ReadAsync<T>(string filePath, T fallback)
    {
        var fileLock = Locks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync();

        try
        {
            await EnsureJsonFileAsync(filePath, fallback);
            var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(content))
            {
                return fallback;
            }

            return JsonSerializer.Deserialize<T>(content, AppJson.Default) ?? fallback;
        }
        catch
        {
            return fallback;
        }
        finally
        {
            fileLock.Release();
        }
    }

    public async Task WriteAsync<T>(string filePath, T data)
    {
        var fileLock = Locks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync();

        try
        {
            EnsureParentDirectory(filePath);
            var content = JsonSerializer.Serialize(data, AppJson.Default);
            await File.WriteAllTextAsync(filePath, content, Encoding.UTF8);
        }
        finally
        {
            fileLock.Release();
        }
    }

    public async Task AppendJsonLineAsync(string filePath, object data)
    {
        var fileLock = Locks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync();

        try
        {
            EnsureParentDirectory(filePath);
            var line = JsonSerializer.Serialize(data, AppJson.Default);
            await File.AppendAllTextAsync(filePath, $"{line}\n", Encoding.UTF8);
        }
        finally
        {
            fileLock.Release();
        }
    }

    public async Task AppendCsvRowAsync(string filePath, IReadOnlyList<string> headers, IReadOnlyDictionary<string, string?> row)
    {
        var fileLock = Locks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync();

        try
        {
            EnsureParentDirectory(filePath);
            var needsHeader = !File.Exists(filePath) || string.IsNullOrWhiteSpace(await File.ReadAllTextAsync(filePath, Encoding.UTF8));
            var lines = new List<string>();
            if (needsHeader)
            {
                lines.Add(string.Join(",", headers));
            }

            lines.Add(string.Join(",", headers.Select(header => EscapeCsv(row.TryGetValue(header, out var value) ? value : null))));
            await File.AppendAllTextAsync(filePath, $"{string.Join('\n', lines)}\n", Encoding.UTF8);
        }
        finally
        {
            fileLock.Release();
        }
    }

    private static async Task EnsureJsonFileAsync<T>(string filePath, T fallback)
    {
        if (File.Exists(filePath))
        {
            return;
        }

        EnsureParentDirectory(filePath);
        var content = JsonSerializer.Serialize(fallback, AppJson.Default);
        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8);
    }

    private static void EnsureParentDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string EscapeCsv(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Contains(',') || text.Contains('"') || text.Contains('\n'))
        {
            return $"\"{text.Replace("\"", "\"\"")}\"";
        }

        return text;
    }
}
