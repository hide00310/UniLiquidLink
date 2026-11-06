using LLiquidLink;
using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEditor;

namespace UniLiquidLink
{
    /// <summary>
    /// Drains an action queue on Unity's main thread. Registered with <see cref="EditorApplication.update"/>,
    /// but once a drain starts it keeps hold of the main thread for as long as new actions keep arriving
    /// (bounded by <see cref="IdleWaitMilliseconds"/>) instead of handling one tick's worth and yielding.
    /// This removes the per-RPC wait for the next Editor tick during a burst of sequential RPC traffic.
    /// </summary>
    public class MainThreadDispatcher : IMainThreadDispatcher
    {
        readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

        // Set by Enqueue so a drain already running on the main thread can pick the next action up
        // without first returning to EditorApplication.update (see ProcessAll).
        readonly ManualResetEventSlim _signal = new ManualResetEventSlim(false);

        // Unity pumps EditorApplication.update from inside some blocking editor operations; a nested
        // ProcessAll must not start its own blocking drain while the outer one owns the queue.
        bool _draining;

        /// <summary>
        /// How long a drain keeps the main thread waiting for the next action before yielding back to the
        /// Editor. The RPC peer turns a response around in roughly 1.5ms, so any value above that keeps a
        /// whole burst inside a single drain; the Editor stays frozen for that burst and recovers this
        /// long after traffic stops. Set to 0 to restore the previous one-drain-per-tick behaviour.
        /// </summary>
        public int IdleWaitMilliseconds { get; set; } = 10;

        /// <summary>Enqueue <paramref name="action"/> for execution on the main thread.</summary>
        /// <param name="action">Action to execute.</param>
        public void Enqueue(Action action) { _queue.Enqueue(action); _signal.Set(); }

        /// <summary>Register the drain loop with <see cref="EditorApplication.update"/>.</summary>
        public void Start() { EditorApplication.update += ProcessAll; }

        /// <summary>Unregister the drain loop from <see cref="EditorApplication.update"/>.</summary>
        public void Stop() { EditorApplication.update -= ProcessAll; }

        /// <summary>
        /// Dequeue and invoke every pending action, then hold the main thread here waiting up to
        /// <see cref="IdleWaitMilliseconds"/> for the next one, repeating until that wait times out.
        /// Each action is isolated so one throwing does not stop the rest. Returns immediately when the
        /// queue is empty (an idle Editor pays nothing) or when a drain is already running on this thread.
        /// </summary>
        internal void ProcessAll()
        {
            if (_draining)
            {
                return;
            }
            // Nothing pending: return immediately so an idle Editor never pays the wait below.
            if (_queue.IsEmpty)
            {
                return;
            }

            _draining = true;
            try
            {
                do
                {
                    // Reset before draining, so an Enqueue that races the drain still leaves the signal
                    // set and cannot be lost between the queue going empty and the Wait below.
                    _signal.Reset();
                    while (_queue.TryDequeue(out Action action))
                    {
                        try
                        {
                            action();
                        }
                        catch (Exception ex)
                        {
                            UnityEngine.Debug.LogException(ex);
                        }
                    }
                }
                while (_signal.Wait(IdleWaitMilliseconds));
            }
            finally
            {
                _draining = false;
            }
        }
    }
}
