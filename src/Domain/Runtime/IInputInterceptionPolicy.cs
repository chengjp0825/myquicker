namespace MyQuicker.Domain.Runtime;

/// <summary>
/// Decides whether a matched input event should be swallowed (not passed to the foreground app).
/// Extracted from <see cref="RawInputSource"/> so the capture adapter stays policy-free.
/// </summary>
public interface IInputInterceptionPolicy
{
    bool ShouldIntercept(WakeContext context);
}
