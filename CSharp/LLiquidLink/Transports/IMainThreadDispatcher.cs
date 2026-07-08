using System;

namespace LLiquidLink
{
    /// <summary>Abstraction for dispatching actions onto Unity's main thread.</summary>
    public interface IMainThreadDispatcher
    {
        /// <summary>Enqueue <paramref name="action"/> to be executed on the main thread.</summary>
        /// <param name="action">Action to dispatch.</param>
        void Enqueue(Action action);

        /// <summary>Start draining the action queue on the main thread.</summary>
        void Start();

        /// <summary>Stop draining the queue and unregister from the main-thread update callback.</summary>
        void Stop();
    }
}
