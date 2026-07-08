using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Aurora.Domain.DTO;

#pragma warning disable CS0618 // 迁移代码需要读取旧版 ActionItem.Command 字段。

namespace Aurora.Services;

/// <summary>
/// 统一配置中心：负责 <see cref="Settings"/> DTO 的磁盘读写与数据迁移。
/// 仅操作纯数据 DTO，不持有任何运行时对象。实例由组合根创建并注入消费者。
/// </summary>
internal sealed class SettingsManager
{
    /// <summary>当前内存中的 <see cref="Settings"/> DTO。</summary>
    public Settings Settings { get; private set; } = new();

    private const string SettingsFile = "settings.json";
    private const string LegacyFile = "appsettings.json";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public SettingsManager() { }

    /// <summary>
    /// 从 settings.json 加载。文件不存在时用默认值新建，并自动迁移旧
    /// appsettings.json 的触发器与动作列表。
    /// </summary>
    public Settings Load()
    {
        string settingsPath = GetSettingsPath();

        if (File.Exists(settingsPath))
        {
            Settings = ReadSettings(settingsPath);
            return Settings;
        }

        // 首次启动：默认值 + 迁移旧 appsettings.json，随即落盘 settings.json。
        Settings settings = CreateDefaultSettings();
        Settings = settings;
        Save();
        return Settings;
    }

    /// <summary>异步加载 settings.json。</summary>
    public async Task<Settings> LoadAsync()
    {
        string settingsPath = GetSettingsPath();

        if (File.Exists(settingsPath))
        {
            Settings = await ReadSettingsAsync(settingsPath).ConfigureAwait(false);
            return Settings;
        }

        Settings settings = await Task.Run(CreateDefaultSettings).ConfigureAwait(false);
        Settings = settings;
        await SaveAsync().ConfigureAwait(false);
        return Settings;
    }

    /// <summary>把当前 <see cref="Settings"/> 同步写入 settings.json。</summary>
    public void Save()
    {
        Save(Settings);
    }

    /// <summary>用新的 <see cref="Settings"/> DTO 替换当前内存配置并落盘。</summary>
    public void Save(Settings settings)
    {
        if (settings is null)
            throw new ArgumentNullException(nameof(settings));

        Settings = Normalize(settings);
        WriteSettingsToFile(JsonSerializer.Serialize(Settings, JsonOptions));
    }

    /// <summary>异步保存当前 Settings，避免 UI 线程被文件 I/O 阻塞。</summary>
    public Task SaveAsync()
    {
        return SaveAsync(Settings);
    }

    /// <summary>异步保存指定 Settings DTO。</summary>
    public async Task SaveAsync(Settings settings)
    {
        if (settings is null)
            throw new ArgumentNullException(nameof(settings));

        Settings = Normalize(settings);
        await WriteSettingsToFileAsync(JsonSerializer.Serialize(Settings, JsonOptions)).ConfigureAwait(false);
    }

    private static string GetSettingsPath() => Path.Combine(AppContext.BaseDirectory, SettingsFile);

    private static Settings CreateDefaultSettings()
    {
        var settings = new Settings();
        MigrateFromLegacy(settings);

        // 默认触发器：鼠标中键唤醒（首次启动无 settings.json 时注入，否则 TriggerEvaluator 无触发器，唤醒无效）。
        if (settings.TriggerBindings.Count == 0)
        {
            settings.TriggerBindings.Add(new TriggerBinding
            {
                Type = TriggerType.Button,
                WakeupMessage = 0x0207, // NativeMethods.WM_MBUTTONDOWN
                InterceptWakeupKey = true,
            });
        }

        // 首次全新安装且无旧配置时，注入四个常驻默认动作。
        if (settings.MenuGroups.Count == 0)
        {
            SeedDefaultActions(settings);
        }

        return settings;
    }

