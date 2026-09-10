![Image text](https://raw.githubusercontent.com/EosZeph/MorseDecoder/refs/heads/main/MorseDecoder.ico?token=GHSAT0AAAAAAEISLLUVBE4ZLOHSS2JRARWS2VC3WIQ)

# Morse Decoder

Windows 声卡输入的多信号 CW 实时解码原型。

## 当前版本

- Windows `winmm` 声卡录音后端，48 kHz、单声道、16-bit。
- 1024 点实时 FFT 瀑布图，256 点步进，约 5.3 ms 一列。
- 自动检测音频频带中的多个窄带 CW 轨迹。
- 每个轨迹独立下变频、窄带包络提取和自适应点划解码。
- 轨迹列表、音频频率、WPM、信号强度和分轨迹文本。
- 合成 `CQ DE MORSE TEST` 测试信号，不需要无线电台也可以验证链路。

当前解码器是可工作的自适应时序分类器，保留了替换成完整 HMM、粒子滤波或后验概率解码的接口。第一版还没有呼号提取、DX Cluster、中文翻译和 I/Q 文件格式。

## 构建

项目使用仓库内 `.tools\dotnet` 中的本地 .NET 8 SDK，不要求修改系统 `PATH`。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Test
```

只构建应用：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

## 运行

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

进入界面后：

1. 等待音频输入设备列表加载。
2. 选择电台或声卡的线路输入设备。
3. 点击“开始”。
4. 如果暂时没有 CW 信号，点击“测试信号”验证瀑布图、轨迹和解码文本。

输入电平建议保持在 -12 至 -6 dBFS。Windows 麦克风增强、自动增益和音效应关闭。

## 绿色免安装发布

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-portable.ps1
```

输出位置：

```text
dist/MorseDecoder-Portable-win-x64/
dist/MorseDecoder-Portable-win-x64.zip
dist/MorseDecoder-Portable-win-x64.sha256.txt
```

发布包是 Windows x64 自包含版本，目标电脑不需要安装 .NET。

生成 32 位版本：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-portable.ps1 -Architecture x86
```

输出位置：

```text
dist/MorseDecoder-Portable-win-x86/
dist/MorseDecoder-Portable-win-x86.zip
dist/MorseDecoder-Portable-win-x86.sha256.txt
```

`win-x86` 版本可用于 32 位 Windows 10，也可以直接运行在 64 位 Windows 上。

## Windows 7 32 位兼容版

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-win7.ps1
```

输出位置：

```text
dist/MorseDecoder-Win7-x86/
dist/MorseDecoder-Win7-x86.zip
dist/MorseDecoder-Win7-x86.sha256.txt
```

该版本使用 `netcoreapp3.1` 和 `win-x86` 自包含部署，支持 Windows 7 SP1。由于 .NET Core 3.1 已停止支持，该版本仅用于旧系统兼容，不建议替代正常的 Windows 10/11 版本。

## 应用图标

图标源文件和多尺寸 Windows ICO 位于：

```text
src/MorseDecoder.App/Assets/MorseDecoder.png
src/MorseDecoder.App/Assets/MorseDecoder.ico
```

重新生成图标：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\generate-icon.ps1
```

## 目录

```text
src/MorseDecoder.Core   音频采集、FFT、轨迹跟踪、CW 解码
src/MorseDecoder.App    WPF 界面和瀑布图渲染
tests/MorseDecoder.SmokeTests
                        合成 CW 闭环测试
scripts                 本地构建和运行脚本
```

## 下一阶段

1. 用真实电台录音调校噪声门限、AGC 和滤波器带宽。
2. 将当前自适应时序分类器替换为显式 HMM/贝叶斯后验解码。
3. 加入可选 WAV 录制与回放，作为算法回归测试输入。
4. 改进轨迹合并、邻频分离和长间隔后的重新捕获。
