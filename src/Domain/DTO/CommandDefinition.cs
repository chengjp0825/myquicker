namespace MyQuicker.Domain.DTO;

/// <summary>
/// A persisted command entry in the user command catalog.
/// Pure data; execution logic lives in the runtime <see cref="Runtime.Commands.ICommand"/> hierarchy.
/// </summary>
public sealed class CommandDefinition
{
    /// <summary>Stable command identifier used by <see cref="ActionItem.CommandId"/>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Human-readable label shown in the command picker.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public CommandType Type { get; set; }

    /// <summary>Application path or URL, depending on <see cref="Type"/>.</summary>
    public string Target { get; set; } = string.Empty;
}
