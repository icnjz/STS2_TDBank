using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using TDBank.TDBankCode.Banking;

namespace TDBank.TDBankCode.UI;

public sealed partial class BankOverlay : Control, IScreenContext
{
    private enum Tab
    {
        Savings,
        Credit,
        ETransfer,
        KkCompound,
    }

    private enum OnboardingStep
    {
        Apply,
        Rules,
    }

    private sealed record CreditCardSpec(
        BankCreditTier Tier,
        Color CardColor,
        string JokeKey);

    private static readonly CreditCardSpec[] CreditCards =
    {
        new(BankCreditTier.Starter, Color.FromHtml("#3A8F5C"), "tier_starter_joke"),
        new(BankCreditTier.MiddleClass, Color.FromHtml("#3C6E91"), "tier_middle_joke"),
        new(BankCreditTier.NouveauRiche, Color.FromHtml("#6B397D"), "tier_rich_joke"),
    };

    private readonly Dictionary<Tab, Button> _tabButtons = new();
    private BankUiSnapshot _snapshot = BankUiSnapshot.Empty;
    private Tab _activeTab;
    private OnboardingStep _onboardingStep;
    private VBoxContainer? _content;
    private Label? _status;
    private Button? _closeButton;
    private Button? _logoButton;
    private Label? _titleLabel;
    private Label? _expandedTitleLabel;
    private Label? _finePrintLabel;
    private Button? _chineseButton;
    private Button? _englishButton;

    public Control? DefaultFocusedControl => _closeButton;

    public override void _Ready()
    {
        SetFullRect(this);
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
        Hide();
        BuildShell();
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (!Visible)
        {
            return;
        }

        if (inputEvent is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }
            || inputEvent.IsActionPressed("ui_cancel"))
        {
            Close();
        }



