using System;
using System.Collections.Generic;
using Godot;
using TDBank.TDBankCode.Banking;

namespace TDBank.TDBankCode.UI;

public enum BankUiLanguage
{
    Auto,
    SimplifiedChinese,
    English,
}

internal static class BankUiText
{
    private static readonly Dictionary<string, (string Zhs, string En)> Strings = new(StringComparer.Ordinal)
    {
        ["brand"] = ("TD Bank", "TD Bank"),
        ["brand_expanded"] = ("Tower Debt · 尖塔负债", "Tower Debt"),
        ["brand_tagline"] = ("把今天的金币，变成明天的财务问题。", "Turning today's gold into tomorrow's financial problem."),
        ["logo_tooltip"] = ("Tower Debt / 尖塔负债", "Tower Debt"),
        ["close"] = ("关闭", "Close"),
        ["language"] = ("语言", "Language"),
        ["language_tooltip"] = ("切换银行界面语言", "Switch the bank interface language"),
        ["ascension_terms"] = (
            "当前进阶 A{0}：本页已自动套用舒适补贴。",
            "A{0} comfort perks applied."),
        ["instant_approval"] = ("开户即批", "Instant approval"),
        ["unlimited"] = ("不封顶", "No cap"),
        ["fine_print"] = (
            "Tower Debt 金融集团\n本行不对任何人造成的损失负责，任何人包括本行。",
            "Tower Debt Financial Group\nNot liable for losses caused by anyone, including this bank."),
        ["account_not_open"] = ("请先申请开户并同意规则。", "Open an account first."),
        ["open_account_title"] = ("TD Bank 开户柜台", "Open TD Bank"),
        ["open_account_welcome"] = (
            "欢迎来到 TD Bank。本局第一次打开银行必须开户；开户免费，后果另算。",
            "Open once each run. It's free; consequences cost extra."),
        ["open_account_first_step"] = (
            "先点“申请开户”。读完全部规则并勾选两项确认后，账户才会开通。",
            "Tap Apply, read the rules, then tick both boxes."),
        ["open_account_apply"] = ("申请开户", "Apply"),
        ["opening_rules"] = (
            "开户规则（当前 A{0}）\n\n" +
            "1. 储蓄：{1}\n" +
            "2. 信用卡：开户后办卡累计从 0G 开始，三档资格为 {2}；额度为 {3}；最大欠款为 {4}。整局第一次欠款免息 {5} 层，此后按三档每层 {6} 复利并向上取整。商店和原生付费事件可自动刷卡；游戏事件损失金币不会制造债务。所有金币入账先还债。刷爆时先扣光现有正金币，剩余欠款由抄家结清，金币不会变负，永久停卡，并且{7}\n" +
            "3. e-Transfer 可给已开户队友转账；到账先还收款人的债，不累计办卡资格。\n" +
            "4. KK 园区：每份肾 -{8} 当前及最大生命换 {9}G；卖屁股标价为每次 -{10} 当前生命，前三次 {11}G、第 4 次起 {12}G。园区收入不累计办卡资格。",
            "Account rules (current A{0})\n\n" +
            "1. Savings: {1}\n" +
            "2. Credit: qualification starts at 0G after opening. Tier requirements are {2}; limits are {3}; maximum debts are {4}. The run's first debt gets {5} interest-free floors, then compounds by tier at {6} per completed floor, rounded up. Merchants and native paid events can charge the card automatically; gold lost to game events cannot create debt. Every deposit pays debt first. Maxing out takes all positive gold, settles the rest through foreclosure without making gold negative, closes the card forever, and {7}\n" +
            "3. e-Transfer sends gold to teammates with accounts. It repays their debt first and never counts toward qualification.\n" +
            "4. KK Compound: each kidney costs {8} current and maximum HP for {9}G. Butt sales are listed at {10} current HP for {11}G for the first three, then {12}G from sale four onward. KK proceeds never count toward qualification."),
        ["opening_credit_example"] = (
            "例：法外狂徒张三第一次欠 100G。前 {0} 个完成层利息为 0；第 {1} 层结束按最低卡 {2}% 收 {3}G，欠款变 {4}G；下一层再收 {5}G。还清再欠没有第二次免息，TD 的计算器只朝自己有利的方向取整。",
            "Example: outlaw Zhang San's first debt is 100G. The first {0} completed floors add 0 interest. At the end of floor {1}, the starter card charges {2}%: {3}G, making the debt {4}G. The next floor adds {5}G. Paying it off and borrowing again grants no second grace period; TD only rounds in TD's favor."),
        ["open_account_checkbox"] = (
            "我已读完并无条件同意以上霸王条款",
            "I have read and unconditionally accept all of the above one-sided terms."),
        ["open_account_checkbox_again"] = (
            "我再次确认：我已读完并无条件同意以上霸王条款",
            "I confirm again that I have read and unconditionally accept all of the above one-sided terms."),
        ["open_account_agree"] = ("我同意并开户", "I agree — open my account"),
        ["open_account_forced_agree"] = ("被迫同意并开户", "Forced to agree and open account"),
        ["open_account_must_accept"] = ("两项都要勾选。TD 连形式都要走两遍。", "Check both boxes. TD insists on performing the ceremony twice."),
        ["open_account_submitted"] = ("开户申请已提交，柜员正在假装审核。", "Application submitted. The teller is pretending to review it."),
        ["open_account_complete"] = ("开户成功。欢迎承担个人财务责任。", "Account opened. Welcome to personal financial responsibility."),
        ["back"] = ("返回", "Back"),
        ["savings"] = ("储蓄账户", "Savings"),
        ["credit"] = ("信用卡", "Credit Card"),
        ["etransfer"] = ("e-Transfer", "e-Transfer"),
        ["kk_tab"] = ("KK园区缅甸总部", "KK Compound"),
        ["current_balance"] = ("当前余额", "Current balance"),
        ["total_earned"] = ("本局累计合资格金币", "Qualifying gold this run"),
        ["savings_opening_terms"] = (
            "每个新地图层开始时按当前进阶发放复利并向下取整。当前 A{0} 为 {1}%，每层最多 {2}G。",
            "At the start of each new map floor, compound interest is paid for the current Ascension and rounded down. Current A{0}: {1}%, up to {2}G per floor."),
        ["savings_blurb"] = (
            "每个新地图层开始时结算一次；不是战斗回合。",
            "Settles once at the start of each new map floor, not per combat turn."),
        ["savings_rules"] = (
            "每个新地图层开始时按当前进阶发放复利，向下取整。A0–A2：5%，每层最多 50G；A3–A5：6%，每层最多 60G；A6–A8：7%，每层最多 70G；A9–A10：8%，每层最多 80G。\n当前 A{0}：{1}%，每层最多 {2}G。例：带着 100G 进入下层时预计获得 {3}G。利息加入余额继续计息；有信用卡欠款时优先自动还债，实际发放的利息计入办卡累计。",
            "At the start of each new map floor, compound interest is paid for the current Ascension and rounded down. A0–A2: 5%, up to 50G per floor; A3–A5: 6%, up to 60G; A6–A8: 7%, up to 70G; A9–A10: 8%, up to 80G.\nCurrent A{0}: {1}%, up to {2}G per floor. Example: entering the next floor with 100G is estimated to pay {3}G. Interest joins the balance and compounds later; active credit debt is repaid first, and interest actually paid counts toward card qualification."),
        ["principal"] = ("计息本金", "Interest principal"),
        ["interest_earned"] = ("已获得利息（累计）", "Interest earned (cumulative)"),
        ["interest_turns"] = ("已发息层数", "Floors paid"),
        ["interest_formula"] = ("下层预计利息按当前进阶条款计算", "Next-floor interest uses the current Ascension terms"),
        ["savings_next_interest"] = ("下层预计利息", "Estimated interest next floor"),
        ["credit_locked"] = ("您目前没有信用卡", "You do not have a credit card"),
        ["credit_locked_blurb"] = (
            "当前 A{0} 最低卡资格：{1}。累计从开户后的 0G 开始；只有原生游戏金币和储蓄利息算。审批由 TD 抠逼大 boss 负责。",
            "Current A{0} starter-card requirement: {1}. Qualification starts at 0G after opening; only native-game gold and savings interest count. Applications are reviewed by TD's stingy big boss."),
        ["credit_balance"] = ("信用卡余额", "Credit balance"),
        ["credit_debt"] = ("当前欠款", "Current debt"),
        ["credit_floor"] = ("买东西可多刷", "Extra purchasing power"),
        ["maximum_debt"] = ("最大欠款额度", "Maximum debt limit"),
        ["debt_cycle_floors"] = ("首次欠款已过层数", "Floors since first debt"),
        ["grace_remaining"] = ("剩余免息层数", "Interest-free floors left"),
        ["interest_rate"] = ("本卡每层利率", "Per-floor card rate"),
        ["tradable_relics"] = ("可被没收圣遗物", "Relics eligible for seizure"),
        ["relic_seizure_unlimited"] = (
            "欠款每 {0}G 随机没收 1 件可安全移除的圣遗物，按最近整数计算，至少 1 件且不封顶。",
            "randomly seizes 1 safely removable relic per {0}G of debt, rounded to the nearest whole relic, with a minimum of 1 and no cap."),
        ["relic_seizure_capped"] = (
            "欠款每 {0}G 随机没收 1 件可安全移除的圣遗物，按最近整数计算，至少 1 件、最多 {1} 件。",
            "randomly seizes 1 safely removable relic per {0}G of debt, rounded to the nearest whole relic, with a minimum of 1 and a cap of {1}."),
        ["next_interest"] = ("下层预计新增利息", "Estimated interest added next floor"),
        ["last_interest"] = ("上层新增利息", "Interest added last floor"),
        ["credit_rules"] = (
            "信用卡规则（当前 A{0}）\n" +
            "• 三档办卡资格：{1}；额度：{2}；最大欠款：{3}。\n" +
            "• 整局第一次欠款免息 {4} 个完成层；之后三档每层复利为 {5}，每次向上取整。还清再欠没有第二次免息。\n" +
            "• 余额不足时，商店和原生付费事件自动刷卡；游戏事件损失金币不会制造债务。所有金币入账先自动还债，没有手动还款。\n" +
            "• 只有开户后的原生游戏金币和储蓄利息累计办卡资格；e-Transfer、KK 园区和信用垫款不算。\n" +
            "• 刷到所持卡最大欠款时先扣光现有正金币，剩余欠款由抄家结清，金币不会变负，信用卡永久停用；随后{6}",
            "Credit rules (current A{0})\n" +
            "• Tier requirements: {1}; limits: {2}; maximum debts: {3}.\n" +
            "• The run's first debt gets {4} completed floors interest-free. After that, tier rates are {5} per floor, compounded and rounded up. Paying it off and borrowing again grants no second grace period.\n" +
            "• If savings are short, merchants and native paid events charge the card automatically. Gold lost to game events cannot create debt. Every deposit repays debt first; there is no manual payment.\n" +
            "• Only native-game gold and savings interest earned after opening count toward qualification; e-Transfer, KK, and credit advances do not.\n" +
            "• Reaching the active card's maximum debt first takes all positive gold; foreclosure settles the rest without making gold negative, and the card closes forever; then {6}"),
        ["credit_interest_example"] = (
            "张三本局第一次欠 100G：前 {0} 个完成层利息为 0。第 {1} 层结束，最低卡按 {2}% 向上取整收 {3}G，欠款变 {4}G；下一层再收 {5}G。TD 的计算器只朝自己有利的方向取整。",
            "Zhang San's first debt is 100G: the first {0} completed floors add 0 interest. At floor {1}'s end, the starter card charges {2}%, rounded up to {3}G, making {4}G debt; the next floor adds {5}G. TD only rounds in TD's favor."),
        ["automatic_repayment"] = (
            "当前有欠款：从现在起每一笔金币入账都会先自动抵债，直到欠款为 0。没有手动还款按钮，TD 看见钱会自己拿。",
            "Debt is active: every incoming gold payment automatically pays it first until debt reaches zero. There is no manual payment button; TD takes the money on sight."),
        ["paid_off"] = ("信用良好到令人怀疑。当前无欠款。", "Suspiciously responsible. No debt is currently owed."),
        ["bankrupt_credit"] = (
            "你破产了，你的蚂蚁信用分现在连共享单车都扫不起还想用信用卡？",
            "You're bankrupt. Your Sesame Credit score cannot even unlock a shared bike, and you still want a credit card?"),
        ["credit_closed_cap"] = (
            "你把信用卡刷爆了，TD 决定清算你全部财产并且抄了你的家。TD 先扣光现有正金币，剩余欠款由抄家结清，金币不会变负，信用卡永久停用；当前 A{0} 的抄家规则为：{1}你的蚂蚁信用分现在连共享单车都扫不起，还想用信用卡？",
            "You maxed out the card, so TD liquidated your assets and raided your home. TD first takes all positive gold; foreclosure settles the rest without making gold negative, and the card closes forever. Current A{0} seizure terms: {1} Your Sesame Credit score cannot unlock a shared bike, and you still want credit?"),
        ["apply_title"] = ("选择您的财务困境", "Choose your financial predicament"),
        ["apply_disclaimer"] = ("点击申请即代表您已阅读并无条件同意8600页霸王条款。", "By applying, you confirm that you read and unconditionally accept all 8,600 pages of take-it-or-leave-it terms."),
        ["apply"] = ("申请", "Apply"),
        ["upgrade"] = ("升级", "Upgrade"),
        ["current_card"] = ("当前卡片", "Current card"),
        ["eligible"] = ("符合资格", "Eligible"),
        ["not_eligible"] = ("还差 {0} 金币", "{0} more gold required"),
        ["tier_starter"] = ("Visa 穷逼", "Visa Broke"),
        ["tier_middle"] = ("Visa 中产", "Visa Middle-Class"),
        ["tier_rich"] = ("Visa 暴发户", "Visa Nouveau Riche"),
        ["tier_starter_joke"] = ("适合吊毛穷逼客户，没钱就不要唧唧歪歪了有什么拿什么。", "For broke-ass customers. If you have no money, quit whining and take what you can get."),
        ["tier_middle_joke"] = ("看起来体面，嗯看起来，但是你真的体面吗？", "Looks respectable—well, looks it. But are you actually respectable?"),
        ["tier_rich_joke"] = ("妈妈我终于当上CEO迎娶白富美走上人生巅峰辣！", "Mom, I finally became a CEO, married rich and beautiful, and reached the peak of life!"),
        ["qualification"] = ("资格进度", "Qualification"),
        ["qualification_instant"] = ("资格：开户即批", "Qualification: instant approval"),
        ["limit"] = ("额度", "Limit"),
        ["first_grace"] = ("首次免息", "First-debt grace"),
        ["amount"] = ("金额", "Amount"),
        ["etransfer_title"] = ("给队友发 e-Transfer", "Send a teammate an e-Transfer"),
        ["etransfer_rules"] = (
            "只能选择在线且已经开户的队友，再输入正整数金额；金币从您的余额转给对方。对方如果欠信用卡，到账金币会先自动还对方的债，剩下的才进入对方余额。转账不会替任何人累计信用卡申请资格。",
            "Choose an online teammate who has opened a TD account, then enter a positive whole amount. Gold moves from your balance to theirs. If the recipient has credit debt, the deposit automatically pays that debt first and only the remainder reaches their balance. Transfers do not count toward anyone's card qualification."),
        ["etransfer_blurb"] = ("110提醒您小心缅北电信诈骗和仙人跳。", "Police hotline 110 reminds you to beware of telecom scams and honey traps."),
        ["recipient"] = ("收款队友", "Recipient"),
        ["send"] = ("发送 e-Transfer", "Send e-Transfer"),
        ["no_teammates"] = ("没有可转账的队友", "No eligible teammates"),
        ["no_teammates_hint"] = ("单人模式里，向自己转账在会计上叫“什么也没做”。", "In single-player, sending yourself money is professionally known as doing nothing."),
        ["kk_title"] = ("KK园区缅甸总部", "KK Compound — Myanmar Headquarters"),
        ["kk_tagline"] = ("走投无路了嘛兄弟？专业团队助您翻身！", "Out of options, brother? Our professional team will turn your life around!"),
        ["kk_rules"] = (
            "当前 A{0} 园区价目：每份肾 -{1} 当前及最大生命，换 {2}G；卖屁股标价为每次 -{3} 当前生命，前三次 {4}G，第 4 次起 {5}G。生命扣完必须仍大于 0。园区扣血只改真实血条，不计入战斗受伤/失血记录，也不会立即触发卡牌、能力或圣遗物；园区金币不触发原生“获得金币”效果。扣完后的血量和金币仍是真实数值，后续游戏照常读取。园区收入不算办卡资格；拔网线也接不回肾。",
            "Current A{0} price list: each kidney costs {1} current and maximum HP for {2}G. Butt sales are listed at {3} current HP for {4}G for the first three sales, then {5}G from sale four onward. HP must remain above 0. KK deductions change the real HP bar but do not enter combat damage/HP-loss history or immediately trigger cards, powers, or relics; KK gold does not trigger native “gain Gold” effects. The resulting HP and Gold remain real values that later game rules can read normally. KK income does not count toward qualification; unplugging cannot restore a kidney."),
        ["current_hp"] = ("当前生命", "Current HP"),
        ["max_hp"] = ("最大生命", "Maximum HP"),
        ["hp_value"] = ("{0} HP", "{0} HP"),
        ["safe_kidney_quantity"] = ("现在最多能卖", "Maximum safe kidney sales"),
        ["butt_sales"] = ("本局卖屁股次数", "Butt sales this run"),
        ["sell_kidney"] = ("卖肾", "Sell kidneys"),
        ["kidney_rules"] = (
            "每卖 1 份：当前生命 -{0}、最大生命 -{0}，换 {1}G。例：50/70 HP 卖 1 份后变 {2}/{3} HP。可输入数量；扣完两种生命都必须大于 0。",
            "Each sale removes {0} current HP and {0} maximum HP for {1}G. Example: 50/70 HP becomes {2}/{3} HP after one sale. Choose a quantity; both HP values must remain above 0."),
        ["kidney_quantity"] = ("卖几个（只能正整数）", "Quantity to sell (positive whole numbers only)"),
        ["quantity_placeholder"] = ("输入正整数数量", "Enter a positive whole quantity"),
        ["kidney_safe_now"] = ("按当前生命，最多还能安全卖 {0} 个。", "With your current health, at most {0} can be sold safely."),
        ["sell_kidney_button"] = ("卖肾获得 {0}G", "Sell kidneys for {0}G"),
        ["kidney_fatal"] = ("再扣就暴毙了。园区只收肾，不收尸体。", "One more deduction would kill you. The compound buys kidneys, not corpses."),
        ["sell_butt"] = ("卖屁股", "Sell your butt"),
        ["butt_risk_hint"] = ("卖多了可能触发", "Repeated sales may trigger"),
        ["butt_risk_link"] = ("菊部风控", "Rear-End Risk Control"),
        ["butt_risk_tooltip"] = ("查看菊部风控说明", "Learn about Rear-End Risk Control"),
        ["butt_risk_title"] = ("菊部风控", "Rear-End Risk Control"),
        ["butt_risk_explanation"] = (
            "卖多了可能触发特殊事件。\n黑市有风险，卖屁股需谨慎。",
            "Repeated sales may trigger special events.\nBlack markets are risky. Sell your butt with caution."),
        ["butt_risk_freeloader_title"] = ("被白嫖辣！", "You got stiffed!"),
        ["butt_risk_freeloader"] = (
            "嫖客没给钱，偷偷从30楼跳窗逃跑了。\n\n-{0} HP　+0G",
            "The customer skipped payment and escaped through a 30th-floor window.\n\n-{0} HP  +0G"),
        ["butt_risk_hemorrhage_title"] = ("坏了，大出血！", "Oh no—massive bleeding!"),
        ["butt_risk_hemorrhage"] = (
            "这次这个嫖客有点大，您正在经历大出血！\n\n-{0} HP　+{1}G\n其中 {2}G 自动还债，{3}G 进入余额。",
            "This customer was a bit much. You are experiencing massive bleeding!\n\n-{0} HP  +{1}G\n{2}G repaid debt and {3}G entered the balance."),
        ["butt_rules"] = (
            "标价：当前生命 -{0}，最大生命不变；前三次 {1}G，第 4 次起 {2}G。扣完生命必须大于 0。熟客价听起来更贵，其实贵的是尊严。",
            "Listed price: {0} current HP, maximum HP unchanged, for {1}G on the first three sales and {2}G from sale four onward. HP must remain above 0. The loyalty rate sounds expensive; dignity costs extra."),
        ["sell_butt_button"] = ("卖屁股：-{0} HP / +{1}G", "Sell your butt: -{0} HP / +{1}G"),
        ["sell_butt_button_repeat"] = (
            "第 {0} 次卖屁股：-{1} HP / +{2}G",
            "Butt sale #{0}: -{1} HP / +{2}G"),
        ["butt_fatal"] = ("再卖就暴毙了。客户死了影响园区复购率。", "One more sale would kill you. Dead customers hurt repeat business."),
        ["organ_sales_combat_disabled"] = (
            "战斗中暂停器官交易。打完再卖，园区不收带战斗特效的货。",
            "Organ sales are disabled during combat. Finish the fight before selling."),
        ["butt_repeat_page_warning"] = (
            "怎么又是你，小心老了被护工骂。",
            "You again? Be careful or your caregiver will scold you when you're old."),
        ["amount_placeholder"] = ("输入正整数", "Enter a positive whole number"),
        ["invalid_amount"] = ("金额必须是大于 0 的整数。", "Amount must be a whole number greater than zero."),
        ["request_sent"] = ("请求已提交，正在数金币……", "Request submitted; counting the gold now…"),
        ["no_handler"] = ("银行柜台还没接线：未注册此操作的处理程序。", "The teller window is not wired yet: no handler is registered for this action."),
        ["unavailable"] = ("银行服务暂不可用", "Banking is currently unavailable"),
        ["default_unavailable"] = ("请先开始一局游戏。大厅里不办理尖塔贷款。", "Start a run first. Spire loans are not available from the lobby."),
        ["status_ready"] = ("安全连接：大概吧。", "Secure connection: probably."),
        ["notification_error_title"] = ("业务出问题辣！", "Transaction problem!"),
        ["notification_dismiss"] = ("知道了", "Got it"),
        ["update_available_title"] = ("TD Bank 有新版本", "TD Bank update available"),
        ["update_available"] = (
            "当前版本 v{0}，最新版本 {1}。使用 Setup 安装的玩家请下载最新版并直接覆盖安装；存档和银行数据会保留。",
            "Installed version: v{0}. Latest version: {1}. Setup users should download the latest Setup and install it over this version; saves and bank data are preserved."),
        ["download_update"] = ("下载最新版 Setup", "Download latest Setup"),
        ["account"] = ("账户", "Account"),
        ["gold"] = ("金币", "gold"),
        ["debt"] = ("欠款", "debt"),
        ["turns_value"] = ("{0} 层", "{0} floors"),
        ["card_required"] = ("开通信用卡后才可使用信用账户。", "Open a credit card before using the credit account."),
        ["already_current"] = ("您已持有此卡。", "You already hold this card."),
        ["lower_tier"] = ("不能降级。TD 只允许财务问题变大。", "Downgrades are unavailable. TD only lets financial problems grow."),
        ["snapshot_failed"] = ("读取银行账户失败：{0}", "Could not read bank accounts: {0}"),
        ["finish_targeting_first"] = (
            "先把手里的药水或目标选择处理完，再来找 TD。",
            "Finish the potion or target selection before opening TD."),
        ["savings_interest_notice"] = (
            "TD储蓄利息：+{0}G\n当前进阶 A{1}：{2}%，本层最多 {3}G",
            "TD savings interest: +{0}G\nAscension A{1}: {2}%, up to {3}G this floor"),
        ["account_opened"] = (
            "开户成功。欢迎来到 TD，财务问题现在正式生效。",
            "Account opened. Welcome to TD; your financial problems are now official."),
        ["debt_interest_notice"] = (
            "免息期结束：信用欠款复利 +{0}G，每次都向上取整。感谢您选择负债。",
            "Grace period over: credit debt compounded by +{0}G, always rounded up. Thank you for choosing debt."),
        ["credit_ceiling_warning_title"] = (
            "TD短信",
            "TD SMS"),
        ["credit_ceiling_warning_message"] = (
            "温馨提醒您下回合不还钱就要抄家了（扣除 {0} 件圣遗物）！",
            "Friendly reminder: repay before the next floor or TD will seize your property ({0} relics)."),
        ["debt_grace_notice"] = (
            "整局唯一一次免息期仍在生效，还剩 {0} 个完成层不收利息；还清再欠没有第二次。",
            "The run's only interest-free period is active: {0} completed floor(s) remain. Paying it off and borrowing again grants no second grace period."),
        ["credit_ceiling_closed_notice"] = (
            "欠款碰到本卡上限：TD 开始清算 {0}G，先扣光现有正金币，剩余欠款由抄家结清；金币不会变负，信用卡永久停用。",
            "Debt reached this card's ceiling: TD began settling {0}G, taking positive gold first and clearing the rest through foreclosure. Gold will not go negative, and the card is permanently closed."),
        ["credit_liquidation_title"] = (
            "TD 抄家清算单",
            "TD Foreclosure Statement"),
        ["credit_liquidation_result"] = (
            "剩余欠款 {0}G 已结清。按当前进阶应没收 {1} 件圣遗物，实际没收 {2} 件。没得扣也算清算完成；当前金币不会低于 0，信用卡永久停用。",
            "The remaining {0}G debt is settled. Current Ascension terms requested {1} relic(s); TD actually seized {2}. If nothing eligible remains, settlement still completes. Gold will not fall below 0, and the card is permanently closed."),
        ["legacy_foreclosure_repaired"] = (
            "TD 已纠正旧版抄家重复扣款：返还 {0}G，当前金币恢复为 0。",
            "TD corrected a legacy double-charge from foreclosure: {0}G was restored and current gold was reset to 0."),
        ["kidney_sale_complete"] = (
            "成功卖肾 {0} 个，共获得 {1}G：{2}G 自动还债，{3}G 进入余额。身体少了，财务问题不一定少。",
            "Sold {0} kidney(s) for {1}G total: {2}G repaid debt and {3}G entered the balance. Less body; not necessarily fewer financial problems."),
        ["butt_sale_complete"] = (
            "本次卖屁股获得 {0}G：{1}G 自动还债，{2}G 进入余额。园区会计没有私吞，至少这次没有。",
            "This butt sale paid {0}G: {1}G repaid debt and {2}G entered the balance. The compound accountant did not steal it—this time."),
        ["butt_repeat_warning"] = (
            "这是本局第 {0} 次。怎么又是你，小心老了被护工骂。本次获得 {1}G：{2}G 自动还债，{3}G 进入余额。",
            "Visit #{0}. You again? Be careful or your caregiver will scold you when you're old. This sale paid {1}G: {2}G repaid debt and {3}G entered the balance."),
        ["kidney_too_weak"] = (
            "再扣就暴毙了。园区只收肾，不收尸体。",
            "One more deduction would kill you. The compound buys kidneys, not corpses."),
        ["butt_too_weak"] = (
            "再卖就暴毙了。客户死了影响园区复购率。",
            "One more sale would kill you. Dead customers hurt repeat business."),
        ["unavailable_mode"] = ("当前模式不能办业务。", "Banking is unavailable in this game mode."),
        ["invalid_teammate"] = ("无效队友。", "Invalid teammate."),
        ["amount_range"] = (
            "金额必须在 1 到 {0} 之间。",
            "Amount must be between 1 and {0}."),
        ["no_active_run"] = ("当前没有进行中的游戏。", "No active run."),
        ["pending_request"] = (
            "柜员还在数上一笔金币。",
            "The teller is still counting your previous request."),
        ["sent_to_manager"] = ("已提交给银行经理……", "Request sent to the bank manager…"),
        ["player_left"] = ("玩家已不在本局。", "Player is no longer in the run."),
        ["choose_online_teammate"] = ("请选择在线队友。", "Choose an online teammate."),
        ["unknown_operation"] = ("未知银行业务。", "Unknown banking operation."),
        ["approved"] = ("已批准，后果自负。", "Approved—against our better judgment."),
        ["etransfer_complete"] = ("e-Transfer 已到账。", "e-Transfer deposited."),
        ["etransfer_received"] = ("收到队友的 e-Transfer：{0} 金币。", "Teammate e-Transfer received: {0} gold."),
        ["done"] = ("已完成。", "Done."),
        ["error_invalid_amount"] = ("金额必须大于零。", "Amount must be greater than zero."),
        ["error_invalid_account"] = ("此账户不能用于这项业务。", "That account is not available for this operation."),
        ["error_same_account"] = ("转出和转入账户不能相同。", "Source and destination accounts must be different."),
        ["error_same_player"] = ("付款人和收款人不能是同一个人。", "Sender and recipient must be different players."),
        ["error_insufficient_funds"] = ("转出账户余额不足。", "The source account does not have enough funds."),
        ["error_credit_not_open"] = ("信用卡尚未获批。", "No credit card has been approved yet."),
        ["error_credit_limit"] = (
            "这笔交易会超过当前进阶下本卡的最大欠款。",
            "This charge would exceed this card's maximum debt under the current Ascension terms."),
        ["error_invalid_tier"] = ("这档信用卡根本不存在。", "That credit-card tier does not exist."),
        ["error_not_upgrade"] = ("申请的卡并不是升级。", "The requested card is not an upgrade."),
        ["error_not_eligible"] = ("累计金币尚未达到申请门槛。", "The cumulative gold requirement has not been met."),
        ["error_highest_tier"] = ("您已经持有最高等级信用卡。", "The highest credit-card tier is already active."),
        ["error_already_processed"] = ("本层利息已经结算。", "Interest for this floor was already processed."),
        ["error_overflow"] = ("余额太大，银行算盘处理不了。", "The balance is too large to process safely."),
        ["error_unavailable_timing"] = (
            "现在正忙着抽牌、挨打或过场；请到玩家操作阶段再来。",
            "The Spire is drawing, attacking, or changing scenes. Try again during the player play phase."),
        ["error_credit_closed"] = (
            "信用卡已因碰到当前最大欠款并被强制收款而永久关闭。",
            "Reaching the current maximum debt and forced collection permanently closed this credit card."),
        ["error_insufficient_health"] = (
            "生命不够，再扣就暴毙了。",
            "Not enough health; another deduction would kill you."),
        ["error_account_not_open"] = (
            "请先阅读规则并开户。",
            "Read the rules and open an account first."),
        ["error_account_already_open"] = (
            "账户已经开通，TD 不会重复送您两份霸王条款。",
            "The account is already open. TD will not issue a second copy of the awful terms."),
        ["error_rejected"] = ("银行拒绝了这项业务。", "The bank rejected the operation."),
    };

