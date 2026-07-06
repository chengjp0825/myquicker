using System.Collections.Generic;
using System.Linq;
using MyQuicker.Domain.DTO;

namespace MyQuicker.Services;

/// <summary>
/// 动作域服务（内存缓存门面）：持有菜单分组的内存缓存，唤醒路径零 IO。
/// 启动时由 <see cref="App.OnStartup"/> 调 <see cref="Init"/> 一次性载入；
/// <see cref="SettingsWindow"/> 保存时调 <see cref="UpdateCache"/> 同步缓存。
/// 极速唤醒渲染规范见 docs/03-ui-and-styling.md §7.4。
/// </summary>
internal static class ActionStore
{
    private static List<MenuGroup> _cachedGroups = new();

    /// <summary>启动时加载到内存缓存（磁盘 IO 仅此一次，由 App.OnStartup 调用）。</summary>
    public static void Init(List<MenuGroup> groups) => _cachedGroups = groups ?? new List<MenuGroup>();

    /// <summary>当前所有动作列表（跨分组扁平化，内存缓存，无 IO）。</summary>
    public static List<ActionItem> GetActions() => _cachedGroups.SelectMany(g => g.Actions).ToList();

    /// <summary>返回缓存的深拷贝，供 SettingsWindow 编辑（隔离未保存编辑，无 IO）。</summary>
    public static List<MenuGroup> LoadForEdit() => Clone(_cachedGroups);

    /// <summary>SettingsWindow 保存后同步内存缓存（落盘由 SettingsManager 统一完成）。</summary>
    public static void UpdateCache(List<MenuGroup> groups) => _cachedGroups = groups ?? new List<MenuGroup>();

    private static List<MenuGroup> Clone(List<MenuGroup> src)
    {
        return src.Select(g => new MenuGroup
        {
            Id = g.Id,
            DisplayName = g.DisplayName,
            Icon = g.Icon,
            Actions = g.Actions.Select(a => new ActionItem
            {
                Name = a.Name,
                CommandId = a.CommandId,
                Arguments = a.Arguments,
                Icon = a.Icon,
            }).ToList(),
        }).ToList();
    }
}
