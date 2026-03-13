using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace N8nAiLeadOps.DemoApi.Infrastructure;

public static class AppJson
{
    public static JsonSerializerOptions Default { get; } = Create();

    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        return options;
    }

    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.PropertyNameCaseInsensitive = true;
        if (!options.Converters.OfType<JsonStringEnumConverter>().Any())
        {
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        }
    }
}

public static class SystemClock
{
    public static string UtcNow() => DateTime.UtcNow.ToString("O");
}

public static class Hashing
{
    public static string CreateDeterministicHash(JsonNode? node)
    {
        var payload = node?.ToJsonString(AppJson.Default) ?? "{}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public static class JsonNodeExtensions
{
    public static JsonObject AsObject(this JsonNode? node)
    {
        return node as JsonObject ?? new JsonObject();
    }

    public static JsonObject DeepCloneObject(this JsonObject? node)
    {
        return node?.DeepClone() as JsonObject ?? new JsonObject();
    }

    public static string? GetTrimmedString(this JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text))
            {
                var normalized = text.Trim();
                return normalized.Length > 0 ? normalized : null;
            }

            if (value.TryGetValue<int>(out var intValue))
            {
                return intValue.ToString(CultureInfo.InvariantCulture);
            }

            if (value.TryGetValue<long>(out var longValue))
            {
                return longValue.ToString(CultureInfo.InvariantCulture);
            }

            if (value.TryGetValue<decimal>(out var decimalValue))
            {
                return decimalValue.ToString(CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    public static int? GetFlexibleInt(this JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<int>(out var intValue))
            {
                return intValue;
            }

            if (value.TryGetValue<long>(out var longValue) && longValue is >= int.MinValue and <= int.MaxValue)
            {
                return (int)longValue;
            }

            if (value.TryGetValue<decimal>(out var decimalValue))
            {
                return decimal.ToInt32(decimal.Truncate(decimalValue));
            }

            if (value.TryGetValue<string>(out var text))
            {
                var digits = new string(text.Where(character => char.IsDigit(character) || character == '.').ToArray());
                if (decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                {
                    return decimal.ToInt32(decimal.Truncate(parsed));
                }
            }
        }

        return null;
    }

    public static T? DeserializeNode<T>(this JsonNode? node)
    {
        return node is null ? default : node.Deserialize<T>(AppJson.Default);
    }
}
