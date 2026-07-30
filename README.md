<p align="center">
  <img src="./TDBank/Assets/bank_logo.png" alt="TD Bank logo" width="180">
</p>

<h1 align="center">TD Bank v0.1.2</h1>

<p align="center"><strong>Turn today’s gold into tomorrow’s financial problems.</strong></p>

<p align="center">
  <a href="#english">English</a> · <a href="#中文">中文</a>
</p>

---

# English

TD Bank is a banking mod for Slay the Spire 2 created by cnj lab.

The current release supports both Steam **default/latest v0.107.1** and **public-beta v0.109.1**. The game is still in public beta, so future updates may require the mod to be rebuilt or changed.

## Features

- Every new run requires account opening the first time the bank is used.
- Native in-game Gold is the savings balance. Capped compound interest becomes stricter with Ascension: 1.5%/10G on A0–A1, 1.25%/8G on A2–A3, 1%/7G on A4–A5, 0.75%/6G on A6–A7, and 0.5%/5G on A8–A10.
- Three rebalanced credit-card tiers, automatic debt repayment, a first-debt grace period, tiered interest rates, card closure after maxing out debt, and relic liquidation.
- Dynamic adjustments for A0–A10.
- Multiplayer e-Transfer.
- KK Compound cash-for-health services and hidden events. Butt sales keep maximum HP intact, but after the third sale their current-HP cost rises while their payout falls.
- Instant switching between English and Chinese.
- Every banking service has its own in-game rules page.

This mod contains some dark humor and may not be suitable for everyone.

Every gameplay value and random probability is published in [`BALANCE.md`](BALANCE.md), including the complete A0–A10 credit tables and Rear-End Risk Control outcome rates.

## Installation

Download `TDBank_Setup_v0.1.2.exe` from this repository’s [Releases](../../releases).

1. Fully exit Slay the Spire 2.
2. Verify the SHA-256 published on the Release page. The current Setup is self-signed by `CNJ Tower Debt`; the signature verifies file integrity but is not a commercial trust certificate.
3. Open Setup, confirm the game directory, and select Install.
4. When the game first detects the mod, personally choose **Load Mods**.

Setup installs no Windows software. It only places these two mods in the game’s `mods` folder:

- **TD Bank v0.1.2**
  Adds a bank to Slay the Spire 2 and changes gameplay.
- **TDLib v0.1**  
  TD Bank’s dedicated save and multiplayer synchronization component. It only stores bank-account data and does not replace or affect BaseLib.

TDLib installed by Setup is removed together with TD Bank. Ordinary BaseLib installations and unrelated mods are never touched.

Before installation, Setup backs up both vanilla and modded saves. It only initializes a missing or provably blank modded profile and never overwrites established progress.

Manually copying DLL files does not run these save backup and initialization safeguards.

Steam Workshop subscriptions update automatically through Steam. Setup installations check GitHub once per game session when the bank is opened; an outdated version shows a button to the latest Release and never downloads or executes files automatically.

## Uninstallation and save safety

Fully exit the game, reopen the same Setup, and select **Uninstall**.

Setup:

- Only handles `TDBank` and `TDLib` that it can prove it manages.
- Never removes an ordinary BaseLib installation or unrelated mods.
- Creates a complete save snapshot and verifies the save handoff before uninstalling.
- May temporarily launch the game through Steam when cloud-save synchronization is required.
- Preserves TD Bank, TDLib, and both save namespaces if any safety check fails.
- Never deletes vanilla saves, modded saves, or Steam Cloud saves.

These safeguards are not a replacement for your own backups. Keep a separate copy of important progress.

## TDLib

TDLib is TD Bank’s independent support component. It only stores bank-account data and related multiplayer state.

It does not replace, modify, or depend on BaseLib and can coexist with an ordinary BaseLib installation. Setup removes TDLib together with TD Bank only when Setup ownership can be proven.

