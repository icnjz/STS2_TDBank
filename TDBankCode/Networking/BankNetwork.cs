using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using TDBank.TDBankCode.Banking;
using TDBank.TDBankCode.UI;

namespace TDBank.TDBankCode.Networking;

public enum BankOperationKind
{
    ApplyCard = 1,
    ETransfer = 3,
    OpenAccount = 5,
    SellKidneys = 6,
    SellButt = 7,
}

public struct TDBankNetOperationAction : INetAction, IPacketSerializable
{



    internal const int ProtocolMagic = 0x54444237;

    public BankOperationKind Kind;
    public CreditTier Tier;
    public int LifecycleEpoch;
    public int Amount;
    public ulong RecipientId;
    public GameActionType ExecutionType;
    public ulong RequestId;
    public bool HostAuthorized;
    private bool _protocolCompatible;

    public readonly GameAction ToGameAction(Player player)
    {
        (bool authorized, GameActionType executionType) =
            GetHostAuthorization(
                Kind,
                ExecutionType,
                HostAuthorized,
                _protocolCompatible,
                LifecycleEpoch == BankNetwork.CurrentLifecycleEpoch);
        if (authorized
            && RunManager.Instance.NetService.Type == NetGameType.Host
            && !BankNetwork.TryAcceptHostRequest(
                player.NetId,
                RequestId))
        {
            authorized = false;
            executionType = GameActionType.Any;
        }

        return new TDBankOperationGameAction(
            player,
            Kind,
            Tier,
            Amount,
            RecipientId,
            executionType,
            RequestId,
            LifecycleEpoch,
            authorized);
    }

    public readonly void Serialize(PacketWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteInt((int)Kind);
        writer.WriteInt((int)Tier);



        writer.WriteInt(ProtocolMagic);
        writer.WriteInt(LifecycleEpoch);
        writer.WriteInt(Amount);
        writer.WriteULong(RecipientId);
        writer.WriteInt((int)ExecutionType);
        writer.WriteULong(RequestId);
        writer.WriteBool(HostAuthorized);
    }

    public void Deserialize(PacketReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        Kind = (BankOperationKind)reader.ReadInt();
        Tier = (CreditTier)reader.ReadInt();
        int protocolMagic = reader.ReadInt();
        LifecycleEpoch = reader.ReadInt();
        Amount = reader.ReadInt();
        RecipientId = reader.ReadULong();
        ExecutionType = (GameActionType)reader.ReadInt();
        RequestId = reader.ReadULong();
        HostAuthorized = reader.ReadBool();
        _protocolCompatible =
            protocolMagic == ProtocolMagic
            && LifecycleEpoch > 0;
    }

    public override readonly string ToString()
        => $"TDBankNetOperationAction {Kind} epoch={LifecycleEpoch} "
           + $"amount={Amount} recipient={RecipientId}";

    private static (bool Authorized, GameActionType ExecutionType)
        GetHostAuthorization(
            BankOperationKind operationKind,
            GameActionType requestedType,
            bool payloadAuthorization,
            bool protocolCompatible,
            bool lifecycleEpochMatches)
    {
        try
        {
            return ResolveAuthorizationForPeer(
                RunManager.Instance.NetService.Type,
                RunManager.Instance.ActionQueueSynchronizer.CombatState,
                operationKind,
                requestedType,
                payloadAuthorization,
                protocolCompatible,
                lifecycleEpochMatches,
                FloorTransitionGate.IsActive,
                RunManager.Instance.IsInProgress
                    && !RunManager.Instance.IsCleaningUp
                    && !RunManager.Instance.IsAbandoned
                    && RunManager.Instance.DebugOnlyGetState() is not null);
        }
        catch
        {


            return (false, GameActionType.Any);
        }
    }

