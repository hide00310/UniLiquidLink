using LLiquidLink;
using System;
using System.Collections.Concurrent;
using UnityEditor;

namespace UniLiquidLink
{
    /// <summary>Drains an action queue on Unity's main thread via <see cref="EditorApplication.update"/>.</summary>
    public class MainThreadDispatcher : IMainThreadDispatcher
    {
        readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

        /// <summary>Enqueue <paramref name="action"/> for execution on the main thread.</summary>
        /// <param name="action">Action to execute.</param>
        public void Enqueue(Action action) { _queue.Enqueue(action); }

        /// <summary>Register the drain loop with <see cref="EditorApplication.update"/>.</summary>
        public void Start() { EditorApplication.update += ProcessAll; }

        /// <summary>Unregister the drain loop from <see cref="EditorApplication.update"/>.</summary>
        public void Stop() { EditorApplication.update -= ProcessAll; }

        /// <summary>Dequeue and invoke all pending actions in the current update tick.</summary>
        void ProcessAll()
        {
            while (_queue.TryDequeue(out Action action))
            {
                action();
            }
        }
    }
}