    public static BankUiLanguage Language { get; set; } = BankUiLanguage.Auto;

    public static bool IsChinese
    {
        get
        {
            if (Language == BankUiLanguage.SimplifiedChinese)
            {
                return true;
            }

            if (Language == BankUiLanguage.English)
            {
                return false;
            }

            string locale;
            try
            {
                locale = TranslationServer.GetLocale();
            }
            catch
            {

                return true;
            }

            return locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                || locale.StartsWith("zhs", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static string Get(string key, params object[] args)
    {
        if (!Strings.TryGetValue(key, out var pair))
        {
            return key;
        }

        var format = IsChinese ? pair.Zhs : pair.En;
        return args.Length == 0 ? format : string.Format(format, args);
    }

    public static string Tier(BankCreditTier tier)
    {
        return tier switch
        {
            BankCreditTier.Starter => Get("tier_starter"),
            BankCreditTier.MiddleClass => Get("tier_middle"),
            BankCreditTier.NouveauRiche => Get("tier_rich"),
            _ => tier.ToString(),
        };
    }

    public static string BankError(BankErrorCode error)
    {
        return error switch
        {
            BankErrorCode.None => string.Empty,
            BankErrorCode.InvalidAmount => Get("error_invalid_amount"),
            BankErrorCode.InvalidAccount => Get("error_invalid_account"),
            BankErrorCode.SameAccount => Get("error_same_account"),
            BankErrorCode.SamePlayer => Get("error_same_player"),
            BankErrorCode.InsufficientFunds => Get("error_insufficient_funds"),
            BankErrorCode.CreditCardNotOpen => Get("error_credit_not_open"),
            BankErrorCode.CreditLimitExceeded => Get("error_credit_limit"),
            BankErrorCode.InvalidCreditTier => Get("error_invalid_tier"),
            BankErrorCode.CreditTierNotUpgrade => Get("error_not_upgrade"),
            BankErrorCode.NotEligible => Get("error_not_eligible"),
            BankErrorCode.AlreadyHighestCreditTier => Get("error_highest_tier"),
            BankErrorCode.AlreadyProcessed => Get("error_already_processed"),
            BankErrorCode.ArithmeticOverflow => Get("error_overflow"),
            BankErrorCode.OperationUnavailable => Get("error_unavailable_timing"),
            BankErrorCode.CreditPermanentlyClosed => Get("error_credit_closed"),
            BankErrorCode.InsufficientHealth => Get("error_insufficient_health"),
            _ => Get("error_rejected"),
        };
    }

    public static string BankErrorToken(BankErrorCode error)
    {
        return $"bank_error:{(int)error}";
    }

    public static string NetworkError(string error)
    {
        const string bankErrorPrefix = "bank_error:";
        if (error.StartsWith(bankErrorPrefix, StringComparison.Ordinal)
            && int.TryParse(
                error.AsSpan(bankErrorPrefix.Length),
                out var rawCode)
            && Enum.IsDefined(typeof(BankErrorCode), rawCode))
        {
            return BankError((BankErrorCode)rawCode);
        }

        if (Strings.ContainsKey(error))
        {
            return Get(error);
        }

        foreach (var code in Enum.GetValues<BankErrorCode>())
        {
            if (code != BankErrorCode.None
                && string.Equals(
                    error,
                    BankService.GetErrorMessage(code),
                    StringComparison.Ordinal))
            {
                return BankError(code);
            }
        }

        return error switch
        {
            "Player is no longer in the run. / 玩家已不在本局。" => Get("player_left"),
            "Choose an online teammate. / 请选择在线队友。" => Get("choose_online_teammate"),
            "Unknown banking operation. / 未知银行业务。" => Get("unknown_operation"),
            _ => error,
        };
    }
}
