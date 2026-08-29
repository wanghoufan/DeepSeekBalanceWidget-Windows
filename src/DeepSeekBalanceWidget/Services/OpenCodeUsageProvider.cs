using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DeepSeekBalanceWidget.Models;

namespace DeepSeekBalanceWidget.Services;

/// <summary>
/// OpenCode Go 额度数据源：官方用量接口
/// GET https://opencode.ai/zen/go/v1/usage（Bearer API Key）。
/// 返回 rolling（5 小时）/ weekly / monthly 三个窗口的已用百分比与重置时间。
/// API Key 解析顺序：显式传入 → 本机 ~/.local/share/opencode/auth.json 中的 opencode 条目。
/// </summary>
public sealed class OpenCodeUsageProvider : IOpenCodeUsageProvider, IDisposable
{
    public const string RollingKind = "rolling";
    public const string WeeklyKind = "weekly";
    public const string MonthlyKind = "monthly";

    private const string UsageUrl = "https://opencode.ai/zen/go/v1/usage";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string? _explicitApiKey;
    private readonly string _authJsonPath;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public OpenCodeUsageProvider(
        string? explicitApiKey = null,
        string? authJsonPath = null,
        HttpClient? httpClient = null)
    {
        _explicitApiKey = explicitApiKey;
        _authJsonPath = authJsonPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "opencode", "auth.json");
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _ownsHttpClient = httpClient is null;
    }

    public async Task<OpenCodeUsageSnapshot> GetUsageAsync(CancellationToken cancellationToken)
    {
        string? apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            return OpenCodeUsageSnapshot.Unavailable("未配置 API Key");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized
                || response.StatusCode == HttpStatusCode.Forbidden)
                return OpenCodeUsageSnapshot.Unavailable("API Key 无效（401）");

            if (!response.IsSuccessStatusCode)
                return OpenCodeUsageSnapshot.Unavailable($"服务返回状态码 {(int)response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return OpenCodeUsageParser.Parse(json)
                ?? OpenCodeUsageSnapshot.Unavailable("响应格式无法解析");
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex)
        {
            return OpenCodeUsageSnapshot.Unavailable("网络请求失败：" + ex.Message);
        }
        catch (JsonException)
        {
            return OpenCodeUsageSnapshot.Unavailable("响应格式无法解析");
        }
    }

    /// <summary>Key 解析：显式 Key 优先；否则读 auth.json 中的 opencode 条目。</summary>
    public string? ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_explicitApiKey)) return _explicitApiKey;
        return ReadKeyFromAuthJson(_authJsonPath);
    }

    internal static string? ReadKeyFromAuthJson(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("opencode", out var entry)) return null;
            // 兼容两种形态：{ "type": "api", "key": "sk-..." } 或直接字符串
            if (entry.ValueKind == JsonValueKind.String)
                return entry.GetString();
            if (entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("key", out var key)
                && key.ValueKind == JsonValueKind.String)
                return key.GetString();
            return null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _httpClient.Dispose();
    }
}

/// <summary>解析 /zen/go/v1/usage 的 JSON 响应（独立静态类便于单元测试）。</summary>
public static class OpenCodeUsageParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class UsageResponse
    {
        [JsonPropertyName("usage")]
        public UsageBody? Usage { get; set; }
    }

    private sealed class UsageBody
    {
        [JsonPropertyName("rolling")]
        public UsageWindowDto? Rolling { get; set; }

        [JsonPropertyName("weekly")]
        public UsageWindowDto? Weekly { get; set; }

        [JsonPropertyName("monthly")]
        public UsageWindowDto? Monthly { get; set; }
    }

    private sealed class UsageWindowDto
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("percent")]
        public int Percent { get; set; }

        [JsonPropertyName("resetsAt")]
        public JsonElement ResetsAt { get; set; }
    }

    public static OpenCodeUsageSnapshot? Parse(string json)
    {
        UsageResponse? response;
        try { response = JsonSerializer.Deserialize<UsageResponse>(json, JsonOptions); }
        catch (JsonException) { return null; }
        if (response?.Usage is null) return null;

        var windows = new List<OpenCodeUsageWindow>();
        AddWindow(windows, OpenCodeUsageProvider.RollingKind, response.Usage.Rolling);
        AddWindow(windows, OpenCodeUsageProvider.WeeklyKind, response.Usage.Weekly);
        AddWindow(windows, OpenCodeUsageProvider.MonthlyKind, response.Usage.Monthly);
        if (windows.Count == 0) return null;
        return new OpenCodeUsageSnapshot(true, null, windows);
    }

    private static void AddWindow(List<OpenCodeUsageWindow> target, string kind, UsageWindowDto? dto)
    {
        if (dto is null) return;
        int used = Math.Clamp(dto.Percent, 0, 100);
        target.Add(new OpenCodeUsageWindow(kind, used, 100 - used, ParseResetsAt(dto.ResetsAt)));
    }

    /// <summary>
    /// resetsAt 容忍三种形态：RFC3339 字符串、unix 秒、epoch 毫秒。
    /// </summary>
    public static DateTimeOffset? ParseResetsAt(JsonElement element)
    {
        try
        {
            return element.ValueKind switch
            {
                JsonValueKind.String
                    when DateTimeOffset.TryParse(element.GetString(), out var parsed) => parsed,
                JsonValueKind.Number
                    when element.TryGetInt64(out var raw) => raw switch
                    {
                        // 粗略分界：大于 1e12 视为毫秒，否则视为秒
                        > 1_000_000_000_000 => DateTimeOffset.FromUnixTimeMilliseconds(raw),
                        _ => DateTimeOffset.FromUnixTimeSeconds(raw)
                    },
                _ => null
            };
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