    internal static (bool Authorized, GameActionType ExecutionType)
        ResolveAuthorizationForPeer(
            NetGameType peerType,
            ActionSynchronizerCombatState hostCombatState,
            BankOperationKind operationKind,
            GameActionType requestedType,
            bool payloadAuthorization,
            bool protocolCompatible = true,
            bool lifecycleEpochMatches = true,
            bool transitionActive = false,
            bool runActive = true)
    {
        if (!protocolCompatible)
        {
            return (false, GameActionType.Any);
        }
        if (!IsSupportedOperation(operationKind))
        {
            return (false, GameActionType.Any);
        }

        if (peerType != NetGameType.Host)
        {
            return (payloadAuthorization, requestedType);
        }

        if (!lifecycleEpochMatches || transitionActive || !runActive)
        {
            return (false, GameActionType.Any);
        }

        return hostCombatState switch
        {
            ActionSynchronizerCombatState.PlayPhase
                when requestedType == GameActionType.CombatPlayPhaseOnly =>
                    (true, GameActionType.CombatPlayPhaseOnly),
            ActionSynchronizerCombatState.NotInCombat
                when requestedType == GameActionType.NonCombat =>
                    (true, GameActionType.NonCombat),
            _ =>
                (false, GameActionType.Any),
        };
    }

    internal static bool IsSupportedOperation(BankOperationKind kind)
    {
        return kind is
            BankOperationKind.ApplyCard
            or BankOperationKind.ETransfer
            or BankOperationKind.OpenAccount
            or BankOperationKind.SellKidneys
            or BankOperationKind.SellButt;
    }
}

public sealed class TDBankOperationGameAction : GameAction
{
    private readonly Player _actor;
    private readonly BankOperationKind _kind;
    private readonly CreditTier _tier;
    private readonly int _amount;
    private readonly ulong _recipientId;
    private readonly GameActionType _executionType;
    private readonly ulong _requestId;
    private readonly int _lifecycleEpoch;
    private readonly bool _hostAuthorized;

    public override ulong OwnerId => _actor.NetId;

    public override GameActionType ActionType => _executionType;

    internal bool IsExecutionEpochCurrent =>
        _hostAuthorized
        && _lifecycleEpoch == BankNetwork.CurrentLifecycleEpoch;

    public TDBankOperationGameAction(
        Player actor,
        BankOperationKind kind,
        CreditTier tier = CreditTier.None,
        int amount = 0,
        ulong recipientId = 0,
        GameActionType executionType = GameActionType.NonCombat,
        ulong requestId = 0,
        int lifecycleEpoch = 0,
        bool hostAuthorized = true)
    {
        _actor = actor ?? throw new ArgumentNullException(nameof(actor));
        _kind = kind;
        _tier = tier;
        _amount = amount;
        _recipientId = recipientId;
        _executionType = hostAuthorized
            ? executionType is
                GameActionType.CombatPlayPhaseOnly or GameActionType.NonCombat
                    ? executionType
                    : GameActionType.NonCombat
            : executionType is
                GameActionType.CombatPlayPhaseOnly
                or GameActionType.NonCombat
                or GameActionType.Any
                    ? executionType
                    : GameActionType.Any;
        _requestId = requestId;
        _lifecycleEpoch = lifecycleEpoch > 0
            ? lifecycleEpoch
            : BankNetwork.CurrentLifecycleEpoch;
        _hostAuthorized = hostAuthorized;
        BeforeCancelled += OnCancelled;
    }

    protected override async Task ExecuteAction()
    {
        BankOperationResult result;
        try
        {
            result = await ExecuteLedgerOperationAsync();
        }
        finally
        {
            if (LocalContext.NetId == _actor.NetId)
            {
                BankNetwork.CompleteLocalRequest(_requestId);
            }
        }

        ReportResult(result);
    }

    private void ReportResult(BankOperationResult result)
    {
        bool isLocalActor = LocalContext.NetId == _actor.NetId;
        bool isLocalRecipient = _kind == BankOperationKind.ETransfer
            && LocalContext.NetId == _recipientId;

        try
        {


            if (isLocalActor || isLocalRecipient)
            {
                BankUiBridge.Refresh();
            }

            if (isLocalActor)
            {
                if (result.Success
                    && _kind == BankOperationKind.SellButt
                    && result.ButtOutcome == ButtRiskOutcome.Unpaid)
                {
                    BankUiBridge.NotifyButtFreeloader(
                        KkCompoundService.GetButtHpCost(_actor));
                }
                else if (result.Success
                    && _kind == BankOperationKind.SellButt
                    && result.ButtOutcome
                        == ButtRiskOutcome.Hemorrhage)
                {
                    BankUiBridge.NotifyButtHemorrhage(
                        checked(
                            KkCompoundService.GetButtHpCost(_actor) * 2),
                        checked(result.SecondaryAmount + result.Amount),
                        result.SecondaryAmount,
                        result.Amount);
                }
                else
                {
                    string message = result.Success
                        ? SuccessMessage(result)
                        : FailureMessage(result);
                    BankUiBridge.Notify(message, !result.Success);
                }
            }
            else if (isLocalRecipient && result.Success)
            {
                BankUiBridge.Notify(BankUiText.Get("etransfer_received", _amount));
            }

            MainFile.Logger.Info(
                $"Bank action id={Id?.ToString() ?? "pending"} kind={_kind} "
                + $"actor={_actor.NetId} amount={_amount} success={result.Success} "
                + $"error={result.Error} buttOutcome={result.ButtOutcome} "
                + $"gold={_actor.Gold}.");
        }
        catch (Exception exception)
        {


            try
            {
                MainFile.Logger.Error($"Could not report TD Bank action result: {exception}");
            }
            catch
            {

            }
        }
    }

