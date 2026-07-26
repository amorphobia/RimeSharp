using System.Collections;
using System.Collections.Specialized;
using System.Collections.Concurrent;
using System.Diagnostics;
using RimeSharp.PowerShell;
using RimeSharp.PowerShell.Cmdlets;

var tests = new (string Name, Action Body)[]
{
    ("Notification option projection", NotificationOptionProjection),
    ("Notification queue drain snapshot", NotificationQueueDrainSnapshot),
    ("Session ownership and destroyed state", SessionOwnershipAndDestroyedState),
    ("Native gate cancellation", NativeGateCancellation),
    ("Config-file path normalization", ConfigFilePathNormalization),
    ("Config-path normalization", ConfigPathNormalization),
    ("Config shape scalar and container normalization", ConfigShapeNormalization),
    ("Config shape invalid and recursive rejection", ConfigShapeRejection),
    ("Notification subscriber fan-out and ordering", SubscriberFanOutAndOrdering),
    ("Slow and throwing subscriber isolation", SubscriberIsolation),
    ("Notification unsubscribe is non-blocking", UnsubscribeIsNonBlocking),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add(test.Name);
        Console.Error.WriteLine($"FAIL {test.Name}: {ex}");
    }
}

if (failures.Count != 0)
{
    Console.Error.WriteLine($"{failures.Count} managed test(s) failed.");
    return 1;
}

Console.WriteLine($"{tests.Length} managed tests passed.");
return 0;

static void NotificationOptionProjection()
{
    var enabled = new RimeNotification(42, "option", "ascii_mode");
    Assert.Equal((ulong)42, enabled.SessionId);
    Assert.Equal("ascii_mode", enabled.OptionName);
    Assert.Equal(true, enabled.OptionState);

    var disabled = new RimeNotification(42, "option", "!ascii_mode");
    Assert.Equal("ascii_mode", disabled.OptionName);
    Assert.Equal(false, disabled.OptionState);

    var ordinary = new RimeNotification(0, "schema", "luna_pinyin");
    Assert.Null(ordinary.OptionName);
    Assert.Null(ordinary.OptionState);
}

static void NotificationQueueDrainSnapshot()
{
    RimeNotificationQueue.Clear();
    RimeNotificationQueue.Enqueue(new RimeNotification(1, "first", "1"));
    RimeNotificationQueue.Enqueue(new RimeNotification(2, "second", "2"));

    var firstDrain = RimeNotificationQueue.Drain();
    Assert.Equal(2, firstDrain.Length);
    Assert.Equal("first", firstDrain[0].MessageType);
    Assert.Equal("second", firstDrain[1].MessageType);
    Assert.Equal(0, RimeNotificationQueue.Drain().Length);
}

static void SessionOwnershipAndDestroyedState()
{
    var generation = new object();
    var owner = new object();
    var session = new RimeSession(new UIntPtr(123), generation, owner);

    Assert.Equal((ulong)123, session.Id);
    Assert.True(session.IsFromGeneration(generation));
    Assert.True(session.IsOwnedBy(owner));
    Assert.False(session.IsDestroyed);
    session.MarkDestroyed();
    session.MarkDestroyed();
    Assert.True(session.IsDestroyed);
}

static void NativeGateCancellation()
{
    using var firstLease = RimeProcessRuntime.AcquireNativeGate(
        CancellationToken.None);
    using var cancellation = new CancellationTokenSource();
    var waiter = Task.Run(() =>
    {
        using var ignored = RimeProcessRuntime.AcquireNativeGate(
            cancellation.Token);
    });

    cancellation.Cancel();
    Assert.Throws<OperationCanceledException>(() => waiter.GetAwaiter().GetResult());
}

static void ConfigFilePathNormalization()
{
    Assert.Equal(
        "build/default.yaml",
        DeployRimeCmdlet.NormalizeConfigFile(@"build\default.yaml"));
    Assert.Equal(
        "nested/default.custom.yaml",
        DeployRimeCmdlet.NormalizeConfigFile("nested//default.custom.yaml"));
    Assert.Throws<ArgumentException>(
        () => DeployRimeCmdlet.NormalizeConfigFile("../default.yaml"));
    Assert.Throws<ArgumentException>(
        () => DeployRimeCmdlet.NormalizeConfigFile("C:/default.yaml"));
}

static void ConfigPathNormalization()
{
    Assert.Equal(
        "schema/icon",
        DeployRimeCmdlet.NormalizeConfigPath(
            @"\schema\icon/",
            "path"));
    Assert.Throws<ArgumentException>(
        () => DeployRimeCmdlet.NormalizeConfigPath("///", "path"));
    Assert.Equal(
        string.Empty,
        RimeConfigPath.Normalize(
            "///",
            allowEmpty: true,
            parameterName: "path"));
}

