namespace Video_Parsing;

using System.Diagnostics;

public partial class Form1 : Form
{
    // === UI ===
    private Button _btnSelectFolder = null!;
    private Label _lblFolder = null!;
    private TreeView _treeView = null!;
    private Label _lblFileInfo = null!;
    private NumericUpDown _numSpeed = null!;
    private Button _btnExport = null!;
    private Button _btnPlay = null!;
    private Button _btnSetupFfmpeg = null!;
    private ProgressBar _progressBar = null!;
    private Label _lblStatus = null!;

    // === 核心 ===
    private RawVideoParser _parser = new();
    private FFmpegManager _ffmpeg = new();
    private VideoConverter? _converter;
    private string _rawdataPath = "";
    private string? _currentRawFile;

    // 临时文件管理
    private static readonly string[] TempFilePrefixes = { "VideoParsing_Play_", "video_", "VideoParsing_Merge_" };
    private const int TempFileMaxAgeHours = 24;

    // 任务状态追踪
    private bool _isTaskRunning = false;

    public Form1()
    {
        InitializeComponent();
        this.Text = "视频解析工具";
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Size = new Size(900, 700);
        this.MinimumSize = new Size(700, 500);

        InitializeCustomComponents();
        
        // 默认路径: 桌面或用户目录
        if (string.IsNullOrEmpty(_rawdataPath))
            _rawdataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "rawdata");
        
        // 启动时清理过期临时文件
        CleanupTempFiles();
        
        LoadData();
        
        // 关闭时检查是否有任务在执行
        this.FormClosing += (s, e) => 
        {
            if (_isTaskRunning)
            {
                var result = MessageBox.Show(
                    "⚠️ 当前有任务正在执行（转码/导出/整合）\n\n" +
                    "强制关闭可能导致数据损坏或文件不完整。\n\n" +
                    "确定要关闭吗？",
                    "确认关闭",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                
                if (result == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }
            }
            
            CleanupTempFiles();
        };
    }

