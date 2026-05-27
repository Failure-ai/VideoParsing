using System.Diagnostics;
using System.Text;

namespace Video_Parsing;

public class MotionDetectForm : Form
{
    private readonly FFmpegManager _ffmpeg;
    private readonly MotionDetector _detector;

    private Button _btnOpenFile = null!;
    private Label _lblFileInfo = null!;
    private Panel _timelinePanel = null!;
    private Label _lblTimelineInfo = null!;
    private TrackBar _trkThreshold = null!;
    private Label _lblThreshold = null!;
    private TrackBar _trkFrameSkip = null!;
    private Label _lblFrameSkip = null!;
    private Button _btnDetect = null!;
    private Button _btnExport = null!;
    private ProgressBar _progressBar = null!;
    private Label _lblStatus = null!;
    private ListBox _lstSegments = null!;

    private string? _selectedFile;
    private MotionDetector.DetectionResult? _currentResult;
    private double _timelinePosition = 0;
    private double _pixelsPerSecond = 10;
    private CancellationTokenSource? _detectCts;
    private CancellationTokenSource? _exportCts;

    public MotionDetectForm(FFmpegManager ffmpeg)
    {
        _ffmpeg = ffmpeg;
        _detector = new MotionDetector();
        
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        this.Text = "运动侦察 - DVR-Scan 模式";
        this.Size = new Size(1100, 800);
        this.StartPosition = FormStartPosition.CenterParent;
        this.MinimumSize = new Size(900, 650);
        this.Font = new Font("Microsoft YaHei", 9F);
        this.Padding = new Padding(15);

        int pw = this.ClientSize.Width - 30;

        _btnOpenFile = new Button
        {
            Text = "📂 选择视频文件", Location = new Point(15, 15), Size = new Size(200, 45),
            BackColor = Color.FromArgb(60, 120, 180), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei", 11F, FontStyle.Bold)
        };
        _btnOpenFile.FlatAppearance.BorderSize = 0;
        _btnOpenFile.Click += BtnOpenFile_Click;
        this.Controls.Add(_btnOpenFile);

        _lblFileInfo = new Label
        {
            Location = new Point(230, 15), Size = new Size(pw - 220, 55),
            Text = "请选择一个视频文件",
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(248, 249, 250),
            Padding = new Padding(8),
            Font = new Font("Microsoft YaHei", 9F)
        };
        this.Controls.Add(_lblFileInfo);

        var settingsGroup = new GroupBox
        {
            Text = "⚙️ 检测设置 (DVR-Scan MOG2 算法)",
            Location = new Point(15, 85),
            Size = new Size(pw, 130),
            Font = new Font("Microsoft YaHei", 9.5F)
        };

        var lblThresh = new Label
        {
            Text = "阈值:", Location = new Point(15, 28), AutoSize = true, 
            Font = new Font("Microsoft YaHei", 9.5F)
        };
        _trkThreshold = new TrackBar
        {
            Location = new Point(65, 24), Width = 250,
            Minimum = 1, Maximum = 50, Value = 15,
            TickFrequency = 1, SmallChange = 1
        };
        _trkThreshold.ValueChanged += (s, e) => 
            _lblThreshold.Text = $" {_trkThreshold.Value / 100.0:F2}";
        _lblThreshold = new Label
        {
            Text = " 0.15", Location = new Point(320, 28), Width = 55, 
            Font = new Font("Consolas", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(70, 140, 200)
        };

        var lblSkip = new Label
        {
            Text = "跳帧:", Location = new Point(15, 62), AutoSize = true,
            Font = new Font("Microsoft YaHei", 9.5F)
        };
        _trkFrameSkip = new TrackBar
        {
            Location = new Point(65, 58), Width = 250,
            Minimum = 0, Maximum = 29, Value = 0,
            TickFrequency = 1, SmallChange = 1
        };
        _trkFrameSkip.ValueChanged += (s, e) => 
        {
            int val = _trkFrameSkip.Value;
            string desc = val switch
            {
                0 => "(每帧都分析 - 最精确)",
                <= 2 => $"(每{val + 1}帧分析1帧 - 较快)",
                <= 10 => $"(每{val + 1}帧分析1帧 - 快)",
                _ => $"(每{val + 1}帧分析1帧 - 极快)"
            };
            _lblFrameSkip.Text = $" {val}{desc}";
        };
        _lblFrameSkip = new Label
        {
            Text = " 0 (每帧都分析 - 最精确)", 
            Location = new Point(320, 62), 
            Width = 280,
            Font = new Font("Microsoft YaHei", 8.5F),
            ForeColor = Color.FromArgb(70, 140, 200)
        };

        var lblNote = new Label
        {
            Text = "💡 阈值: 越小越敏感(推荐0.10-0.20) | 跳帧: 越大速度越快但精度降低",
            Location = new Point(15, 92),
            ForeColor = Color.FromArgb(120, 120, 120),
            Font = new Font("Microsoft YaHei", 8.5F)
        };
        settingsGroup.Controls.AddRange(new Control[] { 
            lblThresh, _trkThreshold, _lblThreshold, 
            lblSkip, _trkFrameSkip, _lblFrameSkip,
            lblNote 
        });
        this.Controls.Add(settingsGroup);

        _btnDetect = new Button
        {
            Text = "🔍 开始检测运动", Location = new Point(15, 230), Size = new Size(220, 48),
            BackColor = Color.FromArgb(70, 140, 200), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei", 12F, FontStyle.Bold),
            Enabled = false
        };
        _btnDetect.FlatAppearance.BorderSize = 0;
        _btnDetect.Click += BtnDetect_Click;
        this.Controls.Add(_btnDetect);

        _btnExport = new Button
        {
            Text = "💾 导出运动片段", Location = new Point(250, 230), Size = new Size(220, 48),
            BackColor = Color.FromArgb(70, 160, 70), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei", 12F, FontStyle.Bold),
            Enabled = false
        };
        _btnExport.FlatAppearance.BorderSize = 0;
        _btnExport.Click += BtnExport_Click;
        this.Controls.Add(_btnExport);

        var timelineGroup = new GroupBox
        {
            Text = "📊 运动时间轴 (🔴 红色=运动 | 🔵 蓝色=静止)",
            Location = new Point(15, 290),
            Size = new Size(pw, 210),
            Font = new Font("Microsoft YaHei", 9.5F)
        };

        _timelinePanel = new Panel
        {
            Location = new Point(15, 28),
            Size = new Size(timelineGroup.Width - 30, 120),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        _timelinePanel.Paint += TimelinePanel_Paint;
        _timelinePanel.MouseClick += TimelinePanel_MouseClick;
        _timelinePanel.MouseMove += TimelinePanel_MouseMove;
        timelineGroup.Controls.Add(_timelinePanel);

        _lblTimelineInfo = new Label
        {
            Location = new Point(15, 155),
            Size = new Size(timelineGroup.Width - 30, 25),
            Text = "",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei", 9.5F),
            ForeColor = Color.FromArgb(80, 80, 80)
        };
        timelineGroup.Controls.Add(_lblTimelineInfo);
        this.Controls.Add(timelineGroup);

        var segmentsGroup = new GroupBox
        {
            Text = "📋 运动片段列表 (点击查看详情)",
            Location = new Point(15, 515),
            Size = new Size(pw, 185),
            Font = new Font("Microsoft YaHei", 9.5F)
        };

        _lstSegments = new ListBox
        {
            Location = new Point(15, 28),
            Size = new Size(segmentsGroup.Width - 30, segmentsGroup.Height - 40),
            Font = new Font("Consolas", 10F),
            SelectionMode = SelectionMode.One,
            BorderStyle = BorderStyle.Fixed3D
        };
        segmentsGroup.Controls.Add(_lstSegments);
        this.Controls.Add(segmentsGroup);

        _progressBar = new ProgressBar
        {
            Location = new Point(15, 665),
            Size = new Size(pw, 28),
            Style = ProgressBarStyle.Continuous
        };
        this.Controls.Add(_progressBar);

        _lblStatus = new Label
        {
            Location = new Point(15, 700),
            Size = new Size(pw, 28),
            Text = "就绪 - 请选择视频文件开始分析",
            ForeColor = Color.FromArgb(100, 100, 100),
            Font = new Font("Microsoft YaHei", 9.5F)
        };
        this.Controls.Add(_lblStatus);

        this.Resize += (s, e) =>
        {
            if (this.WindowState != FormWindowState.Minimized)
            {
                try
                {
                    int w = this.ClientSize.Width - 30;
                    
                    ControlExtensions.SafeSetSize(_lblFileInfo, w - 220, 45);
                    ControlExtensions.SafeSetSize(settingsGroup, w, 95);
                    ControlExtensions.SafeSetSize(timelineGroup, w, 210);
                    ControlExtensions.SafeSetSize(_timelinePanel, w - 30, 120);
                    ControlExtensions.SafeSetSize(_lblTimelineInfo, w - 30, 25);
                    ControlExtensions.SafeSetSize(segmentsGroup, w, 180);
                    ControlExtensions.SafeSetSize(_lstSegments, w - 30, 140);
                    ControlExtensions.SafeSetSize(_progressBar, w, 28);
                    ControlExtensions.SafeSetSize(_lblStatus, w, 28);
                }
                catch { }
            }
        };
    }

    private void BtnOpenFile_Click(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title = "选择视频文件进行运动侦察",
            Filter = "视频文件|*.mp4;*.avi;*.mkv;*.mov;*.wmv;*.flv;*.webm|所有文件|*.*",
            Multiselect = false,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };

        if (ofd.ShowDialog() == DialogResult.OK && !string.IsNullOrEmpty(ofd.FileName))
        {
            SelectVideoFile(ofd.FileName);
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }

    private void SelectVideoFile(string filePath)
    {
        _selectedFile = filePath;
        _currentResult = null;
        _timelinePosition = 0;

        FileInfo fi = new FileInfo(filePath);
        string name = Path.GetFileName(filePath);
        string sizeStr = FormatFileSize(fi.Length);

        _lblFileInfo.Text = $"📁 文件: {name}\n" +
                           $"💾 大小: {sizeStr} | 📂 路径: {filePath}\n" +
                           $"🕐 修改时间: {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}";

        _btnDetect.Enabled = true;
        _btnExport.Enabled = false;
        _lstSegments.Items.Clear();
        
        _timelinePanel.Invalidate();
        _lblTimelineInfo.Text = "";
        _lblStatus.Text = $"已选中: {name} - 点击「开始检测运动」";
        _progressBar.Value = 0;
    }

    private async void BtnDetect_Click(object? sender, EventArgs e)
    {
        if (_selectedFile == null) return;

        StopDetection();
        _detectCts = new CancellationTokenSource();

        _btnDetect.Enabled = false;
        _btnExport.Enabled = false;
        _isDetecting = true;
        _lstSegments.Items.Clear();
        _currentResult = null;
        _lblStatus.Text = "正在初始化...";
        _progressBar.Value = 0;

        try
        {
            double threshold = _trkThreshold.Value / 100.0;
            int frameSkip = _trkFrameSkip.Value;

            _lblStatus.Text = $"正在使用 MOG2 算法检测运动 (阈值: {threshold:F2}, 跳帧: {frameSkip})...";

            _currentResult = await _detector.DetectAsync(
                _selectedFile,
                threshold: threshold,
                frameSkip: frameSkip,
                onStatusUpdate: msg => 
                {
                    if (_lblStatus != null && !IsDisposed)
                        this.BeginInvoke((Action)(() => { if (_lblStatus != null) _lblStatus.Text = msg; }));
                },
                onProgress: progress =>
                {
                    if (!IsDisposed)
                        this.BeginInvoke((Action)(() => 
                        { 
                            if (_progressBar != null) _progressBar.Value = Math.Min(progress, 100); 
                        }));
                },
                cancellationToken: _detectCts.Token
            );

            UpdateUIAfterDetection();
        }
        catch (OperationCanceledException)
        {
            _lblStatus.Text = "⚠️ 检测已取消";
        }
        catch (Exception ex)
        {
            _lblStatus.Text = $"❌ 检测失败: {ex.Message}";
            MessageBox.Show($"检测失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _isDetecting = false;
            _btnDetect.Enabled = _selectedFile != null;
            _detectCts?.Dispose();
            _detectCts = null;
        }
    }

    private bool _isDetecting;

    private void StopDetection()
    {
        if (_detectCts != null && !_detectCts.IsCancellationRequested)
        {
            _detectCts.Cancel();
        }
    }

    private void UpdateUIAfterDetection()
    {
        if (_currentResult == null) return;

        _btnExport.Enabled = _currentResult.MotionSegments.Count > 0;

        foreach (var segment in _currentResult.MotionSegments)
        {
            string item = $"片段 {_lstSegments.Items.Count + 1}: " +
                         $"{segment.StartTime:F1}s → {segment.EndTime:F1}s " +
                         $"| 强度: {segment.AvgIntensity * 100:F1}%";
            _lstSegments.Items.Add(item);
        }

        if (_currentResult.TotalDuration > 0)
        {
            int panelWidth = _timelinePanel.Width - 8;
            _pixelsPerSecond = Math.Max(1, panelWidth / _currentResult.TotalDuration);
        }

        _timelinePanel.Invalidate();
        
        _lblStatus.Text = $"✅ 检测完成! 发现 {_currentResult.MotionSegments.Count} 个运动段 | " +
                         $"总时长: {_currentResult.TotalDuration:F1}秒 | " +
                         $"运动占比: {_currentResult.MotionRatio * 100:F1}%";
    }

    private async void BtnExport_Click(object? sender, EventArgs e)
    {
        if (_selectedFile == null || _currentResult == null) return;
        if (!_currentResult.MotionSegments.Any())
        {
            MessageBox.Show("没有检测到运动片段，无法导出。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var fbd = new FolderBrowserDialog
        {
            Description = "选择保存运动片段的文件夹",
            ShowNewFolderButton = true
        };

        if (fbd.ShowDialog() != DialogResult.OK || string.IsNullOrEmpty(fbd.SelectedPath))
            return;

        StopExport();
        _exportCts = new CancellationTokenSource();

        _btnExport.Enabled = false;
        _btnDetect.Enabled = false;
        _lblStatus.Text = "正在导出运动片段...";
        _progressBar.Value = 0;

        try
        {
            var outputFiles = await _detector.ExtractMotionClipsAsync(
                _selectedFile,
                _currentResult,
                fbd.SelectedPath,
                onStatusUpdate: msg =>
                {
                    if (_lblStatus != null && !IsDisposed)
                        this.BeginInvoke((Action)(() => { if (_lblStatus != null) _lblStatus.Text = msg; }));
                },
                onProgress: progress =>
                {
                    if (!IsDisposed)
                        this.BeginInvoke((Action)(() =>
                        {
                            if (_progressBar != null) _progressBar.Value = Math.Min(progress, 100);
                        }));
                },
                cancellationToken: _exportCts.Token
            );

            _lblStatus.Text = $"✅ 导出完成! 共 {outputFiles.Count} 个片段已保存到: {fbd.SelectedPath}";

            if (MessageBox.Show(
                $"成功导出 {outputFiles.Count} 个运动片段!\n\n是否打开输出文件夹?",
                "导出完成",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes)
            {
                Process.Start("explorer.exe", fbd.SelectedPath);
            }
        }
        catch (OperationCanceledException)
        {
            _lblStatus.Text = "⚠️ 导出已取消";
        }
        catch (Exception ex)
        {
            _lblStatus.Text = $"❌ 导出失败: {ex.Message}";
            MessageBox.Show($"导出失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnExport.Enabled = _currentResult?.MotionSegments.Count > 0;
            _btnDetect.Enabled = _selectedFile != null;
            _exportCts?.Dispose();
            _exportCts = null;
        }
    }

    private void StopExport()
    {
        if (_exportCts != null && !_exportCts.IsCancellationRequested)
        {
            _exportCts.Cancel();
        }
    }

    private void TimelinePanel_Paint(object? sender, PaintEventArgs e)
    {
        if (_currentResult == null || _currentResult.TotalDuration <= 0)
        {
            using var brush = new SolidBrush(Color.FromArgb(240, 240, 240));
            e.Graphics.FillRectangle(brush, e.ClipRectangle);
            
            using var font = new Font("Microsoft YaHei", 11F);
            using var textBrush = new SolidBrush(Color.FromArgb(150, 150, 150));
            string text = "选择视频并点击「检测」以显示时间轴";
            var size = e.Graphics.MeasureString(text, font);
            e.Graphics.DrawString(text, font, textBrush,
                (e.ClipRectangle.Width - size.Width) / 2,
                (e.ClipRectangle.Height - size.Height) / 2);
            return;
        }

        double duration = _currentResult.TotalDuration;
        int width = e.ClipRectangle.Width;
        int height = e.ClipRectangle.Height;
        double pps = _pixelsPerSecond;

        for (int x = 0; x < width; x++)
        {
            double time = x / pps;
            bool isMotion = _currentResult.MotionSegments.Any(s =>
                time >= s.StartTime && time <= s.EndTime);

            using var brush = new SolidBrush(isMotion ? Color.FromArgb(230, 85, 85) : Color.FromArgb(85, 135, 215));
            e.Graphics.FillRectangle(brush, x, 0, 1, height);
        }

        DrawTimeMarkers(e.Graphics, width, height, duration, pps);

        if (_timelinePosition > 0 && _timelinePosition <= duration)
        {
            int xPos = (int)(_timelinePosition * pps);
            using var pen = new Pen(Color.FromArgb(255, 200, 0), 3);
            e.Graphics.DrawLine(pen, xPos, 0, xPos, height);
            
            using var brush = new SolidBrush(Color.FromArgb(255, 200, 0));
            e.Graphics.FillPolygon(brush, new Point[]
            {
                new Point(xPos - 7, 0),
                new Point(xPos + 7, 0),
                new Point(xPos, 12)
            });
        }

        using var borderPen = new Pen(Color.FromArgb(180, 180, 180));
        e.Graphics.DrawRectangle(borderPen, 0, 0, width - 1, height - 1);
    }

    private void DrawTimeMarkers(Graphics g, int width, int height, double duration, double pps)
    {
        using var font = new Font("Consolas", 9F);
        using var brush = new SolidBrush(Color.White);

        double interval = duration <= 60 ? 5 : duration <= 600 ? 30 : 300;
        
        for (double t = 0; t <= duration; t += interval)
        {
            int x = (int)(t * pps);
            if (x < 0 || x > width) continue;

            TimeSpan ts = TimeSpan.FromSeconds(t);
            string label = ts.TotalHours >= 1
                ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes}:{ts.Seconds:D2}";
            
            var size = g.MeasureString(label, font);
            g.DrawString(label, font, brush, x - (size.Width / 2), height - 18);

            using var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 1);
            g.DrawLine(pen, x, 0, x, height - 20);
        }
    }

    private void TimelinePanel_MouseClick(object? sender, MouseEventArgs e)
    {
        if (_currentResult == null || _currentResult.TotalDuration <= 0) return;

        double pps = _pixelsPerSecond;
        _timelinePosition = Math.Max(0, Math.Min(_currentResult.TotalDuration, (e.X - 2) / pps));

        _timelinePanel.Invalidate();
        UpdatePositionLabel();
    }

    private void TimelinePanel_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_currentResult == null || _currentResult.TotalDuration <= 0) return;

        double pps = _pixelsPerSecond;
        double hoverTime = Math.Max(0, Math.Min(_currentResult.TotalDuration, (e.X - 2) / pps));

        TimeSpan ts = TimeSpan.FromSeconds(hoverTime);
        string timeStr = ts.TotalHours >= 1
            ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";

        bool isMotion = _currentResult.MotionSegments.Any(s =>
            hoverTime >= s.StartTime && hoverTime <= s.EndTime);
        string status = isMotion ? "🔴 运动" : "🔵 静止";

        _lblTimelineInfo.Text = $"位置: {timeStr} | 状态: {status}";
    }

    private void UpdatePositionLabel()
    {
        if (_currentResult == null) return;

        TimeSpan ts = TimeSpan.FromSeconds(_timelinePosition);
        string timeStr = ts.TotalHours >= 1
            ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";

        bool isMotion = _currentResult.MotionSegments.Any(s =>
            _timelinePosition >= s.StartTime && _timelinePosition <= s.EndTime);
        string status = isMotion ? "🔴 运动" : "🔵 静止";

        _lblTimelineInfo.Text = $"▶ 位置: {timeStr} | 当前状态: {status}";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        StopDetection();
        StopExport();
        base.OnFormClosing(e);
    }

    private static class ControlExtensions
    {
        public static void SafeSetSize(Control ctrl, int width, int height)
        {
            if (ctrl == null) return;
            try { ctrl.Size = new Size(width, height); } catch { }
        }
    }
}
