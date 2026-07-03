using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MyQuicker.Models;

namespace MyQuicker.Services;

/// <summary>
/// 统一配置中心（单例）：持久化 <see cref="SettingsModel"/> 到 settings.json。
/// <see cref="App.OnStartup"/> 调 <see cref="Load"/> 加载（首次自动从旧 appsettings.json
/// 迁移唤醒键与动作列表），<see cref="App.OnExit"/> 调 <see cref="Save"/> 落盘。
/// 同步 IO（File.ReadAllText / File.WriteAllText），不引入异步以保调用时序。Per SPEC 重构。
/// </summary>
internal sealed class SettingsManager
{
    /// <summary>全局单例。静态初始化不执行 IO，真正的加载发生在首次 <see cref="Load"/>。</summary>
    public static SettingsManager Instance { get; } = new();

    /// <summary>当前内存中的配置模型。</summary>
    public SettingsModel Settings { get; private set; } = new();

    private const string SettingsFile = "settings.json";
    private const string LegacyFile = "appsettings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private SettingsManager() { }

    /// <summary>
    /// 从 settings.json 加载。文件不存在时用默认值新建，并自动迁移旧
    /// appsettings.json 的唤醒键与动作列表（JsonDocument 解析，不依赖已删除的 AppSettings 类型）。
    /// 每次调用重读磁盘，保留 MainWindow 唤醒热重载与 SettingsWindow 编辑隔离。
    /// </summary>
    public SettingsModel Load()
    {
        string settingsPath = Path.Combine(AppContext.BaseDirectory, SettingsFile);

        if (File.Exists(settingsPath))
        {
            Settings = ReadSettings(settingsPath);
            return Settings;
        }

        // 首次启动：默认值 + 迁移旧 appsettings.json，随即落盘 settings.json。
        SettingsModel model = new();
        MigrateFromLegacy(model);
        Settings = model;
        Save();
        return Settings;
    }

    /// <summary>把当前 <see cref="Settings"/> 同步写入 settings.json。</summary>
    public void Save()
    {
        string settingsPath = Path.Combine(AppContext.BaseDirectory, SettingsFile);
        string json = JsonSerializer.Serialize(Settings, JsonOptions);
        File.WriteAllText(settingsPath, json);
    }

    /// <summary>
    /// 读取 settings.json 并做空值兜底；脏文件/解析失败回退默认值。
    /// </summary>
    private static SettingsModel ReadSettings(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            SettingsModel? model = JsonSerializer.Deserialize<SettingsModel>(json, JsonOptions);
            if (model is null)
                return new SettingsModel();

            model.Action ??= new ActionSettings();
            model.Snipping ??= new SnippingSettings();
            model.Menu ??= new MenuSettings();
            model.Pin ??= new PinSettings();
            model.Action.Actions ??= new List<ActionItem>();
            return model;
        }
        catch
        {
            return new SettingsModel();
        }
    }

    /// <summary>
    /// 用 JsonDocument 解析旧 appsettings.json，把唤醒键与动作列表迁入 <paramref name="model"/>.Action。
    /// 不依赖已删除的 AppSettings 类型；旧文件不存在或解析失败则保留默认动作。
    /// </summary>
    private static void MigrateFromLegacy(SettingsModel model)
    {
        string legacyPath = Path.Combine(AppContext.BaseDirectory, LegacyFile);
        if (!File.Exists(legacyPath))
            return;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(legacyPath));
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("WakeupMessage", out JsonElement wm) && wm.TryGetInt32(out int wakeup))
                model.Action.WakeupMessage = wakeup;

            if (root.TryGetProperty("XButtonData", out JsonElement xb) && xb.TryGetInt32(out int xbutton))
                model.Action.XButtonData = xbutton;

            if (root.TryGetProperty("Actions", out JsonElement actions) && actions.ValueKind == JsonValueKind.Array)
            {
                model.Action.Actions.Clear();
                foreach (JsonElement el in actions.EnumerateArray())
                {
                    var item = new ActionItem();
                    if (el.TryGetProperty("Name", out JsonElement n) && n.ValueKind == JsonValueKind.String)
                        item.Name = n.GetString() ?? string.Empty;
                    if (el.TryGetProperty("Command", out JsonElement c) && c.ValueKind == JsonValueKind.String)
                        item.Command = c.GetString() ?? string.Empty;
                    if (el.TryGetProperty("Arguments", out JsonElement a) && a.ValueKind == JsonValueKind.String)
                        item.Arguments = a.GetString() ?? string.Empty;
                    model.Action.Actions.Add(item);
                }
            }
        }
        catch
        {
            // 旧文件损坏：保留默认动作，不抛。
        }
    }
}
