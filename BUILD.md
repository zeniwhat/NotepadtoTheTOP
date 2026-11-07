# 从源码构建

使用 Windows 10/11 自带的 .NET Framework C# 编译器和 Windows PowerShell，不需要下载 NuGet 包或安装 .NET SDK。

在仓库根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

生成 `记事本置顶.exe`，可直接运行。也可以指定输出位置：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -OutputPath .\build\NotepadPin.exe
```

## 代码结构

- `src/Program.cs`：托盘菜单、快捷键、单实例入口。
- `src/Windows.cs`：窗口身份与 Windows 原生窗口操作。
- `src/PinController.cs`：置顶状态及退出恢复。
- `src/Borders.cs`：原生青蓝边框及旧系统的悬浮边框回退。
- `src/DesktopSession.cs`：桌面作用域、托盘图标及本地状态记录。
- `app.manifest`：权限与 DPI 声明。

## 验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Integration.ps1 -NativeOnly
```

核心测试会短暂创建测试窗口，并注册测试快捷键。运行前建议退出正在使用的本工具，避免快捷键占用。

真实记事本集成测试会创建空白临时文件，并清理自己创建的窗口；如果已有记事本窗口，会停止测试以保护当前文档。`-NativeOnly` 只验证原生 API 流程，不验证实际键盘输入。

正常桌面才能检查托盘和实际快捷键。隔离测试桌面上的进程存在，不代表已在用户桌面运行。

## 便携包

先构建根目录的 EXE，再运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1 -Version 1.0.0
```

生成 `dist/v1.0.0/NotepadPin.exe`、便携 ZIP 和 SHA256 校验文件。
