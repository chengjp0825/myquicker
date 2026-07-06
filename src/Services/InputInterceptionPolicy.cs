using MyQuicker.Domain.Runtime;

namespace MyQuicker.Services;

/// <summary>
/// Simple interception policy driven by a user setting.
/// </summary>
public sealed class InputInterceptionPolicy : IInputInterceptionPolicy
{
    /// <summary>
    /// Gets a value indicating whether the configured wake-up key should be intercepted.
    /// </summary>
    public bool InterceptWakeupKey { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="InputInterceptionPolicy"/> class.
    /// </summary>
    /// <param name="interceptWakeupKey">True to swallow the configured wake-up key; otherwise false.</param>
    public InputInterceptionPolicy(bool interceptWakeupKey)
    {
        InterceptWakeupKey = interceptWakeupKey;
    }

    /// <summary>
    /// Returns whether the input should be intercepted, based on the configured flag.
    /// </summary>
    /// <param name="context">The wake context produced by a matched trigger.</param>
    /// <returns>The value of <see cref="InterceptWakeupKey"/>.</returns>
    public bool ShouldIntercept(WakeContext context) => InterceptWakeupKey;
}