        GetViewport().SetInputAsHandled();
    }

    public override void _ExitTree()
    {
        BankUiBridge.OnOverlayClosed();
    }

    public void Open(BankUiSnapshot snapshot)
    {
        _snapshot = snapshot;
        if (!snapshot.IsAccountOpened)
        {



            _onboardingStep = OnboardingStep.Apply;
        }
#if DEBUG
        if (MegaCrit.Sts2.Core.Helpers.CommandLineHelper.HasArg("tdbank-ui-smoke-credit"))
        {
            _activeTab = Tab.Credit;
        }
#endif
        Rebuild();
        Show();
        MoveToFront();
        BankUiBridge.OnOverlayOpened();
        _closeButton?.GrabFocus();
        ActiveScreenContext.Instance.Update();
    }

    public void Close()
    {
        Hide();
        BankUiBridge.OnOverlayClosed();
        ActiveScreenContext.Instance.Update();
    }

    public void Refresh(BankUiSnapshot snapshot)
    {
        bool enteredUnopenedRun =
            _snapshot.IsAccountOpened && !snapshot.IsAccountOpened;
        _snapshot = snapshot;
        if (enteredUnopenedRun)
        {
            _onboardingStep = OnboardingStep.Apply;
        }
        if (Visible)
        {
            Rebuild();
        }
    }

    public void Rebuild()
    {
        if (!IsNodeReady() || _content is null)
        {
            return;
        }

        RefreshChromeText();
        foreach (var (tab, button) in _tabButtons)
        {
            BankUiTheme.ApplyTabButton(button, tab == _activeTab);
            button.Text = TabLabel(tab);
            button.Disabled = !_snapshot.IsAccountOpened;
            button.TooltipText = _snapshot.IsAccountOpened
                ? string.Empty
                : BankUiText.Get("account_not_open");
            button.Modulate = _snapshot.IsAccountOpened
                ? Colors.White
                : new Color(1f, 1f, 1f, 0.48f);
        }

        ClearChildren(_content);

        if (!_snapshot.IsBankingAvailable)
        {
            BuildUnavailable(_content);
            return;
        }

        if (!_snapshot.IsAccountOpened)
        {
            BuildOnboarding(_content);
            SetStatus(BankUiText.Get("account_not_open"), false);
            return;
        }

        switch (_activeTab)
        {
            case Tab.Savings:
                BuildSavings(_content);
                break;
            case Tab.Credit:
                BuildCredit(_content);
                break;
            case Tab.ETransfer:
                BuildETransfer(_content);
                break;
            case Tab.KkCompound:
                BuildKkCompound(_content);
                break;
        }

        SetStatus(BankUiText.Get("status_ready"), false);
    }

    public void Notify(string message, bool isError)
    {
        BankUiBridge.Notify(message, isError);
    }

    private void BuildShell()
    {
        var backdrop = new Button
        {
            Name = "DimmedBackdrop",
            Flat = true,
            FocusMode = FocusModeEnum.None,
            MouseFilter = MouseFilterEnum.Stop,
        };
        SetFullRect(backdrop);
        var dim = BankUiTheme.Panel(new Color(0.01f, 0.025f, 0.02f, 0.86f), 0);
        backdrop.AddThemeStyleboxOverride("normal", dim);
        backdrop.AddThemeStyleboxOverride("hover", dim);
        backdrop.AddThemeStyleboxOverride("pressed", dim);
        backdrop.Pressed += Close;
        AddChild(backdrop);

        var frame = new PanelContainer
        {
            Name = "BankWindow",
            MouseFilter = MouseFilterEnum.Stop,
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -790,
            OffsetTop = -465,
            OffsetRight = 790,
            OffsetBottom = 465,
        };
        frame.AddThemeStyleboxOverride("panel", BankUiTheme.Panel(BankUiTheme.Cream, 24, BankUiTheme.Green, 4, 0));
        AddChild(frame);

        var backgroundTexture = BankUiAssets.Background;
        if (backgroundTexture is not null)
        {
            var background = new TextureRect
            {
                Name = "BankBackground",
                Texture = backgroundTexture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = MouseFilterEnum.Ignore,
                SelfModulate = new Color(1f, 1f, 1f, 0.22f),
            };
            frame.AddChild(background);
        }

        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 0);
        frame.AddChild(layout);

        layout.AddChild(BuildHeader());

        var body = new HBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        body.AddThemeConstantOverride("separation", 0);
        layout.AddChild(body);
        body.AddChild(BuildSidebar());

        var contentMargin = new MarginContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        contentMargin.AddThemeConstantOverride("margin_left", 36);
        contentMargin.AddThemeConstantOverride("margin_top", 28);
        contentMargin.AddThemeConstantOverride("margin_right", 36);
        contentMargin.AddThemeConstantOverride("margin_bottom", 24);
        body.AddChild(contentMargin);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
        };
        contentMargin.AddChild(scroll);

        _content = new VBoxContainer
        {
            Name = "PageContent",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
        };
        _content.AddThemeConstantOverride("separation", 18);
        scroll.AddChild(_content);

        var footer = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, 48),
        };
        footer.AddThemeStyleboxOverride("panel", BankUiTheme.Panel(BankUiTheme.GreenDeep, 0, null, 0, 10));
        _status = BankUiTheme.Label(BankUiText.Get("status_ready"), 17, BankUiTheme.GreenSoft);
        _status.VerticalAlignment = VerticalAlignment.Center;
        footer.AddChild(_status);
        layout.AddChild(footer);
    }

    private Control BuildHeader()
    {
        var header = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, 112),
        };
        header.AddThemeStyleboxOverride("panel", BankUiTheme.Panel(BankUiTheme.Green, 18, null, 0, 18));

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 20);
        header.AddChild(row);

        _logoButton = new Button
        {
            Text = "TD",
            TooltipText = BankUiText.Get("logo_tooltip"),
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(74, 74),
        };
        BankUiTheme.ApplyPrimaryButton(_logoButton);


        _logoButton.CustomMinimumSize = new Vector2(74, 74);
        var logoTexture = BankUiAssets.Logo;
        if (logoTexture is not null)
        {
            _logoButton.Text = string.Empty;
            _logoButton.Icon = logoTexture;
            _logoButton.ExpandIcon = true;
            _logoButton.IconAlignment = HorizontalAlignment.Center;
            _logoButton.VerticalIconAlignment = VerticalAlignment.Center;
        }
        _logoButton.AddThemeStyleboxOverride("normal", BankUiTheme.Panel(BankUiTheme.GreenDark, 8, Colors.White, 3, 4));
        _logoButton.AddThemeStyleboxOverride("hover", BankUiTheme.Panel(Color.FromHtml("#075F36"), 8, Colors.White, 3, 4));
        _logoButton.AddThemeFontSizeOverride("font_size", 30);
        _logoButton.Pressed += () => Notify(BankUiText.Get("brand_tagline"), false);
        row.AddChild(_logoButton);

        var titles = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        _titleLabel = BankUiTheme.Label(BankUiText.Get("brand"), 35, Colors.White);
        _expandedTitleLabel = BankUiTheme.Label(BankUiText.Get("brand_expanded"), 18, BankUiTheme.GreenSoft);
        titles.AddChild(_titleLabel);
        titles.AddChild(_expandedTitleLabel);
        row.AddChild(titles);

        var languagePicker = new VBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        languagePicker.AddThemeConstantOverride("separation", 4);
        var languageCaption = BankUiTheme.Label(
            "中文 / English",
            14,
            BankUiTheme.GreenSoft);
        languageCaption.HorizontalAlignment = HorizontalAlignment.Center;
        languagePicker.AddChild(languageCaption);

        var languageButtons = new HBoxContainer();
        languageButtons.AddThemeConstantOverride("separation", 6);
        _chineseButton = new Button
        {
            Text = "中文",
            TooltipText = BankUiText.Get("language_tooltip"),
            CustomMinimumSize = new Vector2(92, 48),
        };
        _englishButton = new Button
        {
            Text = "English",
            TooltipText = BankUiText.Get("language_tooltip"),
            CustomMinimumSize = new Vector2(108, 48),
        };
        _chineseButton.Pressed += () => BankUiBridge.Language = BankUiLanguage.SimplifiedChinese;
        _englishButton.Pressed += () => BankUiBridge.Language = BankUiLanguage.English;
        languageButtons.AddChild(_chineseButton);
        languageButtons.AddChild(_englishButton);
        languagePicker.AddChild(languageButtons);
        row.AddChild(languagePicker);

        _closeButton = new Button
        {
            Text = "×",
            TooltipText = BankUiText.Get("close"),
            CustomMinimumSize = new Vector2(62, 62),
        };
        BankUiTheme.ApplySecondaryButton(_closeButton);
        _closeButton.AddThemeFontSizeOverride("font_size", 34);
        _closeButton.Pressed += Close;
        row.AddChild(_closeButton);

        return header;
    }

    private Control BuildSidebar()
    {
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(278, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        panel.AddThemeStyleboxOverride("panel", BankUiTheme.Panel(BankUiTheme.GreenDeep, 0, null, 0, 22));

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        panel.AddChild(column);

        AddTabButton(column, Tab.Savings);
        AddTabButton(column, Tab.Credit);
        AddTabButton(column, Tab.ETransfer);
        AddTabButton(column, Tab.KkCompound);

        var spacer = new Control
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddChild(spacer);

        _finePrintLabel = BankUiTheme.Label(
            BankUiText.Get("fine_print"),
            15,
            Color.FromHtml("#9EC9B4"));
        _finePrintLabel.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(_finePrintLabel);
        return panel;
    }

    private void RefreshChromeText()
    {
        if (_logoButton is not null)
        {
            _logoButton.TooltipText = BankUiText.Get("logo_tooltip");
        }
        if (_titleLabel is not null)
        {
            _titleLabel.Text = BankUiText.Get("brand");
        }
        if (_expandedTitleLabel is not null)
        {
            _expandedTitleLabel.Text = BankUiText.Get("brand_expanded");
        }
        if (_finePrintLabel is not null)
        {
            _finePrintLabel.Text = BankUiText.Get("fine_print");
        }
        if (_closeButton is not null)
        {
            _closeButton.TooltipText = BankUiText.Get("close");
        }
        if (_chineseButton is not null)
        {
            _chineseButton.TooltipText = BankUiText.Get("language_tooltip");
            ApplyLanguageButton(_chineseButton, BankUiText.IsChinese);
        }
        if (_englishButton is not null)
        {
            _englishButton.TooltipText = BankUiText.Get("language_tooltip");
            ApplyLanguageButton(_englishButton, !BankUiText.IsChinese);
        }
    }

    private static void ApplyLanguageButton(Button button, bool selected)
    {
        var normal = selected ? BankUiTheme.Cream : BankUiTheme.GreenDark;
        var hover = selected ? Colors.White : Color.FromHtml("#075F36");
        var text = selected ? BankUiTheme.GreenDark : Colors.White;
        button.AddThemeStyleboxOverride(
            "normal",
            BankUiTheme.Panel(normal, 9, BankUiTheme.GreenSoft, selected ? 3 : 1, 8));
        button.AddThemeStyleboxOverride(
            "hover",
            BankUiTheme.Panel(hover, 9, Colors.White, 2, 8));
        button.AddThemeStyleboxOverride(
            "pressed",
            BankUiTheme.Panel(BankUiTheme.GreenSoft, 9, Colors.White, 2, 8));
        button.AddThemeColorOverride("font_color", text);
        button.AddThemeColorOverride("font_hover_color", selected ? BankUiTheme.GreenDark : Colors.White);
        button.AddThemeColorOverride("font_pressed_color", BankUiTheme.GreenDark);
        button.AddThemeFontSizeOverride("font_size", 17);
    }

    private void AddTabButton(VBoxContainer column, Tab tab)
    {
        var button = new Button
        {
            Text = TabLabel(tab),
            FocusMode = FocusModeEnum.All,
        };
        BankUiTheme.ApplyTabButton(button, tab == _activeTab);
        button.Pressed += () =>
        {
            _activeTab = tab;
            Rebuild();
        };
        _tabButtons[tab] = button;
        column.AddChild(button);
    }

    private void BuildUnavailable(VBoxContainer page)
    {
        page.AddChild(BankUiTheme.Heading(BankUiText.Get("unavailable")));
        page.AddChild(InfoPanel(
            _snapshot.UnavailableReason ?? BankUiText.Get("default_unavailable"),
            BankUiTheme.Red,
            new Color(1f, 0.91f, 0.91f)));
        SetStatus(_snapshot.UnavailableReason ?? BankUiText.Get("default_unavailable"), true);
    }

    private void BuildOnboarding(VBoxContainer page)
    {
        page.AddChild(BankUiTheme.Heading(BankUiText.Get("open_account_title")));
        var tagline = BankUiTheme.Label(
            BankUiText.Get("brand_tagline"),
            20,
            BankUiTheme.GreenDark);
        tagline.HorizontalAlignment = HorizontalAlignment.Center;
        page.AddChild(tagline);

        if (_onboardingStep == OnboardingStep.Apply)
        {
            page.AddChild(InfoPanel(
                BankUiText.Get("open_account_welcome"),
                BankUiTheme.Green,
                BankUiTheme.GreenSoft));
            page.AddChild(BankUiTheme.Label(
                BankUiText.Get(
                    "ascension_terms",
                    _snapshot.AscensionLevel),
                18,
                BankUiTheme.GreenDark));
            page.AddChild(BankUiTheme.Label(
                BankUiText.Get("open_account_first_step"),
                20,
                BankUiTheme.Muted));

            var apply = new Button
            {
                Text = BankUiText.Get("open_account_apply"),
                CustomMinimumSize = new Vector2(0, 62),
            };
            BankUiTheme.ApplyPrimaryButton(apply);
            apply.Pressed += () =>
            {
                _onboardingStep = OnboardingStep.Rules;
                Rebuild();
            };
            page.AddChild(apply);
            return;
        }

        page.AddChild(InfoPanel(
            OpeningRules(),
            BankUiTheme.Green,
            Colors.White));
        page.AddChild(InfoPanel(
            CreditExample("opening_credit_example"),
            BankUiTheme.Gold,
            Color.FromHtml("#FFF2CC")));

        var accepted = new CheckBox
        {
            Text = BankUiText.Get("open_account_checkbox"),
            CustomMinimumSize = new Vector2(0, 54),
        };
        accepted.AddThemeFontSizeOverride("font_size", 18);
        accepted.AddThemeColorOverride("font_color", BankUiTheme.Ink);
        page.AddChild(accepted);

        var acceptedAgain = new CheckBox
        {
            Text = BankUiText.Get("open_account_checkbox_again"),
            CustomMinimumSize = new Vector2(0, 54),
        };
        acceptedAgain.AddThemeFontSizeOverride("font_size", 18);
        acceptedAgain.AddThemeColorOverride("font_color", BankUiTheme.Ink);
        page.AddChild(acceptedAgain);

        var actions = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        actions.AddThemeConstantOverride("separation", 12);
        page.AddChild(actions);

        var back = new Button
        {
            Text = BankUiText.Get("open_account_forced_agree"),
            Disabled = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        BankUiTheme.ApplySecondaryButton(back);
        actions.AddChild(back);

        var agree = new Button
        {
            Text = BankUiText.Get("open_account_agree"),
            Disabled = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        BankUiTheme.ApplyPrimaryButton(agree);

        void UpdateOpeningButtons()
        {
            var disabled =
                !accepted.ButtonPressed || !acceptedAgain.ButtonPressed;
            agree.Disabled = disabled;
            back.Disabled = disabled;
        }

        void SubmitOpening()
        {
            if (!accepted.ButtonPressed || !acceptedAgain.ButtonPressed)
            {
                Notify(BankUiText.Get("open_account_must_accept"), true);
                return;
            }

            var handled = BankUiBridge.RequestOpenAccount();
            Notify(
                handled ? BankUiText.Get("open_account_submitted") : BankUiText.Get("no_handler"),
                !handled);
        }

        accepted.Toggled += _ => UpdateOpeningButtons();
        acceptedAgain.Toggled += _ => UpdateOpeningButtons();
        back.Pressed += SubmitOpening;
        agree.Pressed += SubmitOpening;
        actions.AddChild(agree);
    }

    private void BuildSavings(VBoxContainer page)
    {
        page.AddChild(BankUiTheme.Heading(BankUiText.Get("savings")));
        page.AddChild(InfoPanel(
            SavingsRules(),
            BankUiTheme.Green,
            BankUiTheme.GreenSoft));

        var stats = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        stats.AddThemeConstantOverride("h_separation", 18);
        stats.AddThemeConstantOverride("v_separation", 18);
        long next = EstimateSavingsInterest(
            Math.Max(0L, _snapshot.SavingsBalance),
            _snapshot.SavingsTenths);
        stats.AddChild(MoneyTile(BankUiText.Get("current_balance"), _snapshot.SavingsBalance, BankUiTheme.Green));
        stats.AddChild(MoneyTile(BankUiText.Get("interest_earned"), _snapshot.SavingsInterestEarned, BankUiTheme.Gold));
        stats.AddChild(TextTile(
            BankUiText.Get("interest_turns"),
            BankUiText.Get("turns_value", _snapshot.SavingsInterestTurns),
            BankUiTheme.Green));
        stats.AddChild(MoneyTile(
            BankUiText.Get("savings_next_interest"),
            next,
            BankUiTheme.GreenDark));
        page.AddChild(stats);
    }

    private void BuildCredit(VBoxContainer page)
    {
        page.AddChild(BankUiTheme.Heading(BankUiText.Get("credit")));
        page.AddChild(InfoPanel(
            CreditRules(),
            BankUiTheme.GreenDark,
            BankUiTheme.GreenSoft));
        page.AddChild(InfoPanel(
            CreditExample("credit_interest_example"),
            BankUiTheme.Gold,
            Color.FromHtml("#FFF2CC")));

        if (_snapshot.CreditTier is null)
        {
            if (_snapshot.IsBankrupt)
            {
                page.AddChild(InfoPanel(
                    BankUiText.Get(
                        "credit_closed_cap",
                        _snapshot.AscensionLevel,
                        RelicSeizureTerms()),
                    BankUiTheme.Red,
                    Color.FromHtml("#FFE2E2")));
                return;
            }

            page.AddChild(BankUiTheme.Label(BankUiText.Get("credit_locked"), 26, BankUiTheme.GreenDark));
            page.AddChild(BankUiTheme.Label(
                BankUiText.Get(
                    "credit_locked_blurb",
                    _snapshot.AscensionLevel,
                    QualificationText(OfferFor(BankCreditTier.Starter))),
                20,
                BankUiTheme.Muted));
        }
        else
        {
            var currentSpec = CreditCards.First(card => card.Tier == _snapshot.CreditTier.Value);
            var currentOffer = OfferFor(_snapshot.CreditTier.Value) with
            {
                CreditLimit = ClampToInt(_snapshot.CreditLimit),
                MaximumDebt = ClampToInt(_snapshot.MaximumDebt),
                InterestRateBasisPoints = _snapshot.DebtInterestRateBasisPoints,
            };
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 22);
            row.AddChild(BuildCardShell(currentSpec, currentOffer, compact: true));

            var stats = new GridContainer
            {
                Columns = 2,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            stats.AddThemeConstantOverride("h_separation", 14);
            stats.AddThemeConstantOverride("v_separation", 14);
            stats.AddChild(MoneyTile(BankUiText.Get("credit_debt"), _snapshot.Debt, BankUiTheme.Red, debt: true));
            stats.AddChild(MoneyTile(
                BankUiText.Get("limit"),
                _snapshot.CreditLimit,
                BankUiTheme.Green));
            stats.AddChild(MoneyTile(
                BankUiText.Get("maximum_debt"),
                _snapshot.MaximumDebt,
                BankUiTheme.GreenDark));
            stats.AddChild(MoneyTile(
                BankUiText.Get("next_interest"),
                _snapshot.EstimatedNextDebtInterest,
                BankUiTheme.Gold,
                debt: true));
            stats.AddChild(TextTile(
                BankUiText.Get("grace_remaining"),
                BankUiText.Get("turns_value", _snapshot.DebtGraceFloorsRemaining),
                _snapshot.DebtGraceFloorsRemaining > 0 ? BankUiTheme.Green : BankUiTheme.Red));
            stats.AddChild(TextTile(
                BankUiText.Get("interest_rate"),
                InterestRate(_snapshot.DebtInterestRateBasisPoints),
                BankUiTheme.Gold));
            stats.AddChild(TextTile(
                BankUiText.Get("debt_cycle_floors"),
                BankUiText.Get("turns_value", _snapshot.DebtCycleFloors),
                BankUiTheme.GreenDark));
            stats.AddChild(TextTile(
                BankUiText.Get("tradable_relics"),
                _snapshot.TradableRelicCount.ToString(CultureInfo.CurrentCulture),
                BankUiTheme.Green));
            row.AddChild(stats);
            page.AddChild(row);

            if (_snapshot.LastDebtInterestCharge > 0)
            {
                page.AddChild(InfoPanel(
                    $"{BankUiText.Get("last_interest")}: {Money(_snapshot.LastDebtInterestCharge)}",
                    BankUiTheme.Gold,
                    Color.FromHtml("#FFF2CC")));
            }

            page.AddChild(InfoPanel(
                _snapshot.Debt > 0 ? BankUiText.Get("automatic_repayment") : BankUiText.Get("paid_off"),
                _snapshot.Debt > 0 ? BankUiTheme.Red : BankUiTheme.Green,
                _snapshot.Debt > 0 ? Color.FromHtml("#FFE2E2") : BankUiTheme.GreenSoft));

            if (_snapshot.IsBankrupt)
            {
                page.AddChild(InfoPanel(
                    BankUiText.Get(
                        "credit_closed_cap",
                        _snapshot.AscensionLevel,
                        RelicSeizureTerms()),
                    BankUiTheme.Red,
                    Color.FromHtml("#FFE2E2")));
                return;
            }
        }

        page.AddChild(HorizontalRule());
        page.AddChild(BankUiTheme.Heading(BankUiText.Get("apply_title"), 27));
        page.AddChild(BankUiTheme.Label(BankUiText.Get("apply_disclaimer"), 17, BankUiTheme.Muted));

        var cards = new GridContainer
        {
            Columns = 3,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        cards.AddThemeConstantOverride("h_separation", 16);
        cards.AddThemeConstantOverride("v_separation", 16);
        foreach (var spec in CreditCards)
        {
            cards.AddChild(BuildCreditApplication(spec, OfferFor(spec.Tier)));
        }

        page.AddChild(cards);
    }

    private Control BuildCreditApplication(
        CreditCardSpec spec,
        BankCreditOffer offer)
    {
        var outer = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(280, 410),
        };
        outer.AddThemeStyleboxOverride("panel", BankUiTheme.Panel(Colors.White, 15, Color.FromHtml("#C6D2CC"), 2, 16));

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        outer.AddChild(column);
        column.AddChild(BuildCardShell(spec, offer, compact: false));

        bool instantApproval = offer.QualificationThreshold <= 0;
        var progressText = instantApproval
            ? BankUiText.Get("instant_approval")
            : _snapshot.TotalGoldEarned >= offer.QualificationThreshold
                ? BankUiText.Get("eligible")
                : BankUiText.Get(
                    "not_eligible",
                    offer.QualificationThreshold - _snapshot.TotalGoldEarned);
        var progressLabel = BankUiTheme.Label(progressText, 17,
            instantApproval
                || _snapshot.TotalGoldEarned >= offer.QualificationThreshold
                ? BankUiTheme.Green
                : BankUiTheme.Red);
        column.AddChild(progressLabel);

        if (instantApproval)
        {
            column.AddChild(BankUiTheme.Label(
                BankUiText.Get("qualification_instant"),
                16,
                BankUiTheme.Green));
        }
        else
        {
            var progress = new ProgressBar
            {
                MinValue = 0,
                MaxValue = offer.QualificationThreshold,
                Value = Math.Min(
                    _snapshot.TotalGoldEarned,
                    offer.QualificationThreshold),
                ShowPercentage = false,
                CustomMinimumSize = new Vector2(0, 14),
            };
            progress.AddThemeStyleboxOverride("background", BankUiTheme.Panel(Color.FromHtml("#DDE5E1"), 7));
            progress.AddThemeStyleboxOverride("fill", BankUiTheme.Panel(BankUiTheme.Green, 7));
            column.AddChild(progress);
            column.AddChild(BankUiTheme.Label(
                $"{BankUiText.Get("qualification")}: "
                + $"{Math.Min(_snapshot.TotalGoldEarned, offer.QualificationThreshold)}"
                + $" / {offer.QualificationThreshold}",
                16,
                BankUiTheme.Muted));
        }

        var action = new Button();
        var isEligible = instantApproval
            || _snapshot.TotalGoldEarned >= offer.QualificationThreshold;
        var currentTier = _snapshot.CreditTier;
        var isCurrent = currentTier == spec.Tier;
        var isLower = currentTier is not null && spec.Tier < currentTier.Value;
        action.Text = currentTier is null ? BankUiText.Get("apply") : BankUiText.Get("upgrade");
        if (isCurrent)
        {
            action.Text = BankUiText.Get("current_card");
        }

        action.Disabled = !isEligible || isCurrent || isLower;
        action.TooltipText = isCurrent
            ? BankUiText.Get("already_current")
            : isLower
                ? BankUiText.Get("lower_tier")
                : progressText;
        BankUiTheme.ApplyPrimaryButton(action);
        action.Pressed += () => SubmitCredit(spec.Tier, currentTier is not null);
        column.AddChild(action);
        return outer;
    }

    private Control BuildCardShell(
        CreditCardSpec spec,
        BankCreditOffer offer,
        bool compact)
    {
        var shell = new VBoxContainer
        {
            SizeFlagsHorizontal = compact ? SizeFlags.ShrinkBegin : SizeFlags.ExpandFill,
        };
        shell.AddThemeConstantOverride("separation", 8);




        var artworkFrame = new Control
        {
            SizeFlagsHorizontal = compact ? SizeFlags.ShrinkBegin : SizeFlags.ExpandFill,
            CustomMinimumSize = compact ? new Vector2(360, 227) : new Vector2(260, 208),
            ClipContents = true,
        };
        shell.AddChild(artworkFrame);




        var border = new PanelContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
        };
        SetFullRect(border);
        border.AddThemeStyleboxOverride(
            "panel",
            BankUiTheme.Panel(Colors.Transparent, 18, spec.CardColor, 3));
        artworkFrame.AddChild(border);

        var artworkViewport = new Control
        {
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorLeft = 0,
            AnchorTop = 0,
            AnchorRight = 1,
            AnchorBottom = 1,
            OffsetLeft = 4,
            OffsetTop = 4,
            OffsetRight = -4,
            OffsetBottom = -4,
            ClipContents = true,
        };
        artworkFrame.AddChild(artworkViewport);

        var fallback = new PanelContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
        };
        SetFullRect(fallback);
        fallback.AddThemeStyleboxOverride(
            "panel",
            BankUiTheme.Panel(spec.CardColor, 15));
        artworkViewport.AddChild(fallback);

        var cardTexture = BankUiAssets.Card(spec.Tier);
        if (cardTexture is not null)
        {
            var artwork = new TextureRect
            {
                Texture = cardTexture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            SetFullRect(artwork);
            artworkViewport.AddChild(artwork);
        }
        else
        {
            var fallbackCopy = new VBoxContainer
            {
                MouseFilter = MouseFilterEnum.Ignore,
                AnchorLeft = 0,
                AnchorTop = 0,
                AnchorRight = 1,
                AnchorBottom = 1,
                OffsetLeft = 16,
                OffsetTop = 16,
                OffsetRight = -16,
                OffsetBottom = -16,
            };
            var brandRow = new HBoxContainer
            {
                MouseFilter = MouseFilterEnum.Ignore,
            };
            var td = BankUiTheme.Label("TD", compact ? 28 : 23, Colors.White);
            td.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            brandRow.AddChild(td);
            brandRow.AddChild(BankUiTheme.Label("VISA-ish", compact ? 17 : 14, new Color(1, 1, 1, 0.8f)));
            fallbackCopy.AddChild(brandRow);

            var spacer = new Control
            {
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            fallbackCopy.AddChild(spacer);
            fallbackCopy.AddChild(BankUiTheme.Label(BankUiText.Tier(spec.Tier), compact ? 25 : 20, Colors.White));
            artworkViewport.AddChild(fallbackCopy);
        }

        var details = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        details.AddThemeStyleboxOverride(
            "panel",
            BankUiTheme.Panel(Colors.White, 12, spec.CardColor, 2, compact ? 12 : 10));
        shell.AddChild(details);

        var copy = new VBoxContainer();
        copy.AddThemeConstantOverride("separation", 4);
        details.AddChild(copy);
        copy.AddChild(BankUiTheme.Label(BankUiText.Tier(spec.Tier), compact ? 25 : 20, spec.CardColor));
        copy.AddChild(BankUiTheme.Label(BankUiText.Get(spec.JokeKey), compact ? 16 : 14, BankUiTheme.Ink));
        copy.AddChild(BankUiTheme.Label(
            $"{BankUiText.Get("limit")}: {Money(offer.CreditLimit)}  ·  "
            + $"{BankUiText.Get("maximum_debt")}: {Money(offer.MaximumDebt)}",
            compact ? 16 : 14,
            BankUiTheme.Muted));
        copy.AddChild(BankUiTheme.Label(
            $"{BankUiText.Get("credit_floor")}: {Money(offer.CreditLimit)}  ·  "
            + $"{BankUiText.Get("interest_rate")}: "
            + $"{InterestRate(offer.InterestRateBasisPoints)}",
            compact ? 16 : 14,
            BankUiTheme.Muted));
        copy.AddChild(BankUiTheme.Label(
            $"{BankUiText.Get("first_grace")}: "
            + $"{BankUiText.Get("turns_value", _snapshot.DebtGraceFloorCount)}",
            compact ? 16 : 14,
            BankUiTheme.Muted));
        return shell;
    }

    private void BuildETransfer(VBoxContainer page)
    {
        page.AddChild(BankUiTheme.Heading(BankUiText.Get("etransfer_title")));
        page.AddChild(InfoPanel(
            BankUiText.Get("etransfer_rules"),
            BankUiTheme.Green,
            BankUiTheme.GreenSoft));
        page.AddChild(BankUiTheme.Label(BankUiText.Get("etransfer_blurb"), 20, BankUiTheme.Muted));

        if (_snapshot.Teammates.Count == 0)
        {
            page.AddChild(InfoPanel(
                $"{BankUiText.Get("no_teammates")}\n{BankUiText.Get("no_teammates_hint")}",
                BankUiTheme.Gold,
                Color.FromHtml("#FFF2CC")));
            return;
        }

        var form = FormPanel();
        page.AddChild(form.Panel);

        var recipient = new OptionButton
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 50),
        };
        for (var index = 0; index < _snapshot.Teammates.Count; index++)
        {
            var peer = _snapshot.Teammates[index];
            var suffix = peer.Gold is null ? string.Empty : $"  ·  {peer.Gold} {BankUiText.Get("gold")}";
            recipient.AddItem(peer.DisplayName + suffix, index);
        }
        StyleInput(recipient);
        form.Layout.AddChild(FormField(BankUiText.Get("recipient"), recipient));

        var amount = BuildAmountInput();
        form.Layout.AddChild(FormField(BankUiText.Get("amount"), amount.Container));

        var submit = new Button { Text = BankUiText.Get("send") };
        BankUiTheme.ApplyPrimaryButton(submit);
        submit.Pressed += () =>
        {
            if (!TryReadAmount(amount.Input, out var value))
            {
                return;
            }

            var peer = _snapshot.Teammates[Math.Clamp(recipient.Selected, 0, _snapshot.Teammates.Count - 1)];
            var request = new BankETransferRequest(peer.Id, value);
            if (!BankUiBridge.RequestETransfer(request))
            {
                Notify(BankUiText.Get("no_handler"), true);
                return;
            }

            Notify(BankUiText.Get("request_sent"), false);
        };
        form.Layout.AddChild(submit);
    }

    private void BuildKkCompound(VBoxContainer page)
    {
        page.AddChild(BankUiTheme.Heading(BankUiText.Get("kk_title")));
        page.AddChild(BankUiTheme.Label(BankUiText.Get("kk_tagline"), 24, BankUiTheme.GreenDark));
        page.AddChild(InfoPanel(
            BankUiText.Get(
                "kk_rules",
                _snapshot.AscensionLevel,
                _snapshot.KidneyHpCost,
                _snapshot.KidneyGoldValue,
                _snapshot.ButtHpCost,
                _snapshot.ButtGoldValue,
                ButtRepeatGoldValue()),
            BankUiTheme.Red,
            Color.FromHtml("#FFE2E2")));

        var safeKidneys = MaximumSafeKidneys();
        var stats = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        stats.AddThemeConstantOverride("h_separation", 18);
        stats.AddThemeConstantOverride("v_separation", 18);
        stats.AddChild(TextTile(
            BankUiText.Get("current_hp"),
            BankUiText.Get("hp_value", _snapshot.CurrentHp),
            BankUiTheme.Red));
        stats.AddChild(TextTile(
            BankUiText.Get("max_hp"),
            BankUiText.Get("hp_value", _snapshot.MaxHp),
            BankUiTheme.GreenDark));
        stats.AddChild(TextTile(
            BankUiText.Get("safe_kidney_quantity"),
            safeKidneys.ToString(CultureInfo.CurrentCulture),
            BankUiTheme.Gold));
        stats.AddChild(TextTile(
            BankUiText.Get("butt_sales"),
            _snapshot.ButtSalesCount.ToString(CultureInfo.CurrentCulture),
            BankUiTheme.Green));
        page.AddChild(stats);

        page.AddChild(BankUiTheme.Heading(BankUiText.Get("sell_kidney"), 27));
        page.AddChild(InfoPanel(
            BankUiText.Get(
                "kidney_rules",
                _snapshot.KidneyHpCost,
                _snapshot.KidneyGoldValue,
                Math.Max(0, 50 - _snapshot.KidneyHpCost),
                Math.Max(0, 70 - _snapshot.KidneyHpCost)),
            BankUiTheme.Gold,
            Color.FromHtml("#FFF2CC")));

        var kidneyForm = FormPanel();
        page.AddChild(kidneyForm.Panel);
        var quantity = BuildQuantityInput();
        kidneyForm.Layout.AddChild(FormField(BankUiText.Get("kidney_quantity"), quantity.Container));
        kidneyForm.Layout.AddChild(BankUiTheme.Label(
            BankUiText.Get("kidney_safe_now", safeKidneys),
            17,
            BankUiTheme.Muted));

        var sellKidney = new Button
        {
            Text = BankUiText.Get("sell_kidney_button", 0),
            Disabled = !_snapshot.AreOrganSalesAvailable,
            TooltipText = _snapshot.AreOrganSalesAvailable
                ? string.Empty
                : BankUiText.Get("organ_sales_combat_disabled"),
        };
        BankUiTheme.ApplyPrimaryButton(sellKidney);
        quantity.Input.TextChanged += text =>
        {
            long gross = int.TryParse(
                text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var requested)
                && requested > 0
                ? (long)requested * _snapshot.KidneyGoldValue
                : 0;
            sellKidney.Text =
                BankUiText.Get("sell_kidney_button", gross);
        };
        sellKidney.Pressed += () =>
        {
            if (!TryReadAmount(quantity.Input, out var requested))
            {
                return;
            }

            if (requested > safeKidneys)
            {
                BankUiBridge.NotifyImportant(BankUiText.Get("kidney_fatal"));
                return;
            }

            if (!BankUiBridge.RequestKidneySale(requested))
            {
                Notify(BankUiText.Get("no_handler"), true);
                return;
            }

            Notify(BankUiText.Get("request_sent"), false);
        };
        kidneyForm.Layout.AddChild(sellKidney);

        page.AddChild(HorizontalRule());
        page.AddChild(BankUiTheme.Heading(BankUiText.Get("sell_butt"), 27));
        page.AddChild(InfoPanel(
            BankUiText.Get(
                "butt_rules",
                _snapshot.ButtHpCost,
                _snapshot.ButtGoldValue,
                _snapshot.ButtSalesCount + 1,
                ButtSaleHpCost(),
                ButtRepeatGoldValue()),
            BankUiTheme.Green,
            BankUiTheme.GreenSoft));

        var riskHint = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 30),
        };
        riskHint.AddThemeConstantOverride("separation", 6);
        var riskPrefix = BankUiTheme.Label(
            BankUiText.Get("butt_risk_hint"),
            18,
            BankUiTheme.Muted);




        riskPrefix.AutowrapMode = TextServer.AutowrapMode.Off;
        riskPrefix.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        riskPrefix.VerticalAlignment = VerticalAlignment.Center;
        riskHint.AddChild(riskPrefix);
        var riskLink = new LinkButton
        {
            Text = BankUiText.Get("butt_risk_link"),
            TooltipText = BankUiText.Get("butt_risk_tooltip"),
            FocusMode = FocusModeEnum.All,
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
        };
        riskLink.AddThemeFontSizeOverride("font_size", 18);
        riskLink.AddThemeColorOverride("font_color", BankUiTheme.Red);
        riskLink.AddThemeColorOverride("font_hover_color", Color.FromHtml("#FF3038"));
        riskLink.AddThemeColorOverride("font_pressed_color", Color.FromHtml("#8F1018"));
        riskLink.Pressed += () => BankUiBridge.ShowInformation(
            BankUiText.Get("butt_risk_title"),
            BankUiText.Get("butt_risk_explanation"));
        riskHint.AddChild(riskLink);
        page.AddChild(riskHint);

        var sellButt = new Button
        {
            Text = _snapshot.ButtSalesCount >= 3
                ? BankUiText.Get(
                    "sell_butt_button_repeat",
                    _snapshot.ButtSalesCount + 1,
                    ButtSaleHpCost(),
                    ButtRepeatGoldValue())
                : BankUiText.Get(
                    "sell_butt_button",
                    _snapshot.ButtHpCost,
                    _snapshot.ButtGoldValue),
            CustomMinimumSize = new Vector2(0, 58),
            Disabled = !_snapshot.AreOrganSalesAvailable,
            TooltipText = _snapshot.AreOrganSalesAvailable
                ? string.Empty
                : BankUiText.Get("organ_sales_combat_disabled"),
        };
        BankUiTheme.ApplyPrimaryButton(sellButt);
        sellButt.Pressed += () =>
        {
            int maximumHpCost = _snapshot.ButtSalesCount >= 3
                ? checked(ButtSaleHpCost() * 2)
                : ButtSaleHpCost();
            if (_snapshot.CurrentHp <= maximumHpCost)
            {
                BankUiBridge.NotifyImportant(BankUiText.Get("butt_fatal"));
                return;
            }

            if (!BankUiBridge.RequestButtSale())
            {
                Notify(BankUiText.Get("no_handler"), true);
                return;
            }

            Notify(BankUiText.Get("request_sent"), false);
        };
        page.AddChild(sellButt);
    }

    private string OpeningRules()
    {
        return BankUiText.Get(
            "opening_rules",
            _snapshot.AscensionLevel,
            SavingsOpeningTerms(),
            OfferValues(QualificationText),
            OfferValues(offer => Money(offer.CreditLimit)),
            OfferValues(offer => Money(offer.MaximumDebt)),
            _snapshot.DebtGraceFloorCount,
            OfferValues(offer => InterestRate(offer.InterestRateBasisPoints)),
            RelicSeizureTerms(),
            _snapshot.KidneyHpCost,
            _snapshot.KidneyGoldValue,
            _snapshot.ButtHpCost,
            _snapshot.ButtGoldValue,
            ButtRepeatGoldValue());
    }

    private string SavingsOpeningTerms()
    {
        return BankUiText.Get(
            "savings_opening_terms",
            _snapshot.AscensionLevel,
            FlexibleInterestRate(
                _snapshot.SavingsInterestRateBasisPoints),
            _snapshot.SavingsInterestCap);
    }

    private string SavingsRules()
    {
        return BankUiText.Get(
            "savings_rules",
            _snapshot.AscensionLevel,
            FlexibleInterestRate(
                _snapshot.SavingsInterestRateBasisPoints),
            _snapshot.SavingsInterestCap,
            EstimateSavingsInterest(100, 0));
    }

    private string CreditRules()
    {
        return BankUiText.Get(
            "credit_rules",
            _snapshot.AscensionLevel,
            OfferValues(QualificationText),
            OfferValues(offer => Money(offer.CreditLimit)),
            OfferValues(offer => Money(offer.MaximumDebt)),
            _snapshot.DebtGraceFloorCount,
            OfferValues(offer => InterestRate(offer.InterestRateBasisPoints)),
            RelicSeizureTerms());
    }

    private string CreditExample(string key)
    {
        BankCreditOffer starter = OfferFor(BankCreditTier.Starter);
        const long startingDebt = 100;
        long firstCharge = CeilingInterest(
            startingDebt,
            starter.InterestRateBasisPoints);
        long debtAfterFirstCharge = startingDebt + firstCharge;
        long secondCharge = CeilingInterest(
            debtAfterFirstCharge,
            starter.InterestRateBasisPoints);
        return BankUiText.Get(
            key,
            _snapshot.DebtGraceFloorCount,
            _snapshot.DebtGraceFloorCount + 1,
            InterestRateNumber(starter.InterestRateBasisPoints),
            firstCharge,
            debtAfterFirstCharge,
            secondCharge);
    }

    private string RelicSeizureTerms()
    {
        int goldPerRelic = Math.Max(1, _snapshot.RelicGoldPerSeizure);
        return _snapshot.RelicSeizureCap > 0
            ? BankUiText.Get(
                "relic_seizure_capped",
                goldPerRelic,
                _snapshot.RelicSeizureCap)
            : BankUiText.Get(
                "relic_seizure_unlimited",
                goldPerRelic);
    }

    private BankCreditOffer OfferFor(BankCreditTier tier)
    {
        return _snapshot.CreditOffers.FirstOrDefault(
                offer => offer.Tier == tier)
            ?? BankUiSnapshot.Empty.CreditOffers.First(
                offer => offer.Tier == tier);
    }

    private string OfferValues(Func<BankCreditOffer, string> selector)
    {
        return string.Join(
            " / ",
            CreditCards.Select(spec => selector(OfferFor(spec.Tier))));
    }

    private static string QualificationText(BankCreditOffer offer)
    {
        return offer.QualificationThreshold <= 0
            ? BankUiText.Get("instant_approval")
            : Money(offer.QualificationThreshold);
    }

    private long EstimateSavingsInterest(
        long savingsBalance,
        int carriedTenths)
    {
        decimal rawInterest =
            Math.Max(0L, savingsBalance)
            * (decimal)Math.Max(
                0,
                _snapshot.SavingsInterestRateBasisPoints)
            / 10_000m;
        return Math.Min(
            DecimalToLongFloor(rawInterest),
            Math.Max(0, _snapshot.SavingsInterestCap));
    }

    private static long CeilingInterest(long debt, int basisPoints)
    {
        if (debt <= 0 || basisPoints <= 0)
        {
            return 0;
        }

        decimal charge = debt * (decimal)basisPoints / 10_000m;
        return charge >= long.MaxValue
            ? long.MaxValue
            : (long)Math.Ceiling(charge);
    }

    private static long DecimalToLongFloor(decimal value)
    {
        if (value <= 0)
        {
            return 0;
        }

        return value >= long.MaxValue
            ? long.MaxValue
            : (long)Math.Floor(value);
    }

    private static long SaturatingAdd(long left, long right)
    {
        return left > long.MaxValue - right
            ? long.MaxValue
            : left + right;
    }

    private int ButtRepeatGoldValue()
    {
        return KkCompoundService.CalculateButtGoldValue(
            _snapshot.ButtGoldValue,
            _snapshot.ButtSalesCount);
    }

    private int ButtSaleHpCost()
    {
        return KkCompoundService.CalculateButtHpCost(
            _snapshot.ButtHpCost,
            _snapshot.ButtSalesCount);
    }

    private static int ClampToInt(long value)
    {
        return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    }

    private void SubmitCredit(BankCreditTier tier, bool isUpgrade)
    {
        var handled = isUpgrade
            ? BankUiBridge.RequestUpgrade(tier)
            : BankUiBridge.RequestApply(tier);

        Notify(
            handled ? BankUiText.Get("request_sent") : BankUiText.Get("no_handler"),
            !handled);
    }

    private static (PanelContainer Panel, VBoxContainer Layout) FormPanel()
    {
        var panel = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        panel.AddThemeStyleboxOverride("panel", BankUiTheme.Panel(Colors.White, 16, Color.FromHtml("#C6D2CC"), 2, 24));

        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 18);
        panel.AddChild(layout);
        return (panel, layout);
    }

    private static VBoxContainer FormField(string labelText, Control input)
    {
        var field = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        field.AddThemeConstantOverride("separation", 7);
        field.AddChild(BankUiTheme.Label(labelText, 17, BankUiTheme.Muted));
        field.AddChild(input);
        return field;
    }

    private (HBoxContainer Container, LineEdit Input) BuildAmountInput()
    {
        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        row.AddThemeConstantOverride("separation", 8);
        var input = new LineEdit
        {
            PlaceholderText = BankUiText.Get("amount_placeholder"),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 50),
            MaxLength = 12,
        };
        StyleInput(input);
        row.AddChild(input);

        foreach (var amount in new[] { 10, 25, 50, 100 })
        {
            var quick = new Button
            {
                Text = $"+{amount}",
                CustomMinimumSize = new Vector2(74, 50),
            };
            BankUiTheme.ApplySecondaryButton(quick);
            quick.Pressed += () => input.Text = amount.ToString(CultureInfo.InvariantCulture);
            row.AddChild(quick);
        }

        return (row, input);
    }

    private (HBoxContainer Container, LineEdit Input) BuildQuantityInput()
    {
        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        row.AddThemeConstantOverride("separation", 8);
        var input = new LineEdit
        {
            PlaceholderText = BankUiText.Get("quantity_placeholder"),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 50),
            MaxLength = 6,
        };
        StyleInput(input);
        row.AddChild(input);

        foreach (var quantity in new[] { 1, 2 })
        {
            var quick = new Button
            {
                Text = quantity.ToString(CultureInfo.InvariantCulture),
                CustomMinimumSize = new Vector2(74, 50),
            };
            BankUiTheme.ApplySecondaryButton(quick);
            quick.Pressed += () => input.Text = quantity.ToString(CultureInfo.InvariantCulture);
            row.AddChild(quick);
        }

        return (row, input);
    }

    private int MaximumSafeKidneys()
    {
        var limitingHp = Math.Min(_snapshot.CurrentHp, _snapshot.MaxHp);
        if (_snapshot.KidneyHpCost <= 0
            || _snapshot.KidneyGoldValue <= 0)
        {
            return 0;
        }

        return limitingHp <= _snapshot.KidneyHpCost
            ? 0
            : Math.Min(
                (limitingHp - 1) / _snapshot.KidneyHpCost,
                int.MaxValue / _snapshot.KidneyGoldValue);
    }

    private bool TryReadAmount(LineEdit input, out long amount)
    {
        if (!long.TryParse(input.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out amount) || amount <= 0)
        {
            Notify(BankUiText.Get("invalid_amount"), true);
            input.GrabFocus();
            return false;
        }

        return true;
    }

    private static Control MoneyTile(string title, long amount, Color accent, bool debt = false)
    {
        return TextTile(title, debt ? $"{Money(amount)} {BankUiText.Get("debt")}" : Money(amount), accent);
    }

    private static Control TextTile(string title, string value, Color accent)
    {
        var panel = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(300, 126),
        };
        panel.AddThemeStyleboxOverride("panel", BankUiTheme.Panel(Colors.White, 14, accent, 3, 18));

        var column = new VBoxContainer();
        column.AddChild(BankUiTheme.Label(title, 17, BankUiTheme.Muted));
        column.AddChild(BankUiTheme.Label(value, 31, accent));
        panel.AddChild(column);
        return panel;
    }

    private static Control InfoPanel(string text, Color accent, Color background)
    {
        var panel = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        panel.AddThemeStyleboxOverride("panel", BankUiTheme.Panel(background, 12, accent, 2, 15));
        panel.AddChild(BankUiTheme.Label(text, 19, BankUiTheme.Ink));
        return panel;
    }

    private static HSeparator HorizontalRule()
    {
        var rule = new HSeparator
        {
            CustomMinimumSize = new Vector2(0, 12),
        };
        rule.AddThemeStyleboxOverride("separator", BankUiTheme.Panel(Color.FromHtml("#C9D5CF"), 1));
        return rule;
    }

    private static void StyleInput(Control input)
    {
        input.AddThemeStyleboxOverride("normal", BankUiTheme.Panel(Colors.White, 9, Color.FromHtml("#AFC0B7"), 2, 10));
        input.AddThemeStyleboxOverride("focus", BankUiTheme.Panel(Colors.White, 9, BankUiTheme.Green, 3, 10));
        input.AddThemeStyleboxOverride("hover", BankUiTheme.Panel(BankUiTheme.GreenSoft, 9, BankUiTheme.Green, 2, 10));
        input.AddThemeColorOverride("font_color", BankUiTheme.Ink);
        input.AddThemeFontSizeOverride("font_size", 19);
    }

    private void SetStatus(string message, bool isError)
    {
        if (_status is null)
        {
            return;
        }
        _status.Text = message;
        _status.AddThemeColorOverride("font_color", isError ? Color.FromHtml("#FFB3B3") : BankUiTheme.GreenSoft);
    }

    private static string TabLabel(Tab tab)
    {
        return tab switch
        {
            Tab.Savings => $"1.  {BankUiText.Get("savings")}",
            Tab.Credit => $"2.  {BankUiText.Get("credit")}",
            Tab.ETransfer => $"3.  {BankUiText.Get("etransfer")}",
            Tab.KkCompound => $"4.  {BankUiText.Get("kk_tab")}",
            _ => tab.ToString(),
        };
    }

    private static string InterestRate(int basisPoints)
    {
        return $"{basisPoints / 100m:0.00}%";
    }

    private static string FlexibleInterestRate(int basisPoints)
    {
        return $"{basisPoints / 100m:0.##}";
    }

    private static string InterestRateNumber(int basisPoints)
    {
        return $"{basisPoints / 100m:0.00}";
    }

    private static string Money(long amount)
    {
        return $"{amount:N0} G";
    }

    private static void SetFullRect(Control control)
    {
        control.AnchorLeft = 0;
        control.AnchorTop = 0;
        control.AnchorRight = 1;
        control.AnchorBottom = 1;
        control.OffsetLeft = 0;
        control.OffsetTop = 0;
        control.OffsetRight = 0;
        control.OffsetBottom = 0;
    }

    private static void ClearChildren(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }
}