    private string SuccessMessage(BankOperationResult result)
    {
        return _kind switch
        {
            BankOperationKind.ApplyCard =>
                BankUiText.Get("approved"),
            BankOperationKind.ETransfer =>
                BankUiText.Get("etransfer_complete"),
            BankOperationKind.OpenAccount =>
                BankUiText.Get("account_opened"),
            BankOperationKind.SellKidneys =>
                BankUiText.Get(
                    "kidney_sale_complete",
                    _amount,
                    checked(
                        _amount
                        * KkCompoundService.GetKidneyGoldValue(_actor)),
                    result.SecondaryAmount,
                    result.Amount),
            BankOperationKind.SellButt
                when BankStateStore.Get(_actor).ButtSalesCount >= 4 =>
                BankUiText.Get(
                    "butt_repeat_warning",
                    BankStateStore.Get(_actor).ButtSalesCount,
                    checked(result.SecondaryAmount + result.Amount),
                    result.SecondaryAmount,
                    result.Amount),
            BankOperationKind.SellButt =>
                BankUiText.Get(
                    "butt_sale_complete",
                    checked(result.SecondaryAmount + result.Amount),
                    result.SecondaryAmount,
                    result.Amount),
            _ => BankUiText.Get("done"),
        };
    }

    private string FailureMessage(BankOperationResult result)
    {
        if (result.Error == BankErrorCode.InsufficientHealth)
        {
            return _kind == BankOperationKind.SellKidneys
                ? BankUiText.Get("kidney_too_weak")
                : BankUiText.Get("butt_too_weak");
        }

        return BankUiText.BankError(result.Error);
    }

    private void OnCancelled(GameAction _)
    {
        if (LocalContext.NetId == _actor.NetId)
        {
            BankNetwork.CompleteLocalRequest(_requestId);
        }
    }

    internal async Task<BankOperationResult> ExecuteLedgerOperationAsync()
    {
        if (!ShouldExecuteAuthorizedOperation(
                _hostAuthorized,
                _lifecycleEpoch,
                BankNetwork.CurrentLifecycleEpoch))
        {
            return BankOperationResult.Fail(BankErrorCode.OperationUnavailable);
        }

        return _kind switch
        {
            BankOperationKind.ApplyCard =>
                BankService.ApplyForCreditCard(_actor, _tier),
            BankOperationKind.ETransfer =>
                ExecuteETransfer(),
            BankOperationKind.OpenAccount =>
                BankService.OpenBankAccount(_actor),
            BankOperationKind.SellKidneys =>
                await KkCompoundService.SellKidneys(_actor, _amount),
            BankOperationKind.SellButt =>
                await KkCompoundService.SellButt(_actor),
            _ =>
                BankOperationResult.Fail(BankErrorCode.InvalidAccount),
        };
    }

    internal static bool ShouldExecuteAuthorizedOperation(
        bool hostAuthorized,
        int actionLifecycleEpoch,
        int currentLifecycleEpoch)
        => hostAuthorized
            && actionLifecycleEpoch == currentLifecycleEpoch;

    private BankOperationResult ExecuteETransfer()
    {
        Player? recipient = _actor.RunState.GetPlayer(_recipientId);
        return recipient is null
            ? BankOperationResult.Fail(BankErrorCode.InvalidAccount)
            : BankService.ETransfer(_actor, recipient, _amount);
    }

