using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using Emgu;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

using com.HellScape.ScreenCapture;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.IO;

namespace MercExchangeLokator
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 
  
    
    public partial class MainWindow : Window
    {
        //--- found it, render rectangle.
        bool bCaptureStopping = false;
        bool bCapturing = false;
        private object bmpLocker = new object();

        Image<Bgr, byte> refImage = null;
        Image<Bgr, byte> scaledRefImage = null;
        Image<Bgr, byte> refImageAlt = null;
        Image<Bgr, byte> scaledRefImageAlt = null;
        System.Drawing.Size originalImageSize = System.Drawing.Size.Empty;
        System.Drawing.Size scaledImageSize = System.Drawing.Size.Empty;
        System.Threading.Thread capturingThread;

        RenderCanvas RenderCanvas = null;

        bool useOptimizedMethod = true;
        bool enableSoundNotification = true;
        volatile bool isMatchingBusy = false;
        bool wasTargetFound = false;
        volatile bool isProcessingFrame = false;
        DateTime lastAlertUtc = DateTime.MinValue;
        const int alertCooldownMs = 1500;

        public string basePath  => System.AppDomain.CurrentDomain.BaseDirectory;
        public string refFile => $@"{basePath}Images\Ref\merc_exchange_sample02.png";
        public string refFileAlt => $@"{basePath}Images\Ref\merc_exchange_sample.png";
        string debugSaveOutputPath => $@"{basePath}Tests\";
        
        private double defaultScaleFactor = 1.0f;

        Lokator Lokator { get; set; }

        double matchingTolerence = 0.40;
        const double minMatchingTolerance = 0.30;
        const double maxMatchingTolerance = 0.55;
        bool isThresholdUiUpdating = false;

        string thresholdStateFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MercExchangeLokator",
            "threshold.txt");

        public double ScaleFactor
        {
            get { return defaultScaleFactor; }
            set { defaultScaleFactor = value; }
        }

        public MainWindow()
        {
            InitializeComponent();
            Lokator = new Lokator();
            Lokator.onMercExchangeFound += Lokator_onMercExchangeFound;
            Lokator.onMercExchangeNotFound += Lokator_onMercExchangeNotFound;
        }

        private void Lokator_onMercExchangeNotFound(object sender, MercExchangeFoundArguments e)
        {
            wasTargetFound = false;
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
               RenderCanvas.Canvas01.Children.Clear();
            }));
        }

        bool isRendering = false;
        private void Lokator_onMercExchangeFound(object sender, MercExchangeFoundArguments e)
        {
            bool shouldAlert = false;
            if (enableSoundNotification)
            {
                if (!wasTargetFound)
                {
                    shouldAlert = true;
                }
                else if ((DateTime.UtcNow - lastAlertUtc).TotalMilliseconds >= alertCooldownMs)
                {
                    shouldAlert = true;
                }
            }

            if (shouldAlert)
            {
                TryPlayAlertSound();
                lastAlertUtc = DateTime.UtcNow;
            }

            wasTargetFound = true;

            var match = e.Location;
            
            var dpiXProperty = typeof(SystemParameters).GetProperty("DpiX", BindingFlags.NonPublic | BindingFlags.Static);
            var dpiYProperty = typeof(SystemParameters).GetProperty("Dpi", BindingFlags.NonPublic | BindingFlags.Static);

            var dpiX = (int)dpiXProperty.GetValue(null, null) / 96.0;
            var dpiY = (int)dpiYProperty.GetValue(null, null) / 96.0;

            if (useOptimizedMethod)
            {
                double mx, my = 0.0f;
                mx = ((double)match.X * e.ScaleFactor.Width);
                my = ((double)match.Y * e.ScaleFactor.Height);
                match.X = (int)Math.Round(mx);
                match.Y = (int)Math.Round(my);  
                match.Width = (int)Math.Round((double)match.Width * e.ScaleFactor.Width);
                match.Height = (int)Math.Round((double)match.Height * e.ScaleFactor.Height);
            }

            RenderCanvas.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!bCaptureStopping)
                {
                    if (!isRendering)
                    {
                        isRendering = true;
                        try
                        {
                            var height = RenderCanvas.Canvas01.ActualHeight * dpiY;
                            var width = RenderCanvas.Canvas01.ActualWidth * dpiX;

                            WriteableBitmap wb = BitmapFactory.New((int)width, (int)height);

                            using (wb.GetBitmapContext())
                            {
                                var displayX = match.X / dpiX;
                                var displayY = match.Y / dpiY;
                                var displayW = Math.Max(24.0, match.Width / dpiX);
                                var displayH = Math.Max(24.0, match.Height / dpiY);

                                var x = displayX - 6;
                                var y = displayY - 6;
                                var x2 = displayX + displayW + 6;
                                var y2 = displayY + displayH + 6;

                                x = Math.Max(0, x);
                                y = Math.Max(0, y);
                                x2 = Math.Min(width - 1, x2);
                                y2 = Math.Min(height - 1, y2);
                                var thickness = 10;

                                wb.DrawRectangle((int)x, (int)y, (int)x2, (int)y2, System.Windows.Media.Colors.Red);
                                for (var i = 0; i < thickness; i++)
                                {
                                    wb.DrawRectangle((int)x--, (int)y--, (int)x2++, (int)y2++, System.Windows.Media.Colors.Red);
                                }

                                System.Windows.Controls.Image image = new System.Windows.Controls.Image();
                                image.Source = wb;

                                RenderCanvas.Canvas01.Children.Clear();
                                RenderCanvas.Canvas01.Children.Add(image);
                            }
                        }
                        finally
                        {
                            isRendering = false;
                        }
                    }
                }
            }));

        }

        

        #region Screen Capturing Functions
        private void performTemplateMatching(Bitmap bitmap, double threshold)
        {

            if (bitmap == null)
                return;

            Image<Bgr, byte> src = bitmap.ToImage<Bgr, byte>();

            try
            {
                Image<Bgr, byte>[] activeTemplates = useOptimizedMethod
                    ? new[] { scaledRefImage, scaledRefImageAlt }
                    : new[] { refImage, refImageAlt };

                double bestScore = double.MinValue;
                System.Drawing.Point bestLocation = System.Drawing.Point.Empty;
                System.Drawing.Size bestTemplateSize = System.Drawing.Size.Empty;

                foreach (var tmpl in activeTemplates)
                {
                    if (tmpl == null || tmpl.Width > src.Width || tmpl.Height > src.Height)
                        continue;

                    double[] minV, maxV;
                    System.Drawing.Point[] minL, maxL;
                    using (var res = src.MatchTemplate(tmpl, TemplateMatchingType.CcoeffNormed))
                    {
                        res.MinMax(out minV, out maxV, out minL, out maxL);
                    }
                    if (maxV != null && maxV.Length > 0 && maxV[0] > bestScore)
                    {
                        bestScore = maxV[0];
                        bestLocation = maxL[0];
                        bestTemplateSize = tmpl.Size;
                    }
                }

                bool isFound = bestScore >= threshold;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    this.Title = $"Merc Exchange Lokator v0.1 | score {bestScore:0.000} | thr {threshold:0.000}";
                }));

                if (isFound)
                {
                    double scaleFactorX = 1.0d;
                    double scaleFactorY = 1.0d;

                    if (useOptimizedMethod && scaledImageSize.Width > 0 && scaledImageSize.Height > 0)
                    {
                        scaleFactorX = (double)originalImageSize.Width / scaledImageSize.Width;
                        scaleFactorY = (double)originalImageSize.Height / scaledImageSize.Height;
                    }

                    Lokator.Found(new System.Drawing.Rectangle(bestLocation, bestTemplateSize),
                        new System.Drawing.SizeF((float)scaleFactorX, (float)scaleFactorY));
                }
                else
                {
                    Lokator.NotFound();
                }
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    this.Title = $"Merc Exchange Lokator v0.1 | error: {ex.Message}";
                }));
                Lokator.NotFound();
            }

            src.Dispose();
            src = null;
        }

        private Task performTemplateMatchingAsync(Bitmap bitmap, double threshold)
        {
            return Task.Run(() => { performTemplateMatching(bitmap, threshold); } );
        }

        private Bitmap ResizeByHalf(Image<Bgr, byte> image)
        {
            image.Resize(image.Width / 2, image.Height / 2, Inter.Linear);  
            return image.ToBitmap<Bgr, byte>(); 
        }
        private async void CapturingThread(Bitmap bitmap)
        {
            if (isProcessingFrame)
            {
                bitmap.Dispose();
                return;
            }
            isProcessingFrame = true;
            try
            {
                Bitmap bclone = (Bitmap)bitmap.Clone();
                await performTemplateMatchingAsync(bclone, matchingTolerence);
                bclone.Dispose();
            }
            finally
            {
                isProcessingFrame = false;
            }
        }
        public void StartCapturing()
        {
            Snapture.onFrameCaptured += Snapture_onFrameCaptured;
            Snapture.FPS = 60;
            //-- DX is causing memory leaks and eating memory.
            Snapture.Start(FrameCapturingMethod.GDI);

            //-- now everything is completely manual when capturing. CLI C++ doesn't do any while loop.
            int sh = Snapture.ScreenHeight;
            int sw = Snapture.ScreenWidth;
            int left = 0;
            int top = (int)Math.Round(sh * 0.12d);
            int width = sw;
            int height = (int)Math.Round(sh * 0.78d);

            if (top < 0) top = 0;
            if (top >= sh) top = 0;
            if (width <= 0 || width > sw) width = sw;
            if (height <= 0 || height > sh) height = sh;

            if (top + height > sh)
                height = sh - top;

            if (height <= 0)
                height = sh;

            System.Diagnostics.Debug.WriteLine($"Capture region: left={left}, top={top}, width={width}, height={height}, screen={sw}x{sh}");
            
            Snapture.CaptureRegion(left,top, width, height);  

        }

        private void Snapture_onFrameCaptured(object sender, FrameCapturedEventArgs e)
        {
            if (bCapturing)
            {
                if (e.ScreenCapturedBitmap == null)
                    return;
                else
                {
                    originalImageSize = e.ScreenCapturedBitmap.Size;
                    //e.ScreenCapturedBitmap.Save($"{basePath}Screenshots/ScreenCaptured{e.FrameCount}.jpg");
                    //-- resize screen captured here.
                    if (useOptimizedMethod)
                    {
                        Image<Bgr, byte> resizedImage = e.ScreenCapturedBitmap.ToImage<Bgr, byte>();
                        Bitmap resizedBitmap = resizedImage.Resize(ScaleFactor, Inter.Linear).ToBitmap();
                        scaledImageSize = resizedBitmap.Size;

                        CapturingThread(resizedBitmap);
                        resizedBitmap.Dispose();
                        resizedImage.Dispose();
                    }
                    else
                    {
                        CapturingThread(e.ScreenCapturedBitmap);
                    }
                    e.ScreenCapturedBitmap.Dispose();
                }
            }
        }

        #endregion

        #region Main Functions
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (Snapture.isActive)
                Snapture.Stop();

            Environment.Exit(1);
        }
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.Topmost = true;
            LoadThresholdFromDisk();
            UpdateThresholdUi();
          
            refImage = new Image<Bgr, byte>(refFile);
            if (refImage != null)
                System.Diagnostics.Debug.WriteLine($"Successfully loaded reference image: {refFile}");

            if (File.Exists(refFileAlt))
            {
                refImageAlt = new Image<Bgr, byte>(refFileAlt);
                System.Diagnostics.Debug.WriteLine($"Successfully loaded alternate reference image: {refFileAlt}");
            }

            if(useOptimizedMethod)
            {
                ScaleFactor = 0.5f;

                scaledRefImage = refImage.Resize(ScaleFactor, Inter.Linear);
                if (refImageAlt != null)
                    scaledRefImageAlt = refImageAlt.Resize(ScaleFactor, Inter.Linear);
            }
            RenderCanvas = new RenderCanvas();
            RenderCanvas.Topmost = true;
            RenderCanvas.Show();
        }

        private void Window_Unloaded(object sender, RoutedEventArgs e)
        {

        }

        private void StartCaptureBtn_MouseDown(object sender, MouseButtonEventArgs e)
        {

            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                StartCaptureBtn.IsEnabled = false;
                StopCaptureBtn.IsEnabled = true;
                bCapturing = true;
                bCaptureStopping = false;

                Task CapturingTask = new Task(() =>
                {
                    StartCapturing();
                });
                CapturingTask.Start();
                /*
                if (capturingThread == null)
                {
                    capturingThread = new System.Threading.Thread(new System.Threading.ThreadStart(StartCapturing));
                    capturingThread.Name = "Screen Capturing Thread";
                    capturingThread.Start();
                }
                else {
                }
                */

            }
            ));
        }

        private void StopCaptureBtn_MouseDown(object sender, MouseButtonEventArgs e)
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (bCapturing)
                {
                    Snapture.Stop();
                    bCaptureStopping = true;
                    bCapturing = false;
                    this.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        RenderCanvas.Canvas01.Children.Clear();
                    }));

                    StartCaptureBtn.IsEnabled = true;
                    StopCaptureBtn.IsEnabled = false;

                    if (capturingThread != null)
                    {
                        capturingThread.Abort();
                        capturingThread = null;
                    }

                    System.Diagnostics.Debug.WriteLine($"Screen Capturing stopped.");
                }

            }));
        }

        private void SettingsBtn_MouseDown(object sender, MouseButtonEventArgs e)
        {

        }

        private void ThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isThresholdUiUpdating)
                return;

            double next = Math.Round(e.NewValue, 2);
            if (next < minMatchingTolerance)
                next = minMatchingTolerance;
            else if (next > maxMatchingTolerance)
                next = maxMatchingTolerance;

            matchingTolerence = next;
            UpdateThresholdUi();
            SaveThresholdToDisk();
        }

        private void UpdateThresholdUi()
        {
            isThresholdUiUpdating = true;
            if (ThresholdValueText != null)
                ThresholdValueText.Text = matchingTolerence.ToString("0.00");

            if (ThresholdSlider != null && Math.Abs(ThresholdSlider.Value - matchingTolerence) > 0.0001)
                ThresholdSlider.Value = matchingTolerence;
            isThresholdUiUpdating = false;
        }

        private void ThresholdResetButton_Click(object sender, RoutedEventArgs e)
        {
            matchingTolerence = 0.40;
            UpdateThresholdUi();
            SaveThresholdToDisk();
        }

        private void SaveThresholdToDisk()
        {
            try
            {
                var directory = Path.GetDirectoryName(thresholdStateFilePath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(thresholdStateFilePath, matchingTolerence.ToString("0.00"));
            }
            catch
            {
                // ignored
            }
        }

        private void LoadThresholdFromDisk()
        {
            try
            {
                if (!File.Exists(thresholdStateFilePath))
                    return;

                var value = File.ReadAllText(thresholdStateFilePath).Trim();
                double parsed;
                if (!double.TryParse(value, out parsed))
                    return;

                parsed = Math.Round(parsed, 2);
                if (parsed < minMatchingTolerance)
                    parsed = minMatchingTolerance;
                if (parsed > maxMatchingTolerance)
                    parsed = maxMatchingTolerance;

                matchingTolerence = parsed;
            }
            catch
            {
                // ignored
            }
        }

        private void TryPlayAlertSound()
        {
            try
            {
                SystemSounds.Exclamation.Play();
            }
            catch
            {
                // ignored
            }

            try
            {
                MessageBeep(0xFFFFFFFF);
            }
            catch
            {
                // ignored
            }

            // Final fallback for environments where Windows system sounds are disabled.
            Task.Run(() =>
            {
                try
                {
                    Console.Beep(1400, 180);
                }
                catch
                {
                    // ignored
                }
            });
        }

        [DllImport("user32.dll")]
        private static extern bool MessageBeep(uint uType);

        #endregion
    }
}
