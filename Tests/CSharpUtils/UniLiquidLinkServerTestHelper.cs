using LLiquidLink;
using LLiquidLink.Logger;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

public class GoldenTestLogger : ILogger
{
    public LogLevel MinLevel { get; set; }

    public List<string> Entries = new List<string>();
    public void Info(string msg) { Entries.Add("INFO: " + msg); }
    public void Debug(string msg) { Entries.Add("DEBUG: " + msg); }
    public void InfoFormat(string format, params object[] args) { Entries.Add("INFO: " + string.Format(format, args)); }
    public void DebugFormat(string format, params object[] args) { Entries.Add("DEBUG: " + string.Format(format, args)); }
}

public class StubTransport : ITransportServer
{
    public event Action<int, string> OnConnect { add { } remove { } }
    public event Action<int> OnDisconnect { add { } remove { } }
    public event Action<int, ArraySegment<byte>> OnData { add { } remove { } }
    public event Action<int, Exception> OnError { add { } remove { } }
    public int ClientId => 0;
    public void Start() { }
    public void Stop() { }
    public void SendAll(ArraySegment<byte> data) { }
}

public class StubDispatcher : IMainThreadDispatcher
{
    public readonly ConcurrentQueue<Action> Queue;
    public StubDispatcher(ConcurrentQueue<Action> queue) { Queue = queue; }
    public void Enqueue(Action action) { Queue.Enqueue(action); }
    public void Start() { }
    public void Stop() { }
}

public static class UniLiquidLinkTestHelper
{
    public static string Executable = GetSourceDir() + "/../../run_python2.bat";
    public static string Arguments = "";
    public static string GetSourceDir([System.Runtime.CompilerServices.CallerFilePath] string filePath = "")
    {
        return Path.GetDirectoryName(filePath);
    }
}
