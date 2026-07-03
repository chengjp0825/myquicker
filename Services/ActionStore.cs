using System.Collections.Generic;
using MyQuicker.Models;

namespace MyQuicker.Services;

/// <summary>
/// 动作域服务（门面）：封装唤醒键与动作列表的读写，全部委托
/// <see cref="SettingsManager"/> 单例，自身不做任何文件 IO。Per SPEC 重构。
/// 原 SettingsManager 的职责已上移到 SettingsManager 单例 + SettingsModel.Action，
/// 此处仅提供动作域的精简访问面，避免调用方直接触及单例内部结构。
/// </summary>
internal sealed class ActionStore
{
    /// <summary>
    /// 读取当前动作+唤醒键配置。每次重读磁盘（经单例 Load），保留
    /// MainWindow 唤醒热重载与 SettingsWindow 编辑隔离。
    /// </summary>
    public ActionSettings Load() => SettingsManager.Instance.Load().Action;

    /// <summary>当前动作列表（每次重读磁盘，供 MainWindow 唤醒时刷新）。</summary>
    public List<ActionItem> GetActions() => Load().Actions;

    /// <summary>
    /// 持久化动作+唤醒键配置：写回单例内存模型并落盘 settings.json。
    /// </summary>
    public void Save(ActionSettings action)
    {
        SettingsManager.Instance.Settings.Action = action;
        SettingsManager.Instance.Save();
    }
}
