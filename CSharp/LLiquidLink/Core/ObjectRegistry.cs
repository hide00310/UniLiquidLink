using LLiquidLink.Logger;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace LLiquidLink
{
    /// <summary>Maps Unity object instance IDs to live <see cref="object"/> references for RPC lookup.</summary>
    public class ObjectRegistry
    {
        readonly Func<ILogger> _getLogger;

        /// <summary>In-memory map from instance ID to registered Unity object.</summary>
        internal readonly Dictionary<long, object> _objectMap = new Dictionary<long, object>();

        /// <summary>Fired when an object is removed from the registry. Parameter: instance ID.</summary>
        public event Action<int> OnRemoveObject;

        /// <summary>Initialize the registry with a logger factory.</summary>
        /// <param name="getLogger">Factory that returns the current logger instance.</param>
        public ObjectRegistry(Func<ILogger> getLogger)
        {
            _getLogger = getLogger;
        }

        /// <summary>Look up a registered object by instance ID.</summary>
        /// <param name="instanceId">Instance ID to look up.</param>
        /// <returns>The matching <see cref="object"/>, or <c>null</c> if not found.</returns>
        public object GetObject(long instanceId)
        {
            return _objectMap.TryGetValue(instanceId, out object obj) ? obj : null;
        }

        /// <summary>Add <paramref name="obj"/> to the in-memory map so it can be looked up by instance ID.</summary>
        /// <param name="obj">Unity object to register.</param>
        public long RegisterObject(object obj)
        {
            if (obj != null)
            {
                long id = RuntimeHelpers.GetHashCode(obj);
                _objectMap[id] = obj;
                return id;
            }
            return 0;
        }

        /// <summary>Remove <paramref name="obj"/> from the in-memory map and fire <see cref="OnRemoveObject"/>.</summary>
        /// <param name="obj">Unity object to unregister.</param>
        public void UnregisterObject(object obj)
        {
            if (obj != null)
            {
                RemoveObject(RuntimeHelpers.GetHashCode(obj));
            }
        }

        /// <summary>Clear all entries from the in-memory map.</summary>
        public void ClearObjectMap()
        {
            _getLogger().Debug("Clearing object map");
            _objectMap.Clear();
        }

        /// <summary>Remove the entry for <paramref name="instanceId"/> and fire <see cref="OnRemoveObject"/> if it existed.</summary>
        /// <param name="instanceId">Instance ID of the object to remove.</param>
        public void RemoveObject(int instanceId)
        {
            bool removed = _objectMap.Remove(instanceId);
            if (removed)
            {
                OnRemoveObject?.Invoke(instanceId);
            }
        }
    }
}
