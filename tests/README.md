# Tests

- `CoreTests`：不访问 Windows 音频设备，验证提示音过滤、目标选择、Hold、精确恢复和多会话行为。
- `AudioSmokeTest`：只读枚举当前机器的播放会话，验证 Core Audio COM 声明和运行时兼容性。
- `UiRenderTest`：离屏渲染主窗口，用于检查布局和文字裁切；默认只编译，不在 CI 中弹出窗口。

运行根目录下的 `scripts/build.ps1` 会编译全部测试，并自动执行前两项。
