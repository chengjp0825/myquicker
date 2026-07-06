using MyQuicker.Domain.Runtime;

namespace MyQuicker.Services;

/// <summary>
/// Simple interception policy driven by a user setting.
/// </summary>
public sealed class InputInterceptionPolicy : IInputInterceptionPolicy
{
    public bool InterceptWakeupKey { get; }

    public InputInterceptionPolicy(bool interceptWakeupKey)
    {
        InterceptWakeupKey = interceptWakeupKey;
    }

    public bool ShouldIntercept(WakeContext context) => InterceptWakeupKey;
}
