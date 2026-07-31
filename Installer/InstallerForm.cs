using System.Diagnostics;
using System.Runtime.InteropServices;
using CNJ.TowerDebt.Setup.Core;

namespace CNJ.TowerDebt.Setup;

internal sealed class InstallerForm : Form
{
    private static readonly Color TdGreen = Color.FromArgb(0, 122, 51);
    private static readonly Color TdGreenDark = Color.FromArgb(5, 63, 39);
    private static readonly Color Cream = Color.FromArgb(247, 241, 227);
    private static readonly Color Ink = Color.FromArgb(23, 37, 31);
    private static readonly Color Muted = Color.FromArgb(90, 111, 101);
    private static readonly Color SoftGreen = Color.FromArgb(223, 243, 232);
    private static readonly Color Red = Color.FromArgb(190, 54, 54);
    private static readonly Color Amber = Color.FromArgb(165, 104, 0);
    private const uint WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;
    private static readonly TimeSpan SaveHandoffTimeout =
        TimeSpan.FromMinutes(10);
    private static readonly TimeSpan GameExitTimeout =
        TimeSpan.FromMinutes(2);

    private readonly TextBox _pathBox;
    private readonly Button _browseButton;
    private readonly Label _validationLabel;
    private readonly CheckBox _consent;
    private readonly Button _installButton;
    private readonly Button _uninstallButton;
    private readonly Button _cancelButton;
    private readonly Button _licenseButton;
    private readonly ProgressBar _progress;
    private readonly Label _status;
    private readonly Image _logoImage;

    private Label _headerTitleLabel = null!;
    private Label _headerTaglineLabel = null!;
    private Label _disclaimerLabel = null!;
    private Label _payloadSummaryLabel = null!;
    private Label _pathHeadingLabel = null!;
    private Button _chineseButton = null!;
    private Button _englishButton = null!;

    private UiLanguage _language;
    private UiText _statusText = UiText.StatusPrivacy;
    private object?[] _statusArguments = [];
    private bool _busy;
    private bool _uninstalling;
    private bool _isTDBankInstalled;
    private GameValidation _validation =
        new(false, false, null, null, null, ValidationStatus.NoDirectory);
    private InstallResult? _installResult;

