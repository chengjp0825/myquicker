namespace MyQuicker.Domain.Runtime;

/// <summary>触发器输入事件：由 RawInputSource 从底层鼠标钩子翻译而来。</summary>
public sealed record TriggerEvent(
    TriggerEventType EventType,
    Point Location,
    long Timestamp,
    int? Message = null,
    int? XButtonData = null);