Part of TDLib’s save extension code and `Sts2PathDiscovery.props` are adapted from BaseLib-StS2 v3.3.8, commit `8dfbd9367f458fbc076d341708cd93a3e336b905`, under the MIT License. See [`TDLib/THIRD_PARTY_LICENSES/BaseLib-LICENSE.txt`](TDLib/THIRD_PARTY_LICENSES/BaseLib-LICENSE.txt) for the complete notice.

## Multiplayer

Every player in the same multiplayer lobby should use identical TD Bank and TDLib versions. A version or banking-state mismatch may cause the game to disconnect intentionally to prevent multiplayer state desynchronization.

## Building from source

### Requirements

- A legally installed copy of Slay the Spire 2 Steam default/latest v0.107.1 as the minimum build baseline
- Steam public-beta v0.109.1 for full dual-branch regression testing
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Windows 10/11 x64
- NuGet network access

This repository does not contain `sts2.dll`, game source code, game artwork, or other proprietary game files. The build reads the required reference assemblies from your own local game installation.

Build TD Bank and TDLib, run the tests, and publish the complete Setup:

```powershell
.\scripts\build-release.ps1 `
  -Sts2Path "D:\SteamLibrary\steamapps\common\Slay the Spire 2"
```

Point `-Sts2Path` at Steam default/latest v0.107.1 when producing release binaries. The same binaries are then regression-tested against public-beta v0.109.1.

The default output is `artifacts/release-v0.1.2`, including Setup, licenses, third-party notices, and SHA-256 hashes.

Build only the mods:

```powershell
dotnet build .\TDBank.csproj -c Release `
  /p:Sts2Path="D:\SteamLibrary\steamapps\common\Slay the Spire 2" `
  /p:ModsPath="D:\TemporaryMods\"
```

Run only the logic tests:

```powershell
dotnet run --project .\Tests\TDBank.LogicSmokeTests.csproj -c Release `
  /p:Sts2Path="D:\SteamLibrary\steamapps\common\Slay the Spire 2" `
  /p:ModsPath="D:\TemporaryMods\"
```

Test one already-built TD Bank/TDLib pair against either supported game's assemblies:

```powershell
dotnet run --project .\CompatibilityTests\TDBank.BinaryCompatibilitySmokeTests.csproj -c Release `
  /p:Sts2Path="D:\SteamLibrary\steamapps\common\Slay the Spire 2" -- `
  "D:\CandidateMods" `
  "D:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64"
```

The release script tests installation, rollback, save protection, and uninstall handoff using TD Bank and TDLib runtime files produced by the same build. This prevents the tests or Setup from embedding stale DLL files.

## Source layout

```text
TDBankCode/                  Bank rules, game patches, UI, and multiplayer networking
TDLib/                       Independent save and multiplayer-state support
TDBank/Assets/               Logo, background, and six localized credit-card images
Installer/                   Windows installer and safe uninstaller
Installer.Tests/             Installation, rollback, save, and uninstall tests
Tests/                       Bank rules, UI, multiplayer, and compatibility tests
CompatibilityTests/          Same-binary dual-branch Harmony compatibility test
Tools/ArtworkAssetConverter/ Artwork conversion tool
scripts/                     Reproducible release scripts
```

See [`ART_ASSETS.md`](ART_ASSETS.md) for artwork details.

## License

Unless otherwise identified in the third-party notices, this project’s source code and repository assets are released under the [MIT License](LICENSE).

Third-party components remain subject to their respective licenses. See [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) for complete information.

## Disclaimer

In this project, **TD** means **Tower Debt**. It has no affiliation with any bank named TD.

This project is not endorsed, sponsored, or authorized by Mega Crit, Visa, Valve, Steam, or any bank. It is not real banking software and does not provide financial services or financial advice. All trademarks belong to their respective owners.

The project is provided “as is.” Compatibility with future public betas, every third-party mod, every multiplayer environment, or every Steam Cloud state is not guaranteed.

---

# 中文

> 把今天的金币，变成明天的财务问题。

