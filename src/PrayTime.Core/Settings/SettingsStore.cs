using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrayTime.Core.Scheduling;
using PrayTime.Core.Security;

namespace PrayTime.Core.Settings;

/// <summary>
/// تحميل وحفظ الإعدادات وسجل الإطلاق.
/// الحفظ ذرّي دائمًا: نكتب ملفًا مؤقتًا ثم نستبدل، فانقطاع الكهرباء أثناء الكتابة
/// لا يترك ملف JSON مبتورًا يفقد المستخدمَ رمزَه وموقعه.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // حتى تبقى العربية مقروءة في الملف
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _settingsPath;
    private readonly string _backupPath;
    private readonly string _ledgerPath;
    private readonly string _attemptsPath;

    public SettingsStore(string roamingDir, string localDir)
    {
        RoamingDirectory = roamingDir;
        LocalDirectory = localDir;

        Directory.CreateDirectory(roamingDir);
        Directory.CreateDirectory(localDir);

        _settingsPath = Path.Combine(roamingDir, "settings.json");
        _backupPath = Path.Combine(roamingDir, "settings.bak.json");
        _ledgerPath = Path.Combine(localDir, "fired.json");
        _attemptsPath = Path.Combine(localDir, "pin-attempts.json");
    }

    public string RoamingDirectory { get; }
    public string LocalDirectory { get; }
    public string SettingsPath => _settingsPath;

    /// <summary>آخر خطأ واجهه التحميل، لعرضه في السجل بدل ابتلاعه صامتًا.</summary>
    public string? LastLoadError { get; private set; }

    public AppSettings Load()
    {
        LastLoadError = null;

        if (TryRead(_settingsPath, out var settings)) return Migrate(settings!);

        if (File.Exists(_settingsPath) && TryRead(_backupPath, out var backup))
        {
            LastLoadError += " — تمت الاستعادة من النسخة الاحتياطية.";
            return Migrate(backup!);
        }

        return new AppSettings();
    }

    private bool TryRead(string path, out AppSettings? settings)
    {
        settings = null;
        if (!File.Exists(path)) return false;

        try
        {
            var json = File.ReadAllText(path);
            settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return settings is not null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            LastLoadError = $"تعذّرت قراءة {Path.GetFileName(path)}: {ex.Message}";
            return false;
        }
    }

    private static AppSettings Migrate(AppSettings settings)
    {
        // المخطط في نسخته الأولى. أي ترقية مستقبلية تُضاف هنا بالترتيب.
        if (settings.SchemaVersion < AppSettings.CurrentSchemaVersion)
            settings.SchemaVersion = AppSettings.CurrentSchemaVersion;

        return settings;
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        WriteAtomic(_settingsPath, json, keepBackup: true);
    }

    public FiredLedger LoadLedger()
    {
        var ledger = new FiredLedger();
        try
        {
            if (File.Exists(_ledgerPath))
            {
                var keys = JsonSerializer.Deserialize<string[]>(File.ReadAllText(_ledgerPath), JsonOptions);
                if (keys is not null) ledger.Load(keys);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // سجل تالف يعني في أسوأ الحالات تكرار أذان واحد. لا يستحق منع الإقلاع.
        }
        return ledger;
    }

    public void SaveLedger(FiredLedger ledger)
    {
        var json = JsonSerializer.Serialize(ledger.Keys.ToArray(), JsonOptions);
        WriteAtomic(_ledgerPath, json, keepBackup: false);
    }

    public PinAttemptState LoadAttempts()
    {
        try
        {
            if (File.Exists(_attemptsPath))
            {
                var state = JsonSerializer.Deserialize<PinAttemptState>(
                    File.ReadAllText(_attemptsPath), JsonOptions);
                if (state is not null) return state;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
        }
        return new PinAttemptState();
    }

    public void SaveAttempts(PinAttemptState state) =>
        WriteAtomic(_attemptsPath, JsonSerializer.Serialize(state, JsonOptions), keepBackup: false);

    private static void WriteAtomic(string path, string content, bool keepBackup)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);

        if (keepBackup && File.Exists(path))
        {
            var bak = Path.ChangeExtension(path, ".bak.json");
            try { File.Copy(path, bak, overwrite: true); } catch (IOException) { }
        }

        File.Move(tmp, path, overwrite: true);
    }
}
