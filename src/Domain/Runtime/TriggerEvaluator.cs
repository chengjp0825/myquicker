using System;
using System.Collections.Generic;
using System.Linq;

namespace MyQuicker.Domain.Runtime;

/// <summary>
/// Owns the polymorphic trigger loop. Receives <see cref="TriggerEvent"/>s from an input source,
/// evaluates them against configured <see cref="ITrigger"/> strategies, and reports matches.
/// </summary>
public sealed class TriggerEvaluator
{
    private readonly List<ITrigger> _triggers = new();

    public event EventHandler<TriggerMatchResult>? MatchEvaluated;

    public void UpdateTriggers(IEnumerable<ITrigger> triggers)
    {
        _triggers.Clear();
        _triggers.AddRange(triggers ?? Enumerable.Empty<ITrigger>());
    }

    public TriggerMatchResult Evaluate(TriggerEvent triggerEvent)
    {
        if (triggerEvent is null)
            throw new ArgumentNullException(nameof(triggerEvent));

        foreach (var trigger in _triggers)
        {
            var result = trigger.Evaluate(triggerEvent);
            if (result.IsMatch)
            {
                MatchEvaluated?.Invoke(this, result);
                return result;
            }
        }

        return TriggerMatchResult.NoMatch;
    }
}