static void ConfigShapeNormalization()
{
    Assert.Equal(
        RimeConfigShapeKind.String,
        RimeConfigShapeNormalizer.Normalize(typeof(string)).Kind);
    Assert.Equal(
        RimeConfigShapeKind.Boolean,
        RimeConfigShapeNormalizer.Normalize(typeof(bool)).Kind);
    Assert.Equal(
        RimeConfigShapeKind.Integer,
        RimeConfigShapeNormalizer.Normalize(typeof(int)).Kind);
    Assert.Equal(
        RimeConfigShapeKind.Double,
        RimeConfigShapeNormalizer.Normalize(typeof(double)).Kind);

    var list = RimeConfigShape.List(typeof(string));
    var normalizedList = RimeConfigShapeNormalizer.Normalize(list);
    Assert.Equal(RimeConfigShapeKind.List, normalizedList.Kind);
    Assert.Equal(RimeConfigShapeKind.String, normalizedList.ValueShape!.Kind);

    var map = RimeConfigShape.MapOf(RimeConfigShape.List(typeof(int)));
    var normalizedMap = RimeConfigShapeNormalizer.Normalize(map);
    Assert.Equal(RimeConfigShapeKind.MapOf, normalizedMap.Kind);
    Assert.Equal(RimeConfigShapeKind.List, normalizedMap.ValueShape!.Kind);
    Assert.Equal(
        RimeConfigShapeKind.Integer,
        normalizedMap.ValueShape.ValueShape!.Kind);
}

static void ConfigShapeRejection()
{
    Assert.Throws<ArgumentNullException>(
        () => RimeConfigShapeNormalizer.Normalize(null));
    Assert.Throws<ArgumentException>(
        () => RimeConfigShapeNormalizer.Normalize(typeof(long)));

    var caseCollision = new OrderedDictionary
    {
        ["Name"] = typeof(string),
        ["name"] = typeof(string),
    };
    Assert.Throws<ArgumentException>(
        () => RimeConfigShapeNormalizer.Normalize(caseCollision));

    var recursive = new Hashtable();
    recursive["self"] = recursive;
    Assert.Throws<ArgumentException>(
        () => RimeConfigShapeNormalizer.Normalize(recursive));
}

static void SubscriberFanOutAndOrdering()
{
    var source = RimeNotificationSource.Instance;
    var first = new ConcurrentQueue<int>();
    var second = new ConcurrentQueue<int>();
    using var firstComplete = new CountdownEvent(3);
    using var secondComplete = new CountdownEvent(3);

    EventHandler<RimeNotificationEventArgs> firstHandler = (_, args) =>
    {
        first.Enqueue(int.Parse(args.Notification.MessageValue));
        firstComplete.Signal();
    };
    EventHandler<RimeNotificationEventArgs> secondHandler = (_, args) =>
    {
        second.Enqueue(int.Parse(args.Notification.MessageValue));
        secondComplete.Signal();
    };

    source.NotificationReceived += firstHandler;
    source.NotificationReceived += secondHandler;
    try
    {
        for (var i = 0; i < 3; ++i)
        {
            source.Publish(new RimeNotificationEventArgs(
                RimeNotificationOrigin.Startup,
                new RimeNotification(0, "test", i.ToString())));
        }

        Assert.True(firstComplete.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(secondComplete.Wait(TimeSpan.FromSeconds(5)));
        Assert.SequenceEqual(new[] { 0, 1, 2 }, first.ToArray());
        Assert.SequenceEqual(new[] { 0, 1, 2 }, second.ToArray());
    }
    finally
    {
        source.NotificationReceived -= firstHandler;
        source.NotificationReceived -= secondHandler;
    }
}

static void SubscriberIsolation()
{
    var source = RimeNotificationSource.Instance;
    using var releaseSlowSubscriber = new ManualResetEventSlim();
    using var fastSubscriberCalled = new ManualResetEventSlim();

    EventHandler<RimeNotificationEventArgs> slowHandler = (_, _) =>
        releaseSlowSubscriber.Wait(TimeSpan.FromSeconds(5));
    EventHandler<RimeNotificationEventArgs> throwingHandler = (_, _) =>
        throw new InvalidOperationException("Expected test failure.");
    EventHandler<RimeNotificationEventArgs> fastHandler = (_, _) =>
        fastSubscriberCalled.Set();

    source.NotificationReceived += slowHandler;
    source.NotificationReceived += throwingHandler;
    source.NotificationReceived += fastHandler;
    try
    {
        source.Publish(new RimeNotificationEventArgs(
            RimeNotificationOrigin.Deployment,
            new RimeNotification(0, "test", "isolation")));
        Assert.True(fastSubscriberCalled.Wait(TimeSpan.FromSeconds(5)));
    }
    finally
    {
        releaseSlowSubscriber.Set();
        source.NotificationReceived -= slowHandler;
        source.NotificationReceived -= throwingHandler;
        source.NotificationReceived -= fastHandler;
    }
}

static void UnsubscribeIsNonBlocking()
{
    var source = RimeNotificationSource.Instance;
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    EventHandler<RimeNotificationEventArgs> handler = (_, _) =>
    {
        entered.Set();
        release.Wait(TimeSpan.FromSeconds(5));
    };

    source.NotificationReceived += handler;
    source.Publish(new RimeNotificationEventArgs(
        RimeNotificationOrigin.Startup,
        new RimeNotification(0, "test", "unsubscribe")));
    Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

    var stopwatch = Stopwatch.StartNew();
    source.NotificationReceived -= handler;
    stopwatch.Stop();
    release.Set();
    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
}

internal static class Assert
{
    internal static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected true.");
    }

    internal static void False(bool value)
    {
        if (value) throw new InvalidOperationException("Expected false.");
    }

    internal static void Null(object? value)
    {
        if (value is not null)
        {
            throw new InvalidOperationException($"Expected null, got {value}.");
        }
    }

    internal static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Expected {expected}, got {actual}.");
        }
    }

    internal static void SequenceEqual<T>(
        IReadOnlyList<T> expected,
        IReadOnlyList<T> actual)
    {
        Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; ++i)
        {
            Equal(expected[i], actual[i]);
        }
    }

    internal static void Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Expected {typeof(TException).Name}.");
    }
}