TD Bank 是由 cnj lab 制作的《杀戮尖塔 2》银行 Mod。

当前版本同时支持 Steam **默认/latest v0.107.1** 与 **public-beta v0.109.1**。游戏仍在公测，更新后可能需要重新编译或修改 Mod。

## 功能

- 每个新局第一次使用银行时必须开户。
- 游戏原生金币就是储蓄余额。每个新地图层开始时结算有上限的复利，并随进阶收紧：A0–A1 为 1.5%/10G，A2–A3 为 1.25%/8G，A4–A5 为 1%/7G，A6–A7 为 0.75%/6G，A8–A10 为 0.5%/5G。
- 重新平衡的三档信用卡、自动还债、首次欠款免息期、分级利率、刷爆后的停卡与圣遗物清算。
- A0–A10 动态调整。
- 多人联机 e-Transfer。
- KK 园区现金换生命业务和隐藏事件。卖屁股不扣最大生命，但第 4 次起会越卖越伤、给得越少。
- 中文与 English 随时切换。
- 每项业务在游戏内都有独立规则说明。

本 Mod 可能包含一点黑色幽默内容，不适合所有玩家。

全部玩法数值和随机概率都已在 [`BALANCE.md`](BALANCE.md) 公开，包括 A0–A10 信用卡完整表格和“菊部风控”的每档结果概率。

## 安装

推荐从本仓库的 [Releases](../../releases) 下载 `TDBank_Setup_v0.1.2.exe`。

1. 完全退出《杀戮尖塔 2》。
2. 核对 Release 页面公布的 SHA-256。当前 Setup 由 `CNJ Tower Debt` 自签名；签名可以核对文件完整性，但不属于商业信任证书。
3. 打开 Setup，确认游戏目录，然后点击安装。
4. 游戏首次检测到 Mod 时，由玩家亲自选择 **Load Mods / 加载 Mod**。

本 Setup 不安装任何 Windows 软件，只把以下两个 Mod 放入游戏的 `mods` 文件夹：

- **TD Bank v0.1.2**
  给《杀戮尖塔 2》增加一个银行，它会改变游戏玩法。
- **TDLib v0.1**  
  TD Bank 专用的存档与多人同步组件。它只负责保存银行账户数据，不会替换或影响 BaseLib。

由本 Setup 安装的 TDLib 会随 TD Bank 一起卸载；普通 BaseLib 和其他 Mod 一律不动。

安装前会备份普通档和 Mod 档。Setup 只初始化缺失或确认空白的 Mod 档，已有进度绝不覆盖。

手动复制 DLL 不会运行上述存档备份和初始化保护。

Steam Workshop 订阅用户由 Steam 自动更新。Setup 安装版每次启动游戏最多检查一次 GitHub 最新版本；发现旧版时只显示最新版 Release 按钮，不会偷偷下载或执行文件。

## 卸载与存档安全

完全退出游戏后，再次打开同一个 Setup，点击 **注销账户 / Uninstall**。

Setup：

- 只处理能够证明由本 Setup 管理的 `TDBank` 和 `TDLib`。
- 不删除普通 BaseLib 或其他 Mod。
- 卸载前创建完整存档快照并核验存档交接。
- 必要时通过 Steam 临时启动游戏完成云存档同步。
- 任何安全检查失败时保留 TD Bank、TDLib 和两套存档。
- 不删除普通档、Mod 档或 Steam Cloud 存档。

这些保护不能代替玩家自己的备份。重要进度请另外保存副本。

## TDLib

TDLib 是 TD Bank 的独立支持组件，只保存银行账户数据和相关多人状态。

它不替换、不修改也不依赖 BaseLib，可以和普通 BaseLib 同时存在。Setup 只有在能够证明 TDLib 由本 Setup 安装时，才会随 TD Bank 一起卸载。

