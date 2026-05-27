<img width="286" height="189" alt="image" src="https://github.com/user-attachments/assets/e5f0c7ac-7c2a-4e7b-ad51-7f9e6c76dd2e" /># 🎬 VideoParsing - 视频解析工具

[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)


**解析自定义 MO_V 容器格式的视频文件 → 转码为 H.264 MP4 → 软件内播放**

专为 **4G低功耗摄像头** 存储卡视频数据设计的解析与转换工具。

---
# 注意:！！！！！！！！！！！！
- 本项目99%的代码都是ai生成的，且没有仔细查看过代码，可能存在问题！！
- 本工具是个人使用，不负责任何问题或损失。
- 这个不一定能支持你的视频文件！!
- 做这个工具是因为摄像头的存储卡里不是那种能直接播放的视频文件，所以才做的这个工具。
- 发布github上是为了重装系统后方便使用
- 目前支持视云摄像头的视频文件。(因为我就是这个摄像头)
- 除了这个**"注意:！"**，其他介绍都是ai生成(因为懒✨)

---

## 📸 界面预览

```
1.2.1主页面<img width="884" height="692" alt="image" src="https://github.com/user-attachments/assets/b44ef139-1c34-4fd5-a4b8-b187e590b034" />
1.2.1分析页面<img width="1184" height="812" alt="image" src="https://github.com/user-attachments/assets/f6e2b728-8569-4a8d-bd34-9523e13f654f" />

```

---

## 🚀 快速开始

### 环境要求
- **操作系统**: Windows 10(x64)
- **运行时**: .NET 10.0 Desktop Runtime
- **依赖**: FFmpeg (程序可自动下载)

### 安装运行

#### 方式一：构建项目

- 克隆仓库
```bash
git clone https://github.com/Failure-ai/VideoParsing.git --recursive
```
- 使用vs2022打开项目 Video Parsing.slnx 文件


#### 方式二：使用发布版本（推荐）
- 下载发布版本：[VideoParsing-1.2.x.zip](https://github.com/Failure-ai/VideoParsing/releases/tag)
- 解压文件到任意目录，双击 `VideoParsing.exe` 即可运行

---


---

## 📁 数据格式支持

### MO_V 容器结构
```
文件结构:
rawdata/
└── 2026-05-22/           ← 日期文件夹
    ├── 07/               ← 小时文件夹
    │   ├── 1779405933531  ← 视频数据文件（无扩展名）
    │   ├── 1779405933531.txt  ← 索引文件
    │   └── ...
    └── 08/
```

### 二进制格式
```
[MO_V 段 #N]
┌──────────────────────────────────────┐
│ [0x00] "MO_V"         (4B) 魔数      │
│ [0x04] flags          (4B) 标志位    │
│ [0x08] field1         (4B)          │
│ [0x0C] field2         (4B) 数据大小  │
│ [0x10] 00 00 00 01    (4B) NAL起始码 │
│ [0x14] HEVC视频数据...              │
└──────────────────────────────────────┘
```

## 🏗️ 项目架构

```
Video Parsing/
├── Form1.cs              # 主界面 + 业务逻辑
├── VideoConverter.cs     # FFmpeg 转码引擎（含IDisposable）
├── RawVideoParser.cs     # MO_V 容器解析器（O(n)算法）
├── FFmpegManager.cs      # FFmpeg 检测/下载/管理
├── ExportDialog.cs       # 导出/整合对话框
├── Program.cs            # 应用入口
├── Video Parsing.csproj  # 项目配置 (.NET 10.0)
└── bin/Debug/            # 输出目录
    ├── ffmpeg.exe        # FFmpeg 可执行文件
    ├── ffprobe.exe       # 媒体分析工具
    └── ffplay.exe        # 内置播放器
```

### 核心类职责
| 类 | 职责 |
|---|------|
| **Form1** | UI布局、事件处理、业务流程编排 |
| **RawVideoParser** | 二进制解析、MO_V段提取、目录扫描 |
| **VideoConverter** | FFmpeg调用、进度解析、MP4封装/合并 |
| **FFmpegManager** | 环境检测、自动下载、路径管理 |
| **ExportDialog** | 日期选择、文件勾选、整合确认 |

## 📝 更新日志

### v1.2.0 (2026-05-24)
- ✨ 新增 **"整合成视频"** 功能（多碎片→单个MP4）
- 🐛 修复播放时进度条不显示的问题
- 🎨 优化UI布局（Dock模式替代绝对定位）
- ⚡ 性能优化：
  - O(n)算法替代O(n²)查找
  - 流式读取减少内存占用
  - Parser实例复用
  - 自动清理临时文件
- 🔒 移除硬编码路径，增强隐私安全
- ♻️ 实现 IDisposable 接口

### v1.1.0
- ✨ 添加批量导出功能
- ✨ 添加倍速播放支持（1-100x）
- ✨ FFmpeg自动下载安装

### v1.0.0
- 🎉 初始版本
- ✨ MO_V容器解析
- ✨ HEVC→H.264转码
- ✨ 系统播放器集成

---

<div align="center">

**如果这个项目对你有帮助请自己维护吧**

</div>
