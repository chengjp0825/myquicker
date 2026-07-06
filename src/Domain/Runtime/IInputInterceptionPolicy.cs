namespace MyQuicker.Domain.Runtime;

/// <summary>
/// Decides whether a matched input event should be swallowed (not passed to the foreground app).
/// Extracted from <see cref="RawInputSource"/> so the capture adapter stays policy-free.
/// </summary>
public interface IInputInterceptionPolicy
{
    /// <summary>
    /// Determines whether the supplied wake context represents an input that should be intercepted.
    /// </summary>
    /// <param name="context">The wake context produced by a matched trigger.</param>
    /// <returns>True if the input should be swallowed; otherwise false.</returns>
    bool ShouldIntercept(WakeContext context);
}
