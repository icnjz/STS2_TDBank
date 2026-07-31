using System;
using System.Text;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;

namespace TDBank.TDBankCode.UI;

public static class BankUiBridge
{
    private const string LayerName = "TDBankUiLayer";
    private const string CurrentVersion = "0.1.3";
    private const string LatestReleaseApi =
        "https://api.github.com/repos/icnjz/STS2_TDBank/releases/latest";
    private const string LatestReleasePage =
        "https://github.com/icnjz/STS2_TDBank/releases/latest";
    private static CanvasLayer? _layer;
    private static BankOverlay? _overlay;
    private static BankNotificationHost? _notifications;
    private static NRemoteMouseCursorContainer? _liftedRemoteCursorContainer;
    private static Node? _remoteCursorOriginalParent;
    private static int _remoteCursorOriginalIndex = -1;
    private static int _remoteCursorOriginalZIndex;
    private static bool _remoteCursorOriginalZAsRelative;
    private static bool _overlaySurfaceVisible;
    private static bool _importantModalVisible;
    private static bool _updateCheckStarted;
    private static BankUiSnapshot _fallbackSnapshot = BankUiSnapshot.Empty;

    public static Func<BankUiSnapshot>? SnapshotProvider { get; set; }

    internal static IScreenContext? VisibleScreen
    {
        get
        {
            if (!GodotObject.IsInstanceValid(_overlay) || !_overlay!.Visible)
            {
                return null;
            }

            try
            {
                return RunManager.Instance.IsInProgress
                    && !RunManager.Instance.IsAbandoned
                        ? _overlay
                        : null;
            }
            catch
            {
                return null;
            }
        }
    }

    public static event Action? OpenAccountRequested;
    public static event Action<BankCreditTier>? ApplyRequested;
    public static event Action<BankCreditTier>? UpgradeRequested;
    public static event Action<BankETransferRequest>? ETransferRequested;
    public static event Action<long>? KidneySaleRequested;
    public static event Action? ButtSaleRequested;

    public static BankUiLanguage Language
    {
        get => BankUiText.Language;
        set
        {
            if (BankUiText.Language == value)
            {
                return;
            }

            BankUiText.Language = value;
            if (GodotObject.IsInstanceValid(_overlay))
            {
                _overlay!.Refresh(ReadSnapshot());
            }
        }
    }

    public static void SetSnapshot(BankUiSnapshot snapshot)
    {
        _fallbackSnapshot = snapshot ?? BankUiSnapshot.Empty;
        Refresh();
    }

    public static BankUiSnapshot ReadSnapshot()
    {
        try
        {
            return SnapshotProvider?.Invoke() ?? _fallbackSnapshot;
        }
        catch (Exception exception)
        {
            MainFile.Logger.Error($"TD Bank UI snapshot failed: {exception}");
            return _fallbackSnapshot with
            {
                IsBankingAvailable = false,
                UnavailableReason = BankUiText.Get("snapshot_failed", exception.Message),
            };
        }
    }

    public static void Attach(Node context)
    {
        if (GodotObject.IsInstanceValid(_overlay))
        {
            return;
        }

        var root = context.GetTree()?.Root;
        if (root is null)
        {
            return;
        }

        _layer = new CanvasLayer
        {
            Name = LayerName,
            Layer = 90,
        };
        _overlay = new BankOverlay
        {
            Name = "TDBankOverlay",
        };
        _notifications = new BankNotificationHost
        {
            Name = "TDBankNotifications",


            ZIndex = 100,
        };
        _layer.AddChild(_overlay);
        _layer.AddChild(_notifications);
        root.AddChild(_layer);
    }

    public static void Open()
    {
        if (!GodotObject.IsInstanceValid(_overlay))
        {
            MainFile.Logger.Warn("TD Bank UI cannot open before a run top bar has been created.");
            return;
        }

        if (IsNativeTargetingActive())
        {
            Notify(BankUiText.Get("finish_targeting_first"), isError: true);
            return;
        }

        _overlay!.Open(ReadSnapshot());
        CheckForUpdate();
    }

    public static void Open(Node context)
    {
        Attach(context);
        Open();
    }

    public static void Close()
    {
        if (GodotObject.IsInstanceValid(_notifications))
        {
            _notifications!.HideModal();
        }

        if (GodotObject.IsInstanceValid(_overlay))
        {
            _overlay!.Close();
        }
    }

    public static void Refresh()
    {
        if (GodotObject.IsInstanceValid(_overlay)
            && _overlay!.Visible)
        {
            _overlay!.Refresh(ReadSnapshot());
        }
    }

    public static void Notify(string message, bool isError = false)
    {
        if (GodotObject.IsInstanceValid(_notifications))
        {
            _notifications!.ShowToast(message, isError);
        }
        else
        {
            MainFile.Logger.Info($"TD Bank: {message}");
        }
    }