    public InstallerForm(UiLanguage initialLanguage)
    {
        _language = initialLanguage;
        Text = InstallerStrings.Get(_language, UiText.WindowTitle);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(900, 870);
        MinimumSize = new Size(860, 820);
        BackColor = Cream;
        ForeColor = Ink;
        Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;

        _logoImage = EmbeddedPayload.ReadLogoImage();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Cream,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);
        root.Controls.Add(BuildHeader(), 0, 0);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 10,
            Padding = new Padding(28, 18, 28, 18),
            BackColor = Cream,
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 138));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.Controls.Add(body, 0, 1);

        body.Controls.Add(BuildDisclaimer(), 0, 0);

        _pathHeadingLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold),
            TextAlign = ContentAlignment.BottomLeft,
        };
        body.Controls.Add(_pathHeadingLabel, 0, 1);

        var pathRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = new Padding(0, 4, 0, 4),
        };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 174));
        _pathBox = new TextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 10, 0),
        };
        _pathBox.TextChanged += (_, _) => RefreshValidation();
        _browseButton = MakeButton(string.Empty, TdGreenDark, Color.White);
        _browseButton.Dock = DockStyle.Fill;
        _browseButton.Click += BrowseForGame;
        pathRow.Controls.Add(_pathBox, 0, 0);
        pathRow.Controls.Add(_browseButton, 1, 0);
        body.Controls.Add(pathRow, 0, 2);

        _validationLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Muted,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 8, 0),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
        };
        body.Controls.Add(_validationLabel, 0, 3);
        body.Controls.Add(BuildPayloadSummary(), 0, 4);

        _consent = new CheckBox
        {
            Dock = DockStyle.Fill,
            Checked = false,
            ForeColor = Ink,
            Padding = new Padding(4, 4, 0, 0),
        };
        _consent.CheckedChanged += (_, _) => UpdateActionButtons();
        body.Controls.Add(_consent, 0, 5);

        _progress = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Style = ProgressBarStyle.Blocks,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Margin = new Padding(0, 4, 0, 4),
        };
        body.Controls.Add(_progress, 0, 6);

        _status = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Muted,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        body.Controls.Add(_status, 0, 7);

        var utilityRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
        };
        _licenseButton = MakeLinkButton(string.Empty);
        _licenseButton.Click += (_, _) => ShowDependencyLicense();
        utilityRow.Controls.Add(_licenseButton);
        body.Controls.Add(utilityRow, 0, 8);

        var buttonRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Margin = Padding.Empty,
        };
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        _cancelButton = MakeButton(string.Empty, Color.FromArgb(104, 118, 111), Color.White);
        _cancelButton.Dock = DockStyle.Fill;
        _cancelButton.Margin = new Padding(0, 0, 12, 0);
        _cancelButton.Click += (_, _) => Close();
        _uninstallButton = MakeButton(string.Empty, Red, Color.White);
        _uninstallButton.Dock = DockStyle.Fill;
        _uninstallButton.Margin = new Padding(0, 0, 12, 0);
        _uninstallButton.Enabled = false;
        _uninstallButton.Click += UninstallClicked;
        _installButton = MakeButton(string.Empty, TdGreen, Color.White);
        _installButton.Dock = DockStyle.Fill;
        _installButton.Enabled = false;
        _installButton.Click += InstallClicked;
        buttonRow.Controls.Add(_cancelButton, 0, 0);
        buttonRow.Controls.Add(_uninstallButton, 1, 0);
        buttonRow.Controls.Add(_installButton, 2, 0);
        body.Controls.Add(buttonRow, 0, 9);

        FormClosing += (_, eventArgs) =>
        {
            if (_busy)
            {
                eventArgs.Cancel = true;
            }
        };
        Shown += (_, _) => AutoDetectGame();
        ApplyLanguage();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _logoImage.Dispose();
        }
        base.Dispose(disposing);
    }

    private Control BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = TdGreen,
            Padding = new Padding(24, 18, 24, 16),
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            BackColor = TdGreen,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        header.Controls.Add(layout);

        var logo = new PictureBox
        {
            Dock = DockStyle.Fill,
            Image = _logoImage,
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, 0, 16, 0),
        };
        layout.Controls.Add(logo, 0, 0);
        layout.SetRowSpan(logo, 2);

        _headerTitleLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold, GraphicsUnit.Point),
            TextAlign = ContentAlignment.BottomLeft,
            AutoEllipsis = false,
        };
        layout.Controls.Add(_headerTitleLabel, 1, 0);

        _headerTaglineLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = SoftGreen,
            Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Regular, GraphicsUnit.Point),
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = true,
        };
        layout.Controls.Add(_headerTaglineLabel, 1, 1);

        var languageHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 0, 0, 36),
            BackColor = TdGreen,
        };
        var languageRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        languageRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        languageRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        _chineseButton = MakeLanguageButton("中文");
        _chineseButton.AccessibleName = "切换到中文";
        _chineseButton.Click += (_, _) => SetLanguage(UiLanguage.ZhCn);
        _englishButton = MakeLanguageButton("English");
        _englishButton.AccessibleName = "Switch to English";
        _englishButton.Click += (_, _) => SetLanguage(UiLanguage.En);
        languageRow.Controls.Add(_chineseButton, 0, 0);
        languageRow.Controls.Add(_englishButton, 1, 0);
        languageHost.Controls.Add(languageRow);
        layout.Controls.Add(languageHost, 2, 0);
        layout.SetRowSpan(languageHost, 2);

        EnableNativeHeaderDrag(header);
        EnableNativeHeaderDrag(layout);
        EnableNativeHeaderDrag(logo);
        EnableNativeHeaderDrag(_headerTitleLabel);
        EnableNativeHeaderDrag(_headerTaglineLabel);
        EnableNativeHeaderDrag(languageHost);
        EnableNativeHeaderDrag(languageRow);
        return header;
    }

    private Control BuildDisclaimer()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SoftGreen,
            Padding = new Padding(16, 12, 16, 10),
            Margin = new Padding(0, 0, 0, 8),
            BorderStyle = BorderStyle.FixedSingle,
        };
        _disclaimerLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            ForeColor = Ink,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font.FontFamily, 10.2f, FontStyle.Regular, GraphicsUnit.Point),
        };
        panel.Controls.Add(_disclaimerLabel);
        return panel;
    }

    private Control BuildPayloadSummary()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(14, 8, 14, 8),
            Margin = new Padding(0, 4, 0, 6),
        };
        _payloadSummaryLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Ink,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        panel.Controls.Add(_payloadSummaryLabel);
        return panel;
    }

    internal void SetLanguage(UiLanguage language)
    {
        if (_busy || _language == language)
        {
            return;
        }

        _language = language;
        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        SuspendLayout();
        Text = InstallerStrings.Get(_language, UiText.WindowTitle);
        _headerTitleLabel.Text = InstallerStrings.Get(_language, UiText.HeaderTitle);
        _headerTaglineLabel.Text = InstallerStrings.Get(_language, UiText.HeaderTagline);
        _disclaimerLabel.Text = InstallerStrings.Get(_language, UiText.Disclaimer);
        _pathHeadingLabel.Text = InstallerStrings.Get(_language, UiText.PathHeading);
        _browseButton.Text = InstallerStrings.Get(_language, UiText.Browse);
        RenderPayloadSummary();
        _consent.Text = InstallerStrings.Get(_language, UiText.Consent);
        _licenseButton.Text = InstallerStrings.Get(_language, UiText.LicenseLink);
        _cancelButton.Text = InstallerStrings.Get(_language, UiText.Cancel);
        RenderValidation();
        RenderStatus();
        UpdateActionButtons();
        StyleLanguageButton(_chineseButton, _language == UiLanguage.ZhCn);
        StyleLanguageButton(_englishButton, _language == UiLanguage.En);
        ResumeLayout(performLayout: true);
    }

    private void AutoDetectGame()
    {
        SetStatus(UiText.Detecting);
        try
        {
            var candidates = SteamLocator.FindGameDirectories();
            if (candidates.Count > 0)
            {
                _pathBox.Text = candidates[0];
                if (candidates.Count == 1)
                {
                    SetStatus(UiText.DetectedOne);
                }
                else
                {
                    SetStatus(UiText.DetectedMany, candidates.Count);
                }
            }
            else
            {
                SetStatus(UiText.DetectionNotFound);
                RefreshValidation();
            }

            if (_isTDBankInstalled)
            {
                SetStatus(UiText.InstalledDetected);
            }
        }
        catch (Exception exception)
        {
            SetStatus(UiText.DetectionFailed, exception.Message);
            RefreshValidation();
        }
    }

    private void BrowseForGame(object? sender, EventArgs eventArgs)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = InstallerStrings.Get(_language, UiText.FolderBrowserDescription),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            InitialDirectory = Directory.Exists(_pathBox.Text) ? _pathBox.Text : string.Empty,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _pathBox.Text = dialog.SelectedPath;
        }
    }

    private void RefreshValidation()
    {
        _validation = GameValidator.Validate(_pathBox.Text);
        _isTDBankInstalled = TransactionUninstaller.IsInstalled(_pathBox.Text);
        RenderValidation();
        RenderPayloadSummary();
        UpdateActionButtons();
        if (_isTDBankInstalled && !_busy)
        {
            SetStatus(UiText.InstalledDetected);
        }
    }

    private void RenderValidation()
    {
        _validationLabel.Text = InstallerStrings.FormatValidation(_language, _validation);
        var forwardCompatible =
            _validation.Status == ValidationStatus.ForwardCompatible;
        _validationLabel.ForeColor = forwardCompatible
            ? Amber
            : _validation.IsSupportedVersion
                ? TdGreen
                : Red;
        _validationLabel.BackColor = forwardCompatible
            ? Color.FromArgb(255, 248, 225)
            : _validation.IsSupportedVersion
                ? Color.FromArgb(239, 249, 243)
                : Color.FromArgb(255, 239, 239);
    }

    private void SetStatus(UiText text, params object?[] arguments)
    {
        _statusText = text;
        _statusArguments = arguments;
        RenderStatus();
    }

    private void RenderStatus()
    {
        _status.Text = InstallerStrings.Get(_language, _statusText, _statusArguments);
    }

    private void RenderPayloadSummary()
    {
        _payloadSummaryLabel.Text = InstallerStrings.Get(
            _language,
            _isTDBankInstalled
                ? UiText.PayloadSummaryInstalled
                : UiText.PayloadSummary);
    }

    private void UpdateActionButtons()
    {
        _installButton.Text = _installResult is not null
            ? InstallerStrings.Get(_language, UiText.Installed)
            : _busy && !_uninstalling
                ? InstallerStrings.Get(_language, UiText.Installing)
                : InstallerStrings.Get(
                    _language,
                    _isTDBankInstalled ? UiText.Reinstall : UiText.Install);
        _installButton.Enabled = !_busy
            && _validation.IsSupportedVersion
            && _consent.Checked
            && _installResult is null;

        _uninstallButton.Text = _busy && _uninstalling
            ? InstallerStrings.Get(_language, UiText.Uninstalling)
            : InstallerStrings.Get(_language, UiText.Uninstall);
        _uninstallButton.Enabled = !_busy && _isTDBankInstalled;
    }

    private async void InstallClicked(object? sender, EventArgs eventArgs)
    {
        RefreshValidation();
        if (!_validation.IsSupportedVersion || !_consent.Checked)
        {
            return;
        }

        SetBusy(true);
        _progress.Style = ProgressBarStyle.Marquee;
        _status.ForeColor = Muted;
        var progress = new Progress<InstallStage>(
            stage =>
            {
                _status.Text = InstallerStrings.FormatProgress(_language, stage);
            });

        try
        {
            var selectedPath = _pathBox.Text;
            _installResult = await Task.Run(
                () => TransactionInstaller.Install(selectedPath, progress));
            _isTDBankInstalled = true;
            RenderPayloadSummary();
            _progress.Style = ProgressBarStyle.Blocks;
            _progress.Value = 100;
            _status.ForeColor = TdGreen;
            var saveMessage = InstallerStrings.FormatSaveProtection(
                _language,
                _installResult.SaveProtection);
            SetStatus(UiText.StatusSuccess, saveMessage);
            SetBusy(false);

            var dependencyMessageKey = _installResult.TDLibAction switch
            {
                TDLibInstallAction.PreserveExact => UiText.SuccessBaseExact,
                TDLibInstallAction.PreserveNewer => UiText.SuccessBaseNewer,
                TDLibInstallAction.Install => UiText.SuccessBaseInstalled,
                _ => UiText.SuccessBaseRepaired,
            };
            var dependencyMessage = InstallerStrings.Get(
                _language,
                dependencyMessageKey);
            MessageBox.Show(
                this,
                InstallerStrings.Get(
                    _language,
                    UiText.SuccessDialogBody,
                    dependencyMessage,
                    saveMessage),
                InstallerStrings.Get(_language, UiText.SuccessDialogTitle),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            _progress.Style = ProgressBarStyle.Blocks;
            _progress.Value = 0;
            _status.ForeColor = Red;
            SetStatus(UiText.StatusFailure);
            SetBusy(false);
            var error = InstallerStrings.FormatError(_language, exception);
            MessageBox.Show(
                this,
                InstallerStrings.Get(
                    _language,
                    UiText.FailureDialogBody,
                    error,
                    InstallerLog.Path),
                InstallerStrings.Get(_language, UiText.FailureDialogTitle),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async void UninstallClicked(object? sender, EventArgs eventArgs)
    {
        RefreshValidation();
        if (!_isTDBankInstalled)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            InstallerStrings.Get(_language, UiText.UninstallConfirmBody),
            InstallerStrings.Get(_language, UiText.UninstallConfirmTitle),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        _uninstalling = true;
        SetBusy(true);
        _progress.Style = ProgressBarStyle.Marquee;
        _status.ForeColor = Muted;
        var progress = new Progress<UninstallStage>(
            stage =>
            {
                _status.Text = InstallerStrings.FormatProgress(_language, stage);
            });

        try
        {
            var selectedPath = _pathBox.Text;
            ThrowIfGameRunning();
            _status.Text = _language == UiLanguage.ZhCn
                ? "正在更新安全销户组件，旧版卸载 Bug 不许赖账……"
                : "Updating the safe account-closure component before handoff…";
            await Task.Run(
                () => TransactionInstaller.Install(selectedPath));
            var transactionId =
                $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
            _status.Text = _language == UiLanguage.ZhCn
                ? "正在完整备份普通档和 Mod 档，并准备安全交接……"
                : "Backing up vanilla and modded saves before the safe handoff…";
            UninstallSaveHandoffPreparation handoff;
            try
            {
                handoff = await Task.Run(
                    () => UninstallSaveHandoff.Prepare(
                        SaveProtection.DefaultSaveRoot,
                        transactionId));
            }
            catch (Exception exception)
            {
                throw new InstallerOperationException(
                    InstallerErrorCode.UninstallSaveHandoffFailed,
                    exception,
                    backupDirectory: string.Empty,
                    detail: exception.Message);
            }

            if (handoff.RequiresGameHandoff)
            {
                _status.Text = _language == UiLanguage.ZhCn
                    ? "正在自动启动游戏同步存档；若出现官方安全提示，请点击“加载 Mod”。完成后游戏会自动退出……"
                    : "Launching the game to sync saves. If the official safety prompt appears, click “Load Mods”; the game will exit automatically when done…";
                LaunchSteamForSaveHandoff(handoff);
                await WaitForSaveHandoffAsync(handoff);
            }

            var result = await Task.Run(
                () => TransactionUninstaller.Uninstall(selectedPath, progress));
            _installResult = null;
            _isTDBankInstalled = false;
            RenderPayloadSummary();
            _progress.Style = ProgressBarStyle.Blocks;
            _progress.Value = 100;
            _status.ForeColor = TdGreen;
            var tdLibReceiptText = InstallerStrings.GetTDLibUninstallText(
                result.TDLibDisposition);
            var recoveryReceipt = string.IsNullOrWhiteSpace(
                handoff.BackupDirectory)
                ? result.BackupDirectory
                : _language == UiLanguage.ZhCn
                    ? $"Mod 恢复备份：{result.BackupDirectory}\r\n存档交接备份：{handoff.BackupDirectory}"
                    : $"Mod recovery backup: {result.BackupDirectory}\r\nSave-handoff backup: {handoff.BackupDirectory}";
            SetStatus(
                result.Removed
                    ? UiText.StatusUninstallSuccess
                    : UiText.StatusUninstallAlreadyAbsent,
                tdLibReceiptText,
                recoveryReceipt);
            _uninstalling = false;
            SetBusy(false);

            MessageBox.Show(
                this,
                InstallerStrings.Get(
                    _language,
                    result.Removed
                        ? UiText.UninstallSuccessDialogBody
                        : UiText.UninstallAlreadyAbsentDialogBody,
                    tdLibReceiptText,
                    recoveryReceipt),
                InstallerStrings.Get(
                    _language,
                    UiText.UninstallSuccessDialogTitle),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            _progress.Style = ProgressBarStyle.Blocks;
            _progress.Value = 0;
            _status.ForeColor = Red;
            SetStatus(UiText.StatusUninstallFailure);
            _uninstalling = false;
            _isTDBankInstalled = TransactionUninstaller.IsInstalled(_pathBox.Text);
            RenderPayloadSummary();
            SetBusy(false);
            var error = InstallerStrings.FormatError(_language, exception);
            MessageBox.Show(
                this,
                InstallerStrings.Get(
                    _language,
                    UiText.UninstallFailureDialogBody,
                    error,
                    InstallerLog.Path),
                InstallerStrings.Get(
                    _language,
                    UiText.UninstallFailureDialogTitle),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void ThrowIfGameRunning()
    {
        if (IsGameRunning())
        {
            throw new InstallerOperationException(
                InstallerErrorCode.GameRunning);
        }
    }

    private static void LaunchSteamForSaveHandoff(
        UninstallSaveHandoffPreparation handoff)
    {
        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "steam://run/2868840",
                    UseShellExecute = true,
                });
        }
        catch (Exception exception)
        {
            throw new InstallerOperationException(
                InstallerErrorCode.UninstallSaveHandoffLaunchFailed,
                exception,
                backupDirectory: handoff.BackupDirectory,
                detail: exception.Message);
        }
    }

    private async Task WaitForSaveHandoffAsync(
        UninstallSaveHandoffPreparation handoff)
    {
        var deadline = DateTime.UtcNow + SaveHandoffTimeout;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(750);
            UninstallSaveHandoffInspection inspection;
            try
            {
                inspection = await Task.Run(
                    () => UninstallSaveHandoff.Inspect(handoff));
            }
            catch (Exception exception)
            {
                throw new InstallerOperationException(
                    InstallerErrorCode.UninstallSaveHandoffFailed,
                    exception,
                    backupDirectory: handoff.BackupDirectory,
                    detail: exception.Message);
            }

            if (inspection.State == UninstallSaveHandoffState.Failed)
            {
                throw new InstallerOperationException(
                    InstallerErrorCode.UninstallSaveHandoffFailed,
                    backupDirectory: handoff.BackupDirectory,
                    detail: HandoffDetails(inspection));
            }

            if (!inspection.MayRemoveMods)
            {
                continue;
            }

            var exitDeadline = DateTime.UtcNow + GameExitTimeout;
            while (DateTime.UtcNow < exitDeadline)
            {
                if (!IsGameRunning())
                {
                    return;
                }
                await Task.Delay(500);
            }

            throw new InstallerOperationException(
                InstallerErrorCode.UninstallSaveHandoffTimedOut,
                backupDirectory: handoff.BackupDirectory,
                detail: "The verified receipt exists, but SlayTheSpire2.exe is still running.");
        }

        var finalInspection = await Task.Run(
            () => UninstallSaveHandoff.Inspect(handoff));
        throw new InstallerOperationException(
            InstallerErrorCode.UninstallSaveHandoffTimedOut,
            backupDirectory: handoff.BackupDirectory,
            detail: HandoffDetails(finalInspection));
    }

    private static string HandoffDetails(
        UninstallSaveHandoffInspection inspection)
    {
        if (inspection.Accounts.Count == 0)
        {
            return inspection.State.ToString();
        }

        return string.Join(
            "; ",
            inspection.Accounts.Select(
                account =>
                    $"{account.AccountId}: {account.State} — {account.Detail}"));
    }

    private static bool IsGameRunning()
    {
        var processes = Process.GetProcessesByName("SlayTheSpire2");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _browseButton.Enabled = !busy;
        _pathBox.Enabled = !busy;
        _consent.Enabled = !busy;
        _cancelButton.Enabled = !busy;
        _licenseButton.Enabled = !busy;
        _chineseButton.Enabled = !busy;
        _englishButton.Enabled = !busy;
        UseWaitCursor = busy;
        UpdateActionButtons();
    }

    private void EnableNativeHeaderDrag(Control control)
    {
        control.MouseDown += BeginNativeHeaderDrag;
    }

    private void BeginNativeHeaderDrag(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left)
        {
            return;
        }

        ReleaseCapture();
        _ = SendMessage(Handle, WmNcLButtonDown, HtCaption, 0);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(
        nint windowHandle,
        uint message,
        nint wordParameter,
        nint longParameter);

    private void ShowDependencyLicense()
    {
        using var dialog = new Form
        {
            Text = InstallerStrings.Get(_language, UiText.LicenseDialogTitle),
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(720, 500),
            MinimumSize = new Size(620, 400),
            BackColor = Cream,
            Font = Font,
            Icon = Icon,
        };
        var text = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            BackColor = Color.White,
            ForeColor = Ink,
            Text = EmbeddedPayload.ReadDependencyLicense(),
            Margin = new Padding(12),
        };
        var close = MakeButton(
            InstallerStrings.Get(_language, UiText.LicenseDialogClose),
            TdGreen,
            Color.White);
        close.Dock = DockStyle.Bottom;
        close.Height = 50;
        close.Click += (_, _) => dialog.Close();
        dialog.Controls.Add(text);
        dialog.Controls.Add(close);
        dialog.ShowDialog(this);
    }

    private Button MakeButton(string text, Color background, Color foreground)
    {
        return new Button
        {
            Text = text,
            BackColor = background,
            ForeColor = foreground,
            FlatStyle = FlatStyle.Flat,
            Font = new Font(Font, FontStyle.Bold),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        }.Also(button =>
        {
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(background, 0.12f);
            button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(background, 0.08f);
        });
    }

    private Button MakeLanguageButton(string text)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(2),
            FlatStyle = FlatStyle.Flat,
            Font = new Font(Font, FontStyle.Bold),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Color.White;
        return button;
    }

    private static void StyleLanguageButton(Button button, bool selected)
    {
        button.BackColor = selected ? Color.White : TdGreenDark;
        button.ForeColor = selected ? TdGreenDark : Color.White;
        button.FlatAppearance.MouseOverBackColor = selected
            ? Color.White
            : ControlPaint.Light(TdGreenDark, 0.12f);
    }

    private Button MakeLinkButton(string text)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            ForeColor = TdGreenDark,
            BackColor = Cream,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 14, 0),
            Height = 30,
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }
}

internal static class ControlExtensions
{
    public static T Also<T>(this T value, Action<T> configure)
    {
        configure(value);
        return value;
    }
}
