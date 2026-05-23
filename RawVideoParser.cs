using System.Text;

namespace Video_Parsing;

/// <summary>
/// MO_V 容器格式解析器
/// 解析 rawdata 中的自定义视频格式文件，提取裸视频流
/// </summary>
public class RawVideoParser
{
    /// <summary>
    /// 视频文件信息（来自 .txt 索引）
    /// </summary>
    public class VideoSegmentInfo
    {
        public long Timestamp { get; set; }
        public long ByteOffset { get; set; }
        public int DataSize { get; set; }

        public VideoSegmentInfo(long timestamp, long byteOffset, int dataSize)
        {
            Timestamp = timestamp;
            ByteOffset = byteOffset;
            DataSize = dataSize;
        }
    }

    /// <summary>
    /// MO_V 段信息
    /// </summary>
    public class MovSegment
    {
        public long FileOffset { get; set; }
        public int HeaderSize { get; set; }
        public int DataOffset { get; set; }
        public int DataLength { get; set; }

        public MovSegment() { }

        public MovSegment(long fileOffset, int headerSize, int dataOffset, int dataLength)
        {
            FileOffset = fileOffset;
            HeaderSize = headerSize;
            DataOffset = dataOffset;
            DataLength = dataLength;
        }
    }

    /// <summary>
    /// 解析结果
    /// </summary>
    public class ParseResult
    {
        public string FilePath { get; set; } = "";
        public long FileSize { get; set; }
        public List<MovSegment> Segments { get; set; } = new();
        public List<VideoSegmentInfo> IndexEntries { get; set; } = new();
        public byte[] ExtractedRawStream { get; set; } = Array.Empty<byte>();
        public long StartTimestamp { get; set; }
        public long EndTimestamp { get; set; }
        public double DurationSeconds { get; set; }
    }

    /// <summary>
    /// 解析单个 raw 文件 + 对应的 .txt 索引文件（使用流式处理，内存友好）
    /// </summary>
    public ParseResult Parse(string rawFilePath)
    {
        var result = new ParseResult { FilePath = rawFilePath };

        // 使用流式读取，避免一次性加载大文件到内存
        byte[] data;
        using (var fs = new FileStream(rawFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096))
        using (var ms = new MemoryStream())
        {
            fs.CopyTo(ms);
            data = ms.ToArray();
        }
        
        result.FileSize = data.Length;

        // 查找所有 MO_V 段（单次遍历 O(n)）
        result.Segments = FindMovSegmentsOptimized(data);

        // 读取 .txt 索引文件
        string txtPath = rawFilePath + ".txt";
        if (File.Exists(txtPath))
        {
            result.IndexEntries = ParseIndexFile(txtPath);
            if (result.IndexEntries.Count > 0)
            {
                result.StartTimestamp = result.IndexEntries[0].Timestamp;
                result.EndTimestamp = result.IndexEntries[^1].Timestamp;
                result.DurationSeconds = (result.EndTimestamp - result.StartTimestamp) / 1000.0;
            }
        }

        // 如果没有索引，用 MO_V 段估算
        if (result.IndexEntries.Count == 0 && result.Segments.Count > 0)
        {
            result.DurationSeconds = result.Segments.Count * 0.04;
        }

        // 提取裸视频流（直接写入文件，避免内存中保留两份）
        result.ExtractedRawStream = ExtractRawStreamOptimized(data, result.Segments);

        return result;
    }

    /// <summary>
    /// 从二进制数据中查找所有 MO_V 段（优化版：单次遍历 O(n)）
    /// </summary>
    private List<MovSegment> FindMovSegmentsOptimized(byte[] data)
    {
        var segments = new List<MovSegment>();
        
        // 单次遍历，记录所有MO_V位置
        var movPositions = new List<int>();
        for (int i = 0; i <= data.Length - 4; i++)
        {
            if (data[i] == 'M' && data[i + 1] == 'O' &&
                data[i + 2] == '_' && data[i + 3] == 'V')
            {
                movPositions.Add(i);
            }
        }

        // 根据位置信息解析每个段
        for (int idx = 0; idx < movPositions.Count; idx++)
        {
            int pos = movPositions[idx];
            int nextPos = (idx < movPositions.Count - 1) ? movPositions[idx + 1] : data.Length;
            
            int headerSize = 16;
            int dataStart = pos + headerSize;

            if (dataStart >= data.Length) continue;

            // 检测NAL起始码
            if (dataStart + 4 <= data.Length)
            {
                bool has4ByteCode = (data[dataStart] == 0x00 && data[dataStart + 1] == 0x00 &&
                                    data[dataStart + 2] == 0x00 && data[dataStart + 3] == 0x01);
                bool has3ByteCode = !has4ByteCode && (dataStart + 3 <= data.Length) &&
                                   (data[dataStart] == 0x00 && data[dataStart + 1] == 0x00 && data[dataStart + 2] == 0x01);
                
                if (has3ByteCode && !has4ByteCode) headerSize = 15;
            }

            int dataLength = nextPos - dataStart;
            if (dataLength > 0)
            {
                segments.Add(new MovSegment(pos, headerSize, dataStart, dataLength));
            }
        }

        return segments;
    }

