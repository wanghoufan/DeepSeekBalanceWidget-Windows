using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeepSeekBalanceWidget.Models;

namespace DeepSeekBalanceWidget.Services;

public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _dir;
    private readonly string _filePath;
    private readonly object _writeLock = new();
    private AppConfig? _lastConfig;

    public ConfigService()
    {
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DeepSeekBalanceWidget");
        _filePath = Path.Combine(_dir, "config.json");
    }

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return _lastConfig = new AppConfig();
            var json = File.ReadAllText(_filePath, Encoding.UTF8);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts);
            if (cfg is null) throw new InvalidDataException("config null");
            cfg.Normalize();
            return _lastConfig = cfg;
        }
        catch
        {
            TryBackupCorrupt();
            return _lastConfig = new AppConfig();
        }
    }

    private void TryBackupCorrupt()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var bak = $"{_filePath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
            File.Move(_filePath, bak);
        }
        catch { }
    }

    public void Save(AppConfig cfg)
    {
        lock (_writeLock)
        {
            Directory.CreateDirectory(_dir);
            CleanupTmpFiles();
            var tmp = _filePath + ".tmp";
            var json = JsonSerializer.Serialize(cfg, JsonOpts);
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            try
            {
                if (File.Exists(_filePath)) File.Replace(tmp, _filePath, null);
                else File.Move(tmp, _filePath);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
            _lastConfig = cfg;
        }
    }

    private void CleanupTmpFiles()
    {
        if (!Directory.Exists(_dir)) return;
        foreach (var f in Directory.GetFiles(_dir, "config.json.tmp*"))
        {
            try { File.Delete(f); } catch { }
        }
    }

    public string? GetApiKey()
    {
        var b64 = _lastConfig?.ApiKeyEncrypted;
        if (string.IsNullOrEmpty(b64)) return null;
        try
        {
            var enc = Convert.FromBase64String(b64);
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser));
        }
        catch { return null; }
    }

    public void SetApiKey(AppConfig cfg, string? plainKey)
    {
        if (string.IsNullOrEmpty(plainKey)) { cfg.ApiKeyEncrypted = null; return; }
        var enc = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plainKey), null, DataProtectionScope.CurrentUser);
        cfg.ApiKeyEncrypted = Convert.ToBase64String(enc);
    }

    public string? GetOpenCodeApiKey()
    {
        var b64 = _lastConfig?.OpenCodeApiKeyEncrypted;
        if (string.IsNullOrEmpty(b64)) return null;
        try
        {
            var enc = Convert.FromBase64String(b64);
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser));
        }
        catch { return null; }
    }

    public void SetOpenCodeApiKey(AppConfig cfg, string? plainKey)
    {
        if (string.IsNullOrEmpty(plainKey)) { cfg.OpenCodeApiKeyEncrypted = null; return; }
        var enc = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plainKey), null, DataProtectionScope.CurrentUser);
        cfg.OpenCodeApiKeyEncrypted = Convert.ToBase64String(enc);
    }
}