    /// <summary>
    /// 为首次安装注入四个常驻默认动作：计算器、记事本、我的网页、截图。
    /// 这些动作被视为应用的“默认快捷项”，自带 logo 并在 CLAUDE.md 中声明。
    /// </summary>
    private static void SeedDefaultActions(Settings settings)
    {
        var calc = new CommandDefinition
        {
            Id = "cmd:calc",
            DisplayName = "计算器",
            Type = CommandType.LaunchApplication,
            Target = "calc.exe"
        };
        var notepad = new CommandDefinition
        {
            Id = "cmd:notepad",
            DisplayName = "记事本",
            Type = CommandType.LaunchApplication,
            Target = "notepad.exe"
        };
        var web = new CommandDefinition
        {
            Id = "cmd:moongazer",
            DisplayName = "我的网页",
            Type = CommandType.OpenUrl,
            Target = "https://moongazer.cn"
        };

        settings.Commands.AddRange(new[] { calc, notepad, web });

        settings.MenuGroups.Add(new MenuGroup
        {
            Id = "default",
            DisplayName = "默认",
            Icon = "EFA8",
            Actions = new List<ActionItem>
            {
                new() { Name = "计算器", CommandId = calc.Id, Icon = "E94C" },
                new() { Name = "记事本", CommandId = notepad.Id, Icon = "E8A5" },
                new() { Name = "我的网页", CommandId = web.Id, Icon = "E71E" },
                new() { Name = "截图", CommandId = "sys:snipping", Icon = "E70F" },
            }
        });
    }

