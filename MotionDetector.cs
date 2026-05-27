using System.Diagnostics;
using System.Text;
using OpenCvSharp;

namespace Video_Parsing;

public class MotionDetector
{
    public class MotionSegment
    {
        public double StartTime { get; set; }
        public double EndTime { get; set; }
        public double MaxIntensity { get; set; }
        public double AvgIntensity { get; set; }
    }

    public class DetectionResult
    {
        public string VideoFile { get; set; } = "";
        public double TotalDuration { get; set; }
        public List<MotionSegment> MotionSegments { get; set; } = new();
        public double[] FrameMotionValues { get; set; } = Array.Empty<double>();
        public double SampleInterval { get; set; }
        public int TotalFrames { get; set; }
        public double MotionRatio { get; set; }
    }

    private const int Mog2History = 500;
    private const double Mog2VarThreshold = 16.0;
    private const int PostEventFrames = 5;
    private const int PreEventFrames = 3;

    public async Task<DetectionResult> DetectAsync(
        string videoFilePath,
        double threshold = 0.15,
        int downscaleFactor = 2,
        int frameSkip = 0,
        Action<string>? onStatusUpdate = null,
        Action<int>? onProgress = null,
        CancellationToken? cancellationToken = null)
    {
        return await Task.Run(() =>
        {
            return DetectInternal(
                videoFilePath,
                threshold,
                downscaleFactor,
                frameSkip,
                onStatusUpdate,
                onProgress,
                cancellationToken
            );
        }, cancellationToken ?? CancellationToken.None);
    }

