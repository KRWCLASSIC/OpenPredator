using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Diagnostics;

namespace OpenPredator.Gui;

internal static class DevToolsService
{
    private static PropertyInfo? _lastFrameStatsProp;
    private static object? _profilerInstance;
    private static object? _profilerRingBuffer;
    private static MethodInfo? _ringBufferAddMethod;
    private static long _windowSourceId = 1L;
    private static long _frameCounter = 0;
    private static long _lastFrameTimestamp = 0;
    private static bool _frameRenderedHooked = false;

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "DevTools internal reflection")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "DevTools internal reflection")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(WindowDevTools))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Window))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(FramePerformanceStats))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "Aprillz.MewUI.Diagnostics.PerformanceProfiler", "Aprillz.MewUI")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "Aprillz.MewUI.Diagnostics.PerformanceStatsRingBuffer", "Aprillz.MewUI")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "Aprillz.MewUI.Diagnostics.RollingPerformanceStats", "Aprillz.MewUI")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "Aprillz.MewUI.Diagnostics.DebugPerformanceOverlay", "Aprillz.MewUI")]
    public static void EnsureDevTools(Window? window)
    {
        if (window == null) return;

        // 1. Enable MewUI DevTools AppContext switch
        AppContext.SetSwitch("Aprillz.MewUI.DevTools.Enabled", true);

        // 2. Initialize PerformanceProfiler instance and enable it
        try
        {
            var profilerType = Type.GetType("Aprillz.MewUI.Diagnostics.PerformanceProfiler, Aprillz.MewUI");
            if (profilerType != null)
            {
                var instanceProp = profilerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                _profilerInstance = instanceProp?.GetValue(null);
                if (_profilerInstance != null)
                {
                    var isEnabledProp = profilerType.GetProperty("IsEnabled", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    isEnabledProp?.SetValue(_profilerInstance, true);

                    var statsField = profilerType.GetField("_stats", BindingFlags.Instance | BindingFlags.NonPublic);
                    _profilerRingBuffer = statsField?.GetValue(_profilerInstance);

                    var ringBufferType = Type.GetType("Aprillz.MewUI.Diagnostics.PerformanceStatsRingBuffer, Aprillz.MewUI");
                    _ringBufferAddMethod = ringBufferType?.GetMethod("Add", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }
            }
        }
        catch { }

        // 3. In NativeAOT, WindowDevTools is not attached automatically in Window constructor
        // because DevToolsGate._isSupported evaluates to false at compile time.
        // We attach WindowDevTools dynamically to enable all built-in inspector, visual tree,
        // and performance monitoring features.
        if (window.DevTools == null)
        {
            try
            {
                var winType = typeof(Window);
                var wdtType = typeof(WindowDevTools);
                var ctor = wdtType.GetConstructor(
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new[] { winType },
                    null);

                if (ctor != null)
                {
                    var wdt = (WindowDevTools?)ctor.Invoke(new object[] { window });
                    if (wdt != null)
                    {
                        var devToolsField = winType.GetField("_devTools", BindingFlags.NonPublic | BindingFlags.Instance);
                        devToolsField?.SetValue(window, wdt);
                    }
                }
            }
            catch { }
        }

        // 4. Hook Window.FrameRendered to calculate live frame times and feed both
        // PerformanceProfiler (which DebugPerformanceOverlay/F10 reads) and Window.LastFramePerformanceStats.
        HookFrameRendered(window);
    }

    private static long GetWindowProfilerSourceId(Window window)
    {
        try
        {
            var prop = typeof(Window).GetProperty("ProfilerSourceId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null)
            {
                var val = prop.GetValue(window);
                if (val is long l && l > 0) return l;
            }
            var field = typeof(Window).GetField("_profilerSourceId", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
            {
                var val = field.GetValue(window);
                if (val is long l && l > 0) return l;
            }
        }
        catch { }
        return 1L;
    }

    private static void HookFrameRendered(Window window)
    {
        if (_frameRenderedHooked) return;
        _frameRenderedHooked = true;

        _windowSourceId = GetWindowProfilerSourceId(window);
        _lastFrameStatsProp = typeof(Window).GetProperty("LastFramePerformanceStats", BindingFlags.Public | BindingFlags.Instance);
        _lastFrameTimestamp = Stopwatch.GetTimestamp();

        window.FrameRendered += () =>
        {
            try
            {
                long now = Stopwatch.GetTimestamp();
                long elapsedTicks = now - _lastFrameTimestamp;
                double rawMs = elapsedTicks > 0 ? (elapsedTicks * 1000.0 / Stopwatch.Frequency) : 16.6;
                double frameMs = (rawMs > 250.0) ? 16.6 : rawMs;
                _lastFrameTimestamp = now;
                long frameIdx = ++_frameCounter;

                var renderStats = window.LastFrameStats;
                double presentMs = Math.Min(frameMs * 0.2, 1.0);
                double renderBodyMs = Math.Max(0.05, frameMs - presentMs);

                // FramePerformanceStats constructor:
                // (frameIndex, sourceId, frameMs, beginFrameMs, animationMs, layoutMs, measureMs, arrangeMs,
                //  renderBodyMs, devToolsMs, endFrameMs, presentMs, drawCalls, cullCount, primitiveStats,
                //  allocatedBytes, gen0, gen1, gen2, layoutRan, measureRan, arrangeRan)
                var stats = new FramePerformanceStats(
                    frameIdx,
                    _windowSourceId,
                    frameMs,
                    0.0, // beginFrameMs
                    0.0, // animationMs
                    0.0, // layoutMs (0 when no layout pass ran)
                    0.0, // measureMs
                    0.0, // arrangeMs
                    renderBodyMs,
                    0.02, // devToolsMs
                    0.02, // endFrameMs
                    presentMs,
                    renderStats.DrawCalls,
                    renderStats.CullCount,
                    renderStats.PrimitiveStats,
                    GC.GetAllocatedBytesForCurrentThread(),
                    GC.CollectionCount(0),
                    GC.CollectionCount(1),
                    GC.CollectionCount(2),
                    false, // layoutRan
                    false, // measureRan
                    false); // arrangeRan

                // 1. Feed the PerformanceProfiler ring buffer that DebugPerformanceOverlay (F10) reads from
                if (_profilerRingBuffer != null && _ringBufferAddMethod != null)
                {
                    try
                    {
                        _ringBufferAddMethod.Invoke(_profilerRingBuffer, new object[] { stats });
                    }
                    catch { }
                }

                // 2. Also set on Window property
                _lastFrameStatsProp?.SetValue(window, stats);
            }
            catch { }
        };
    }

    public static void ToggleInspector()
    {
        var win = AppState.MainWindow;
        if (win == null) return;
        EnsureDevTools(win);
        try
        {
            win.DevTools?.ToggleInspector();
        }
        catch { }
    }

    public static void ToggleVisualTree()
    {
        var win = AppState.MainWindow;
        if (win == null) return;
        EnsureDevTools(win);
        try
        {
            win.DevTools?.ToggleVisualTree();
        }
        catch { }
    }

    public static void TogglePerformanceMonitor()
    {
        var win = AppState.MainWindow;
        if (win == null) return;
        EnsureDevTools(win);
        try
        {
            win.DevTools?.TogglePerformanceMonitor();
            win.InvalidateVisual();
        }
        catch { }
    }

    public static void ToggleProfiler()
    {
        var win = AppState.MainWindow;
        if (win == null) return;
        EnsureDevTools(win);
        try
        {
            win.DevTools?.ToggleProfiler();
        }
        catch { }
    }

    public static void ToggleRepaintOverlay()
    {
        var win = AppState.MainWindow;
        if (win == null) return;
        try
        {
            win.ToggleBitmapCacheInvalidationOverlay();
        }
        catch { }
    }
}
