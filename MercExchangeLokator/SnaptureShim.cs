using System;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace com.HellScape.ScreenCapture
{
    public enum FrameCapturingMethod { GDI = 0, DX = 1 }

    public class FrameCapturedEventArgs : EventArgs
    {
        public Bitmap ScreenCapturedBitmap { get; set; }
        public int FrameCount { get; set; }
        public FrameCapturedEventArgs(Bitmap bmp, int count) { ScreenCapturedBitmap = bmp; FrameCount = count; }
    }

    public static class Snapture
    {
        public static event EventHandler<FrameCapturedEventArgs> onFrameCaptured;
        public static double FPS { get; set; } = 30;
        public static bool isActive { get; private set; } = false;

        private static readonly object syncRoot = new object();
        private static readonly NativeSnaptureEngine nativeEngine;
        private static bool isNativeEngineAvailable;
        private static bool useNativeCapture;
        private static bool usingNativeEngine;

        private static Rectangle managedCaptureRegion;
        private static bool hasManagedCaptureRegion;
        private static int managedFrameCount;
        private static CancellationTokenSource managedCaptureCts;
        private static Task managedCaptureTask;

        public static int ScreenWidth
        {
            get
            {
                try
                {
                    Screen primary = Screen.PrimaryScreen;
                    if (primary != null)
                        return primary.Bounds.Width;
                }
                catch
                {
                    // Ignore and fall back to WPF system metrics.
                }

                return (int)System.Windows.SystemParameters.PrimaryScreenWidth;
            }
        }

        public static int ScreenHeight
        {
            get
            {
                try
                {
                    Screen primary = Screen.PrimaryScreen;
                    if (primary != null)
                        return primary.Bounds.Height;
                }
                catch
                {
                    // Ignore and fall back to WPF system metrics.
                }

                return (int)System.Windows.SystemParameters.PrimaryScreenHeight;
            }
        }

        static Snapture()
        {
            nativeEngine = NativeSnaptureEngine.TryCreate();
            isNativeEngineAvailable = nativeEngine != null;
            useNativeCapture = IsNativeCaptureRequested();
        }

        public static void Start(FrameCapturingMethod method)
        {
            lock (syncRoot)
            {
                isActive = true;
                usingNativeEngine = false;
                managedFrameCount = 0;

                if (useNativeCapture && isNativeEngineAvailable)
                {
                    try
                    {
                        nativeEngine.Start(method);
                        usingNativeEngine = true;
                        return;
                    }
                    catch
                    {
                        // If native engine fails at runtime, fall back to managed capture.
                        isNativeEngineAvailable = false;
                        usingNativeEngine = false;
                    }
                }
            }
        }

        public static void CaptureRegion(int left, int top, int width, int height)
        {
            if (!isActive)
                return;

            left = Math.Max(left, 0);
            top = Math.Max(top, 0);
            width = Math.Max(width, 1);
            height = Math.Max(height, 1);

            if (usingNativeEngine && isNativeEngineAvailable)
            {
                try
                {
                    nativeEngine.CaptureRegion(left, top, width, height);
                    return;
                }
                catch
                {
                    // Ignore and fall back to managed screen capture.
                    isNativeEngineAvailable = false;
                    usingNativeEngine = false;
                }
            }

            lock (syncRoot)
            {
                managedCaptureRegion = new Rectangle(left, top, width, height);
                hasManagedCaptureRegion = true;
                EnsureManagedCaptureLoop();
            }
        }

        public static void Stop()
        {
            lock (syncRoot)
            {
                if (usingNativeEngine && isNativeEngineAvailable)
                {
                    try
                    {
                        nativeEngine.Stop();
                    }
                    catch
                    {
                        // ignore native stop failures
                    }
                }

                if (managedCaptureCts != null)
                {
                    managedCaptureCts.Cancel();
                    managedCaptureCts.Dispose();
                    managedCaptureCts = null;
                }

                managedCaptureTask = null;
                hasManagedCaptureRegion = false;
                usingNativeEngine = false;
                isActive = false;
            }
        }

        private static void EnsureManagedCaptureLoop()
        {
            if (managedCaptureTask != null && !managedCaptureTask.IsCompleted)
                return;

            managedCaptureCts = new CancellationTokenSource();
            CancellationToken token = managedCaptureCts.Token;
            managedCaptureTask = Task.Run(() => ManagedCaptureLoop(token), token);
        }

        private static void ManagedCaptureLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                Rectangle region;
                lock (syncRoot)
                {
                    if (!isActive)
                        break;

                    if (!hasManagedCaptureRegion)
                    {
                        region = Rectangle.Empty;
                    }
                    else
                    {
                        region = managedCaptureRegion;
                    }
                }

                if (region == Rectangle.Empty)
                {
                    if (token.WaitHandle.WaitOne(50))
                        break;
                    continue;
                }

                Bitmap bitmap = null;
                try
                {
                    bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format24bppRgb);
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        g.CopyFromScreen(region.Left, region.Top, 0, 0, region.Size, CopyPixelOperation.SourceCopy);
                    }

                    int frame = Interlocked.Increment(ref managedFrameCount);
                    RaiseFrame(bitmap, frame);
                }
                catch
                {
                    bitmap?.Dispose();
                }

                int delayMs = CalculateFrameDelayMs();
                if (token.WaitHandle.WaitOne(delayMs))
                    break;
            }
        }

        private static int CalculateFrameDelayMs()
        {
            if (FPS <= 0)
                return 33;

            double delay = 1000d / FPS;
            if (delay < 1)
                delay = 1;

            return (int)Math.Round(delay);
        }

        private static bool IsNativeCaptureRequested()
        {
            string configuredValue = Environment.GetEnvironmentVariable("MERC_USE_NATIVE_CAPTURE");
            if (string.IsNullOrWhiteSpace(configuredValue))
                return false;

            if (bool.TryParse(configuredValue, out bool boolValue))
                return boolValue;

            return string.Equals(configuredValue, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(configuredValue, "yes", StringComparison.OrdinalIgnoreCase);
        }

        public static void RaiseFrame(Bitmap bmp, int frame)
        {
            onFrameCaptured?.Invoke(null, new FrameCapturedEventArgs(bmp, frame));
        }

        private sealed class NativeSnaptureEngine
        {
            private readonly object nativeInstance;
            private readonly MethodInfo startMethod;
            private readonly MethodInfo captureRegionMethod;
            private readonly MethodInfo stopMethod;
            private readonly PropertyInfo fpsProperty;

            private NativeSnaptureEngine(object nativeInstance,
                MethodInfo startMethod,
                MethodInfo captureRegionMethod,
                MethodInfo stopMethod,
                PropertyInfo fpsProperty)
            {
                this.nativeInstance = nativeInstance;
                this.startMethod = startMethod;
                this.captureRegionMethod = captureRegionMethod;
                this.stopMethod = stopMethod;
                this.fpsProperty = fpsProperty;
            }

            public static NativeSnaptureEngine TryCreate()
            {
                string[] searchPaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SnaptureCLI.dll"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "x64", "SnaptureCLI.dll"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "x86", "SnaptureCLI.dll"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "SnaptureCLI", "Debug", "SnaptureCLI.dll"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "SnaptureCLI", "Release", "SnaptureCLI.dll"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "SnaptureCLI", "Debug", "SnaptureCLI.dll"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "SnaptureCLI", "Release", "SnaptureCLI.dll"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "SnaptureCLI", "x64", "Debug", "SnaptureCLI.dll"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "SnaptureCLI", "x64", "Release", "SnaptureCLI.dll"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "SnaptureCLI", "x86", "Debug", "SnaptureCLI.dll"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "SnaptureCLI", "x86", "Release", "SnaptureCLI.dll")
                };

                foreach (string path in searchPaths)
                {
                    try
                    {
                        string resolvedPath = Path.GetFullPath(path);
                        if (!File.Exists(resolvedPath))
                            continue;

                        Assembly assembly = Assembly.LoadFrom(resolvedPath);
                        Type nativeType = assembly.GetType("com.HellStormGames.ScreenCapture.Snapture");
                        if (nativeType == null)
                            continue;

                        Type nativeEnumType = assembly.GetType("com.HellStormGames.ScreenCapture.FrameCapturingMethod");
                        Type eventArgsType = assembly.GetType("com.HellStormGames.ScreenCapture.FrameCapturedEventArgs");
                        EventInfo eventInfo = nativeType.GetEvent("onFrameCaptured");
                        MethodInfo startMethod = nativeType.GetMethod("Start", new[] { nativeEnumType });
                        MethodInfo captureRegionMethod = nativeType.GetMethod("CaptureRegion", new[] { typeof(int), typeof(int), typeof(int), typeof(int) });
                        MethodInfo stopMethod = nativeType.GetMethod("Stop", Type.EmptyTypes);
                        PropertyInfo fpsProperty = nativeType.GetProperty("FPS");

                        if (nativeType == null || nativeEnumType == null || eventArgsType == null || eventInfo == null || startMethod == null || captureRegionMethod == null || stopMethod == null || fpsProperty == null)
                            continue;

                        object nativeInstance = Activator.CreateInstance(nativeType);
                        MethodInfo handlerTemplate = typeof(NativeSnaptureEngine).GetMethod(nameof(OnNativeFrameCaptured), BindingFlags.Instance | BindingFlags.NonPublic);
                        MethodInfo handlerMethod = handlerTemplate.MakeGenericMethod(eventArgsType);
                        Delegate handlerDelegate = Delegate.CreateDelegate(eventInfo.EventHandlerType, null, handlerMethod);
                        eventInfo.AddEventHandler(nativeInstance, handlerDelegate);

                        return new NativeSnaptureEngine(nativeInstance, startMethod, captureRegionMethod, stopMethod, fpsProperty);
                    }
                    catch
                    {
                        continue;
                    }
                }

                return null;
            }

            public void Start(FrameCapturingMethod method)
            {
                Type nativeEnumType = startMethod.GetParameters()[0].ParameterType;
                object enumValue = Enum.Parse(nativeEnumType, method.ToString());
                startMethod.Invoke(nativeInstance, new[] { enumValue });
                if (fpsProperty != null)
                    fpsProperty.SetValue(nativeInstance, FPS);
            }

            public void CaptureRegion(int left, int top, int width, int height)
            {
                captureRegionMethod.Invoke(nativeInstance, new object[] { left, top, width, height });
            }

            public void Stop()
            {
                stopMethod.Invoke(nativeInstance, null);
            }

            private void OnNativeFrameCaptured<TEventArgs>(object sender, TEventArgs args)
            {
                PropertyInfo bitmapProperty = typeof(TEventArgs).GetProperty("ScreenCapturedBitmap");
                PropertyInfo frameCountProperty = typeof(TEventArgs).GetProperty("FrameCount");
                if (bitmapProperty == null || frameCountProperty == null)
                    return;

                object bitmapValue = bitmapProperty.GetValue(args);
                object frameCountValue = frameCountProperty.GetValue(args);
                if (bitmapValue is Bitmap bitmap && frameCountValue is int frameCount)
                {
                    Bitmap clone = new Bitmap(bitmap);
                    Snapture.RaiseFrame(clone, frameCount);
                    bitmap.Dispose();
                }
            }
        }
    }
}
