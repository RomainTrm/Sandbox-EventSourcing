using Sandbox.Common;

namespace Sandbox.DCB.Full;

/// <summary>The state a decider builds from the events it loaded.</summary>
public interface IDeciderState<out TState, in TEvent>
    where TState : IDeciderState<TState, TEvent>
    where TEvent : IEvent
{
    /// <summary>The state before any loaded event is applied.</summary>
    static abstract TState InitialState { get; }

    /// <summary>Folds one loaded event into the state. Events are applied in load order.</summary>
    TState ApplyEvent(TEvent evt);

    /// <summary>Folds the loaded events, in order, starting from <see cref="InitialState"/>.</summary>
    static TState Build(IEnumerable<TEvent> history) => history.Aggregate(TState.InitialState, (state, evt) => state.ApplyEvent(evt));
}
