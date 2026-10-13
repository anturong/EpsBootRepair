# EPS 引导修复工具（EpsBootRepair）

> 一键重建 Windows 10 的 **UEFI 引导分区（ESP）**，把被清空、损坏或误删引导文件的 ESP
> 恢复到可开机状态。**专为 PE / 系统维护盘环境设计。**

A one-click Windows 10 UEFI ESP boot-repair tool for WinPE / recovery environments.

---

## 这是干什么的

Windows 10 的 UEFI 引导文件全都在一块很小的 **ESP 分区**（EFI System Partition，FAT32，
通常 100–300 MB）里。系统重装、分区工具误操作、杀毒软件清理"多余文件"之后，这块分区常被
**整个清空**——盘还在、Windows 也还在，就是开不了机，开机直接进 BIOS 或报
`Boot Device Not Found` / `No bootable device`。

修它其实只需要一条系统自带的命令。本工具做的就是：**为你选好那块 ESP、自动分配盘符、
把这条命令跑起来，并实时显示输出**，全程点两下。

## 它怎么修（原理）

只有一条命令，不自己实现任何引导逻辑：

```
bcdboot <系统分区>\Windows /s <ESP盘符> /f UEFI
```

`bcdboot` 会从「系统分区」里的 `Windows\Boot\*` 读出引导文件，往 ESP 上铺回去：

| 铺回的文件 | 作用 |
|---|---|
| `\EFI\Boot\bootx64.efi` | 固件默认引导项（不依赖 NVRAM，最保底） |
| `\EFI\Microsoft\Boot\bootmgfw.efi` | Windows 引导管理器 |
| `\EFI\Microsoft\Boot\bootmgr.efi` | 辅助加载器 |
| 字体（`wgl4_boot.ttf` 等） | 引导菜单显示 |
| `\EFI\Microsoft\Boot\BCD` | 引导配置数据库（由 `BCD-Template` 现场生成） |

**ESP 被整个清空过也无所谓**——`bcdboot` 缺什么补什么。这正是它适合"修"的原因：
不需要知道原来丢了哪几个文件。

## 特性

- **自动识别 ESP**：选中 Windows 系统分区后，自动找到**同一块物理盘上**的 ESP，
  无需手动挑盘。
- **自动分配盘符**：PE 里 ESP 和系统盘经常都没有盘符。工具先在进程内直接挂载，
  失败才回退 `diskpart`——且分配的是**临时盘符，重启即消失**，不留痕。
- **内置官方原版工具**：不依赖 PE 自带的精简版（很多 PE 的 `bcdboot.exe` 是被换过壳的，
  直接以退出码 193 失败）。
- **中英双语界面**，语言包可扩展。
- **零 WMI 依赖**：分区枚举全走 Win32 API，因为精简 PE 常没有 Winmgmt 服务。
- **自我提权**：普通 Windows 上右键运行即可，程序自己弹 UAC；PE 里本身是 SYSTEM，
  直接跑。
- **全程可追溯**：每个关键步骤（选了哪块分区、用了哪个 exe、来源是系统还是内置、
  退出码）都写进界面日志，可选落盘。

## 适用 / 不适用

**适用**

- GPT 分区表 + UEFI 引导的 Windows 10（同理也适合 Win11）。
- ESP 被清空 / 引导文件损坏 / 误删导致的**开不了机**。
- 在 PE 或维护盘里跑，也支持在能开机的正常 Windows 里修**另一块**盘上的系统。

**本工具不做的事**（有意为之，别指望它）

- ✗ **MBR / Legacy 引导** —— 那是另一条路，见下面的 **`mbr` 分支**。
- ✗ 写固件（NVRAM）启动项 —— 那属于"文件没丢、只是启动项丢了"的场景。
- ✗ 修复引导**之外**的系统故障（蓝屏、驱动、系统文件损坏）。
- ✗ 分区表操作、分区大小调整、数据恢复。
- ✗ 多系统引导菜单的编辑。

## 系统要求

| 项 | 要求 |
|---|---|
| 系统 | Windows 10 / 11，或 Windows 10 PE |
| 架构 | x64 或 x86（启动器会按当前 PE 自动挑） |
| 权限 | **必须管理员**（普通 Windows 会自动弹 UAC；PE 里本就是 SYSTEM） |
| 运行时 | **无需安装**——自包含 .NET 8，exe 直接就是成品 |
| 分区表 | GPT（UEFI） |

## 快速开始

### 1. 准备便携目录

程序、语言包、配置、内置工具必须放在一起：

```
EpsBootRepair\
├─ GO.CMD                  ← 启动器（按 PE 架构自动选 x64\x86）
├─ config.ini              ← 程序行为配置
├─ lang\
│   ├─ zh-CN.ini
│   └─ en-US.ini
├─ tools\                  ← 内置的微软原版工具（不随源码仓库提供，见下文）
│   ├─ x64\
│   │   ├─ bcdboot.exe
│   │   └─ diskpart.exe
│   └─ x86\
│       ├─ bcdboot.exe
│       └─ diskpart.exe
├─ x64\EpsBootRepair.exe   ← 实际程序（构建产物）
└─ x86\EpsBootRepair.exe
```

