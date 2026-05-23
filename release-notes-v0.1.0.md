# CC-Tray v0.1.0

## English

Initial public release of CC-Tray.

CC-Tray is a lightweight Windows tray app for controlling an already configured `cc-connect daemon` running inside WSL. `cc-connect` itself must be installed and configured in WSL first; CC-Tray only provides the Windows tray controls.

Highlights:

- Native Windows tray app, no Python/PyQt runtime required.
- Left-click or right-click tray menu.
- WSL distribution/user auto-detection.
- Daemon install, start, restart, stop, and refresh actions.
- Colored status indicator.
- Optional keep-running mode.
- Optional Windows startup.
- Optional stop daemon on tray exit.
- Language menu: EN, ZH, KO, JA, FR.

## 中文

CC-Tray 首个公开版本。

CC-Tray 是一个运行在 Windows 上的轻量托盘应用，用来控制已经在 WSL 中配置好的 `cc-connect daemon`。`cc-connect` 本体需要先在 WSL 中安装并配置完成；CC-Tray 只提供 Windows 托盘控制入口。

主要内容：

- Windows 原生托盘应用，不依赖 Python/PyQt 运行环境。
- 左键或右键点击托盘图标打开菜单。
- 自动检测 WSL 发行版和用户。
- 支持 daemon 安装、启动、重启、停止和刷新状态。
- 彩色状态指示点。
- 可选保持运行。
- 可选开机自启。
- 可选退出托盘时停止 daemon。
- 语言菜单：EN、ZH、KO、JA、FR。
