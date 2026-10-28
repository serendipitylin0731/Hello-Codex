using System.Text.Json;

namespace PlusQuotaLauncher.Core;

public static class QuotaParser
{
    public static AccountInfo ParseAccount(JsonElement result)
    {
        if (!result.TryGetProperty("account", out var account) || account.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return new AccountInfo("none", null, null);
        return new AccountInfo(
            GetString(account, "type") ?? "unknown",
            GetString(account, "email"),
            GetString(account, "planType"));
    }

    public static QuotaWindow? ParseWindow(JsonElement result, string limitId, int requiredMinutes)
    {
        JsonElement snapshot = default;
        var found = false;
        if (result.TryGetProperty("rateLimitsByLimitId", out var buckets) &&
            buckets.ValueKind == JsonValueKind.Object &&
            buckets.TryGetProperty(limitId, out snapshot))
        {
            found = true;
        }
        else if (result.TryGetProperty("rateLimits", out var fallback) && fallback.ValueKind == JsonValueKind.Object)
        {
            var fallbackId = GetString(fallback, "limitId");
            if (string.IsNullOrWhiteSpace(fallbackId) || fallbackId == limitId)
            {
                snapshot = fallback;
                found = true;
            }
        }

        if (!found)
            return null;

        foreach (var name in new[] { "primary", "secondary" })
        {
            if (!snapshot.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object)
                continue;
            var duration = GetInt32(window, "windowDurationMins");
            var resetsAt = GetInt64(window, "resetsAt");
            if (duration != requiredMinutes || resetsAt is null or <= 0)
                continue;
            return new QuotaWindow(
                GetString(snapshot, "limitId") ?? limitId,
                name,
                GetDouble(window, "usedPercent"),
                duration.Value,
                resetsAt.Value);
        }
        return null;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt32(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : null;

    private static long? GetInt64(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt64(out var number) ? number : null;

    private static double? GetDouble(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetDouble(out var number) ? number : null;
}
