# TD Bank v0.1.4 LTS — Complete Balance Data

This document publishes every gameplay number and random probability used by TD Bank v0.1.4 LTS. English appears first; the complete Chinese version follows below.

## English

### Savings

Savings interest is paid at the start of each new real map floor. It is compound interest, rounded down to whole Gold, and capped per floor.

| Ascension | Rate | Per-floor cap |
|---|---:|---:|
| A0–A1 | 1.5% | 10G |
| A2–A3 | 1.25% | 8G |
| A4–A5 | 1% | 7G |
| A6–A7 | 0.75% | 6G |
| A8–A10 | 0.5% | 5G |

Savings interest actually paid becomes part of the balance, compounds on later floors, repays active credit debt first, and counts toward credit-card qualification.

### Credit-card qualification

Qualification starts at 0G when the account is opened. Only native-game Gold obtained after opening and savings interest actually paid count. Examples of native-game Gold include combat rewards, events, treasure, and relic effects that grant Gold. e-Transfer receipts, KK Compound proceeds, and credit advances do not count.

There is no random approval chance: once the displayed threshold is reached, the application succeeds.

| Ascension | Starter requirement | Middle requirement | Tycoon requirement |
|---|---:|---:|---:|
| A0 | 150G | 600G | 1,600G |
| A1 | 175G | 650G | 1,700G |
| A2 | 200G | 700G | 1,800G |
| A3 | 225G | 800G | 2,000G |
| A4 | 250G | 900G | 2,200G |
| A5 | 275G | 1,000G | 2,400G |
| A6 | 300G | 1,100G | 2,600G |
| A7 | 325G | 1,200G | 2,800G |
| A8 | 350G | 1,300G | 3,000G |
| A9 | 375G | 1,400G | 3,200G |
| A10 | 400G | 1,500G | 3,500G |

### Credit limits, maximum debt, grace period, and rates

The credit limit is the extra amount a single supported purchase may use. Maximum debt is the hard ceiling across purchases and compounded interest.

| Ascension | Limits: Starter / Middle / Tycoon | Maximum debt: Starter / Middle / Tycoon | One-time grace period | Per-floor rates: Starter / Middle / Tycoon |
|---|---|---|---:|---|
| A0–A1 | 200 / 700 / 1,200G | 400 / 1,400 / 2,400G | 3 floors | 21.99% / 24.99% / 27.99% |
| A2 | 200 / 650 / 1,100G | 380 / 1,235 / 2,090G | 3 floors | 22.99% / 25.99% / 28.99% |
| A3 | 200 / 650 / 1,100G | 380 / 1,235 / 2,090G | 2 floors | 22.99% / 25.99% / 28.99% |
| A4–A5 | 200 / 600 / 1,000G | 360 / 1,080 / 1,800G | 2 floors | 23.99% / 26.99% / 29.99% |
| A6–A7 | 175 / 550 / 900G | 297 / 935 / 1,530G | 1 floor | 24.99% / 27.99% / 30.99% |
| A8 | 150 / 500 / 800G | 240 / 800 / 1,280G | 1 floor | 25.99% / 28.99% / 31.99% |
| A9–A10 | 150 / 450 / 750G | 225 / 675 / 1,125G | 1 floor | 26.99% / 29.99% / 32.99% |

The grace period is granted only the first time debt appears in a run. Paying the debt off and borrowing again does not grant another grace period. After grace ends, interest is charged at the end of each completed map floor, compounded, and rounded up to whole Gold.

All incoming Gold automatically repays debt first. Native game events that remove Gold cannot create credit debt. Supported purchases may use credit automatically.

### Maxed-out card and relic liquidation

When debt reaches the maximum-debt ceiling, TD Bank:

1. Takes all positive Gold toward the debt.
2. Clears the remaining debt without leaving the player on negative Gold.
3. Permanently closes the credit card for that run.
4. Randomly seizes safely removable relics for the remaining settlement amount.

Relic count is **1 relic per 100G of remaining debt**, rounded to the nearest whole relic with halves rounded upward, minimum 1, and no maximum cap. Examples: 1–149G requests 1 relic; 150–249G requests 2; 250–349G requests 3.

Only tradable relics using the game's default safe removal behavior are eligible. Selection is randomized from the eligible pool. If fewer eligible relics exist than requested, every eligible relic is taken and settlement still completes.

### e-Transfer

The sender chooses a teammate and an amount. The amount leaves the sender's Gold and reaches the selected teammate. A recipient's active credit debt is repaid first. e-Transfer receipts do not count toward credit qualification. There is no fee and no random failure chance in the bank rules.

### Kidney sales

- Cost per kidney: **10 current HP and 10 maximum HP**.
- Proceeds per kidney: **200G**.
- Multiple kidneys may be sold at once, but both current and maximum HP must remain above 0.
- Proceeds repay credit debt first and do not count toward credit qualification.
- Kidney sales are disabled during combat.
- There is no random event chance.