`GO.CMD` 按 `PROCESSOR_ARCHITECTURE` 在 `x64\` 和 `x86\` 之间选一个启动——
因为 32 位 PE 跑不了 x64 程序，反之精简 x64 PE 又常没有 WoW64。

> 只想单架构用，也可以直接把 `EpsBootRepair.exe` 单独拷出来双击，
> 只要它旁边的 `lang\ tools\ config.ini` 齐全即可。

### 2. 启动

- **PE 里**：直接双击 `GO.CMD` 或 `EpsBootRepair.exe`。
- **正常 Windows 上**：右键 → **以管理员身份运行**（或直接运行，程序会弹 UAC 自我提权）。

### 3. 操作（就三步）

1. 在上方下拉框里选 **Windows 系统分区**——就是那个含 `Windows\` 文件夹的分区。
2. 往下看**警告栏**：显示绿色的「环境检查通过」才表示可以动手。
   ESP 分区一栏会自动填上识别结果。
3. 点 **「一键修复 ESP」**，确认弹出的对话框，等日志出现 `修复完成。`。

### 4. 重启验证

拔掉 U 盘 / 退出 PE，重启。能进系统即修复成功。

## 自动识别与判定逻辑

理解这几条，就知道工具为什么有时"拦着不让点"：

- **ESP 归属**：工具用的是**和所选系统分区同一块物理盘上**的 ESP。
  只有当前提下——那块盘上确实没有 ESP，而**全机恰好只有一个 ESP**——才会退而用那一个；
  否则认为"选错了盘"，**禁用修复**，不猜。
- **警告栏的三种状态**
  - 🟢 **通过**：可以修。
  - 🟠 **提醒**（仍可修）：例如所选分区里没有 `Windows\System32`。工具不拦你，
    但日志会告诉你 `bcdboot` 很可能会报错——先确认选对了分区。
  - 🔴 **阻断**（禁用按钮）：例如所选分区所在盘上没有 ESP，或你选中的就是 ESP 本身。
- **有意不拦的情况**：选中了"看起来像系统分区但里面没有 Windows"的分区时，只警告不阻断。
  这是为了兼容一些分区布局不规范的场景——最终以 `bcdboot` 的实际结果为准。
- **确认对话框是唯一的安全闸**：点「一键修复」后必然先弹一次
  「即将往 ESP 分区写入引导文件……确认继续？」，点掉才开始写。**这是刻意保留的一步。**

## ⚠️ 内置工具（`tools\`）不随仓库提供

`tools\x64\`、`tools\x86\` 里放的是**微软的原版 `bcdboot.exe` / `diskpart.exe`，
受版权保护、不可再分发**，因此 **不包含在本源码仓库中**（`.gitignore` 已排除），
`tools\` 里只保留一张来源登记表 `tools/SOURCE.txt`（含来源镜像、文件名、大小、md5）。

**想自己组一份可用的程序，需要补上这两颗文件**，任选其一：

**方法 A：从本机系统拷（最省事）**
```cmd
copy %SystemRoot%\System32\bcdboot.exe  <程序目录>\tools\x64\
copy %SystemRoot%\System32\diskpart.exe <程序目录>\tools\x64\
```
（x86 版从 32 位系统或 32 位 PE 的 `SysWOW64` / PE 镜像里取。）

**方法 B：从官方镜像取（版本可控）**
按 `tools/SOURCE.txt` 里登记的来源镜像挂载后取出——例如 x64 取自官方
**Windows 10 22H2 x64 DVD** 的 `sources\boot.wim`，x86 取自 32 位 Win10 PE。
取完可用 `SOURCE.txt` 里的 md5 校验。

> **没有 `tools\` 也能跑**：程序启动时会先探 PE 自带的
> `%SystemRoot%\System32\bcdboot.exe`（配置项 `PreferBundled=0`）。但在**精简 PE** 里
> 这条路不可靠——例如 USM 系统总裁 PE 的 `bcdboot.exe` 直接以 **193** 失败，
> 于是自带一份稳妥的「官方原版」是默认策略。

## 从源码构建

需要 **.NET 8 SDK**。

```bash
cd uefi引导修复
dotnet build -c Release                 # 默认 win-x64
dotnet build -c Release -r win-x86      # 要 x86 版必须显式指定 RID
```

产物在 `bin/Release/net8.0-windows/<rid>\`，`lang\`、`config.ini`、`tools\`
会一并复制过去；启动器 `GO.CMD` 会被自动铺到 `bin\Release\net8.0-windows\`
（即 `win-x64\` 与 `win-x86\` 的上一级），这正是它需要的布局。

发布单文件：

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

> 构建完记得往 `publish\tools\<arch>\` 里补上 `bcdboot.exe` / `diskpart.exe`
> （见上一节）——它们不会被构建带出来。

## 打包进 PE / 维护盘

把 `bin\Release\net8.0-windows\` 下的 `x64\`、`x86\`、`GO.CMD`、`lang\`、
`config.ini`、`tools\` 整体拷进 PE 的某个目录，开机后双击 `GO.CMD` 即可。
工具是自包含的，PE 无需装 .NET。

> 若 PE 的 `%TEMP%` 是内存盘（多数 PE 都是），日志重启就没了——
> 想留存就把 `config.ini` 的 `[Log] Dir` 指到一个真实盘。

## 配置：`config.ini`

```ini
[General]
Language=zh-CN          ; 界面语言，对应 lang\<名字>.ini

