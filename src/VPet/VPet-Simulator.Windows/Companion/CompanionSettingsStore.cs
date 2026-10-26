using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VPet_Simulator.Windows.Companion;

internal sealed class CompanionSettingsStore
{
    private readonly string settingsPath, keyPath;
    public string? LoadWarning { get; private set; }
    public CompanionSettingsStore(string dataDirectory, string profile)
    {
        settingsPath = Path.Combine(dataDirectory, $"Companion{profile}.json");
        keyPath = Path.Combine(dataDirectory, $"CompanionKey{profile}.bin");
    }
    public bool HasKey => File.Exists(keyPath);
    public CompanionOptions Load()
    {
        try
        {
            if (File.Exists(settingsPath))
            {
                string json = File.ReadAllText(settingsPath);
                var value = JsonSerializer.Deserialize<CompanionOptions>(json) ?? throw new FormatException();
                value.Validate();
                return value;
            }
            string persona = Path.Combine(ApplicationPaths.InstallDirectory, "dialogue", "ai_personality.md");
            return File.Exists(persona) ? new CompanionOptions { Persona = File.ReadAllText(persona) } : new();
        }
        catch (Exception e) when (e is IOException or JsonException or FormatException or ArgumentException or UnauthorizedAccessException)
        {
            LoadWarning = "原有 AI 设置无法读取，已使用默认值；保存前请核对配置。";
            return new();
        }
    }
    public string ReadKey(CompanionOptions options)
    {
        if (!HasKey) return "";
        try
        {
            byte[] bytes = ProtectedData.Unprotect(File.ReadAllBytes(keyPath), null, DataProtectionScope.CurrentUser);
            try
            {
                var credential = JsonSerializer.Deserialize<Credential>(bytes) ?? throw new JsonException();
                if (credential.Endpoint != options.Endpoint().AbsoluteUri)
                    throw new InvalidOperationException("密钥与服务地址不匹配，请重新保存密钥。");
                return credential.Key;
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception e) when (e is CryptographicException or IOException or UnauthorizedAccessException or JsonException)
        { throw new InvalidOperationException("密钥无法解密，请在当前 Windows 账户下重新填写。"); }
    }
    public void Save(CompanionOptions options, string newKey, bool clearKey, bool endpointChanged)
    {
        options.Validate();
        newKey = newKey.Trim();
        if (newKey.Contains('\r') || newKey.Contains('\n') || newKey.Length > 4096) throw new ArgumentException("密钥格式不正确。");
        byte[]? encrypted = string.IsNullOrWhiteSpace(newKey) ? null :
            ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(new Credential(options.Endpoint().AbsoluteUri, newKey.Trim())), null, DataProtectionScope.CurrentUser);
        if ((clearKey || endpointChanged) && File.Exists(keyPath)) File.Delete(keyPath);
        if (encrypted != null) AtomicWrite(keyPath, encrypted);
        AtomicWrite(settingsPath, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true })));
        LoadWarning = null;
    }
    private static void AtomicWrite(string path, byte[] bytes)
    {
        string temporary = path + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private sealed record Credential(string Endpoint, string Key);
}