### Butt sales

Butt sales reduce current HP only and never reduce maximum HP. Current HP must remain above 0. Proceeds repay credit debt first and do not count toward credit qualification. Butt sales are disabled during combat.

| Sale number | Normal HP cost | Normal payout |
|---|---:|---:|
| 1–3 | 5 HP | 50G |
| 4–6 | 8 HP | 30G |
| 7–9 | 12 HP | 17G |
| 10 | 17 HP | 10G |
| 11 and later | 19 HP on sale 11, then +2 HP each sale | 10G |

From sale 4 onward, Rear-End Risk Control makes one deterministic run-seeded roll from 0 to 99. Multiplayer uses the host-authoritative result.

- **Unpaid:** normal HP cost is deducted, payout is 0G.
- **Hemorrhage:** double the normal HP cost is deducted, normal payout is received.
- **Normal:** normal HP cost and normal payout.

#### Sale 4 probabilities

| Ascension | Unpaid | Hemorrhage | Normal |
|---|---:|---:|---:|
| A0–A2 | 20% | 10% | 70% |
| A3–A4 | 20% | 9% | 71% |
| A5–A6 | 20% | 8% | 72% |
| A7–A8 | 20% | 7% | 73% |
| A9–A10 | 20% | 5% | 75% |

#### Sale 5 probabilities

| Ascension | Unpaid | Hemorrhage | Normal |
|---|---:|---:|---:|
| A0–A2 | 30% | 15% | 55% |
| A3–A4 | 30% | 13% | 57% |
| A5–A6 | 30% | 11% | 59% |
| A7–A8 | 30% | 10% | 60% |
| A9–A10 | 30% | 8% | 62% |

#### Sale 6 probabilities

| Ascension | Unpaid | Hemorrhage | Normal |
|---|---:|---:|---:|
| A0–A2 | 40% | 20% | 40% |
| A3–A4 | 40% | 17% | 43% |
| A5–A6 | 40% | 15% | 45% |
| A7–A8 | 40% | 13% | 47% |
| A9–A10 | 40% | 10% | 50% |

#### Sale 7 and every later sale

| Ascension | Unpaid | Hemorrhage | Normal |
|---|---:|---:|---:|
| A0–A2 | 50% | 25% | 25% |
| A3–A4 | 50% | 21% | 29% |
| A5–A6 | 50% | 19% | 31% |
| A7–A8 | 50% | 16% | 34% |
| A9–A10 | 50% | 13% | 37% |

---

## 中文

### 储蓄利息

每进入一个新的真实地图层时发放复利，向下取整为整数金币，并受每层上限限制。

| 进阶 | 利率 | 每层上限 |
|---|---:|---:|
| A0–A1 | 1.5% | 10G |
| A2–A3 | 1.25% | 8G |
| A4–A5 | 1% | 7G |
| A6–A7 | 0.75% | 6G |
| A8–A10 | 0.5% | 5G |

实际发放的利息会进入余额、参与之后的复利；有信用卡欠款时先自动还债，并计入信用卡办卡累计金额。

### 信用卡办卡要求

开户时办卡累计金额从 0G 开始。只有开户后游戏原生获得的金币和实际发放的储蓄利息计入。原生金币包括战斗奖励、事件、藏宝和增加金币的圣遗物效果等。e-Transfer 收款、KK 园区收入和信用卡垫付不计入。

办卡没有随机审批概率：达到页面显示的累计门槛就一定成功。

| 进阶 | 穷逼卡要求 | 中产卡要求 | 暴发户卡要求 |
|---|---:|---:|---:|
| A0 | 150G | 600G | 1,600G |
| A1 | 175G | 650G | 1,700G |
| A2 | 200G | 700G | 1,800G |
| A3 | 225G | 800G | 2,000G |
| A4 | 250G | 900G | 2,200G |
| A5 | 275G | 1,000G | 2,400G |
| A6 | 300G | 1,100G | 2,600G |
| A7 | 325G | 1,200G | 2,800G |
| A8 | 350G | 1,300G | 3,000G |
| A9 | 375G | 1,400G | 3,200G |
| A10 | 400G | 1,500G | 3,500G |

### 信用额度、最大欠款、免息期和利率

信用额度是一笔受支持消费最多可以额外刷出的金额；最大欠款额度是多次消费与复利累计后的硬上限。