    public static void NotifyImportant(string title, string message, bool danger = true)
    {



        CancelNativeTargeting();

        if (GodotObject.IsInstanceValid(_notifications))
        {
            _notifications!.ShowModal(title, message, danger);
        }
        else
        {
            MainFile.Logger.Info($"TD Bank: {title}: {message}");
        }
    }

    public static void NotifyImportant(string message)
    {
        NotifyImportant(
            BankUiText.Get("notification_error_title"),
            message,
            danger: true);
    }

    public static void ShowInformation(string title, string message)
    {
        NotifyImportant(title, message, danger: false);
    }

    private static void CheckForUpdate()
    {
        if (_updateCheckStarted
            || !GodotObject.IsInstanceValid(_layer))
        {
            return;
        }

        _updateCheckStarted = true;
        var request = new HttpRequest
        {
            Name = "TDBankUpdateCheck",
            Timeout = 5,
        };
        request.RequestCompleted += (
            result,
            responseCode,
            headers,
            body) =>
        {
            try
            {
                if (responseCode != 200
                    || !TryGetNewerVersion(body, out string latest))
                {
                    return;
                }

                _notifications?.ShowActionModal(
                    BankUiText.Get("update_available_title"),
                    BankUiText.Get(
                        "update_available",
                        CurrentVersion,
                        latest),
                    BankUiText.Get("download_update"),
                    () => OS.ShellOpen(LatestReleasePage));
            }
            catch (Exception exception)
            {
                MainFile.Logger.Warn(
                    $"TD Bank update check could not read the response: {exception.Message}");
            }
            finally
            {
                request.QueueFree();
            }
        };
        _layer!.AddChild(request);
        Error error = request.Request(
            LatestReleaseApi,
            ["User-Agent: TD-Bank-Mod"]);
        if (error != Error.Ok)
        {
            request.QueueFree();
        }
    }

    internal static bool TryGetNewerVersion(
        byte[] responseBody,
        out string latest)
    {
        latest = string.Empty;
        using JsonDocument document = JsonDocument.Parse(
            Encoding.UTF8.GetString(responseBody));
        if (!document.RootElement.TryGetProperty(
                "tag_name",
                out JsonElement tagElement))
        {
            return false;
        }

        string tag = tagElement.GetString()?.Trim() ?? string.Empty;
        string normalized = tag.TrimStart('v', 'V');
        if (!Version.TryParse(CurrentVersion, out Version? current)
            || !Version.TryParse(normalized, out Version? available)
            || available <= current)
        {
            return false;
        }

        latest = tag;
        return true;
    }

    public static void NotifyButtFreeloader(int hpLoss)
    {
        NotifyImportant(
            BankUiText.Get("butt_risk_freeloader_title"),
            BankUiText.Get("butt_risk_freeloader", hpLoss),
            danger: true);
    }

    public static void NotifyButtHemorrhage(
        int hpLoss,
        int grossGold,
        int debtPaid,
        int walletGold)
    {
        NotifyImportant(
            BankUiText.Get("butt_risk_hemorrhage_title"),
            BankUiText.Get(
                "butt_risk_hemorrhage",
                hpLoss,
                grossGold,
                debtPaid,
                walletGold),
            danger: true);
    }

    internal static bool RequestApply(BankCreditTier tier)
    {
        if (ApplyRequested is null)
        {
            return false;
        }

        ApplyRequested.Invoke(tier);
        return true;
    }

    internal static bool RequestUpgrade(BankCreditTier tier)
    {
        if (UpgradeRequested is null)
        {
            return false;
        }

        UpgradeRequested.Invoke(tier);
        return true;
    }

    internal static bool RequestOpenAccount()
    {
        if (OpenAccountRequested is null)
        {
            return false;
        }

        OpenAccountRequested.Invoke();
        return true;
    }

    internal static bool RequestETransfer(BankETransferRequest request)
    {
        if (ETransferRequested is null)
        {
            return false;
        }

        ETransferRequested.Invoke(request);
        return true;
    }

    internal static bool RequestKidneySale(long quantity)
    {
        if (KidneySaleRequested is null)
        {
            return false;
        }

        KidneySaleRequested.Invoke(quantity);
        return true;
    }

    internal static bool RequestButtSale()
    {
        if (ButtSaleRequested is null)
        {
            return false;
        }

        ButtSaleRequested.Invoke();
        return true;
    }

    internal static void OnOverlayOpened()
    {
        _overlaySurfaceVisible = true;
        EnsureRemoteCursorsLifted();
    }

    internal static void OnOverlayClosed()
    {
        _overlaySurfaceVisible = false;
        TryRestoreRemoteCursors();
    }

    internal static void OnImportantModalOpened()
    {
        _importantModalVisible = true;
        EnsureRemoteCursorsLifted();
    }

