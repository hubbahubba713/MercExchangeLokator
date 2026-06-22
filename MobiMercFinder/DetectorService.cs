using OpenCvSharp;

namespace MobiMercFinder;

/// <summary>
/// Template matching service — same CcoeffNormed algorithm as desktop app.
/// </summary>
public class DetectorService
{
    private Mat? _template1;
    private Mat? _template2;

    public double Threshold { get; set; } = 0.40;
    public double BandTop { get; set; } = 0.12;
    public double BandBottom { get; set; } = 0.90;

    public void LoadTemplates(Stream? stream1, Stream? stream2)
    {
        _template1?.Dispose();
        _template2?.Dispose();
        _template1 = null;
        _template2 = null;

        if (stream1 != null)
            _template1 = StreamToMat(stream1);

        if (stream2 != null)
            _template2 = StreamToMat(stream2);
    }

    public DetectionResult Detect(Stream imageStream)
    {
        if (_template1 == null && _template2 == null)
            return new DetectionResult { Score = 0, Found = false, Error = "No reference images loaded." };

        try
        {
            using var src = StreamToMat(imageStream);
            if (src.Empty())
                return new DetectionResult { Score = 0, Found = false, Error = "Could not decode image." };

            double bestScore = double.MinValue;
            OpenCvSharp.Point bestLocation = default;
            OpenCvSharp.Size bestTemplateSize = default;

            foreach (var tmpl in new[] { _template1, _template2 })
            {
                if (tmpl == null || tmpl.Width > src.Width || tmpl.Height > src.Height)
                    continue;

                using var result = new Mat();
                Cv2.MatchTemplate(src, tmpl, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);

                if (maxVal > bestScore)
                {
                    bestScore = maxVal;
                    bestLocation = maxLoc;
                    bestTemplateSize = new OpenCvSharp.Size(tmpl.Width, tmpl.Height);
                }
            }

            bool inBand = true;
            if (src.Height > 0)
            {
                double centerY = bestLocation.Y + (bestTemplateSize.Height / 2.0);
                double normY = centerY / src.Height;
                inBand = normY >= BandTop && normY <= BandBottom;
            }

            bool found = bestScore >= Threshold && inBand;

            return new DetectionResult
            {
                Score = bestScore,
                Found = found,
                MatchX = bestLocation.X,
                MatchY = bestLocation.Y,
                MatchWidth = bestTemplateSize.Width,
                MatchHeight = bestTemplateSize.Height
            };
        }
        catch (Exception ex)
        {
            return new DetectionResult { Score = 0, Found = false, Error = ex.Message };
        }
    }

    private static Mat StreamToMat(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        return Cv2.ImDecode(bytes, ImreadModes.Color);
    }
}

public class DetectionResult
{
    public double Score { get; set; }
    public bool Found { get; set; }
    public int MatchX { get; set; }
    public int MatchY { get; set; }
    public int MatchWidth { get; set; }
    public int MatchHeight { get; set; }
    public string? Error { get; set; }
}