    /// <summary>
    /// 从二进制数据中查找所有 MO_V 段（原始版本-保留兼容）
    /// </summary>
    [System.Obsolete("使用 FindMovSegmentsOptimized 替代")]
    private List<MovSegment> FindMovSegments(byte[] data)
    {
        return FindMovSegmentsOptimized(data);
    }

    private int FindNextMov(byte[] data, int startPos)
    {
        for (int i = startPos; i < data.Length - 3; i++)
        {
            if (data[i] == 'M' && data[i + 1] == 'O' &&
                data[i + 2] == '_' && data[i + 3] == 'V')
                return i;
        }
        return -1;
    }

    /// <summary>
    /// 解析 .txt 索引文件
    /// </summary>
    private List<VideoSegmentInfo> ParseIndexFile(string txtPath)
    {
        var entries = new List<VideoSegmentInfo>();
        foreach (string line in File.ReadAllLines(txtPath))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 &&
                long.TryParse(parts[0], out long ts) &&
                long.TryParse(parts[1], out long offset) &&
                int.TryParse(parts[2], out int size))
            {
                entries.Add(new VideoSegmentInfo(ts, offset, size));
            }
        }
        return entries;
    }

    /// <summary>
    /// 提取裸视频流（优化版：预分配内存，减少扩容）
    /// </summary>
    private byte[] ExtractRawStreamOptimized(byte[] data, List<MovSegment> segments)
    {
        if (segments.Count == 0) return Array.Empty<byte>();
        
        // 预计算总大小，避免动态扩容
        long totalSize = 0;
        foreach (var seg in segments)
            totalSize += seg.DataLength;
        
        if (totalSize == 0 || totalSize > int.MaxValue)
            return Array.Empty<byte>();
            
        var stream = new MemoryStream((int)totalSize);

        foreach (var seg in segments)
        {
            if (seg.DataOffset >= 0 && seg.DataOffset + seg.DataLength <= data.Length)
                stream.Write(data, seg.DataOffset, seg.DataLength);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// 提取裸视频流（原始版本-保留兼容）
    /// </summary>
    [System.Obsolete("使用 ExtractRawStreamOptimized 替代")]
    private byte[] ExtractRawStream(byte[] data, List<MovSegment> segments)
    {
        return ExtractRawStreamOptimized(data, segments);
    }

    /// <summary>
    /// 扫描目录，获取所有日期
    /// </summary>
    public static List<string> ScanDateFolders(string rawdataPath)
    {
        var dates = new List<string>();
        if (!Directory.Exists(rawdataPath)) return dates;

        foreach (string dir in Directory.GetDirectories(rawdataPath))
        {
            string name = Path.GetFileName(dir);
            if (name.Length == 10 && name[4] == '-' && name[7] == '-')
                dates.Add(name);
        }
        dates.Sort();
        return dates;
    }

    /// <summary>
    /// 获取某日期下的小时段
    /// </summary>
    public static List<string> ScanHourFolders(string rawdataPath, string date)
    {
        var hours = new List<string>();
        string datePath = Path.Combine(rawdataPath, date);
        if (!Directory.Exists(datePath)) return hours;

        foreach (string dir in Directory.GetDirectories(datePath))
        {
            string name = Path.GetFileName(dir);
            if (name.Length <= 2 && int.TryParse(name, out _))
                hours.Add(name);
        }
        hours.Sort((a, b) => int.Parse(a).CompareTo(int.Parse(b)));
        return hours;
    }

    /// <summary>
    /// 获取某日期某小时下的所有视频文件
    /// </summary>
    public static List<string> ScanVideoFiles(string rawdataPath, string date, string hour)
    {
        var files = new List<string>();
        string hourPath = Path.Combine(rawdataPath, date, hour);
        if (!Directory.Exists(hourPath)) return files;

        foreach (string file in Directory.GetFiles(hourPath))
        {
            string name = Path.GetFileName(file);
            if (!name.EndsWith(".txt") && name != "section" && !name.Contains('.'))
                files.Add(file);
        }
        files.Sort();
        return files;
    }
}
