# TD Bank 美术文件 / Art assets

运行时从 Mod 目录的 `Assets` 文件夹读取 Logo、背景和六张中英文信用卡图。切换游戏内语言时会立即切换卡图。图片缺失或损坏时会使用代码生成的卡面或背景，不影响银行功能。

The runtime loads a shared logo, background, and six localized card images from the installed Mod's `Assets` folder. Missing or invalid images fall back to code-generated artwork without disabling banking features.

## 文件

| 文件名 | 推荐比例 | 用途 |
| --- | ---: | --- |
| `bank_logo.png` | 1:1 | 共用银行 Logo |
| `bank_background.png` | 约 16:9 | 共用银行背景 |
| `visa_broke_zh.png` | 约 1.6:1 | 中文 Visa 穷逼 |
| `visa_middle_zh.png` | 约 1.6:1 | 中文 Visa 中产 |
| `visa_rich_zh.png` | 约 1.6:1 | 中文 Visa 暴发户 |
| `visa_broke_en.png` | 约 1.6:1 | English Visa Broke |
| `visa_middle_en.png` | 约 1.6:1 | English Visa Middle Class |
| `visa_rich_en.png` | 约 1.6:1 | English Visa Nouveau Riche |

## 要求

- 使用真实 PNG 文件。
- 六张信用卡使用相同画布比例。
- 卡图会完整等比显示，不裁切。
- 卡名、额度、利率、进度和按钮由 UI 放在图片外，不会遮挡卡图。
- Logo 保留适当边距，方便在按钮与页眉中缩放。
- 替换图片后需要完全退出并重新启动游戏。

旧版现实银行卡照片不属于公开源码包，也不再被代码加载。

Unless identified otherwise, repository art is distributed under the root MIT License. Trademark rights are not granted by that license.