| 进阶 | 额度：穷逼 / 中产 / 暴发户 | 最大欠款：穷逼 / 中产 / 暴发户 | 整局一次免息期 | 每层利率：穷逼 / 中产 / 暴发户 |
|---|---|---|---:|---|
| A0–A1 | 200 / 700 / 1,200G | 400 / 1,400 / 2,400G | 3 层 | 21.99% / 24.99% / 27.99% |
| A2 | 200 / 650 / 1,100G | 380 / 1,235 / 2,090G | 3 层 | 22.99% / 25.99% / 28.99% |
| A3 | 200 / 650 / 1,100G | 380 / 1,235 / 2,090G | 2 层 | 22.99% / 25.99% / 28.99% |
| A4–A5 | 200 / 600 / 1,000G | 360 / 1,080 / 1,800G | 2 层 | 23.99% / 26.99% / 29.99% |
| A6–A7 | 175 / 550 / 900G | 297 / 935 / 1,530G | 1 层 | 24.99% / 27.99% / 30.99% |
| A8 | 150 / 500 / 800G | 240 / 800 / 1,280G | 1 层 | 25.99% / 28.99% / 31.99% |
| A9–A10 | 150 / 450 / 750G | 225 / 675 / 1,125G | 1 层 | 26.99% / 29.99% / 32.99% |

每局第一次产生欠款时才获得免息期；还清后再次欠款不会再免息。免息结束后，每完成一层结算一次信用卡复利，向上取整为整数金币。

任何金币入账都会先自动还债。游戏原生事件造成的金币损失不会制造信用卡欠款；受支持的消费可以自动调用信用卡。

### 刷爆与圣遗物清算

欠款达到最大欠款额度时，TD 会：

1. 先拿走全部正金币抵债。
2. 清除剩余欠款，不把游戏金币留成负数。
3. 本局永久关闭信用卡。
4. 按剩余清算金额随机没收能够安全移除的圣遗物。

没收数量为**每 100G 剩余欠款 1 件圣遗物**，四舍五入到整数，0.5 向上取整，至少 1 件，不封顶。例如：1–149G 没收 1 件，150–249G 没收 2 件，250–349G 没收 3 件。

只有可交易且使用游戏默认安全移除逻辑的圣遗物会进入抽取池，再从合格池中随机选择。如果合格圣遗物不够，就全部没收，之后仍视为完成清算。

### e-Transfer

付款人选择队友和金额；金币从付款人扣除并进入指定队友账户。收款人有信用卡欠款时先自动还债。e-Transfer 收款不计入办卡累计金额。银行规则中没有手续费，也没有随机失败概率。

### 卖肾

- 每个肾扣 **10 当前生命和 10 最大生命**。
- 每个肾获得 **200G**。
- 可以一次卖多个，但当前生命和最大生命都必须保持大于 0。
- 收入先自动还债，不计入办卡累计金额。
- 战斗中禁止卖肾。
- 没有随机事件概率。

### 卖屁股

卖屁股只扣当前生命，永远不扣最大生命；当前生命必须保持大于 0。收入先自动还债，不计入办卡累计金额。战斗中禁止卖屁股。

| 第几次卖 | 正常扣血 | 正常收入 |
|---|---:|---:|
| 第 1–3 次 | 5 HP | 50G |
| 第 4–6 次 | 8 HP | 30G |
| 第 7–9 次 | 12 HP | 17G |
| 第 10 次 | 17 HP | 10G |
| 第 11 次及以后 | 第 11 次 19 HP，此后每次再加 2 HP | 10G |

从第 4 次起，“菊部风控”会根据本局种子进行一次 0–99 的确定性随机。多人游戏采用主机权威结果。

- **被白嫖：**扣正常生命，获得 0G。
- **大出血：**扣双倍正常生命，获得正常收入。
- **正常：**扣正常生命，获得正常收入。

#### 第 4 次概率

| 进阶 | 被白嫖 | 大出血 | 正常 |
|---|---:|---:|---:|
| A0–A2 | 20% | 10% | 70% |
| A3–A4 | 20% | 9% | 71% |
| A5–A6 | 20% | 8% | 72% |
| A7–A8 | 20% | 7% | 73% |
| A9–A10 | 20% | 5% | 75% |

#### 第 5 次概率

| 进阶 | 被白嫖 | 大出血 | 正常 |
|---|---:|---:|---:|
| A0–A2 | 30% | 15% | 55% |
| A3–A4 | 30% | 13% | 57% |
| A5–A6 | 30% | 11% | 59% |
| A7–A8 | 30% | 10% | 60% |
| A9–A10 | 30% | 8% | 62% |

#### 第 6 次概率

| 进阶 | 被白嫖 | 大出血 | 正常 |
|---|---:|---:|---:|
| A0–A2 | 40% | 20% | 40% |
| A3–A4 | 40% | 17% | 43% |
| A5–A6 | 40% | 15% | 45% |
| A7–A8 | 40% | 13% | 47% |
| A9–A10 | 40% | 10% | 50% |

#### 第 7 次及以后每一次的概率

| 进阶 | 被白嫖 | 大出血 | 正常 |
|---|---:|---:|---:|
| A0–A2 | 50% | 25% | 25% |
| A3–A4 | 50% | 21% | 29% |
| A5–A6 | 50% | 19% | 31% |
| A7–A8 | 50% | 16% | 34% |
| A9–A10 | 50% | 13% | 37% |
