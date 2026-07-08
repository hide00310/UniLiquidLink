using LLiquidLink;
using System;

namespace UniLiquidLink
{
    public class NullTransport : ITransportServer
    {
        public int ClientId => 0;

        public event Action<int, string> OnConnect;
        public event Action<int> OnDisconnect;
        public event Action<int, ArraySegment<byte>> OnData;
        public event Action<int, Exception> OnError;

        public void SendAll(ArraySegment<byte> data) { }

        public void Start() { }

        public void Stop() { }
    }

    public class NullDispatcher : IMainThreadDispatcher
    {
        public void Enqueue(Action action) { }
        public void Start() { }
        public void Stop() { }
    }
}