    public override INetAction ToNetAction()
    {
        return new TDBankNetOperationAction
        {
            Kind = _kind,
            Tier = _tier,
            LifecycleEpoch = _lifecycleEpoch,
            Amount = _amount,
            RecipientId = _recipientId,
            ExecutionType = _executionType,
            RequestId = _requestId,
            HostAuthorized = _hostAuthorized,
        };
    }

    public override string ToString()
        => $"TDBankOperationGameAction {_kind} owner={OwnerId} "
           + $"amount={_amount} recipient={_recipientId}";
}

public static class BankNetwork
{
    private const int MaxAcceptedRemoteRequestsPerRun = 4096;
    private static readonly object AcceptedRequestGate = new();
    private static readonly HashSet<(ulong ActorId, ulong RequestId)>
        AcceptedRemoteRequests = new();
    private static readonly ulong LocalRequestSessionPrefix =
        (ulong)(uint)RandomNumberGenerator.GetInt32(1, int.MaxValue) << 32;
    private static int _lifecycleEpoch = 1;
    private static long _nextLocalRequestId;
    private static long _pendingRequestId;

    public static void Initialize()
    {


    }

    internal static int CurrentLifecycleEpoch =>
        Volatile.Read(ref _lifecycleEpoch);

    internal static int AdvanceLifecycleEpoch()
    {
        while (true)
        {
            int current = Volatile.Read(ref _lifecycleEpoch);
            int next = current == int.MaxValue ? 1 : current + 1;
            if (Interlocked.CompareExchange(
                    ref _lifecycleEpoch,
                    next,
                    current) == current)
            {
                return next;
            }
        }
    }

    internal static int RestoreLifecycleEpoch(RunState runState)
    {
        ArgumentNullException.ThrowIfNull(runState);
        int restored = DeriveLifecycleEpoch(
            runState.TotalFloor,
            runState.Players.Select(BankStateStore.Get));
        Interlocked.Exchange(ref _lifecycleEpoch, restored);
        return restored;
    }

    internal static int DeriveLifecycleEpoch(
        int totalFloor,
        IEnumerable<AccountState> accountStates)
    {
        ArgumentNullException.ThrowIfNull(accountStates);

        long epoch = Math.Max(1, totalFloor);
        foreach (AccountState state in accountStates)
        {
            ArgumentNullException.ThrowIfNull(state);
            long lastSettled = Math.Max(
                state.LastSavingsTurnToken,
                state.LastDebtFloorToken);
            if (lastSettled >= 0)
            {
                epoch = Math.Max(epoch, lastSettled + 1L);
            }
        }

        return (int)Math.Min(int.MaxValue, epoch);
    }

    public static void SubmitApplyCard(CreditTier tier)
    {
        if (!TryGetLocalPlayer(out Player? player))
        {
            return;
        }

        if (!TryGetExecutionType(out GameActionType executionType))
        {
            return;
        }

        Submit(requestId => new TDBankOperationGameAction(
            player,
            BankOperationKind.ApplyCard,
            tier: tier,
            executionType: executionType,
            requestId: requestId));
    }

    public static void SubmitOpenAccount()
    {
        if (!TryGetLocalPlayer(out Player? player))
        {
            return;
        }

        if (!TryGetExecutionType(out GameActionType executionType))
        {
            return;
        }

        Submit(requestId => new TDBankOperationGameAction(
            player,
            BankOperationKind.OpenAccount,
            executionType: executionType,
            requestId: requestId));
    }

    public static void SubmitETransfer(ulong recipientId, int amount)
    {
        if (!TryGetLocalPlayer(out Player? player))
        {
            return;
        }

        if (!TryGetExecutionType(out GameActionType executionType))
        {
            return;
        }

        Submit(requestId => new TDBankOperationGameAction(
            player,
            BankOperationKind.ETransfer,
            amount: amount,
            recipientId: recipientId,
            executionType: executionType,
            requestId: requestId));
    }

    public static void SubmitSellKidneys(int quantity)
    {
        if (quantity <= 0)
        {
            SafeNotify("invalid_amount", isError: true);
            return;
        }
        if (!TryGetLocalPlayer(out Player? player))
        {
            return;
        }
        if (!TryGetExecutionType(out GameActionType executionType))
        {
            return;
        }

        Submit(requestId => new TDBankOperationGameAction(
            player,
            BankOperationKind.SellKidneys,
            amount: quantity,
            executionType: executionType,
            requestId: requestId));
    }

