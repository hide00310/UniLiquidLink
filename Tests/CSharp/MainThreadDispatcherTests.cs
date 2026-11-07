using NUnit.Framework;
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using UniLiquidLink;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Edit Mode tests for <see cref="MainThreadDispatcher"/>'s greedy drain: a running drain must pick up
/// actions enqueued from other threads mid-drain, bound its idle wait, and never block when idle.
/// </summary>
[TestFixture]
public class MainThreadDispatcherTests
{
    /// <summary>Fresh dispatcher per test; tests drive <see cref="MainThreadDispatcher.ProcessAll"/> directly.</summary>
    MainThreadDispatcher _dispatcher;

    [SetUp]
    public void SetUp()
    {
        _dispatcher = new MainThreadDispatcher();
    }

    // 1. Every action queued before the drain still runs (unchanged from the pre-greedy behaviour).
    [Test]
    public void ProcessAll_RunsEveryActionQueuedBeforeTheDrain()
    {
        _dispatcher.IdleWaitMilliseconds = 5;
        int count = 0;
        _dispatcher.Enqueue(() => count++);
        _dispatcher.Enqueue(() => count++);
        _dispatcher.Enqueue(() => count++);

        _dispatcher.ProcessAll();

        Assert.AreEqual(3, count);
    }

    // 2. Core of this change: an action enqueued from another thread while the drain is running is
    //    executed inside the SAME ProcessAll call, without first returning to EditorApplication.update.
    [Test]
    public void ProcessAll_PicksUpActionEnqueuedFromAnotherThreadMidDrain()
    {
        _dispatcher.IdleWaitMilliseconds = 250;
        bool laterActionRan = false;
        Thread enqueuer = null;

        // The first action wakes a background thread that enqueues the second action ~20ms later,
        // i.e. after this action has returned and the inner dequeue loop has already gone empty.
        _dispatcher.Enqueue(() =>
        {
            enqueuer = new Thread(() =>
            {
                Thread.Sleep(20);
                _dispatcher.Enqueue(() => laterActionRan = true);
            });
            enqueuer.Start();
        });

        _dispatcher.ProcessAll();

        Assert.IsTrue(laterActionRan,
            "A drain must catch an action enqueued mid-drain within IdleWaitMilliseconds and run it before returning.");
        enqueuer?.Join(1000);
    }

    // 3. With nothing following, ProcessAll returns after about IdleWaitMilliseconds rather than
    //    blocking forever (lower bound only; the upper bound is too scheduler-dependent to assert).
    [Test]
    public void ProcessAll_ReturnsAfterIdleWaitWhenNothingFollows()
    {
        _dispatcher.IdleWaitMilliseconds = 80;
        _dispatcher.Enqueue(() => { });

        var sw = Stopwatch.StartNew();
        _dispatcher.ProcessAll();
        sw.Stop();

        Assert.GreaterOrEqual(sw.Elapsed.TotalMilliseconds, 60,
            "The drain should have waited roughly IdleWaitMilliseconds for a follow-up before yielding.");
    }

    // 4. An empty queue costs nothing: ProcessAll returns immediately without paying the idle wait
    //    (upper bound only).
    [Test]
    public void ProcessAll_ReturnsImmediatelyWhenQueueIsEmpty()
    {
        _dispatcher.IdleWaitMilliseconds = 1000;

        var sw = Stopwatch.StartNew();
        _dispatcher.ProcessAll();
        sw.Stop();

        Assert.Less(sw.Elapsed.TotalMilliseconds, 200,
            "An idle Editor must not pay the idle wait when the queue is empty.");
    }

    // 5. A throwing action is logged via Debug.LogException and does not stop later actions.
    [Test]
    public void ProcessAll_ContinuesAfterAThrowingAction()
    {
        _dispatcher.IdleWaitMilliseconds = 5;
        LogAssert.Expect(LogType.Exception, new Regex("boom"));
        bool secondRan = false;
        _dispatcher.Enqueue(() => { throw new InvalidOperationException("boom"); });
        _dispatcher.Enqueue(() => secondRan = true);

        _dispatcher.ProcessAll();

        Assert.IsTrue(secondRan, "An exception in one action must not prevent the rest of the drain.");
    }

    // 6. Re-entrant ProcessAll (Unity can pump EditorApplication.update from inside a blocking editor
    //    call) returns immediately via the _draining guard and leaves the queue to the outer drain.
    [Test]
    public void ProcessAll_ReentrantCallReturnsImmediatelyAndLeavesQueueToOuterDrain()
    {
        _dispatcher.IdleWaitMilliseconds = 30;
        bool innerRan = false;
        bool innerRanDuringReentrantCall = false;
        bool reentrantCallReturnedFast = false;

        _dispatcher.Enqueue(() =>
        {
            // Queue a follow-up so the re-entrant call would have work to do if the guard were absent.
            _dispatcher.Enqueue(() => innerRan = true);

            var sw = Stopwatch.StartNew();
            _dispatcher.ProcessAll(); // re-entrant: must hit the _draining guard and bail out
            sw.Stop();

            reentrantCallReturnedFast = sw.Elapsed.TotalMilliseconds < 25;
            innerRanDuringReentrantCall = innerRan;
        });

        _dispatcher.ProcessAll();

        Assert.IsTrue(reentrantCallReturnedFast, "A re-entrant ProcessAll must return at once, not run its own drain.");
        Assert.IsFalse(innerRanDuringReentrantCall, "The re-entrant call must not drain the queue the outer call owns.");
        Assert.IsTrue(innerRan, "The follow-up still runs once the outer drain resumes.");
    }
}
