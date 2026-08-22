# 架构说明

VoiceDuck 的代码刻意保持在几个边界清楚的模块中，便于独立测试和后续替换实现。

## 数据流

```text
Windows Core Audio
        │
        ▼
AudioSessionGraph ──► AudioSessionInfo ──► MainForm
        │
        ▼
DuckingCoordinator ◄── AppSettings
        │
        ├── VoiceGate：阈值、触发延迟、断句保持
        └── VolumeState：原音量、最近写入值、手动调整检测
```

## 模块职责

### CoreAudio.cs

封装 MMDevice 和 Audio Session COM 接口。`AudioSessionGraph` 枚举所有活动播放设备，使用“设备 ID + 会话实例 ID”作为稳定键，因此同一进程的多个会话不会互相覆盖。

所有 COM 操作都在专用 MTA 音频线程执行。界面只读取复制后的会话快照，不直接持有 COM 对象。

### DuckingCoordinator.cs

纯状态机和音量控制策略，不依赖 WinForms 或具体 COM 类型。它负责：

- 找出已配置触发进程的最大峰值；
- 驱动 `VoiceGate`；
- 在第一次闪避时记录每个目标会话的基准音量；
- 平滑移动到 `基准音量 × DuckRatio`；
- 检测音量混合器里的外部调整并更新基准；
- 停止闪避或退出时恢复基准音量。

### VoiceGate.cs

将瞬时峰值转换为带时间语义的开关。触发延迟用于过滤短提示音，Hold 用于覆盖人声断句。它不做语音内容识别，也不读取 PCM 数据。

### MainForm.cs

负责应用选择、参数编辑、状态显示和托盘交互。所有配置变更先规范化，再持久化并发送给音频服务。

### SettingsStore.cs

使用 `DataContractJsonSerializer` 保存配置。写入时先落临时文件，再替换正式文件，减少异常退出造成半截配置的概率。

## 线程模型

- UI 线程：WinForms、用户操作、配置保存。
- 音频线程：设备枚举、峰值读取、音量写入、状态机 Tick。
- 两个线程之间只交换配置副本、会话快照和状态副本。

## 恢复保证

VoiceDuck 只记录自己实际接管过的目标会话。正常暂停、托盘退出、窗口关闭和应用退出路径都会调用恢复逻辑。操作系统强制终止进程或断电无法执行进程内恢复，这是所有用户态会话音量工具共有的限制。
