using System.Diagnostics;

namespace Video_Parsing;

/// <summary>
/// FFmpeg 管理器 - 检测、下载、调用 FFmpeg
/// </summary>
public class FFmpegManager
{
    private string? _ffmpegPath;
    private string? _ffprobePath;
    private string? _ffplayPath;
    private readonly string _projectDir;

    public bool IsAvailable => !string.IsNullOrEmpty(_ffmpegPath) && File.Exists(_ffmpegPath);
    public bool IsFFplayAvailable => !string.IsNullOrEmpty(_ffplayPath) && File.Exists(_ffplayPath);

    public FFmpegManager()
    {
        _projectDir = AppDomain.CurrentDomain.BaseDirectory;
        DetectFFmpeg();
    }

    /// <summary>
    /// 自动检测 FFmpeg
    /// 优先级: 1.项目 bin 目录 2.PATH 环境变量 3.常见安装路径
    /// </summary>
    private void DetectFFmpeg()
    {
        // 1. 检查项目 bin 目录
        string localFfmpeg = Path.Combine(_projectDir, "ffmpeg.exe");
        string localFfprobe = Path.Combine(_projectDir, "ffprobe.exe");
        string localFfplay = Path.Combine(_projectDir, "ffplay.exe");
        if (File.Exists(localFfmpeg))
        {
            _ffmpegPath = localFfmpeg;
            _ffprobePath = File.Exists(localFfprobe) ? localFfprobe : localFfmpeg;
            _ffplayPath = File.Exists(localFfplay) ? localFfplay : null;
            return;
        }

        // 2. 检查 PATH
        string? pathFfmpeg = FindInPath("ffmpeg.exe");
        if (pathFfmpeg != null)
        {
            _ffmpegPath = pathFfmpeg;
            _ffprobePath = FindInPath("ffprobe.exe") ?? pathFfmpeg;
            _ffplayPath = FindInPath("ffplay.exe");
            return;
        }

        // 3. 常见安装路径
        string[] commonPaths = {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FFmpeg", "bin", "ffmpeg.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "FFmpeg", "bin", "ffmpeg.exe"),
        };

        foreach (var p in commonPaths)
        {
            if (File.Exists(p))
            {
                _ffmpegPath = p;
                _ffprobePath = p.Replace("ffmpeg.exe", "ffprobe.exe");
                _ffplayPath = p.Replace("ffmpeg.exe", "ffplay.exe");
                if (!File.Exists(_ffplayPath)) _ffplayPath = null;
                return;
            }
        }
    }

    private static string? FindInPath(string filename)
    {
        string? paths = Environment.GetEnvironmentVariable("PATH");
        if (paths == null) return null;
        foreach (string dir in paths.Split(Path.PathSeparator))
        {
            string fullPath = Path.Combine(dir.Trim(), filename);
            if (File.Exists(fullPath)) return fullPath;
        }
        return null;
    }

    /// <summary>
    /// 下载 FFmpeg 到项目 bin 目录
    /// </summary>
    public async Task<bool> DownloadFFmpegAsync(IProgress<int>? progress = null)
    {
        string zipPath = Path.Combine(Path.GetTempPath(), "ffmpeg_essentials.zip");
        string extractDir = Path.Combine(Path.GetTempPath(), "ffmpeg_extract");

        try
        {
            // 下载 essentials 版本（较小）
            string url = "https://github.com/GyanD/codexffmpeg/releases/download/8.1.1/ffmpeg-8.1.1-essentials_build.zip";

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(10);

            var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return false;

            long totalBytes = response.Content.Headers.ContentLength ?? -1;
            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = File.Create(zipPath);

            byte[] buffer = new byte[8192];
            long downloaded = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                if (totalBytes > 0 && progress != null)
                    progress.Report((int)(downloaded * 100 / totalBytes));
            }

            // 解压
            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, true);

            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir);

            // 找到 bin 目录
            string? binDir = FindBinDirectory(extractDir);
            if (binDir == null) return false;

            // 复制到项目 bin 目录
            foreach (string exe in Directory.GetFiles(binDir, "*.exe"))
            {
                string dest = Path.Combine(_projectDir, Path.GetFileName(exe));
                File.Copy(exe, dest, true);
            }

            // 重新检测
            DetectFFmpeg();
            return IsAvailable;
        }
        catch
        {
            return false;
        }
        finally
        {
            // 清理
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
            try { if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true); } catch { }
        }
    }

    private static string? FindBinDirectory(string extractDir)
    {
        // 解压后的结构可能是: extractDir/ffmpeg-xxx/bin/
        foreach (string dir in Directory.GetDirectories(extractDir))
        {
            string binPath = Path.Combine(dir, "bin");
            if (Directory.Exists(binPath) && File.Exists(Path.Combine(binPath, "ffmpeg.exe")))
                return binPath;
        }
        // 或者直接在 extractDir/bin/
        string directBin = Path.Combine(extractDir, "bin");
        if (Directory.Exists(directBin) && File.Exists(Path.Combine(directBin, "ffmpeg.exe")))
            return directBin;
        return null;
    }

    /// <summary>
    /// 获取 ffmpeg 路径（如果未安装则抛出）
    /// </summary>
    public string FFmpegPath => _ffmpegPath
        ?? throw new InvalidOperationException("FFmpeg 未安装。请先点击设置 FFmpeg。");

    public string FFprobePath => _ffprobePath ?? FFmpegPath;

    public string? FFplayPath => _ffplayPath;
}