    /// <summary>原子写 settings.json：先写临时文件再 Move 覆盖，防中途截断。</summary>
    private static void WriteSettingsToFile(string json)
    {
        string settingsPath = GetSettingsPath();
        string tmpPath = settingsPath + ".tmp";

        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, settingsPath, overwrite: true);
    }

    private static async Task WriteSettingsToFileAsync(string json)
    {
        string settingsPath = GetSettingsPath();
        string tmpPath = settingsPath + ".tmp";

        await File.WriteAllTextAsync(tmpPath, json).ConfigureAwait(false);
        await Task.Run(() => File.Move(tmpPath, settingsPath, overwrite: true)).ConfigureAwait(false);
    }

    private static async Task<Settings> ReadSettingsAsync(string path)
    {
        try
        {
            string json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            return ParseSettingsJson(json, path);
        }
        catch (JsonException)
        {
            await Task.Run(() => BackupCorruptFile(path)).ConfigureAwait(false);
            // KI-4：损坏回退默认配置（含默认触发器+动作），而非空 Settings 导致唤醒瘫痪。
            return await Task.Run(CreateDefaultSettings).ConfigureAwait(false);
        }
        catch
        {
            // KI-4：其他读取异常同样回退默认，保证唤醒链路可用。
            return await Task.Run(CreateDefaultSettings).ConfigureAwait(false);
        }
    }

    /// <summary>读取 settings.json 并做空值兜底；脏文件/解析失败回退默认值。</summary>
    private static Settings ReadSettings(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            return ParseSettingsJson(json, path);
        }
        catch (JsonException)
        {
            BackupCorruptFile(path);
            // KI-4：损坏回退默认配置（含默认触发器+动作），而非空 Settings 导致唤醒瘫痪。
            return CreateDefaultSettings();
        }
        catch
        {
            // KI-4：其他读取异常同样回退默认，保证唤醒链路可用。
            return CreateDefaultSettings();
        }
    }

    private static Settings ParseSettingsJson(string json, string path)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        // 旧格式检测：根节点含 Action 属性即为旧 SettingsModel 结构。
        if (root.TryGetProperty("Action", out _))
        {
            return MigrateFromOldFormat(root);
        }

        Settings? settings = JsonSerializer.Deserialize<Settings>(json, JsonOptions);
        return Normalize(settings ?? new Settings());
    }

    /// <summary>确保 Settings 及其子对象无 null，避免后续调用空引用。</summary>
    private static Settings Normalize(Settings settings)
    {
        settings.TriggerBindings ??= new List<TriggerBinding>();
        settings.Preferences ??= new Preferences();
        settings.Preferences.Snipping ??= new SnippingSettings();
        settings.Preferences.Menu ??= new MenuSettings();
        settings.Preferences.Pin ??= new PinSettings();
        settings.MenuGroups ??= new List<MenuGroup>();
        settings.Commands ??= new List<CommandDefinition>();

        foreach (var group in settings.MenuGroups)
        {
            group.Actions ??= new List<ActionItem>();
        }

        return settings;
    }

    /// <summary>
    /// 把损坏的配置文件重命名为 .bak（同名已存在则覆盖），便于用户找回配置。
    /// 备份失败不抛：回退默认值仍可运行。
    /// </summary>
    private static void BackupCorruptFile(string path)
    {
        try
        {
            string bakPath = path + ".bak";
            if (File.Exists(bakPath))
                File.Delete(bakPath);
            File.Move(path, bakPath);
        }
        catch
        {
            // 备份失败忽略。
        }
    }

    /// <summary>
    /// 将旧 SettingsModel 格式（Action / Snipping / Menu / Pin）迁移为新的
    /// Settings DTO 结构（TriggerBindings / Preferences / MenuGroups）。
    /// </summary>
    private static Settings MigrateFromOldFormat(JsonElement root)
    {
        var settings = new Settings();

        if (root.TryGetProperty("Action", out JsonElement action))
        {
            var binding = new TriggerBinding();

            if (action.TryGetProperty("WakeupMessage", out JsonElement wm) && wm.TryGetInt32(out int wakeup))
            {
                if (wakeup == -1)
                {
                    binding.Type = TriggerType.CircleGesture;
                }
                else
                {
                    binding.Type = TriggerType.Button;
                    binding.WakeupMessage = wakeup;
                }
            }

            if (action.TryGetProperty("XButtonData", out JsonElement xb) && xb.TryGetInt32(out int xbutton))
                binding.XButtonData = xbutton;

            if (action.TryGetProperty("InterceptWakeupKey", out JsonElement intercept) && intercept.ValueKind == JsonValueKind.True)
                binding.InterceptWakeupKey = true;
            else if (intercept.ValueKind == JsonValueKind.False)
                binding.InterceptWakeupKey = false;

            if (action.TryGetProperty("CircleSensitivity", out JsonElement cs) && cs.TryGetInt32(out int sensitivity))
                binding.CircleSensitivity = (CircleSensitivity)sensitivity;

            settings.TriggerBindings.Add(binding);

            // 旧格式把动作列表放在 Action 里；迁移时放进默认菜单组。
            if (action.TryGetProperty("Actions", out JsonElement actions) && actions.ValueKind == JsonValueKind.Array)
            {
                var defaultGroup = new MenuGroup
                {
                    Id = "default",
                    DisplayName = "默认",
                    Icon = "EFA8",
                };

                foreach (JsonElement el in actions.EnumerateArray())
                {
                    var item = new ActionItem();
                    if (el.TryGetProperty("Name", out JsonElement n) && n.ValueKind == JsonValueKind.String)
                        item.Name = n.GetString() ?? string.Empty;
                    if (el.TryGetProperty("Command", out JsonElement c) && c.ValueKind == JsonValueKind.String)
                        item.Command = c.GetString() ?? string.Empty;
                    if (el.TryGetProperty("Arguments", out JsonElement a) && a.ValueKind == JsonValueKind.String)
                        item.Arguments = a.GetString() ?? string.Empty;
                    if (el.TryGetProperty("Icon", out JsonElement ic) && ic.ValueKind == JsonValueKind.String)
                        item.Icon = ic.GetString() ?? "EFA8";
                    defaultGroup.Actions.Add(item);
                }

                if (defaultGroup.Actions.Count > 0)
                    settings.MenuGroups.Add(defaultGroup);
            }
        }

        settings.Preferences.Snipping = DeserializeOrDefault<SnippingSettings>(root, "Snipping");
        settings.Preferences.Menu = DeserializeOrDefault<MenuSettings>(root, "Menu");
        settings.Preferences.Pin = DeserializeOrDefault<PinSettings>(root, "Pin");

        Normalize(settings);
        MigrateActionCommandsIntoCatalog(settings);
        return settings;
    }

    /// <summary>从 JsonElement 读取指定属性并反序列化为 T；失败返回默认值。</summary>
    private static T DeserializeOrDefault<T>(JsonElement root, string propertyName) where T : new()
    {
        if (root.TryGetProperty(propertyName, out JsonElement element))
        {
            try
            {
                T? value = JsonSerializer.Deserialize<T>(element.GetRawText(), JsonOptions);
                if (value is not null)
                    return value;
            }
            catch
            {
                // 忽略单个属性的解析失败，回退默认值。
            }
        }

        return new T();
    }

    /// <summary>
    /// 用 JsonDocument 解析旧 appsettings.json，把触发器与动作列表迁入
    /// <paramref name="settings"/>。
    /// </summary>
    private static void MigrateFromLegacy(Settings settings)
    {
        string legacyPath = Path.Combine(AppContext.BaseDirectory, LegacyFile);
        if (!File.Exists(legacyPath))
            return;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(legacyPath));
            JsonElement root = doc.RootElement;

            var binding = settings.TriggerBindings.Count > 0
                ? settings.TriggerBindings[0]
                : new TriggerBinding();

            if (root.TryGetProperty("WakeupMessage", out JsonElement wm) && wm.TryGetInt32(out int wakeup))
            {
                if (wakeup == -1)
                {
                    binding.Type = TriggerType.CircleGesture;
                }
                else
                {
                    binding.Type = TriggerType.Button;
                    binding.WakeupMessage = wakeup;
                }
            }

            if (root.TryGetProperty("XButtonData", out JsonElement xb) && xb.TryGetInt32(out int xbutton))
                binding.XButtonData = xbutton;

            if (settings.TriggerBindings.Count == 0)
                settings.TriggerBindings.Add(binding);

            if (root.TryGetProperty("Actions", out JsonElement actions) && actions.ValueKind == JsonValueKind.Array)
            {
                var defaultGroup = settings.MenuGroups.Count > 0
                    ? settings.MenuGroups[0]
                    : new MenuGroup { Id = "default", DisplayName = "默认", Icon = "EFA8" };

                defaultGroup.Actions.Clear();
                foreach (JsonElement el in actions.EnumerateArray())
                {
                    var item = new ActionItem();
                    if (el.TryGetProperty("Name", out JsonElement n) && n.ValueKind == JsonValueKind.String)
                        item.Name = n.GetString() ?? string.Empty;
                    if (el.TryGetProperty("Command", out JsonElement c) && c.ValueKind == JsonValueKind.String)
                        item.Command = c.GetString() ?? string.Empty;
                    if (el.TryGetProperty("Arguments", out JsonElement a) && a.ValueKind == JsonValueKind.String)
                        item.Arguments = a.GetString() ?? string.Empty;
                    defaultGroup.Actions.Add(item);
                }

                if (settings.MenuGroups.Count == 0 && defaultGroup.Actions.Count > 0)
                    settings.MenuGroups.Add(defaultGroup);
            }

            MigrateActionCommandsIntoCatalog(settings);
        }
        catch
        {
            // 旧文件损坏：保留默认配置，不抛。
        }
    }

    /// <summary>
    /// 将旧版 <see cref="ActionItem.Command"/> 字符串迁移到 <see cref="Settings.Commands"/> 目录，
    /// 并用 <see cref="ActionItem.CommandId"/> 指向新条目。
    /// </summary>
    internal static void MigrateActionCommandsIntoCatalog(Settings settings)
    {
        var catalog = settings.Commands;
        foreach (var group in settings.MenuGroups)
        {
            foreach (var action in group.Actions)
            {
                if (!string.IsNullOrWhiteSpace(action.CommandId))
                    continue;

                string? raw = action.Command;
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                // sys: commands are built-in; do not create user catalog entries for them.
                if (raw.StartsWith("sys:", StringComparison.Ordinal))
                {
                    action.CommandId = raw;
                    continue;
                }

                var existing = catalog.FirstOrDefault(c =>
                    string.Equals(c.Target, raw, StringComparison.Ordinal));

                if (existing is null)
                {
                    var type = Uri.TryCreate(raw, UriKind.Absolute, out var uri)
                        && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                        ? CommandType.OpenUrl
                        : CommandType.LaunchApplication;

                    existing = new CommandDefinition
                    {
                        Id = $"cmd:{Guid.NewGuid():N}",
                        DisplayName = action.Name,
                        Type = type,
                        Target = raw,
                    };
                    catalog.Add(existing);
                }

                action.CommandId = existing.Id;
            }
        }
    }
}

#pragma warning restore CS0618