    private void InitializeCustomComponents()
    {
        _converter = new VideoConverter(_ffmpeg);

        // === 顶部工具栏 (H=72) ===
        var topPanel = new Panel 
        { 
            Location = new Point(0, 0), 
            Size = new Size(this.Width, 30),
            BackColor = Color.FromArgb(240, 240, 245) 
        };
        
        _btnSelectFolder = new Button { Text = "📁 选择文件夹", Location = new Point(3, 3), Size = new Size(130, 27), Font = new Font("Microsoft YaHei", 9F) };
        _btnSelectFolder.Click += BtnSelectFolder_Click;
        
        _lblFolder = new Label 
        { 
            Location = new Point(155, 3), 
            Size = new Size(this.Width - 350, 27),
            Text = _rawdataPath, 
            ForeColor = Color.Gray, 
            Font = new Font("Microsoft YaHei", 9F),
            AutoEllipsis = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        
        _btnSetupFfmpeg = new Button
        {
            Text = _ffmpeg.IsAvailable ? "✓ FFmpeg已就绪" : "⚠ 安装FFmpeg",
            Location = new Point(this.Width - 165, 3), Size = new Size(150, 27),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = _ffmpeg.IsAvailable ? Color.FromArgb(100, 180, 100) : Color.FromArgb(220, 100, 80),
            ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei", 9F)
        };
        _btnSetupFfmpeg.FlatAppearance.BorderSize = 0;
        _btnSetupFfmpeg.Click += BtnSetupFfmpeg_Click;
        
        topPanel.Controls.AddRange([_btnSelectFolder, _lblFolder, _btnSetupFfmpeg]);
        this.Controls.Add(topPanel);

        // === 主内容区 ===
        var mainPanel = new SplitContainer { Dock = DockStyle.Fill };
        
        // 左侧: 文件树
        _treeView = new TreeView { Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei", 10F), HideSelection = false };
        _treeView.AfterSelect += TreeView_AfterSelect;
        mainPanel.Panel1.Controls.Add(_treeView);

        // 右侧: 文件信息 + 操作
        var rightPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15) };
        
        // 文件信息区域
        var infoGroup = new GroupBox { Text = "文件信息", Location = new Point(15, 15), Size = new Size(280, 200), Font = new Font("Microsoft YaHei", 9F) };
        _lblFileInfo = new Label { Location = new Point(15, 25), Size = new Size(250, 160), Font = new Font("Consolas", 10F), ForeColor = Color.DimGray };
        _lblFileInfo.Text = "选择一个视频文件查看信息";
        infoGroup.Controls.Add(_lblFileInfo);
        
        // 操作按钮区域
        var actionGroup = new GroupBox { Text = "操作", Location = new Point(15, 230), Size = new Size(280, 120), Font = new Font("Microsoft YaHei", 9F) };

        var lSpd = new Label { Text = "导出倍速:", Location = new Point(15, 30), AutoSize = true, Font = new Font("Microsoft YaHei", 9F) };
        _numSpeed = new NumericUpDown { Location = new Point(90, 27), Width = 60, Minimum = 1, Maximum = 100, Value = 1, Font = new Font("Microsoft YaHei", 9F) };

        _btnPlay = new Button
        {
            Text = "▶ 播放", Location = new Point(15, 65), Size = new Size(120, 35),
            BackColor = Color.FromArgb(70, 140, 200), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Enabled = false, Font = new Font("Microsoft YaHei", 10F)
        };
        _btnPlay.FlatAppearance.BorderSize = 0;
        _btnPlay.Click += BtnPlay_Click;

        _btnExport = new Button
        {
            Text = "📦 导出MP4", Location = new Point(145, 65), Size = new Size(120, 35),
            BackColor = Color.FromArgb(220, 180, 60), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Enabled = false, Font = new Font("Microsoft YaHei", 10F)
        };
        _btnExport.FlatAppearance.BorderSize = 0;
        _btnExport.Click += BtnExport_Click;

        actionGroup.Controls.AddRange([lSpd, _numSpeed, _btnPlay, _btnExport]);

        rightPanel.Controls.AddRange([infoGroup, actionGroup]);
        mainPanel.Panel2.Controls.Add(rightPanel);

        this.Controls.Add(mainPanel);

        // 延迟设置分割位置和最小尺寸（必须在控件添加到父容器后）
        this.Load += (s, e) =>
        {
            mainPanel.Panel1MinSize = 350;
            mainPanel.Panel2MinSize = 200;
            if (mainPanel.Width > 550)
                mainPanel.SplitterDistance = 550;
        };

        // === 底部状态栏 (H=35) ===
        var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 35, BackColor = Color.FromArgb(245, 245, 245) };
        _progressBar = new ProgressBar { Location = new Point(10, 9), Width = 300, Height = 18, Visible = false };
        _lblStatus = new Label { Location = new Point(320, 9), AutoSize = true, ForeColor = Color.DimGray, Text = "就绪", Font = new Font("Microsoft YaHei", 9F) };
        bottomPanel.Controls.AddRange([_progressBar, _lblStatus]);
        this.Controls.Add(bottomPanel);
    }

    // === 数据加载 ===
    private void LoadData()
    {
        _treeView.Nodes.Clear();
        if (!Directory.Exists(_rawdataPath)) { _treeView.Nodes.Add("未找到视频数据目录"); return; }

        var dates = RawVideoParser.ScanDateFolders(_rawdataPath);
        foreach (var date in dates)
        {
            var dateNode = new TreeNode(date);
            var hours = RawVideoParser.ScanHourFolders(_rawdataPath, date);
            foreach (var hour in hours)
            {
                var files = RawVideoParser.ScanVideoFiles(_rawdataPath, date, hour);
                var hourNode = new TreeNode($"{hour.PadLeft(2, '0')}:00  ({files.Count}段)");
                foreach (var file in files)
                {
                    string name = Path.GetFileName(file);
                    string timeStr = name;
                    if (long.TryParse(name, out long ms))
                    {
                        var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
                        timeStr = dt.ToString("HH:mm:ss");
                    }
                    var fileNode = new TreeNode($"◉ {timeStr}");
                    fileNode.Tag = file;
                    hourNode.Nodes.Add(fileNode);
                }
                dateNode.Nodes.Add(hourNode);
            }
            _treeView.Nodes.Add(dateNode);
        }
        _lblStatus.Text = $"就绪 - {dates.Count} 天数据";
    }

    private void TreeView_AfterSelect(object? s, TreeViewEventArgs e)
    {
        if (e.Node?.Tag is string fp && File.Exists(fp))
        {
            _currentRawFile = fp;
            _btnPlay.Enabled = _btnExport.Enabled = true;

            // 显示文件信息
            FileInfo fi = new FileInfo(fp);
            string name = Path.GetFileName(fp);
            string timeStr = name;
            if (long.TryParse(name, out long ms))
            {
                var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
                timeStr = dt.ToString("yyyy-MM-dd HH:mm:ss");
            }

            _lblFileInfo.Text = $"文件名: {name}\n\n" +
                               $"时间戳: {timeStr}\n\n" +
                               $"大小: {fi.Length / 1024.0:F1} KB\n\n" +
                               $"路径: {fp}";

            _lblStatus.Text = $"选中: {timeStr}";
        }
    }

    // === 播放（使用系统默认播放器）===
    private async void BtnPlay_Click(object? s, EventArgs e)
    {
        if (_currentRawFile == null) return;
        
        _isTaskRunning = true;
        
        // 保存当前状态，避免影响导出进度条
        bool wasProgressBarVisible = _progressBar.Visible;
        string previousStatusText = _lblStatus.Text;
        
        if (!wasProgressBarVisible)
        {
            _progressBar.Visible = true; 
            _progressBar.Value = 0;
        }
        _btnPlay.Enabled = false;

        try
        {
            double spd = (double)_numSpeed.Value;
            
            // 转码到临时目录
            string mp4File = await ConvertForPlaybackAsync(_currentRawFile, spd);
            if (mp4File == null) return;

            // 使用系统默认播放器打开
            Process.Start(new ProcessStartInfo(mp4File) { UseShellExecute = true });
            
            // 只在非导出状态下更新状态文本
            if (!wasProgressBarVisible || !previousStatusText.Contains("导出"))
                _lblStatus.Text = $"已在系统播放器中打开 (x{spd:F0})";
        }
        catch (Exception ex) { MessageBox.Show($"播放失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally 
        { 
            _isTaskRunning = false;
            
            // 只恢复到之前的状态，不强制隐藏
            if (!wasProgressBarVisible)
                _progressBar.Visible = false;
            _btnPlay.Enabled = true;
        }
    }

    private async Task<string?> ConvertForPlaybackAsync(string rawFile, double speed)
    {
        if (!_ffmpeg.IsAvailable) { MessageBox.Show("需要 FFmpeg!", "错误"); return null; }
        
        string dir = Path.Combine(Path.GetTempPath(), "VideoParsing_Play");
        Directory.CreateDirectory(dir);
        string mp4 = Path.Combine(dir, $"play_{Path.GetFileName(rawFile)}_x{speed:F0}.mp4");
        
        if (File.Exists(mp4)) return mp4;
        
        var result = await _converter!.ConvertRawFileAsync(rawFile, mp4, speed, 
            p => this.Invoke(() => _progressBar.Value = p));
        
        if (!result.Success) MessageBox.Show(result.ErrorMessage, "转码失败");
        return result.Success ? mp4 : null;
    }

    // === 导出 ===
    private async void BtnExport_Click(object? s, EventArgs e)
    {
        if (!_ffmpeg.IsAvailable) { MessageBox.Show("需要 FFmpeg!"); return; }

        var dates = RawVideoParser.ScanDateFolders(_rawdataPath);
        if (dates.Count == 0) { MessageBox.Show("没有找到视频数据。"); return; }

        using var dlg = new ExportDialog(_rawdataPath, dates);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        string outputDir = dlg.OutputDirectory;
        double speed = (double)_numSpeed.Value;
        var selectedFiles = dlg.SelectedFiles;

        if (selectedFiles.Count == 0) { MessageBox.Show("没有选择任何文件。"); return; }

        // === 整合模式 ===
        if (dlg.MergeIntoOneVideo)
        {
            await MergeVideosAsync(selectedFiles, outputDir, speed);
            return;
        }

        _progressBar.Visible = true; _progressBar.Value = 0;
        _btnExport.Enabled = false;
        _lblStatus.Text = $"正在导出 {selectedFiles.Count} 个文件...";
        _isTaskRunning = true;

        Directory.CreateDirectory(outputDir);

        int done = 0;
        int failed = 0;
        
        foreach (var file in selectedFiles)
        {
            string fname = Path.GetFileName(file);
            
            // 解析时间戳，确定所属日期
            string dateFolder = "unknown";
            string timeStr = fname;
            if (long.TryParse(fname, out long ms))
            {
                var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
                dateFolder = dt.ToString("yyyy-MM-dd");
                timeStr = dt.ToString("HH-mm-ss");
            }
            else
            {
                // 从路径中提取日期
                string? dirName = Path.GetDirectoryName(file);
                if (dirName != null)
                {
                    var parts = dirName.Split(Path.DirectorySeparatorChar);
                    foreach (var part in parts.Reverse())
                    {
                        if (part.Length == 10 && part[4] == '-' && part[7] == '-')
                        {
                            dateFolder = part;
                            break;
                        }
                    }
                }
            }

            // 创建日期子文件夹
            string dateOutputDir = Path.Combine(outputDir, dateFolder);
            Directory.CreateDirectory(dateOutputDir);
            
            string outPath = Path.Combine(dateOutputDir, $"{timeStr}.mp4");

            var result = await _converter!.ConvertRawFileAsync(file, outPath, speed, null);
            done++;
            
            this.Invoke(() =>
            {
                _progressBar.Value = done * 100 / selectedFiles.Count;
                _lblStatus.Text = $"导出中 {done}/{selectedFiles.Count}";
            });

            if (!result.Success)
            {
                failed++;
                _lblStatus.Text = $"{Path.GetFileName(file)} 失败";
            }
        }

        _progressBar.Visible = false;
        _btnExport.Enabled = true;
        _isTaskRunning = false;
        
        if (failed > 0)
            _lblStatus.Text = $"完成: {done - failed}/{selectedFiles.Count} ({failed}个失败)";
        else
            _lblStatus.Text = $"完成: {selectedFiles.Count} 个文件已导出到 {outputDir}";
        
        MessageBox.Show($"导出完成！\n\n成功: {done - failed}\n失败: {failed}\n\n输出目录:\n{outputDir}", 
            "导出结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task MergeVideosAsync(List<string> files, string outputDir, double speed)
    {
        _progressBar.Visible = true; _progressBar.Value = 0;
        _btnExport.Enabled = false;
        _isTaskRunning = true;

        // 创建整合文件夹
        string mergeDir = Path.Combine(outputDir, "整合");
        Directory.CreateDirectory(mergeDir);

        // 按时间戳排序（文件名就是毫秒时间戳）
        var sortedFiles = files.OrderBy(f =>
        {
            string name = Path.GetFileName(f);
            if (long.TryParse(name, out long ms)) return ms;
            return 0;
        }).ToList();

        _lblStatus.Text = $"正在整合 {sortedFiles.Count} 个视频碎片...";

        try
        {
            // 第一步：转码所有片段为临时MP4
            List<string> tempMp4s = new();
            for (int i = 0; i < sortedFiles.Count; i++)
            {
                string file = sortedFiles[i];
                this.Invoke(() => 
                {
                    int progress = (i * 100 / sortedFiles.Count) / 2;  // 前半段：转码
                    _progressBar.Value = progress;
                    _lblStatus.Text = $"转码中 ({i + 1}/{sortedFiles.Count})...";
                });

                string tempMp4 = Path.Combine(Path.GetTempPath(), $"VideoParsing_Merge_{Guid.NewGuid():N}.mp4");
                var result = await _converter!.ConvertRawFileAsync(file, tempMp4, speed, null);
                
                if (result.Success)
                    tempMp4s.Add(tempMp4);
                else
                {
                    MessageBox.Show($"转码失败: {Path.GetFileName(file)}\n{result.ErrorMessage}", "错误");
                    return;
                }
            }

            this.Invoke(() =>
            {
                _progressBar.Value = 50;
                _lblStatus.Text = "正在合并视频...";
            });

            // 第二步：使用FFmpeg concat合并
            string outputFile = Path.Combine(mergeDir, $"整合_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
            
            bool mergeSuccess = await _converter.MergeMp4FilesAsync(tempMp4s, outputFile, p => 
                this.Invoke(() => 
                {
                    _progressBar.Value = 50 + p / 2;  // 后半段：合并
                    _lblStatus.Text = $"合并中... {p}%";
                }));

            // 清理临时文件
            foreach (var tmp in tempMp4s)
            {
                try { File.Delete(tmp); } catch { }
            }

            _progressBar.Visible = false;
            _btnExport.Enabled = true;
            _isTaskRunning = false;

            if (mergeSuccess)
            {
                _lblStatus.Text = $"整合完成: {sortedFiles.Count} 个碎片 → 1 个视频";
                MessageBox.Show($"整合完成！\n\n{sortedFiles.Count} 个视频碎片已整合为:\n{outputFile}", 
                    "整合完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                
                Process.Start(new ProcessStartInfo(outputFile) { UseShellExecute = true });
            }
            else
            {
                _lblStatus.Text = "整合失败";
                MessageBox.Show("视频整合失败，请检查日志。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            _progressBar.Visible = false;
            _btnExport.Enabled = true;
            _isTaskRunning = false;
            _lblStatus.Text = "整合出错";
            MessageBox.Show($"整合过程出错: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 清理过期的临时文件（超过24小时的VideoParsing临时文件）
    /// </summary>
    private static void CleanupTempFiles()
    {
        try
        {
            string tempDir = Path.GetTempPath();
            var cutoffTime = DateTime.Now.AddHours(-TempFileMaxAgeHours);
            int cleanedCount = 0;

            foreach (string file in Directory.GetFiles(tempDir))
            {
                string fileName = Path.GetFileName(file);
                
                // 检查是否是我们的临时文件
                bool isOurFile = false;
                foreach (var prefix in TempFilePrefixes)
                {
                    if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        isOurFile = true;
                        break;
                    }
                }

                if (isOurFile)
                {
                    try
                    {
                        FileInfo fi = new FileInfo(file);
                        if (fi.LastWriteTime < cutoffTime)
                        {
                            fi.Delete();
                            cleanedCount++;
                        }
                    }
                    catch { }
                }
            }

            // 同时清理空目录
            foreach (var dir in new[] { "VideoParsing_Play", "VideoParsing" })
            {
                try
                {
                    string dirPath = Path.Combine(tempDir, dir);
                    if (Directory.Exists(dirPath) && !Directory.EnumerateFileSystemEntries(dirPath).Any())
                        Directory.Delete(dirPath);
                }
                catch { }
            }

            if (cleanedCount > 0)
                Debug.WriteLine($"[VideoParsing] 清理了 {cleanedCount} 个过期临时文件");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VideoParsing] 清理临时文件失败: {ex.Message}");
        }
    }

    // === 文件夹选择 ===
    private void BtnSelectFolder_Click(object? s, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog { Description = "选择 rawdata 目录", SelectedPath = _rawdataPath };
        if (dlg.SelectedPath == "") dlg.SelectedPath = _rawdataPath;
        if (dlg.ShowDialog() == DialogResult.OK) 
        { 
            _rawdataPath = dlg.SelectedPath; 
            _lblFolder.Text = _rawdataPath; 
            LoadData(); 
        }
    }

    // === FFmpeg 管理 ===
    private async void BtnSetupFfmpeg_Click(object? s, EventArgs e)
    {
        if (_ffmpeg.IsAvailable) { MessageBox.Show("FFmpeg 已就绪!"); return; }
        if (MessageBox.Show("是否自动下载 FFmpeg? (约 35MB)", "安装", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        
        _btnSetupFfmpeg.Enabled = false; 
        _progressBar.Visible = true;
        
        bool ok = await _ffmpeg.DownloadFFmpegAsync(new Progress<int>(p => this.Invoke(() => _progressBar.Value = p)));
        
        _progressBar.Visible = false; 
        _btnSetupFfmpeg.Enabled = true;
        _btnSetupFfmpeg.Text = ok ? "✓ FFmpeg已就绪" : "⚠ 安装FFmpeg";
        _btnSetupFfmpeg.BackColor = ok ? Color.FromArgb(100, 180, 100) : Color.FromArgb(220, 100, 80);
    }
}