    public static void SubmitSellButt()
    {
        if (!TryGetLocalPlayer(out Player? player))
        {
            return;
        }
        if (!TryGetExecutionType(out GameActionType executionType))
        {
            return;
        }

        Submit(requestId => new TDBankOperationGameAction(
            player,
            BankOperationKind.SellButt,
            amount: 1,
            executionType: executionType,
            requestId: requestId));
    }

    private static bool TryGetLocalPlayer([NotNullWhen(true)] out Player? player)
    {
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        ulong? localId = LocalContext.NetId;
        player = runState is not null && localId.HasValue
            ? runState.GetPlayer(localId.Value)
            : null;
        if (player is null)
        {
            SafeNotify("no_active_run", isError: true);
            return false;
        }

        return true;
    }

    private static void Submit(Func<ulong, TDBankOperationGameAction> createAction)
    {
        if (!TryBeginLocalRequest(out ulong requestId))
        {
            SafeNotify("pending_request", isError: true);
            return;
        }

        try
        {
            TDBankOperationGameAction action = createAction(requestId);
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
        }
        catch (Exception exception)
        {
            CompleteLocalRequest(requestId);
            SafeLogError($"Could not enqueue TD Bank action: {exception}");
            SafeNotify("error_rejected", isError: true);
            return;
        }

        SafeNotify("request_sent", isError: false);
    }

    internal static void CompleteLocalRequest(ulong requestId)
    {
        if (requestId == 0)
        {
            return;
        }

        long rawRequestId = unchecked((long)requestId);
        Interlocked.CompareExchange(
            ref _pendingRequestId,
            0,
            rawRequestId);
    }

    internal static void ResetLocalRequest()
    {
        Interlocked.Exchange(ref _pendingRequestId, 0);
    }

    internal static void ResetRunState()
    {
        ResetLocalRequest();
        Interlocked.Exchange(ref _lifecycleEpoch, 1);
        lock (AcceptedRequestGate)
        {
            AcceptedRemoteRequests.Clear();
        }
    }

    internal static bool TryAcceptHostRequest(
        ulong actorId,
        ulong requestId)
    {
        if (requestId == 0)
        {
            return false;
        }

        lock (AcceptedRequestGate)
        {
            if (AcceptedRemoteRequests.Count
                >= MaxAcceptedRemoteRequestsPerRun)
            {
                return false;
            }

            return AcceptedRemoteRequests.Add((actorId, requestId));
        }
    }

    private static bool TryBeginLocalRequest(out ulong requestId)
    {
        if (Interlocked.Read(ref _pendingRequestId) != 0)
        {
            requestId = 0;
            return false;
        }

        long nextCounter = Interlocked.Increment(ref _nextLocalRequestId);
        uint counter = unchecked((uint)nextCounter);
        if (counter == 0)
        {


            Interlocked.Exchange(ref _nextLocalRequestId, 1);
            counter = 1;
        }

        ulong nextRequestId = LocalRequestSessionPrefix | counter;
        long nextId = unchecked((long)nextRequestId);
        if (Interlocked.CompareExchange(
                ref _pendingRequestId,
                nextId,
                0) != 0)
        {
            requestId = 0;
            return false;
        }

        requestId = nextRequestId;
        return true;
    }

    private static bool TryGetExecutionType(out GameActionType executionType)
    {
        if (FloorTransitionGate.IsActive)
        {
            executionType = GameActionType.None;
            SafeNotify("error_unavailable_timing", isError: true);
            return false;
        }

        executionType =
            RunManager.Instance.ActionQueueSynchronizer.CombatState switch
            {
                ActionSynchronizerCombatState.PlayPhase =>
                    GameActionType.CombatPlayPhaseOnly,
                ActionSynchronizerCombatState.NotInCombat =>
                    GameActionType.NonCombat,
                _ =>
                    GameActionType.None,
            };
        if (executionType != GameActionType.None)
        {
            return true;
        }

        SafeNotify("error_unavailable_timing", isError: true);
        return false;
    }

    private static void SafeNotify(string key, bool isError)
    {
        try
        {
            BankUiBridge.Notify(BankUiText.Get(key), isError);
        }
        catch (Exception exception)
        {
            SafeLogError($"Could not show TD Bank notification '{key}': {exception}");
        }
    }

    private static void SafeLogError(string message)
    {
        try
        {
            MainFile.Logger.Error(message);
        }
        catch
        {

        }
    }
}
