using NUnit.Framework;
using UniLiquidLink;

/// <summary>
/// Edit Mode tests for <see cref="Server.DispatcherIdleWaitMilliseconds"/>, which forwards to the
/// wrapped <see cref="MainThreadDispatcher"/> and is a no-op for any other dispatcher.
/// </summary>
[TestFixture]
public class ServerDispatcherIdleWaitTests
{
    // Writes reach the MainThreadDispatcher the server was built with.
    [Test]
    public void DispatcherIdleWaitMilliseconds_ForwardsToMainThreadDispatcher()
    {
        var dispatcher = new MainThreadDispatcher();
        var server = new Server(string.Empty, new NullTransport(), dispatcher);

        server.DispatcherIdleWaitMilliseconds = 200;

        Assert.AreEqual(200, dispatcher.IdleWaitMilliseconds);
        Assert.AreEqual(200, server.DispatcherIdleWaitMilliseconds);
    }

    // The server reports the dispatcher's own default until something overrides it.
    [Test]
    public void DispatcherIdleWaitMilliseconds_ReadsDispatcherDefault()
    {
        var dispatcher = new MainThreadDispatcher();
        var server = new Server(string.Empty, new NullTransport(), dispatcher);

        Assert.AreEqual(dispatcher.IdleWaitMilliseconds, server.DispatcherIdleWaitMilliseconds);
    }

    // Any other dispatcher has no idle wait: reads 0 and silently ignores writes.
    [Test]
    public void DispatcherIdleWaitMilliseconds_IsNoOpForOtherDispatchers()
    {
        var server = new Server(string.Empty, new NullTransport(), new NullDispatcher());

        server.DispatcherIdleWaitMilliseconds = 200;

        Assert.AreEqual(0, server.DispatcherIdleWaitMilliseconds);
    }
}