TDLib 的部分存档扩展代码及 `Sts2PathDiscovery.props` 改编自 BaseLib-StS2 v3.3.8，提交 `8dfbd9367f458fbc076d341708cd93a3e336b905`，依据 MIT License 使用。完整声明见 [`TDLib/THIRD_PARTY_LICENSES/BaseLib-LICENSE.txt`](TDLib/THIRD_PARTY_LICENSES/BaseLib-LICENSE.txt)。

## 多人联机

同一多人房间内的所有玩家应使用完全相同的 TD Bank 和 TDLib 版本。版本或银行状态不同可能导致游戏主动断开，以避免多人数据不同步。

## 从源码构建

### 要求

- 合法安装的《杀戮尖塔 2》Steam 默认/latest v0.107.1，作为最低编译基线
- Steam public-beta v0.109.1，用于完整的双分支回归测试
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Windows 10/11 x64
- NuGet 网络访问

本仓库不包含 `sts2.dll`、游戏源码、游戏美术或其他专有游戏文件。构建时会从你自己的游戏安装目录读取必要的引用程序集。

生成 TD Bank、TDLib、运行测试并发布完整 Setup：

```powershell
.\scripts\build-release.ps1 `
  -Sts2Path "D:\SteamLibrary\steamapps\common\Slay the Spire 2"
```

生成发布 DLL 时，`-Sts2Path` 必须指向 Steam 默认/latest v0.107.1；随后使用同一份 DLL 在 public-beta v0.109.1 上做回归验证。

默认输出位于 `artifacts/release-v0.1.2`，包括 Setup、许可证、第三方声明和 SHA-256。

只构建 Mod：

```powershell
dotnet build .\TDBank.csproj -c Release `
  /p:Sts2Path="D:\SteamLibrary\steamapps\common\Slay the Spire 2" `
  /p:ModsPath="D:\TemporaryMods\"
```

只运行逻辑测试：

```powershell
dotnet run --project .\Tests\TDBank.LogicSmokeTests.csproj -c Release `
  /p:Sts2Path="D:\SteamLibrary\steamapps\common\Slay the Spire 2" `
  /p:ModsPath="D:\TemporaryMods\"
```

让同一份已经编译好的 TD Bank/TDLib 在任一支持版本的游戏程序集上测试：

```powershell
dotnet run --project .\CompatibilityTests\TDBank.BinaryCompatibilitySmokeTests.csproj -c Release `
  /p:Sts2Path="D:\SteamLibrary\steamapps\common\Slay the Spire 2" -- `
  "D:\CandidateMods" `
  "D:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64"
```

发布脚本会使用同一次构建生成的 TD Bank 与 TDLib 运行文件执行安装器、回滚、存档保护和卸载交接测试，避免测试或 Setup 嵌入旧 DLL。

## 源码结构

```text
TDBankCode/                  银行规则、游戏补丁、UI 与多人网络
TDLib/                       独立存档及多人状态支持
TDBank/Assets/               Logo、背景和六张中英文信用卡图
Installer/                   Windows 安装与安全卸载器
Installer.Tests/             安装、回滚、存档及卸载测试
Tests/                       银行规则、UI、联机和兼容性测试
CompatibilityTests/          同一 DLL 的双分支 Harmony 兼容测试
Tools/ArtworkAssetConverter/ 美术转换工具
scripts/                     可复现发布脚本
```

美术文件说明见 [`ART_ASSETS.md`](ART_ASSETS.md)。

## 许可证

除第三方声明另有说明外，本项目源码与仓库素材依据 [MIT License](LICENSE) 开源。

第三方组件继续适用各自的许可证。完整信息见 [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)。

## 免责声明

本项目中的 **TD** 只代表 **Tower Debt**，保证和任何叫 TD 的银行没有关联。

本项目未获 Mega Crit、Visa、Valve、Steam 或任何银行认可、赞助或授权，不是真实银行软件，也不提供金融服务或金融建议。所有商标归各自权利人所有。

项目按“现状”提供，不保证适配未来 public beta、所有第三方 Mod、所有多人环境或所有 Steam Cloud 状态。
