using System;

namespace LLiquidLink
{
    /// <summary>Transport layer abstraction for bidirectional binary communication with clients.</summary>
    public interface ITransportServer
    {
        /// <summary>Fired when a new client connects. Parameters: client ID, remote endpoint string.</summary>
        event Action<int, string> OnConnect;

        /// <summary>Fired when a client disconnects. Parameter: client ID.</summary>
        event Action<int> OnDisconnect;

        /// <summary>Fired when binary data arrives from a client. Parameters: client ID, data segment.</summary>
        event Action<int, ArraySegment<byte>> OnData;

        /// <summary>Fired when a transport-level error occurs. Parameters: client ID, exception.</summary>
        event Action<int, Exception> OnError;

        /// <summary>ID assigned to the most recently connected client.</summary>
        int ClientId { get; }

        /// <summary>Start listening for incoming connections.</summary>
        void Start();

        /// <summary>Stop the server and close all connections.</summary>
        void Stop();

        /// <summary>Send <paramref name="data"/> to all currently connected clients.</summary>
        /// <param name="data">Binary data to broadcast.</param>
        void SendAll(ArraySegment<byte> data);
    }
}
