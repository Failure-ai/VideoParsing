using System.Diagnostics;
using System.Text;

namespace Video_Parsing;

/// <summary>
/// 视频转换器 - 使用 FFmpeg 将裸视频流封装为 MP4
/// </summary>
public class VideoConverter : IDisposable
{
    private readonly FFmpegManager _ffmpeg;
    private readonly RawVideoParser _parser;
    private long _currentOutTimeMs;
    private long _totalDurationMs;
    private Action<int>? _onProgressCallback;
    private bool _disposed;

    public VideoConverter(FFmpegManager ffmpeg)
    {
        _ffmpeg = ffmpeg;
        _parser = new RawVideoParser();
    }

    private void UpdateProgress()
    {
        if (_totalDurationMs > 0 && _currentOutTimeMs > 0)
        {
            int progress = Math.Min(99, (int)(_currentOutTimeMs * 100 / _totalDurationMs));
            _onProgressCallback?.Invoke(progress);
        }
        else if (_currentOutTimeMs > 0)
        {
            // 没有总时长时，使用估算（假设最多60秒）
            int progress = Math.Min(95, (int)(_currentOutTimeMs * 100 / 60000));
            _onProgressCallback?.Invoke(progress);
        }
    }

    /// <summary>
    /// 转换结果
    /// </summary>
    public class ConvertResult
    {
        public bool Success { get; init; }
        public string OutputFile { get; init; } = "";
        public string ErrorMessage { get; init; } = "";
        public long OutputSize { get; init; }
    }

    /// <summary>
    /// 将提取的裸视频流转换为 MP4
    /// </summary>
    /// <param name="rawStreamPath">裸视频流文件路径</param>
    /// <param name="outputMp4Path">输出 MP4 路径</param>
    /// <param name="speedMultiplier">播放速度倍率 (1-100)</param>
    /// <param name="onProgress">进度回调 (百分比 0-100)</param>
    public async Task<ConvertResult> ConvertToMp4Async(
        string rawStreamPath,
        string outputMp4Path,
        double speedMultiplier = 1.0,
        Action<int>? onProgress = null)
    {
        if (!_ffmpeg.IsAvailable)
            return new ConvertResult { Success = false, ErrorMessage = "FFmpeg 未安装，无法转换。请先设置 FFmpeg。" };

        // 构建 FFmpeg 参数
        var args = new StringBuilder();

        // 输入格式: HEVC 裸流
        args.Append($"-f hevc -i \"{rawStreamPath}\" ");

        // 强制覆盖
        args.Append("-y ");

        // 变速滤镜
        if (Math.Abs(speedMultiplier - 1.0) > 0.001)
        {
            double ptsFactor = 1.0 / speedMultiplier;
            args.Append($"-filter:v \"setpts={ptsFactor:F6}*PTS\" ");
        }

        // 强制重新编码为 H.264 (Windows Media Player 不支持 HEVC)
        args.Append("-c:v libx264 -preset fast -crf 23 -pix_fmt yuv420p ");
        args.Append("-c:a copy ");

        // 输出
        args.Append($"\"{outputMp4Path}\"");

        return await RunFFmpegAsync(args.ToString(), onProgress);
    }

    /// <summary>
    /// 将单个 raw 文件直接转换为 MP4（解析 + 转换一体化）
    /// 使用复用的Parser实例，减少内存分配
    /// </summary>
    public async Task<ConvertResult> ConvertRawFileAsync(
        string rawFilePath,
        string outputMp4Path,
        double speedMultiplier = 1.0,
        Action<int>? onProgress = null)
    {
        if (!_ffmpeg.IsAvailable)
            return new ConvertResult { Success = false, ErrorMessage = "FFmpeg 未安装。" };

        // 使用复用的Parser实例（避免每次new对象）
        var parseResult = _parser.Parse(rawFilePath);

        if (parseResult.ExtractedRawStream.Length == 0)
            return new ConvertResult { Success = false, ErrorMessage = "未能从文件中提取视频流。" };

        // 写入临时裸流文件
        string tempRaw = Path.Combine(Path.GetTempPath(), $"video_{Guid.NewGuid():N}.raw");
        try
        {
            await File.WriteAllBytesAsync(tempRaw, parseResult.ExtractedRawStream);
            
            // 释放解析结果中的大数据
            parseResult.ExtractedRawStream = Array.Empty<byte>();
            
            return await ConvertToMp4Async(tempRaw, outputMp4Path, speedMultiplier, onProgress);
        }
        finally
        {
            try { File.Delete(tempRaw); } catch { }
        }
    }

