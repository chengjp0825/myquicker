using System.Collections.Generic;
using System.Linq;
using MyQuicker.Models;

namespace MyQuicker.Services;

/// <summary>
/// 动作域服务（内存缓存门面）：持有动作列表与唤醒键的内存缓存，唤醒路径零 IO。
/// 启动时由 <see cref="App.OnStartup"/> 调 <see cref="Init"/> 一次性载入；
/// <see cref="SettingsWindow"/> 保存时调 <see cref="UpdateCache"/> 同步缓存。
/// 极速唤醒渲染规范见 docs/03-ui-and-styling.md §7.4。
/// </summary>
internal static class ActionStore
{
    private static ActionSettings _cached = new();

    /// <summary>启动时加载到内存缓存（磁盘 IO 仅此一次，由 App.OnStartup 调用）。</summary>
    public static void Init(ActionSettings action) => _cached = action;

    /// <summary>当前动作列表（内存缓存，无 IO），供 MainWindow 唤醒时绑定。</summary>
    public static List<ActionItem> GetActions() => _cached.Actions;

    /// <summary>返回缓存的深拷贝，供 SettingsWindow 编辑（隔离未保存编辑，无 IO）。</summary>
    public static ActionSettings LoadForEdit() => Clone(_cached);

    /// <summary>SettingsWindow 保存后同步内存缓存（落盘由 SettingsManager 统一完成）。</summary>
    public static void UpdateCache(ActionSettings action) => _cached = action;

    private static ActionSettings Clone(ActionSettings src) => new()
    {
        WakeupMessage = src.WakeupMessage,
        XButtonData = src.XButtonData,
        Actions = src.Actions.Select(a => new ActionItem
        {
            Name = a.Name,
            Command = a.Command,
            Arguments = a.Arguments,
        }).ToList(),
    };
}
