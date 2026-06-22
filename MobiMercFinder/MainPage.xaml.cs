using Microsoft.Maui.Media;

namespace MobiMercFinder;

public partial class MainPage : ContentPage
{
    private readonly DetectorService _detector;
    private int _refLoadedCount = 0;
    private Stream? _ref1Stream;
    private Stream? _ref2Stream;

    public MainPage(DetectorService detector)
    {
        InitializeComponent();
        _detector = detector;
        LoadSettingsFromPreferences();
    }

    // ── Reference images ──────────────────────────────────────────────────────

    private async void LoadRef1_Clicked(object sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select Reference Image 1",
                FileTypes = FilePickerFileType.Images
            });
            if (result == null) return;

            _ref1Stream = await result.OpenReadAsync();
            _refLoadedCount = (_ref2Stream != null) ? 2 : 1;
            ReloadTemplates();
            UpdateRefStatus();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private async void LoadRef2_Clicked(object sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select Reference Image 2",
                FileTypes = FilePickerFileType.Images
            });
            if (result == null) return;

            _ref2Stream = await result.OpenReadAsync();
            _refLoadedCount = (_ref1Stream != null) ? 2 : 1;
            ReloadTemplates();
            UpdateRefStatus();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private void ReloadTemplates()
    {
        Stream? s1 = RewindStream(_ref1Stream);
        Stream? s2 = RewindStream(_ref2Stream);
        _detector.LoadTemplates(s1, s2);
    }

    private static Stream? RewindStream(Stream? s)
    {
        if (s == null) return null;
        if (s.CanSeek) s.Seek(0, SeekOrigin.Begin);
        return s;
    }

    private void UpdateRefStatus()
    {
        RefStatusLabel.Text = _refLoadedCount switch
        {
            0 => "No reference images loaded.",
            1 => "1 reference image loaded.",
            _ => "2 reference images loaded."
        };
    }

    // ── Detection ─────────────────────────────────────────────────────────────

    private async void PickScreenshot_Clicked(object sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Pick Screenshot to Scan",
                FileTypes = FilePickerFileType.Images
            });
            if (result == null) return;

            using var stream = await result.OpenReadAsync();
            await RunDetection(stream, result.FullPath);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
        }
    }

    private async void TakePhoto_Clicked(object sender, EventArgs e)
    {
        try
        {
            if (!MediaPicker.Default.IsCaptureSupported)
            {
                await DisplayAlert("Not supported", "Camera capture is not supported on this device.", "OK");
                return;
            }

            var photo = await MediaPicker.Default.CapturePhotoAsync();
            if (photo == null) return;

            using var stream = await photo.OpenReadAsync();
            await RunDetection(stream, photo.FullPath);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
        }
    }

    private async Task RunDetection(Stream imageStream, string imagePath)
    {
        if (_refLoadedCount == 0)
        {
            await DisplayAlert("No templates", "Load at least one reference image first.", "OK");
            return;
        }

        StatusLabel.Text = "Scanning…";
        ScoreLabel.Text = "Score: — | Threshold: " + _detector.Threshold.ToString("0.00");
        ResultBorder.IsVisible = false;

        // Run detection on background thread
        var result = await Task.Run(() =>
        {
            imageStream.Seek(0, SeekOrigin.Begin);
            return _detector.Detect(imageStream);
        });

        ScoreLabel.Text = $"Score: {result.Score:0.000} | Threshold: {_detector.Threshold:0.00}";

        if (!string.IsNullOrEmpty(result.Error))
        {
            StatusLabel.Text = $"Error: {result.Error}";
            return;
        }

        if (result.Found)
        {
            StatusLabel.Text = "FOUND — Merc Exchange detected!";
            StatusLabel.TextColor = Colors.Green;
            await ShowResultImage(imagePath, result);
            PlayAlert();
        }
        else
        {
            StatusLabel.Text = "MISS — target not in this screenshot.";
            StatusLabel.TextColor = Color.FromArgb("#888888");
            ResultBorder.IsVisible = false;
        }
    }

    private async Task ShowResultImage(string imagePath, DetectionResult result)
    {
        try
        {
            ResultImage.Source = ImageSource.FromFile(imagePath);
            ResultBorder.IsVisible = true;
        }
        catch
        {
            // Ignored — image display is non-critical
        }

        await Task.CompletedTask;
    }

    private static void PlayAlert()
    {
        try { Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(300)); } catch { }
    }

    // ── Threshold controls ────────────────────────────────────────────────────

    private void ThresholdSlider_ValueChanged(object sender, ValueChangedEventArgs e)
    {
        double v = Math.Round(e.NewValue, 2);
        _detector.Threshold = v;
        ThresholdValueLabel.Text = v.ToString("0.00");
        ScoreLabel.Text = $"Score: — | Threshold: {v:0.00}";
        SaveSettings();
    }

    private void ResetThreshold_Clicked(object sender, EventArgs e)
    {
        ThresholdSlider.Value = 0.40;
    }

    // ── Zone controls ─────────────────────────────────────────────────────────

    private void BandTopSlider_ValueChanged(object sender, ValueChangedEventArgs e)
    {
        double v = Math.Round(e.NewValue, 2);
        if (v > _detector.BandBottom - 0.05)
            v = Math.Max(0, _detector.BandBottom - 0.05);

        _detector.BandTop = v;
        BandTopLabel.Text = $"{(int)(v * 100)}%";
        SaveSettings();
    }

    private void BandBottomSlider_ValueChanged(object sender, ValueChangedEventArgs e)
    {
        double v = Math.Round(e.NewValue, 2);
        if (v < _detector.BandTop + 0.05)
            v = Math.Min(1.0, _detector.BandTop + 0.05);

        _detector.BandBottom = v;
        BandBottomLabel.Text = $"{(int)(v * 100)}%";
        SaveSettings();
    }

    private void ResetZone_Clicked(object sender, EventArgs e)
    {
        BandTopSlider.Value = 0.12;
        BandBottomSlider.Value = 0.90;
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private void SaveSettings()
    {
        Preferences.Default.Set("threshold", _detector.Threshold);
        Preferences.Default.Set("bandTop", _detector.BandTop);
        Preferences.Default.Set("bandBottom", _detector.BandBottom);
    }

    private void LoadSettingsFromPreferences()
    {
        double thr = Preferences.Default.Get("threshold", 0.40);
        double top = Preferences.Default.Get("bandTop", 0.12);
        double bot = Preferences.Default.Get("bandBottom", 0.90);

        _detector.Threshold = thr;
        _detector.BandTop = top;
        _detector.BandBottom = bot;

        ThresholdSlider.Value = thr;
        ThresholdValueLabel.Text = thr.ToString("0.00");
        BandTopSlider.Value = top;
        BandTopLabel.Text = $"{(int)(top * 100)}%";
        BandBottomSlider.Value = bot;
        BandBottomLabel.Text = $"{(int)(bot * 100)}%";
    }
}
