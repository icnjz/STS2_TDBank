using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace TDBank.TDBankCode.UI;

internal sealed partial class BankNotificationHost : Control
{
    private PanelContainer? _toast;
    private Label? _toastLabel;
    private Godot.Timer? _toastTimer;
    private Control? _modalRoot;
    private Button? _modalBackdrop;
    private Label? _modalTitle;
    private Label? _modalMessage;
    private Button? _modalDismiss;

    public override void _Ready()
    {
        SetFullRect(this);
        MouseFilter = MouseFilterEnum.Ignore;
        ProcessMode = ProcessModeEnum.Always;
        BuildToast();
        BuildModal();
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (_modalRoot is not { Visible: true })
        {
            return;
        }




        if (inputEvent is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }
            || inputEvent.IsActionPressed("ui_cancel"))
        {
            HideModal();
            GetViewport().SetInputAsHandled();
            return;
        }






        if (inputEvent.IsActionPressed("ui_accept")
            || inputEvent is InputEventMouseButton
            {
                ButtonIndex: MouseButton.Left,
                Pressed: true,
            } mouse
                && IsDismissPoint(mouse.Position)
            || inputEvent is InputEventScreenTouch { Pressed: true } touch
                && IsDismissPoint(touch.Position))
        {
            HideModal();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (inputEvent is InputEventKey
            or InputEventJoypadButton
            or InputEventJoypadMotion
            or InputEventMouseButton
            or InputEventMouseMotion
            or InputEventScreenTouch
            or InputEventScreenDrag)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (_modalRoot is { Visible: true })
        {


            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree()
    {
        BankUiBridge.OnImportantModalClosed();
    }

    public void ShowToast(string message, bool isError)
    {
        if (_toast is null || _toastLabel is null || _toastTimer is null)
        {
            return;
        }

        var accent = isError ? BankUiTheme.Red : BankUiTheme.Green;
        var background = isError
            ? Color.FromHtml("#4A1015")
            : BankUiTheme.GreenDeep;
        _toast.AddThemeStyleboxOverride(
            "panel",
            BankUiTheme.Panel(background, 14, accent, 3, 18));
        _toastLabel.Text = message;
        _toastLabel.AddThemeColorOverride("font_color", Colors.White);
        _toast.Show();
        _toast.MoveToFront();
        _toastTimer.Start(isError ? 6.0 : 4.5);
    }

    public void ShowModal(string title, string message, bool danger)
    {
        if (_modalRoot is null
            || _modalTitle is null
            || _modalMessage is null
            || _modalDismiss is null)
        {
            return;
        }

        var accent = danger ? BankUiTheme.Red : BankUiTheme.Green;
        _modalTitle.Text = title;
        _modalTitle.AddThemeColorOverride("font_color", accent);
        _modalMessage.Text = message;
        _modalDismiss.Text = BankUiText.Get("notification_dismiss");
        MouseFilter = MouseFilterEnum.Stop;
        _modalRoot.Show();
        _modalRoot.MoveToFront();
        BankUiBridge.OnImportantModalOpened();
        _modalDismiss.GrabFocus();
    }

    public void HideModal()
    {
        if (_modalRoot is not { Visible: true })
        {
            return;
        }

        _modalRoot.Hide();
        MouseFilter = MouseFilterEnum.Ignore;
        BankUiBridge.OnImportantModalClosed();
        ActiveScreenContext.Instance.Update();
    }

    private bool IsDismissPoint(Vector2 point)
    {
        return _modalDismiss is not null
            && GodotObject.IsInstanceValid(_modalDismiss)
            && _modalDismiss.IsVisibleInTree()
            && _modalDismiss.GetGlobalRect().HasPoint(point);
    }

    private void BuildToast()
    {
        _toast = new PanelContainer
        {
            Name = "TDBankTopToast",
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 0,
            AnchorBottom = 0,
            OffsetLeft = -480,
            OffsetRight = 480,
            OffsetTop = 92,
            OffsetBottom = 172,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _toast.AddThemeStyleboxOverride(
            "panel",
            BankUiTheme.Panel(BankUiTheme.GreenDeep, 14, BankUiTheme.Green, 3, 18));

        _toastLabel = BankUiTheme.Label(string.Empty, 24, Colors.White);
        _toastLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _toastLabel.VerticalAlignment = VerticalAlignment.Center;
        _toastLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _toast.AddChild(_toastLabel);
        _toast.Hide();
        AddChild(_toast);

        _toastTimer = new Godot.Timer
        {
            Name = "TDBankToastTimer",
            OneShot = true,
            WaitTime = 4.5,
        };
        _toastTimer.Timeout += () => _toast?.Hide();
        AddChild(_toastTimer);
    }

    private void BuildModal()
    {
        _modalRoot = new Control
        {
            Name = "TDBankNotificationModal",
            MouseFilter = MouseFilterEnum.Stop,
            ZIndex = 10,
        };
        SetFullRect(_modalRoot);

        _modalBackdrop = new Button
        {
            Name = "TDBankNotificationBackdrop",
            Flat = true,
            FocusMode = FocusModeEnum.None,
            MouseFilter = MouseFilterEnum.Stop,
        };
        SetFullRect(_modalBackdrop);
        var dim = BankUiTheme.Panel(new Color(0.01f, 0.02f, 0.015f, 0.74f), 0);
        _modalBackdrop.AddThemeStyleboxOverride("normal", dim);
        _modalBackdrop.AddThemeStyleboxOverride("hover", dim);
        _modalBackdrop.AddThemeStyleboxOverride("pressed", dim);
        _modalBackdrop.Pressed += HideModal;
        _modalRoot.AddChild(_modalBackdrop);

        var panel = new PanelContainer
        {
            Name = "TDBankNotificationDialog",
            ZIndex = 1,
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -430,
            OffsetTop = -185,
            OffsetRight = 430,
            OffsetBottom = 185,
            MouseFilter = MouseFilterEnum.Stop,
        };
        panel.AddThemeStyleboxOverride(
            "panel",
            BankUiTheme.Panel(BankUiTheme.Cream, 18, BankUiTheme.GreenDark, 4, 28));

        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride("separation", 22);

        _modalTitle = BankUiTheme.Label(string.Empty, 34, BankUiTheme.GreenDark);
        _modalTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _modalTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_modalTitle);

        _modalMessage = BankUiTheme.Label(string.Empty, 23, BankUiTheme.Ink);
        _modalMessage.HorizontalAlignment = HorizontalAlignment.Center;
        _modalMessage.VerticalAlignment = VerticalAlignment.Center;
        _modalMessage.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _modalMessage.SizeFlagsVertical = SizeFlags.ExpandFill;
        column.AddChild(_modalMessage);

        _modalDismiss = new Button
        {
            Text = BankUiText.Get("notification_dismiss"),
            CustomMinimumSize = new Vector2(0, 56),
        };
        BankUiTheme.ApplyPrimaryButton(_modalDismiss);
        _modalDismiss.Pressed += HideModal;
        column.AddChild(_modalDismiss);

        panel.AddChild(column);
        _modalRoot.AddChild(panel);
        _modalRoot.Hide();
        AddChild(_modalRoot);
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
}