    private DetectionResult DetectInternal(
        string videoFilePath,
        double threshold,
        int downscaleFactor,
        int frameSkip,
        Action<string>? onStatusUpdate,
        Action<int>? onProgress,
        CancellationToken? cancellationToken)
    {
        var result = new DetectionResult 
        { 
            VideoFile = videoFilePath, 
            SampleInterval = 1.0 
        };

        if (!File.Exists(videoFilePath))
            throw new FileNotFoundException("视频文件不存在", videoFilePath);

        try
        {
            using var capture = new VideoCapture(videoFilePath);
            if (!capture.IsOpened())
                throw new InvalidOperationException("无法打开视频文件");

            double fps = capture.Fps;
            if (fps <= 0) fps = 30.0;

            int totalFrames = (int)capture.FrameCount;
            if (totalFrames <= 0) totalFrames = (int)(fps * 60.0);

            double duration = totalFrames / fps;
            result.TotalDuration = duration;
            result.TotalFrames = totalFrames;

            int frameWidth = (int)capture.FrameWidth;
            int frameHeight = (int)capture.FrameHeight;

            ReportStatus(onStatusUpdate, $"📹 视频信息: {frameWidth}x{frameHeight} @ {fps:F1}fps | 时长: {duration:F1}秒 | 总帧数: {totalFrames}");
            ReportProgress(onProgress, 5);

            int effectiveDownscale = Math.Max(downscaleFactor, 1);
            int processWidth = frameWidth / effectiveDownscale;
            int processHeight = frameHeight / effectiveDownscale;

            int kernelSize = GetRecommendedKernelSize(processWidth);

            using var bgSubtractor = BackgroundSubtractorMOG2.Create(
                history: Mog2History,
                varThreshold: Mog2VarThreshold,
                detectShadows: false
            );

            Mat? kernel = null;
            if (kernelSize > 1)
            {
                kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(kernelSize, kernelSize));
            }

            var motionScores = new List<double>();
            Mat frame = new Mat();
            Mat grayFrame = new Mat();
            Mat fgMask = new Mat();

            bool firstFrame = true;
            int processedCount = 0;
            int currentFrameIndex = 0;
            int skipCounter = 0;

            ReportStatus(onStatusUpdate, "⏳ 正在使用 MOG2 算法分析运动...");
            ReportProgress(onProgress, 10);

            while (true)
            {
                if (cancellationToken?.IsCancellationRequested == true)
                    throw new OperationCanceledException();

                if (!capture.Read(frame) || frame.Empty())
                    break;

                currentFrameIndex++;

                skipCounter++;
                if (skipCounter <= frameSkip)
                    continue;
                skipCounter = 0;

                if (effectiveDownscale > 1)
                {
                    using var resizedFrame = new Mat();
                    Cv2.Resize(frame, resizedFrame, new OpenCvSharp.Size(processWidth, processHeight));
                    Cv2.CvtColor(resizedFrame, grayFrame, ColorConversionCodes.BGR2GRAY);
                    resizedFrame.Dispose();
                }
                else
                {
                    Cv2.CvtColor(frame, grayFrame, ColorConversionCodes.BGR2GRAY);
                }

                bgSubtractor.Apply(grayFrame, fgMask, learningRate: -1);

                if (!fgMask.Empty() && kernel != null)
                {
                    Cv2.MorphologyEx(fgMask, fgMask, MorphTypes.Open, kernel);
                }

                if (firstFrame)
                {
                    firstFrame = false;
                    motionScores.Add(0.0);
                    processedCount++;
                    
                    if (processedCount % 10 == 0 || currentFrameIndex == totalFrames)
                    {
                        int progress = 10 + (int)((double)currentFrameIndex / totalFrames * 80);
                        ReportProgress(onProgress, Math.Min(progress, 90));
                    }
                    continue;
                }

                double score = CalculateMotionScore(fgMask);
                motionScores.Add(score);
                processedCount++;

                if (processedCount % 30 == 0 || currentFrameIndex == totalFrames)
                {
                    int progress = 10 + (int)((double)currentFrameIndex / totalFrames * 80);
                    double currentTime = currentFrameIndex / fps;
                    ReportStatus(onStatusUpdate, $"⏳ 已处理: {currentFrameIndex}/{totalFrames} 帧 | " +
                                     $"时间: {currentTime:F1}s/{duration:F1}s | 运动: {score * 100:F1}%");
                    ReportProgress(onProgress, Math.Min(progress, 90));
                }
            }

            frame.Dispose();
            grayFrame.Dispose();
            fgMask.Dispose();

            ReportStatus(onStatusUpdate, "🔍 正在识别运动段...");
            ReportProgress(onProgress, 90);

            result.FrameMotionValues = motionScores.ToArray();
            
            int actualFrameSkip = frameSkip + 1;
            result.SampleInterval = actualFrameSkip / fps;

            result.MotionSegments = IdentifyMotionSegments(
                motionScores.ToArray(),
                threshold,
                result.SampleInterval
            );

            if (motionScores.Count > 0)
            {
                double motionFrameCount = result.MotionSegments.Sum(s => 
                    (s.EndTime - s.StartTime) / result.SampleInterval);
                result.MotionRatio = motionFrameCount / motionScores.Count;
            }

            ReportStatus(onStatusUpdate, $"✅ 检测完成! 发现 {result.MotionSegments.Count} 个运动段 | " +
                             $"总时长: {duration:F1}秒 | 运动占比: {result.MotionRatio * 100:F1}%");
            ReportProgress(onProgress, 100);

            return result;
        }
        catch (OperationCanceledException)
        {
            ReportStatus(onStatusUpdate, "⚠️ 检测已取消");
            throw;
        }
        catch (Exception ex)
        {
            ReportStatus(onStatusUpdate, $"❌ 检测失败: {ex.Message}");
            throw;
        }
    }

    private void ReportStatus(Action<string>? callback, string message)
    {
        callback?.Invoke(message);
    }

    private void ReportProgress(Action<int>? callback, int progress)
    {
        callback?.Invoke(Math.Min(progress, 100));
    }

    private int GetRecommendedKernelSize(int width)
    {
        return width >= 1920 ? 7 : width >= 1280 ? 5 : 3;
    }

    private double CalculateMotionScore(Mat mask)
    {
        if (mask.Empty()) return 0.0;

        int nonZero = Cv2.CountNonZero(mask);
        int totalPixels = mask.Rows * mask.Cols;

        return totalPixels > 0 ? (double)nonZero / totalPixels : 0.0;
    }

    private List<MotionSegment> IdentifyMotionSegments(
        double[] motionValues,
        double threshold,
        double interval)
    {
        List<MotionSegment> segments = new List<MotionSegment>();
        
        if (motionValues.Length == 0)
            return segments;

        bool inMotion = false;
        int startIdx = 0;
        double maxIntensity = 0;
        double sumIntensity = 0;
        int count = 0;
        int framesBelowThreshold = 0;

        for (int i = 0; i < motionValues.Length; i++)
        {
            bool isMotion = motionValues[i] >= threshold;

            if (isMotion && !inMotion)
            {
                startIdx = Math.Max(0, i - PreEventFrames);
                inMotion = true;
                maxIntensity = motionValues[i];
                sumIntensity = motionValues[i];
                count = 1;
                framesBelowThreshold = 0;
            }
            else if (isMotion && inMotion)
            {
                maxIntensity = Math.Max(maxIntensity, motionValues[i]);
                sumIntensity += motionValues[i];
                count++;
                framesBelowThreshold = 0;
            }
            else if (!isMotion && inMotion)
            {
                framesBelowThreshold++;

                if (framesBelowThreshold > PostEventFrames)
                {
                    int endIdx = Math.Min(motionValues.Length - 1, i - 1 + PostEventFrames);
                    
                    if (endIdx > startIdx && count >= 1)
                    {
                        segments.Add(new MotionSegment
                        {
                            StartTime = startIdx * interval,
                            EndTime = endIdx * interval,
                            MaxIntensity = maxIntensity,
                            AvgIntensity = sumIntensity / count
                        });
                    }

                    inMotion = false;
                    framesBelowThreshold = 0;
                }
            }
        }

        if (inMotion && count >= 1)
        {
            int endIdx = motionValues.Length - 1 + PostEventFrames;
            segments.Add(new MotionSegment
            {
                StartTime = startIdx * interval,
                EndTime = endIdx * interval,
                MaxIntensity = maxIntensity,
                AvgIntensity = sumIntensity / count
            });
        }

        MergeCloseSegments(segments, interval * 3);

        return segments;
    }

    private void MergeCloseSegments(List<MotionSegment> segments, double maxGap)
    {
        if (segments.Count < 2)
            return;

        for (int i = segments.Count - 2; i >= 0; i--)
        {
            double gap = segments[i + 1].StartTime - segments[i].EndTime;
            
            if (gap <= maxGap)
            {
                segments[i].EndTime = segments[i + 1].EndTime;
                segments[i].MaxIntensity = Math.Max(segments[i].MaxIntensity, segments[i + 1].MaxIntensity);
                segments[i].AvgIntensity = (segments[i].AvgIntensity + segments[i + 1].AvgIntensity) / 2;
                
                segments.RemoveAt(i + 1);
            }
        }
    }

    public async Task<List<string>> ExtractMotionClipsAsync(
        string inputVideoPath,
        DetectionResult detectionResult,
        string outputDirectory,
        Action<string>? onStatusUpdate = null,
        Action<int>? onProgress = null,
        CancellationToken? cancellationToken = null)
    {
        return await Task.Run(() =>
        {
            return ExtractClipsInternal(
                inputVideoPath,
                detectionResult,
                outputDirectory,
                onStatusUpdate,
                onProgress,
                cancellationToken
            );
        }, cancellationToken ?? CancellationToken.None);
    }

    private List<string> ExtractClipsInternal(
        string inputVideoPath,
        DetectionResult detectionResult,
        string outputDirectory,
        Action<string>? onStatusUpdate,
        Action<int>? onProgress,
        CancellationToken? cancellationToken)
    {
        var outputFiles = new List<string>();

        if (!detectionResult.MotionSegments.Any())
        {
            ReportStatus(onStatusUpdate, "ℹ️ 没有检测到运动片段，无需导出");
            return outputFiles;
        }

        Directory.CreateDirectory(outputDirectory);

        string videoName = Path.GetFileNameWithoutExtension(inputVideoPath);
        string extension = Path.GetExtension(inputVideoPath).ToLower();

        ReportStatus(onStatusUpdate, $"💾 开始导出 {detectionResult.MotionSegments.Count} 个运动片段...");

        for (int i = 0; i < detectionResult.MotionSegments.Count; i++)
        {
            if (cancellationToken?.IsCancellationRequested == true)
                throw new OperationCanceledException();

            var segment = detectionResult.MotionSegments[i];
            
            string outputFileName = $"{videoName}_motion_{i + 1:D3}{extension}";
            string outputPath = Path.Combine(outputDirectory, outputFileName);

            ExtractClipInternal(
                inputVideoPath,
                outputPath,
                segment.StartTime,
                segment.EndTime,
                cancellationToken
            );

            outputFiles.Add(outputPath);

            int progress = (int)((i + 1) / (double)detectionResult.MotionSegments.Count * 100);
            ReportStatus(onStatusUpdate, $"✅ 已导出 ({i + 1}/{detectionResult.MotionSegments.Count}): " +
                             $"{segment.StartTime:F1}s → {segment.EndTime:F1}s");
            ReportProgress(onProgress, progress);
        }

        ReportStatus(onStatusUpdate, $"🎉 导出完成! 共 {outputFiles.Count} 个片段保存到: {outputDirectory}");
        ReportProgress(onProgress, 100);

        return outputFiles;
    }

    private void ExtractClipInternal(
        string inputPath,
        string outputPath,
        double startTime,
        double endTime,
        CancellationToken? cancellationToken)
    {
        string ffmpegPath = FindFFmpegPath();
        
        if (string.IsNullOrEmpty(ffmpegPath))
            throw new InvalidOperationException("未找到 FFmpeg，请安装 FFmpeg 并添加到系统 PATH");

        int startHours = (int)(startTime / 3600);
        int startMinutes = (int)((startTime % 3600) / 60);
        double startSeconds = startTime % 60;
        string startTimeStr = $"{startHours:D2}:{startMinutes:D2}:{startSeconds:00.000}";

        double duration = endTime - startTime;

        var args = new StringBuilder();
        args.Append("-y ");
        args.Append("-ss ").Append(startTimeStr).Append(' ');
        args.Append("-i \"").Append(inputPath).Append("\" ");
        args.Append("-t ").Append(duration.ToString("F3")).Append(' ');
        args.Append("-c:v libx264 -preset ultrafast -crf 28 ");
        args.Append("-c:a aac -b:a 128k ");
        args.Append("-movflags +faststart ");
        args.Append("-avoid_negative_ts make_zero ");
        args.Append("\"").Append(outputPath).Append("\"");

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = args.ToString(),
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("无法启动 FFmpeg 进程");

        var errorTask = Task.Run(() => process.StandardError.ReadToEnd());
        var outputTask = Task.Run(() => process.StandardOutput.ReadToEnd());

        bool exited = false;
        
        if (cancellationToken.HasValue)
        {
            try
            {
                exited = process.WaitForExit((int)TimeSpan.FromMinutes(5).TotalMilliseconds);
                
                if (!exited || cancellationToken.Value.IsCancellationRequested)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    throw new OperationCanceledException();
                }
            }
            catch (Exception)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw;
            }
        }
        else
        {
            try
            {
                exited = process.WaitForExit((int)TimeSpan.FromMinutes(5).TotalMilliseconds);
                
                if (!exited)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    throw new TimeoutException("导出超时（5分钟）");
                }
            }
            catch (TimeoutException)
            {
                throw;
            }
            catch (Exception ex)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new InvalidOperationException($"导出失败: {ex.Message}");
            }
        }

        errorTask.Wait(TimeSpan.FromSeconds(5));
        outputTask.Wait(TimeSpan.FromSeconds(5));

        if (process.ExitCode != 0)
        {
            string error = errorTask.IsCompleted ? errorTask.Result : "读取错误信息失败";
            throw new InvalidOperationException($"FFmpeg 导出失败 (退出码: {process.ExitCode}): {error}");
        }

        if (!File.Exists(outputPath))
            throw new InvalidOperationException($"输出文件未生成: {outputPath}");

        long fileSize = new FileInfo(outputPath).Length;
        if (fileSize == 0)
            throw new InvalidOperationException("输出文件为空");
    }

    private string FindFFmpegPath()
    {
        string[] possiblePaths =
        {
            "ffmpeg",
            @"C:\ffmpeg\bin\ffmpeg.exe",
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe")
        };

        foreach (var path in possiblePaths)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = "-version",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    process.WaitForExit(3000);
                    if (process.ExitCode == 0)
                        return path;
                }
            }
            catch
            {
                continue;
            }
        }

        return string.Empty;
    }
}
