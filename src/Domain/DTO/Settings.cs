using System.Collections.Generic;

namespace MyQuicker.Domain.DTO;

/// <summary>
/// 顶层持久化 DTO，唯一被序列化到 settings.json 的对象。
/// 仅包含纯数据，不持有任何运行时服务引用或 WPF 依赖。
/// </summary>
public sealed class Settings
{
    /// <summary>配置的唤醒触发器列表。</summary>
    public List<TriggerBinding> TriggerBindings { get; set; } = new();

    /// <summary>应用通用偏好设置（截屏、贴图、菜单外观等）。</summary>
    public Preferences Preferences { get; set; } = new();

    /// <summary>菜单分组结构。</summary>
    public List<MenuGroup> MenuGroups { get; set; } = new();

    /// <summary>用户命令目录。ActionItem 通过 CommandId 引用其中条目。</summary>
    public List<CommandDefinition> Commands { get; set; } = new();
}
