namespace Video_Parsing;

/// <summary>
/// 导出对话框 - 选择日期、视频碎片和输出目录
/// </summary>
public class ExportDialog : Form
{
    private ComboBox _cmbStart = null!;
    private ComboBox _cmbEnd = null!;
    private CheckedListBox _chkFiles = null!;
    private Button _btnOutDir = null!;
    private Label _lblOutDir = null!;
    private Label _lblFileCount = null!;
    private Button _btnOk = null!;
    private Button _btnSelectAll = null!;
    private Button _btnDeselectAll = null!;
    private Button _btnMerge = null!;

    public string OutputDirectory { get; private set; } = "";
    public List<string> SelectedDates { get; private set; } = new();
    public List<string> SelectedFiles { get; private set; } = new();
    public bool MergeIntoOneVideo { get; private set; } = false;

    private readonly string _rawdataPath;
    private readonly List<string> _allDates;

    public ExportDialog(string rawdataPath, List<string> allDates)
    {
        _rawdataPath = rawdataPath;
        _allDates = allDates;

        this.Text = "导出视频";
        this.Size = new Size(650, 580);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.MaximizeBox = true;
        this.MinimizeBox = false;
        this.MinimumSize = new Size(600, 500);
        this.Font = new Font("Microsoft YaHei", 9F);

        // === 日期选择区 ===
        var grpDate = new GroupBox { Text = "日期范围", Location = new Point(10, 10), Size = new Size(610, 70) };

        var lblStart = new Label { Text = "开始:", Location = new Point(15, 30), AutoSize = true };
        _cmbStart = new ComboBox { Location = new Point(55, 27), Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var d in allDates) _cmbStart.Items.Add(d);
        if (allDates.Count > 0) _cmbStart.SelectedIndex = 0;
        _cmbStart.SelectedIndexChanged += DateRangeChanged;

        var lblEnd = new Label { Text = "结束:", Location = new Point(210, 30), AutoSize = true };
        _cmbEnd = new ComboBox { Location = new Point(250, 27), Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var d in allDates) _cmbEnd.Items.Add(d);
        if (allDates.Count > 0) _cmbEnd.SelectedIndex = allDates.Count - 1;
        _cmbEnd.SelectedIndexChanged += DateRangeChanged;

        _lblFileCount = new Label { Location = new Point(410, 32), AutoSize = true, Text = "", ForeColor = Color.DimGray };

        grpDate.Controls.AddRange([lblStart, _cmbStart, lblEnd, _cmbEnd, _lblFileCount]);

        // === 文件列表区 ===
        var grpFiles = new GroupBox { Text = "视频文件（勾选要导出的）", Location = new Point(10, 90), Size = new Size(610, 330) };

        _chkFiles = new CheckedListBox
        {
            Location = new Point(10, 25),
            Size = new Size(588, 270),
            CheckOnClick = true,
            Font = new Font("Microsoft YaHei", 9F),
            SelectionMode = SelectionMode.One,
            IntegralHeight = false,
        };
        _chkFiles.ItemCheck += (s, e) => UpdateCount();

        // 全选/取消按钮
        _btnSelectAll = new Button { Text = "全选", Location = new Point(10, 300), Size = new Size(75, 26) };
        _btnSelectAll.Click += (s, e) =>
        {
            for (int i = 0; i < _chkFiles.Items.Count; i++)
                _chkFiles.SetItemChecked(i, true);
            UpdateCount();
        };

        _btnDeselectAll = new Button { Text = "取消全选", Location = new Point(95, 300), Size = new Size(85, 26) };
        _btnDeselectAll.Click += (s, e) =>
        {
            for (int i = 0; i < _chkFiles.Items.Count; i++)
                _chkFiles.SetItemChecked(i, false);
            UpdateCount();
        };

        _btnMerge = new Button { Text = "📦 整合成视频", Location = new Point(190, 300), Size = new Size(110, 26), BackColor = Color.FromArgb(80, 120, 200), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        _btnMerge.FlatAppearance.BorderSize = 0;
        _btnMerge.Click += BtnMerge_Click;

        grpFiles.Controls.AddRange([_chkFiles, _btnSelectAll, _btnDeselectAll, _btnMerge]);

        // === 输出目录区 ===
        var grpOut = new GroupBox { Text = "输出设置", Location = new Point(10, 430), Size = new Size(610, 60) };

        var lblOut = new Label { Text = "输出到:", Location = new Point(15, 28), AutoSize = true };
        
        // 默认路径: 用户下载目录 + VideoParsing_日期
        string defaultDir = GetDefaultOutputDir();
        OutputDirectory = defaultDir;

        _lblOutDir = new Label
        {
            Text = defaultDir, Location = new Point(68, 27),
            Width = 380, AutoEllipsis = true, ForeColor = Color.Gray,
        };
        _btnOutDir = new Button { Text = "浏览...", Location = new Point(460, 24), Size = new Size(70, 26) };
        _btnOutDir.Click += (s, e) =>
        {
            using var dlg = new FolderBrowserDialog 
            { 
                Description = "选择导出目录",
                SelectedPath = OutputDirectory
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                OutputDirectory = dlg.SelectedPath;
                _lblOutDir.Text = OutputDirectory;
                _lblOutDir.ForeColor = Color.Black;
            }
        };

        grpOut.Controls.AddRange([lblOut, _lblOutDir, _btnOutDir]);

        // === 确认按钮 ===
        _btnOk = new Button
        {
            Text = "开始导出", Location = new Point(490, 500), Size = new Size(120, 35),
            BackColor = Color.FromArgb(100, 180, 100), ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
            Font = new Font("Microsoft YaHei", 10F)
        };
        _btnOk.FlatAppearance.BorderSize = 0;
        _btnOk.Click += BtnOk_Click;

        var btnCancel = new Button 
        { 
            Text = "取消", Location = new Point(360, 500), Size = new Size(100, 35),
            Font = new Font("Microsoft YaHei", 9F)
        };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        this.Controls.AddRange([grpDate, grpFiles, grpOut, _btnOk, btnCancel]);

        // 初始化加载文件列表
        DateRangeChanged(null!, EventArgs.Empty);
    }

    private string GetDefaultOutputDir()
    {
        try
        {
            // 获取用户下载目录
            string downloads = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            downloads = Path.Combine(downloads, "Downloads");

            // 如果有选中的日期，使用第一个日期命名子文件夹
            string dateFolder = DateTime.Now.ToString("yyyy-MM-dd");
            if (_allDates.Count > 0)
                dateFolder = $"VideoParsing_{_allDates[0]}";

            return Path.Combine(downloads, dateFolder);
        }
        catch
        {
            return Path.Combine(Path.GetTempPath(), "VideoParsing_Export");
        }
    }

    private void DateRangeChanged(object? s, EventArgs e)
    {
        SelectedDates = GetDateRange();

        // 清空并重新加载文件列表
        _chkFiles.Items.Clear();

        foreach (var date in SelectedDates)
        {
            var hours = RawVideoParser.ScanHourFolders(_rawdataPath, date);
            foreach (var hour in hours)
            {
                var files = RawVideoParser.ScanVideoFiles(_rawdataPath, date, hour);
                foreach (var file in files)
                {
                    string name = Path.GetFileName(file);
                    string displayText = name;
                    
                    // 解析时间戳为可读时间
                    if (long.TryParse(name, out long ms))
                    {
                        var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
                        
                        // 获取文件大小（快速操作）
                        string sizeStr = "0KB";
                        try
                        {
                            var fi = new FileInfo(file);
                            long sizeBytes = fi.Length;
                            if (sizeBytes > 1024 * 1024)
                                sizeStr = $"{sizeBytes / 1024.0 / 1024.0:F1}MB";
                            else
                                sizeStr = $"{sizeBytes / 1024.0:F0}KB";
                        }
                        catch { }

                        // 格式: 2026年5月22日---14:29:10( 大小-1.5MB )
                        displayText = $"{dt:yyyy年M月d日}---{dt:HH:mm:ss}( 大小-{sizeStr} )";
                    }

                    // 添加到列表，默认选中
                    int idx = _chkFiles.Items.Add(displayText);
                    _chkFiles.SetItemChecked(idx, true);

                    // 存储完整路径作为Tag（用文件对象包装）
                    _chkFiles.Items[idx] = new FileItem(file, displayText);
                }
            }
        }

        UpdateCount();
    }

    private void UpdateCount()
    {
        int checkedCount = 0;
        for (int i = 0; i < _chkFiles.Items.Count; i++)
        {
            if (_chkFiles.GetItemChecked(i)) checkedCount++;
        }

        _lblFileCount.Text = $"已选择: {checkedCount} / {_chkFiles.Items.Count} 个文件";
    }

    private List<string> GetDateRange()
    {
        if (_cmbStart.SelectedIndex < 0 || _cmbEnd.SelectedIndex < 0)
            return new List<string>();

        int start = _cmbStart.SelectedIndex;
        int end = _cmbEnd.SelectedIndex;
        if (start > end) (start, end) = (end, start);

        return _allDates.Skip(start).Take(end - start + 1).ToList();
    }

    private void BtnOk_Click(object? s, EventArgs e)
    {
        if (string.IsNullOrEmpty(OutputDirectory))
        {
            MessageBox.Show("请先选择输出目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 收集选中的文件
        SelectedFiles = new List<string>();
        for (int i = 0; i < _chkFiles.Items.Count; i++)
        {
            if (_chkFiles.GetItemChecked(i) && _chkFiles.Items[i] is FileItem item)
            {
                SelectedFiles.Add(item.FilePath);
            }
        }

        if (SelectedFiles.Count == 0)
        {
            MessageBox.Show("请至少选择一个视频文件。", "提示");
            return;
        }

        SelectedDates = GetDateRange();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void BtnMerge_Click(object? s, EventArgs e)
    {
        // 收集选中的文件
        var selectedFiles = new List<string>();
        for (int i = 0; i < _chkFiles.Items.Count; i++)
        {
            if (_chkFiles.GetItemChecked(i) && _chkFiles.Items[i] is FileItem item)
                selectedFiles.Add(item.FilePath);
        }

        if (selectedFiles.Count == 0)
        {
            MessageBox.Show("请先勾选要整合的视频文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (selectedFiles.Count == 1)
        {
            MessageBox.Show("整合功能需要选择2个或以上视频碎片。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // 计算总大小
        long totalSize = 0;
        foreach (var file in selectedFiles)
        {
            try { totalSize += new FileInfo(file).Length; } catch { }
        }
        string sizeStr = totalSize > 1024 * 1024 * 1024 
            ? $"{totalSize / 1024.0 / 1024.0 / 1024.0:F2} GB"
            : totalSize > 1024 * 1024 
                ? $"{totalSize / 1024.0 / 1024.0:F1} MB" 
                : $"{totalSize / 1024.0:F0} KB";

        string msg = $"将 {selectedFiles.Count} 个视频碎片整合成 1 个视频\n\n" +
                     $"总大小: {sizeStr}\n" +
                     $"输出到: 整合 文件夹\n\n" +
                     $"视频将按时间线从前往后排序合并";
        
        if (MessageBox.Show(msg, "确认整合", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        MergeIntoOneVideo = true;
        SelectedFiles = selectedFiles;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// 文件项包装类，用于在CheckedListBox中存储完整路径
    /// </summary>
    private class FileItem
    {
        public string FilePath { get; }
        public string DisplayText { get; }

        public FileItem(string filePath, string displayText)
        {
            FilePath = filePath;
            DisplayText = displayText;
        }

        public override string ToString() => DisplayText;
    }
}