    /// <summary>
    /// 批量转换一个日期小时段下的所有文件
    /// </summary>
    public async Task<List<ConvertResult>> ConvertHourFolderAsync(
        string hourFolderPath,
        string outputDir,
        double speedMultiplier = 1.0,
        Action<int, int>? onFileProgress = null,
        Action<int>? onOverallProgress = null)
    {
        var results = new List<ConvertResult>();
        var files = RawVideoParser.ScanVideoFiles(
            Path.GetDirectoryName(Path.GetDirectoryName(hourFolderPath))!,
            Path.GetFileName(Path.GetDirectoryName(hourFolderPath))!,
            Path.GetFileName(hourFolderPath)!);

        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        for (int i = 0; i < files.Count; i++)
        {
            onOverallProgress?.Invoke(i * 100 / files.Count);
            onFileProgress?.Invoke(i + 1, files.Count);

            string fileName = Path.GetFileNameWithoutExtension(Path.GetFileName(files[i]));
            string outputFile = Path.Combine(outputDir, $"{fileName}.mp4");

            var result = await ConvertRawFileAsync(files[i], outputFile, speedMultiplier, null);
            results.Add(result);
        }

        onOverallProgress?.Invoke(100);
        return results;
    }

    /// <summary>
    /// 生成指定倍速的预览 MP4（用于播放速度控制）
    /// </summary>
    public async Task<ConvertResult> GenerateSpeedPreviewAsync(
        string rawFilePath,
        double speed,
        Action<int>? onProgress = null)
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "VideoParsing");
        Directory.CreateDirectory(outputDir);
        string fileName = Path.GetFileName(rawFilePath);
        string output = Path.Combine(outputDir, $"{fileName}_x{speed:F0}.mp4");

        return await ConvertRawFileAsync(rawFilePath, output, speed, onProgress);
    }

    /// <summary>
    /// 合并多个MP4文件为一个（使用FFmpeg concat协议）
    /// </summary>
    public async Task<bool> MergeMp4FilesAsync(
        List<string> mp4Files,
        string outputPath,
        Action<int>? onProgress = null)
    {
        if (!_ffmpeg.IsAvailable) return false;
        if (mp4Files.Count == 0) return false;

        // 创建concat列表文件
        string listFile = Path.Combine(Path.GetTempPath(), $"VideoParsing_Concat_{Guid.NewGuid():N}.txt");
        try
        {
            using (var writer = new StreamWriter(listFile))
            {
                foreach (var file in mp4Files)
                {
                    writer.WriteLine($"file '{file.Replace("'", "'\\''")}'");
                }
            }

            // FFmpeg concat命令
            string args = $"-f concat -safe 0 -i \"{listFile}\" -c copy -movflags +faststart \"{outputPath}\"";

            var result = await RunFFmpegAsync(args, onProgress);
            return result.Success && File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
        }
        finally
        {
            try { File.Delete(listFile); } catch { }
        }
    }

    private async Task<ConvertResult> RunFFmpegAsync(string arguments, Action<int>? onProgress)
    {
        // 保存回调引用并重置进度
        _onProgressCallback = onProgress;
        _currentOutTimeMs = 0;
        _totalDurationMs = 0;

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpeg.FFmpegPath,
            Arguments = $"-progress pipe:1 -nostats {arguments}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        var process = new Process { StartInfo = psi };
        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                output.AppendLine(e.Data);
                // 解析进度: out_time_ms=xxx, duration=xxx
                if (e.Data.StartsWith("out_time_ms="))
                {
                    string val = e.Data["out_time_ms=".Length..];
                    if (long.TryParse(val, out long ms) && ms > 0)
                    {
                        _currentOutTimeMs = ms;
                        UpdateProgress();
                    }
                }
                if (e.Data.StartsWith("duration="))
                {
                    string val = e.Data["duration=".Length..];
                    if (double.TryParse(val, System.Globalization.NumberStyles.Float, 
                        System.Globalization.CultureInfo.InvariantCulture, out double dur) && dur > 0)
                    {
                        _totalDurationMs = (long)(dur * 1000);
                        UpdateProgress();
                    }
                }
                if (e.Data.StartsWith("progress=") && e.Data.Contains("end"))
                {
                    onProgress?.Invoke(100);
                }
            }
        };

        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null) error.AppendLine(e.Data);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                // 从参数中提取输出文件路径
                string? outputFile = ExtractOutputPath(arguments);
                long size = 0;
                if (outputFile != null && File.Exists(outputFile))
                    size = new FileInfo(outputFile).Length;

                return new ConvertResult
                {
                    Success = true,
                    OutputFile = outputFile ?? "",
                    OutputSize = size
                };
            }
            else
            {
                return new ConvertResult
                {
                    Success = false,
                    ErrorMessage = error.ToString()
                };
            }
        }
        catch (Exception ex)
        {
            return new ConvertResult
            {
                Success = false,
                ErrorMessage = $"FFmpeg 执行失败: {ex.Message}"
            };
        }
    }

    private static string? ExtractOutputPath(string arguments)
    {
        // 从参数中提取最后一个引号包裹的路径
        int lastQuote = arguments.LastIndexOf('"');
        if (lastQuote < 0) return null;
        int prevQuote = arguments.LastIndexOf('"', lastQuote - 1);
        if (prevQuote < 0) return null;
        return arguments.Substring(prevQuote + 1, lastQuote - prevQuote - 1);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        
        if (disposing)
        {
            // 清理托管资源（如果有）
            _onProgressCallback = null;
        }
        
        _disposed = true;
    }
}
