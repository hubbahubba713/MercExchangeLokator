using System;
using System.Drawing;
using System.Threading.Tasks;

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
        public static int ScreenWidth => (int)System.Windows.SystemParameters.PrimaryScreenWidth;
        public static int ScreenHeight => (int)System.Windows.SystemParameters.PrimaryScreenHeight;

        public static void Start(FrameCapturingMethod method)
        {
            isActive = true;
            // no-op: stub does not capture frames
        }

        public static void CaptureRegion(int left, int top, int width, int height)
        {
            // no-op stub
        }

        public static void Stop()
        {
            isActive = false;
        }

        // Helper to raise a fake frame (not used automatically)
        public static void RaiseFrame(Bitmap bmp, int frame)
        {
            onFrameCaptured?.Invoke(null, new FrameCapturedEventArgs(bmp, frame));
        }
    }
}