    internal static void OnImportantModalClosed()
    {
        _importantModalVisible = false;
        TryRestoreRemoteCursors();
    }

    private static void EnsureRemoteCursorsLifted()
    {
        if (!GodotObject.IsInstanceValid(_layer))
        {
            return;
        }

        if (GodotObject.IsInstanceValid(_liftedRemoteCursorContainer))
        {




            try
            {
                NRemoteMouseCursorContainer cursors =
                    _liftedRemoteCursorContainer!;
                if (cursors.GetParent() != _layer)
                {
                    cursors.Reparent(_layer!, keepGlobalTransform: true);
                }

                cursors.ZAsRelative = false;
                cursors.ZIndex = 200;
                cursors.ForceUpdateAllCursors();
            }
            catch (Exception exception)
            {
                MainFile.Logger.Warn(
                    $"TD Bank could not re-lift multiplayer cursors: {exception.Message}");
            }

            return;
        }

        try
        {
            NRemoteMouseCursorContainer? cursors =
                NGame.Instance?.RemoteCursorContainer;
            Node? originalParent = cursors?.GetParent();
            if (!GodotObject.IsInstanceValid(cursors)
                || !GodotObject.IsInstanceValid(originalParent))
            {
                return;
            }

            _liftedRemoteCursorContainer = cursors;
            _remoteCursorOriginalParent = originalParent;
            _remoteCursorOriginalIndex = cursors!.GetIndex();
            _remoteCursorOriginalZIndex = cursors.ZIndex;
            _remoteCursorOriginalZAsRelative = cursors.ZAsRelative;

            cursors.Reparent(_layer!, keepGlobalTransform: true);
            cursors.ZAsRelative = false;
            cursors.ZIndex = 200;
        }
        catch (Exception exception)
        {
            MainFile.Logger.Warn(
                $"TD Bank could not lift multiplayer cursors above the bank: {exception.Message}");
            TryRestoreRemoteCursors(force: true);
            return;
        }

        try
        {
            _liftedRemoteCursorContainer?.ForceUpdateAllCursors();
        }
        catch (Exception exception)
        {


            MainFile.Logger.Warn(
                $"TD Bank could not refresh multiplayer cursor positions: {exception.Message}");
        }
    }

    private static void TryRestoreRemoteCursors(bool force = false)
    {
        if (!force && (_overlaySurfaceVisible || _importantModalVisible))
        {
            return;
        }

        NRemoteMouseCursorContainer? cursors =
            _liftedRemoteCursorContainer;
        Node? originalParent = _remoteCursorOriginalParent;
        int originalIndex = _remoteCursorOriginalIndex;
        int originalZIndex = _remoteCursorOriginalZIndex;
        bool originalZAsRelative = _remoteCursorOriginalZAsRelative;

        if (!GodotObject.IsInstanceValid(cursors))
        {
            ClearRemoteCursorLease();
            return;
        }

        if (!GodotObject.IsInstanceValid(originalParent))
        {
            MainFile.Logger.Warn(
                "TD Bank could not restore multiplayer cursors because their original parent no longer exists.");
            ClearRemoteCursorLease();
            return;
        }

        try
        {
            cursors!.ZIndex = originalZIndex;
            cursors.ZAsRelative = originalZAsRelative;

            if (cursors.GetParent() != originalParent)
            {
                cursors.Reparent(originalParent!, keepGlobalTransform: true);
            }

            int lastIndex = Math.Max(0, originalParent!.GetChildCount() - 1);
            originalParent.MoveChild(
                cursors,
                Math.Clamp(originalIndex, 0, lastIndex));
        }
        catch (Exception exception)
        {


            MainFile.Logger.Warn(
                $"TD Bank could not restore multiplayer cursors: {exception.Message}");
            return;
        }

        ClearRemoteCursorLease();

        try
        {
            cursors.ForceUpdateAllCursors();
        }
        catch (Exception exception)
        {


            MainFile.Logger.Warn(
                $"TD Bank restored multiplayer cursors but could not refresh their positions: {exception.Message}");
        }
    }

    private static void ClearRemoteCursorLease()
    {
        _liftedRemoteCursorContainer = null;
        _remoteCursorOriginalParent = null;
        _remoteCursorOriginalIndex = -1;
        _remoteCursorOriginalZIndex = 0;
        _remoteCursorOriginalZAsRelative = true;
    }

    private static bool IsNativeTargetingActive()
    {
        try
        {
            return NTargetManager.Instance.IsInSelection;
        }
        catch
        {
            return false;
        }
    }

    private static void CancelNativeTargeting()
    {
        try
        {
            if (NTargetManager.Instance.IsInSelection)
            {
                NTargetManager.Instance.CancelTargeting();
            }
        }
        catch
        {

        }
    }
}