[Tools]
PreferBundled=1         ; 1=一律用内置 tools\（推荐，PE 实测更稳）
                        ; 0=先探系统自带 %SystemRoot%\System32，缺失才回退内置

[Log]
CodePage=936            ; 捕获工具输出所用代码页，中文乱码时改（936=GBK，65001=UTF-8）
Dir=                    ; 日志目录；留空=自动（程序目录\logs → %TEMP%\eps-logs）

[Esp]
PreferredLetter=        ; 分配盘符时先试的字母；留空=从 Z 往前自动找
```

界面文字**不在这里**，全在 `lang\*.ini`；`config.ini` 只管程序行为。

## 退出码与排错

修复失败时，日志里会出现 `命令执行失败，退出码 <n>`。常见值：

| 退出码 | 含义 | 怎么办 |
|---|---|---|
| `193` (0xC1) | `ERROR_BAD_EXE_FORMAT`——`bcdboot` 读不到源 | 确认所选的"系统分区"里真的有 Windows（含 `Windows\Boot\EFI\bootmgfw.efi`） |
| `-2146885629` (0x80092003) | `CRYPT_E_FILE_ERROR`——同上，源缺引导文件 | 同上；再检查 ESP 能否正常读写 |
| `31` | BCD 字符串 MUI 加载失败 | **源** Windows 缺 `System32\bootstr.dll` 及其语言 `.mui` |
| 其它 | 看日志里紧跟的一行原文 | —— |

其它常见问题：

- **按钮是灰的**：多半是没选分区、所选盘上没有 ESP、或内置工具缺失——警告栏会写明原因。
- **中文乱码**：改 `[Log] CodePage`。
- **不是管理员**：程序会提示并自我提权，允许即可。

## 日志

- 界面右下有「打开日志目录」按钮。
- 落盘位置：`config.ini [Log] Dir` → 否则 `<exe>\logs\` → 否则 `%TEMP%\eps-logs`。
- PE 的 `%TEMP%` 是内存盘，重启即失；要留证就写死一个真盘路径。

## 项目结构

```
src/
├─ Program.cs           入口：设错误模式 → 载语言 → 提权检查 → 主窗口
├─ MainForm.cs          全部界面 + 状态机（枚举→选分区→更新警告/启用）
├─ Repair.cs            核心：那条 bcdboot 调用 + 盘符分配
├─ Runner.cs            起子进程、逐行泵出并嗅探输出编码
├─ Volumes.cs           纯 Win32 分区/卷枚举（无 WMI）
├─ Tools.cs             按架构选并解析要用哪个 exe
├─ AppConfig.cs / I18n.cs / CodePageEncoding.cs / Theme.cs   叶子类
lang/                   语言包（zh-CN / en-US，键一一对应）
tools/SOURCE.txt        内置工具来源登记表（含 md5）
docs/方案.md            最初的设计方案（部分已过时，以代码为准）
```

## 分支

| 分支 | 内容 |
|---|---|
| **`master`**（默认） | UEFI / GPT 版：一键修复 **ESP**。即本文档描述的全部功能。 |
| `mbr` | 在 master 基础上增加 **MBR / Legacy BIOS 引导修复**与**「高级…」手动指定源/目标**。尚未并入 master。 |

## 许可与免责

- 本仓库**当前未附开源许可证**（无 `LICENSE` 文件）。
- `tools\` 下的 **`bcdboot.exe` / `diskpart.exe` 版权归 Microsoft**，受其许可约束，
  **不可再分发**，故未包含在本仓库中；使用者需自行从其系统或官方镜像取得。
- **本工具会写你的磁盘。** 修复动作会向所选 ESP 写入引导文件，可能影响现有启动项。
  请**务必先确认选中的是正确分区**——尤其当机器上有多块盘、多个 ESP 时。
  作者不对因误操作造成的数据丢失或启动损坏负责。

## 相关文档

- [`docs/方案.md`](docs/方案.md) —— 最初的完整设计方案（部分内容已被实现取代）。
- [`tools/SOURCE.txt`](tools/SOURCE.txt) —— 内置工具来源与 md5 登记表。
