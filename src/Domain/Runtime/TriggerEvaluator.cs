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

    /// <summary>
    /// Raised whenever a trigger evaluation produces a match.
    /// </summary>
    public event EventHandler<TriggerMatchResult>? MatchEvaluated;

    /// <summary>
    /// Replaces the current trigger list with the supplied triggers.
    /// </summary>
    /// <param name="triggers">The triggers to evaluate against; null is treated as an empty list.</param>
    public void UpdateTriggers(IEnumerable<ITrigger> triggers)
    {
        _triggers.Clear();
        _triggers.AddRange(triggers ?? Enumerable.Empty<ITrigger>());
    }

    /// <summary>
    /// Evaluates the supplied event against the configured triggers and returns the first match.
    /// </summary>
    /// <param name="triggerEvent">The input event to evaluate.</param>
    /// <returns>A <see cref="TriggerMatchResult"/> describing whether a trigger matched and its context.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="triggerEvent"/> is null.</exception>
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
