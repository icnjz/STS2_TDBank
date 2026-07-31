using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using TDBank.TDBankCode.Banking;
using TDBank.TDBankCode.Compatibility;
using TDBank.TDBankCode.Integration;
using TDBank.TDBankCode.Networking;
using TDBank.TDBankCode.UI;
using TDLib;
using TDLib.Saves;

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        SmokeFailures.Messages.Add(message);
        Console.Error.WriteLine($"FAIL: {message}");
    }
}

static IReadOnlyDictionary<string, int> SnapshotRunRngCounters(
    IRunState runState)
{
    var rngs = (System.Collections.IDictionary)(
        typeof(RunRngSet)
            .GetField(
                "_rngs",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(runState.Rng)
        ?? throw new InvalidOperationException(
            "The public-beta RunRngSet dictionary is unavailable."));
    FieldInfo counter = typeof(Rng).GetField(
        "_counter",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? typeof(Rng).GetField(
            "<Counter>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(
            typeof(Rng).FullName,
            "counter");
    var snapshot = new Dictionary<string, int>(
        StringComparer.Ordinal);
    System.Collections.IDictionaryEnumerator enumerator =
        rngs.GetEnumerator();
    while (enumerator.MoveNext())
    {
        System.Collections.DictionaryEntry entry = enumerator.Entry;
        string name = entry.Key.ToString()
            ?? throw new InvalidOperationException(
                "A run RNG type has no stable name.");
        snapshot[name] = (int)(
            counter.GetValue(entry.Value)
            ?? throw new InvalidOperationException(
                "A run RNG counter is unavailable."));
    }

    return snapshot;
}

static bool SameRunRngCounters(
    IReadOnlyDictionary<string, int> left,
    IReadOnlyDictionary<string, int> right)
{
    return left.Count == right.Count
        && left.All(pair =>
            right.TryGetValue(pair.Key, out int counter)
            && counter == pair.Value);
}

static Player NewPlayer(
    int gold,
    ulong netId,
    int? currentHp = null,
    int? maxHp = null)
{
    var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
    typeof(Player)
        .GetField(
            "<NetId>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(player, netId);
    player.Gold = gold;

    if (currentHp.HasValue || maxHp.HasValue)
    {
        int current = currentHp
            ?? throw new ArgumentException("Current HP is required.");
        int maximum = maxHp
            ?? throw new ArgumentException("Maximum HP is required.");
        typeof(Player)
            .GetField(
                "<Creature>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(player, new Creature(player, current, maximum));
    }

    BankStateStore.Set(player, new AccountState());
    return player;
}

static Player OpenPlayer(
    int gold,
    ulong netId,
    int? currentHp = null,
    int? maxHp = null)
{
    Player player = NewPlayer(gold, netId, currentHp, maxHp);
    BankOperationResult opened = BankService.OpenBankAccount(player);
    if (!opened.Success)
    {
        throw new InvalidOperationException(
            $"Could not create opened-account fixture: {opened.Error}");
    }

    return player;
}

static Player OpenCardPlayer(
    CreditTier tier,
    ulong netId,
    int gold = 0,
    int? currentHp = null,
    int? maxHp = null)
{
    Player player = OpenPlayer(gold, netId, currentHp, maxHp);
    int missingQualification = Math.Max(
        0,
        BankService.GetQualificationThreshold(player, tier)
        - BankService.GetQualifyingEarned(player));
    if (missingQualification > 0)
    {
        BankOperationResult earned =
            BankService.RecordGoldEarned(player, missingQualification);
        if (!earned.Success)
        {
            throw new InvalidOperationException(
                $"Could not qualify card fixture: {earned.Error}");
        }
    }

    BankOperationResult applied =
        BankService.ApplyForCreditCard(player, tier);
    if (!applied.Success)
    {
        throw new InvalidOperationException(
            $"Could not open card fixture: {applied.Error}");
    }

    return player;
}

static RunState AttachSyntheticRunState(
    int ascensionLevel,
    params Player[] players)
{
    var runState = (RunState)RuntimeHelpers.GetUninitializedObject(
        typeof(RunState));
    typeof(RunState)
        .GetField(
            "_players",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(runState, players.ToList());
    typeof(RunState)
        .GetField(
            "<AscensionLevel>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(runState, ascensionLevel);
    typeof(RunState)
        .GetField(
            "<Rng>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(
            runState,
            new RunRngSet(
                $"td-bank-smoke-a{ascensionLevel}-"
                + string.Join("-", players.Select(player => player.NetId))));
    foreach (string emptyListFieldName in new[]
    {
        "<Modifiers>k__BackingField",
        "<BadgeModels>k__BackingField",
    })
    {
        FieldInfo emptyListField = typeof(RunState).GetField(
            emptyListFieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(
                typeof(RunState).FullName,
                emptyListFieldName);
        Type elementType =
            emptyListField.FieldType.GetGenericArguments().Single();
        emptyListField.SetValue(
            runState,
            Array.CreateInstance(elementType, 0));
    }

    FieldInfo scalingField = typeof(RunState).GetField(
        "<MultiplayerScalingModel>k__BackingField",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(
            typeof(RunState).FullName,
            "<MultiplayerScalingModel>k__BackingField");
    object scalingModel = RuntimeHelpers.GetUninitializedObject(
        scalingField.FieldType);
    scalingField.SetValue(runState, scalingModel);
    scalingField.FieldType
        .GetMethod(
            "Initialize",
            BindingFlags.Instance | BindingFlags.Public)!
        .Invoke(scalingModel, new object[] { runState });

    FieldInfo playerRunState = typeof(Player).GetField(
        "_runState",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(
            typeof(Player).FullName,
            "_runState");
    foreach (Player player in players)
    {
        playerRunState.SetValue(player, runState);
    }

    return runState;
}

static Player OpenAscensionPlayer(
    int ascensionLevel,
    int gold,
    ulong netId,
    int? currentHp = null,
    int? maxHp = null)
{
    Player player = NewPlayer(gold, netId, currentHp, maxHp);
    _ = AttachSyntheticRunState(ascensionLevel, player);
    BankOperationResult opened = BankService.OpenBankAccount(player);
    if (!opened.Success)
    {
        throw new InvalidOperationException(
            $"Could not create A{ascensionLevel} opened-account fixture: "
            + opened.Error);
    }

    return player;
}

static Player OpenFourthButtSaleFixture(
    ButtRiskOutcome wantedOutcome,
    int ascensionLevel,
    int gold,
    int currentHp,
    int maxHp,
    ulong firstNetId)
{
    for (ulong offset = 0; offset < 10_000; offset++)
    {
        Player candidate = OpenAscensionPlayer(
            ascensionLevel,
            gold,
            checked(firstNetId + offset),
            currentHp,
            maxHp);
        BankStateStore.Get(candidate).ButtSalesCount = 3;
        if (KkCompoundService.GetButtRiskOutcomeForNextSale(candidate)
            == wantedOutcome)
        {
            return candidate;
        }
    }

    throw new InvalidOperationException(
        $"Could not find deterministic {wantedOutcome} fixture.");
}

static Player OpenAscensionCardPlayer(
    int ascensionLevel,
    CreditTier tier,
    ulong netId,
    int gold = 0,
    int? currentHp = null,
    int? maxHp = null)
{
    Player player = OpenAscensionPlayer(
        ascensionLevel,
        gold,
        netId,
        currentHp,
        maxHp);
    int missingQualification = Math.Max(
        0,
        BankService.GetQualificationThreshold(player, tier)
        - BankService.GetQualifyingEarned(player));
    if (missingQualification > 0)
    {
        BankOperationResult earned =
            BankService.RecordGoldEarned(player, missingQualification);
        if (!earned.Success)
        {
            throw new InvalidOperationException(
                $"Could not qualify A{ascensionLevel} card fixture: "
                + earned.Error);
        }
    }

    BankOperationResult applied =
        BankService.ApplyForCreditCard(player, tier);
    if (!applied.Success)
    {
        throw new InvalidOperationException(
            $"Could not open A{ascensionLevel} card fixture: "
            + applied.Error);
    }

    return player;
}

static BankUiSnapshot UiSnapshotFor(
    AscensionBankBenefits benefits,
    int buttSalesCount = 0)
{
    return new BankUiSnapshot
    {
        AscensionLevel = benefits.AscensionLevel,
        IsAccountOpened = true,
        SavingsInterestRateBasisPoints =
            benefits.SavingsInterestBasisPoints,
        SavingsInterestCap =
            benefits.SavingsInterestCap,
        CreditOffers =
        [
            new(
                BankCreditTier.Starter,
                benefits.PoorQualification,
                benefits.PoorCreditLimit,
                benefits.GetMaximumDebt(CreditTier.VisaPoor),
                benefits.PoorDebtInterestBasisPoints),
            new(
                BankCreditTier.MiddleClass,
                benefits.MiddleClassQualification,
                benefits.MiddleClassCreditLimit,
                benefits.GetMaximumDebt(CreditTier.VisaMiddleClass),
                benefits.MiddleClassDebtInterestBasisPoints),
            new(
                BankCreditTier.NouveauRiche,
                benefits.TycoonQualification,
                benefits.TycoonCreditLimit,
                benefits.GetMaximumDebt(CreditTier.VisaTycoon),
                benefits.TycoonDebtInterestBasisPoints),
        ],
        DebtGraceFloorCount = benefits.DebtGraceFloorCount,
        RelicGoldPerSeizure =
            benefits.RelicLiquidationGoldPerRelic,
        RelicSeizureCap =
            benefits.RelicLiquidationMaximumRelics == int.MaxValue
                ? 0
                : benefits.RelicLiquidationMaximumRelics,
        KidneyHpCost = benefits.KidneyHpCost,
        KidneyGoldValue = benefits.KidneyGoldValue,
        ButtHpCost = benefits.ButtHpCost,
        ButtGoldValue = benefits.ButtGoldValue,
        ButtSalesCount = buttSalesCount,
    };
}

static T InvokeOverlayPureMethod<T>(
    BankUiSnapshot snapshot,
    string methodName,
    params object[] arguments)
{
    var overlay = (BankOverlay)RuntimeHelpers.GetUninitializedObject(
        typeof(BankOverlay));
    typeof(BankOverlay)
        .GetField(
            "_snapshot",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(overlay, snapshot);
    MethodInfo method = typeof(BankOverlay).GetMethod(
        methodName,
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(
            typeof(BankOverlay).FullName,
            methodName);
    return (T)(method.Invoke(overlay, arguments)
        ?? throw new InvalidOperationException(
            $"{methodName} returned null."));
}

static TDBankNetOperationAction RoundTrip(
    TDBankNetOperationAction source)
{
    var writer = new PacketWriter();
    source.Serialize(writer);
    var reader = new PacketReader();
    reader.Reset(writer.Buffer);
    var copy = new TDBankNetOperationAction();
    copy.Deserialize(reader);
    return copy;
}

static void ExpectSamePayload(
    TDBankNetOperationAction expected,
    TDBankNetOperationAction actual,
    string message)
{
    Expect(
        actual.Kind == expected.Kind
        && actual.Tier == expected.Tier
        && actual.LifecycleEpoch == expected.LifecycleEpoch
        && actual.Amount == expected.Amount
        && actual.RecipientId == expected.RecipientId
        && actual.ExecutionType == expected.ExecutionType
        && actual.RequestId == expected.RequestId
        && actual.HostAuthorized == expected.HostAuthorized
        && actual.HasAuthoritativeState
            == expected.HasAuthoritativeState
        && actual.AuthoritativeButtOutcome
            == expected.AuthoritativeButtOutcome
        && SameAuthoritativePlayerState(
            actual.ActorState,
            expected.ActorState)
        && SameAuthoritativePlayerState(
            actual.RecipientState,
            expected.RecipientState),
        message);
}

static bool SameAuthoritativePlayerState(
    TDBankAuthoritativePlayerState? left,
    TDBankAuthoritativePlayerState? right)
{
    if (left is null || right is null)
    {
        return left is null && right is null;
    }

    PropertyInfo[] properties = typeof(AccountState)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property =>
            property.PropertyType == typeof(int)
            && property.CanRead)
        .ToArray();
    return left.Gold == right.Gold
        && left.CurrentHp == right.CurrentHp
        && left.MaxHp == right.MaxHp
        && properties.All(property =>
            Equals(
                property.GetValue(left.Account),
                property.GetValue(right.Account)));
}

static bool HasPatchOwner(
    IEnumerable<Patch> patches,
    string owner)
{
    return patches.Any(patch => patch.owner == owner);
}

static void VerifyCompletedFloorPatchCanApply()
{
    MethodBase target =
        AccessTools.DeclaredMethod(typeof(RunManager), "ExitCurrentRoom")
        ?? throw new MissingMethodException(
            typeof(RunManager).FullName,
            "ExitCurrentRoom");
    const string owner = "TDBank.Tests.CompletedFloorPatch";
    var harmony = new Harmony(owner);

    try
    {
        harmony
            .CreateClassProcessor(typeof(CompletedMapFloorBankingPatch))
            .Patch();
        Patches? patchInfo = Harmony.GetPatchInfo(target);
        Expect(
            patchInfo is not null
            && HasPatchOwner(patchInfo.Prefixes, owner)
            && HasPatchOwner(patchInfo.Postfixes, owner),
            "Completed-floor Prefix/Postfix did not patch "
            + "RunManager.ExitCurrentRoom on public beta v0.109.1.");
    }
    finally
    {
        harmony.UnpatchAll(owner);
    }

    Patches? afterUnpatch = Harmony.GetPatchInfo(target);
    Expect(
        afterUnpatch is null
        || (!HasPatchOwner(afterUnpatch.Prefixes, owner)
            && !HasPatchOwner(afterUnpatch.Postfixes, owner)),
        "Completed-floor Harmony patches were not removed.");
}

static void VerifyExactGainGoldPatchCanApply()
{
    MethodInfo original =
        AccessTools.Method(
            typeof(PlayerCmd),
            nameof(PlayerCmd.GainGold),
            new[] { typeof(decimal), typeof(Player), typeof(bool) })
        ?? throw new MissingMethodException(
            typeof(PlayerCmd).FullName,
            nameof(PlayerCmd.GainGold));
    MethodBase moveNext =
        AccessTools.AsyncMoveNext(original)
        ?? throw new MissingMethodException(
            original.DeclaringType?.FullName,
            $"{original.Name}.MoveNext");
    const string owner = "TDBank.Tests.ExactGainGold";
    var harmony = new Harmony(owner);

    try
    {
        harmony
            .CreateClassProcessor(typeof(NativeGoldInitializationPatch))
            .Patch();
        harmony
            .CreateClassProcessor(typeof(QualifyingGoldPatch))
            .Patch();
        Patches? outerPatchInfo = Harmony.GetPatchInfo(original);
        Patches? moveNextPatchInfo = Harmony.GetPatchInfo(moveNext);
        Expect(
            outerPatchInfo is not null
            && HasPatchOwner(outerPatchInfo.Prefixes, owner)
            && moveNextPatchInfo is not null
            && HasPatchOwner(moveNextPatchInfo.Transpilers, owner),
            "Exact native-gold Prefix/MoveNext transpiler did not patch "
            + "PlayerCmd.GainGold on public beta v0.109.1.");
    }
    finally
    {
        harmony.UnpatchAll(owner);
    }

    Patches? outerAfterUnpatch = Harmony.GetPatchInfo(original);
    Patches? moveNextAfterUnpatch = Harmony.GetPatchInfo(moveNext);
    Expect(
        (outerAfterUnpatch is null
            || !HasPatchOwner(outerAfterUnpatch.Prefixes, owner))
        && (moveNextAfterUnpatch is null
            || !HasPatchOwner(moveNextAfterUnpatch.Transpilers, owner)),
        "Exact native-gold Harmony patches were not removed.");
}

static void VerifyNextActFloorPatchCanApply()
{
    MethodBase target =
        AccessTools.DeclaredMethod(
            typeof(ActChangeSynchronizer),
            "MoveToNextAct")
        ?? throw new MissingMethodException(
            typeof(ActChangeSynchronizer).FullName,
            "MoveToNextAct");
    const string owner = "TDBank.Tests.NextActFloor";
    var harmony = new Harmony(owner);

    try
    {
        harmony
            .CreateClassProcessor(typeof(NextActFloorBankingPatch))
            .Patch();
        Patches? patchInfo = Harmony.GetPatchInfo(target);
        Expect(
            patchInfo is not null
            && HasPatchOwner(patchInfo.Prefixes, owner),
            "Next-act floor Prefix did not patch "
            + "ActChangeSynchronizer.MoveToNextAct.");
    }
    finally
    {
        harmony.UnpatchAll(owner);
    }

    Patches? afterUnpatch = Harmony.GetPatchInfo(target);
    Expect(
        afterUnpatch is null
        || !HasPatchOwner(afterUnpatch.Prefixes, owner),
        "Next-act floor Harmony patch was not removed.");
}

static void VerifyFreshRunAccountResetPatchCanApply()
{
    MethodInfo targetMethodsFactory =
        typeof(FreshRunBankAccountResetPatch).GetMethod(
            "TargetMethods",
            BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(
            typeof(FreshRunBankAccountResetPatch).FullName,
            "TargetMethods");
    MethodBase[] targets =
        ((IEnumerable<MethodBase>?)targetMethodsFactory.Invoke(null, null)
            ?? throw new InvalidOperationException(
                "Fresh-run patch returned no target sequence."))
        .ToArray();
    string[] targetNames = targets
        .Select(method => method.Name)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();
    string[] savedSetupNames =
    {
        nameof(RunManager.SetUpSavedMultiplayer),
        nameof(RunManager.SetUpSavedSingleplayer),
        nameof(RunManager.SetUpReplay),
    };

    Expect(
        targetNames.SequenceEqual(
            new[]
            {
                nameof(RunManager.SetUpNewMultiplayer),
                nameof(RunManager.SetUpNewSingleplayer),
            })
        && targets.All(method => method.DeclaringType == typeof(RunManager))
        && !targets.Any(method =>
            savedSetupNames.Contains(
                method.Name,
                StringComparer.Ordinal)),
        "Fresh-run account reset patch targets anything other than the two "
        + "new-run setup methods, or includes a saved-run/replay setup.");

    const string owner = "TDBank.Tests.FreshRunAccountReset";
    var harmony = new Harmony(owner);
    try
    {
        harmony
            .CreateClassProcessor(typeof(FreshRunBankAccountResetPatch))
            .Patch();
        Expect(
            targets.All(target =>
            {
                Patches? patchInfo = Harmony.GetPatchInfo(target);
                return patchInfo is not null
                    && HasPatchOwner(patchInfo.Postfixes, owner);
            }),
            "Fresh-run reset postfix did not patch both new-run setup methods.");

        foreach (string savedSetupName in savedSetupNames)
        {
            foreach (MethodInfo savedSetup in typeof(RunManager)
                .GetMethods(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic)
                .Where(method =>
                    method.Name == savedSetupName))
            {
                Patches? patchInfo = Harmony.GetPatchInfo(savedSetup);
                Expect(
                    patchInfo is null
                    || !HasPatchOwner(patchInfo.Postfixes, owner),
                    $"Fresh-run reset patch was attached to {savedSetupName}.");
            }
        }
    }
    finally
    {
        harmony.UnpatchAll(owner);
    }

    Expect(
        targets.All(target =>
        {
            Patches? patchInfo = Harmony.GetPatchInfo(target);
            return patchInfo is null
                || !HasPatchOwner(patchInfo.Postfixes, owner);
        }),
        "Fresh-run account reset Harmony postfixes were not removed.");
}

static void VerifyAbandonPatchCanApply()
{
    MethodInfo original =
        AccessTools.Method(
            typeof(CreatureCmd),
            "KillWithoutCheckingWinCondition",
            new[] { typeof(Creature), typeof(bool), typeof(int) })
        ?? throw new MissingMethodException(
            typeof(CreatureCmd).FullName,
            "KillWithoutCheckingWinCondition");
    MethodBase target =
        AccessTools.AsyncMoveNext(original)
        ?? throw new MissingMethodException(
            original.DeclaringType?.FullName,
            $"{original.Name}.MoveNext");
    const string owner = "TDBank.Tests.AbandonCompatibility";
    var harmony = new Harmony(owner);

    try
    {
        harmony
            .CreateClassProcessor(typeof(AbandonRunCompatibilityPatch))
            .Patch();
        Patches? patchInfo = Harmony.GetPatchInfo(target);
        Expect(
            patchInfo is not null
            && HasPatchOwner(patchInfo.Transpilers, owner),
            "Abandon compatibility transpiler did not patch the "
            + "public-beta async death guard.");
    }
    finally
    {
        harmony.UnpatchAll(owner);
    }

    Patches? afterUnpatch = Harmony.GetPatchInfo(target);
    Expect(
        afterUnpatch is null
        || !HasPatchOwner(afterUnpatch.Transpilers, owner),
        "Abandon compatibility transpiler was not removed.");
}

static void VerifyEventCreditAvailabilityPatchCanApply()
{
    IReadOnlyList<MethodBase> targets =
        EventCreditAvailabilityPatch.GetValidatedTargets();
    const string owner = "TDBank.Tests.EventCreditAvailability";
    var harmony = new Harmony(owner);

    Expect(
        targets.Count == 16
        && targets.Count(method =>
            method.Name.StartsWith(
                "<IsAllowed>b__",
                StringComparison.Ordinal)) == 10
        && targets.Count(method =>
            method.Name == "GenerateInitialOptions"
            || method.Name
                == "GenerateGrabSomethingOffTheBeltOption") == 6,
        "The public-beta event-credit target set is not locked to "
        + "10 IsAllowed lambdas and 6 option methods.");

    try
    {
        harmony
            .CreateClassProcessor(typeof(EventCreditAvailabilityPatch))
            .Patch();
        foreach (MethodBase target in targets)
        {
            Patches? patchInfo = Harmony.GetPatchInfo(target);
            Expect(
                patchInfo is not null
                && HasPatchOwner(patchInfo.Transpilers, owner),
                "Event-credit transpiler did not patch "
                + $"{target.DeclaringType?.FullName}.{target.Name} "
                + "on public beta v0.109.1.");
        }
    }
    finally
    {
        harmony.UnpatchAll(owner);
    }

    Expect(
        targets.All(target =>
        {
            Patches? patchInfo = Harmony.GetPatchInfo(target);
            return patchInfo is null
                || !HasPatchOwner(patchInfo.Transpilers, owner);
        }),
        "Event-credit Harmony transpilers were not removed.");
}

static void VerifyAllTDBankPatchesCanApply()
{
    const string owner = "TDBank.Tests.FullPatchMatrix";
    var harmony = new Harmony(owner);
    try
    {
        harmony.PatchAll(typeof(TDBank.TDBankCode.MainFile).Assembly);
        MethodBase[] ownedTargets = Harmony.GetAllPatchedMethods()
            .Where(method =>
            {
                Patches? info = Harmony.GetPatchInfo(method);
                return info is not null
                    && (HasPatchOwner(info.Prefixes, owner)
                        || HasPatchOwner(info.Postfixes, owner)
                        || HasPatchOwner(info.Transpilers, owner)
                        || HasPatchOwner(info.Finalizers, owner));
            })
            .ToArray();
        Expect(
            ownedTargets.Length >= 30,
            $"The full TD Bank Harmony matrix patched only {ownedTargets.Length} targets.");
    }
    finally
    {
        harmony.UnpatchAll(owner);
    }
}

BankStateStore.Register();
var persistedStateField = (SavedPlayerField<AccountState>)(
    typeof(BankStateStore).GetField(
        "StateField",
        BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)
    ?? throw new InvalidOperationException(
        "TD Bank's persisted player field was not initialized."));
Expect(
    persistedStateField.Name == "Player_TDBank_AccountState"
    && persistedStateField.JsonPropertyName
        == "save_dict_TDBank.TDBankCode.Banking.AccountState"
    && persistedStateField.EntryKey
        == "spirefield_Player_TDBank_AccountState",
    "TDLib changed TD Bank's legacy BaseLib-compatible JSON field names.");

AssemblyName[] tdBankReferences =
    typeof(BankStateStore).Assembly.GetReferencedAssemblies();
Expect(
    tdBankReferences.Any(reference => reference.Name == "TDLib")
    && tdBankReferences.All(reference => reference.Name != "BaseLib"),
    "TDBank.dll does not reference TDLib exclusively.");
Expect(
    typeof(SavedPlayerField<>).Assembly.GetName().Name == "TDLib"
    && typeof(SavedPlayerField<>).Assembly.GetReferencedAssemblies()
        .All(reference => reference.Name != "BaseLib"),
    "TDLib.dll still references or identifies as BaseLib.");



Type serializerContextPatchTargetType = AccessTools.TypeByName(
        "MegaCrit.Sts2.Core.Saves.MegaCritSerializerContext")
    ?? throw new TypeLoadException(
        "The public-beta MegaCritSerializerContext type is unavailable.");
MethodBase serializablePlayerPropInit = AccessTools.DeclaredMethod(
        serializerContextPatchTargetType,
        "SerializablePlayerPropInit",
        [typeof(JsonSerializerOptions)])
    ?? throw new MissingMethodException(
        serializerContextPatchTargetType.FullName,
        "SerializablePlayerPropInit");
const string baseLibCompatibilityOwner = "BaseLib";
var baseLibCompatibilityHarmony = new Harmony(baseLibCompatibilityOwner);
string? baseLibCompatibilityDll = Environment.GetEnvironmentVariable(
    "TDBANK_BASELIB_COMPAT_DLL");
bool isRealBaseLibCompatibilityTest =
    !string.IsNullOrWhiteSpace(baseLibCompatibilityDll)
    && File.Exists(baseLibCompatibilityDll);
if (isRealBaseLibCompatibilityTest)
{
    Assembly baseLibAssembly = Assembly.LoadFrom(baseLibCompatibilityDll!);
    Type extendedSavePatches = baseLibAssembly.GetType(
            "BaseLib.Patches.Saves.ExtendedSavePatches",
            throwOnError: true)!
        ?? throw new TypeLoadException(
            "BaseLib ExtendedSavePatches is unavailable.");
    MethodInfo patchExtendedSaveContexts =
        extendedSavePatches.GetMethod(
            "Patch",
            BindingFlags.Public | BindingFlags.Static)
        ?? throw new MissingMethodException(
            extendedSavePatches.FullName,
            "Patch");
    patchExtendedSaveContexts.Invoke(
        null,
        [baseLibCompatibilityHarmony]);

    foreach (string patchTypeName in new[]
             {
                 "PrepExtendedPlayerData",
                 "LoadExtendedPlayerData",
                 "SerializeExtendedPlayerData",
                 "DeserializeExtendedPlayerData",
             })
    {
        Type patchType = extendedSavePatches.GetNestedType(
                patchTypeName,
                BindingFlags.NonPublic)
            ?? throw new TypeLoadException(
                $"{extendedSavePatches.FullName}+{patchTypeName}");
        baseLibCompatibilityHarmony
            .CreateClassProcessor(patchType)
            .Patch();
    }

    Type extendedSaveTypes = baseLibAssembly.GetType(
            "BaseLib.Patches.Saves.ExtendedSaveTypes",
            throwOnError: true)!
        ?? throw new TypeLoadException(
            "BaseLib ExtendedSaveTypes is unavailable.");
    baseLibCompatibilityHarmony
        .CreateClassProcessor(extendedSaveTypes)
        .Patch();
}
else
{
    baseLibCompatibilityHarmony.Patch(
        serializablePlayerPropInit,
        postfix: new HarmonyMethod(
            typeof(BaseLibSavePatchProbe),
            nameof(BaseLibSavePatchProbe.AdjustPropArray)));
}

new Harmony(TDLibMain.ModId).PatchAll(
    typeof(SavedPlayerField<>).Assembly);
MethodBase[] tdLibPatchedMethods = Harmony.GetAllPatchedMethods()
    .Where(method =>
    {
        Patches? info = Harmony.GetPatchInfo(method);
        return info is not null
            && info.Postfixes.Any(patch => patch.owner == TDLibMain.ModId);
    })
    .ToArray();
Expect(
    tdLibPatchedMethods.Length == 5
    && tdLibPatchedMethods.Any(method =>
        method.DeclaringType?.Name == "MegaCritSerializerContext"
        && method.Name.Contains("GetTypeInfo", StringComparison.Ordinal))
    && tdLibPatchedMethods.All(method =>
        method != serializablePlayerPropInit)
    && tdLibPatchedMethods.Any(method =>
        method.DeclaringType?.Name == "Player"
        && method.Name == "ToSerializable")
    && tdLibPatchedMethods.Any(method =>
        method.DeclaringType?.Name == "Player"
        && method.Name == "FromSerializable")
    && tdLibPatchedMethods.Any(method =>
        method.DeclaringType?.Name == "SerializablePlayer"
        && method.Name == "Serialize")
    && tdLibPatchedMethods.Any(method =>
        method.DeclaringType?.Name == "SerializablePlayer"
        && method.Name == "Deserialize"),
    "TDLib did not apply exactly its five isolated save/packet postfixes.");
Patches? serializablePlayerPropInitPatches =
    Harmony.GetPatchInfo(serializablePlayerPropInit);
Expect(
    serializablePlayerPropInitPatches is not null
    && serializablePlayerPropInitPatches.Postfixes.Any(
        patch => patch.owner == baseLibCompatibilityOwner)
    && serializablePlayerPropInitPatches.Postfixes.All(
        patch => patch.owner != TDLibMain.ModId),
    "TDLib patched BaseLib's fragile SerializablePlayerPropInit target.");

if (isRealBaseLibCompatibilityTest)
{
    baseLibCompatibilityHarmony.UnpatchAll(baseLibCompatibilityOwner);
}

Type serializerContextType = AccessTools.TypeByName(
    "MegaCrit.Sts2.Core.Saves.MegaCritSerializerContext")
    ?? throw new TypeLoadException(
        "The public-beta MegaCritSerializerContext type is unavailable.");
var serializerContext = (JsonSerializerContext)(
    serializerContextType.GetProperty(
        "Default",
        BindingFlags.Public | BindingFlags.Static)!.GetValue(null)
    ?? throw new InvalidOperationException(
        "The game serializer context default instance is unavailable."));

JsonTypeInfo? accountStateTypeInfo =
    serializerContext.GetTypeInfo(typeof(AccountState));
Expect(
    accountStateTypeInfo is not null,
    "TDLib did not supply AccountState JSON metadata to the game serializer.");
if (accountStateTypeInfo is not null)
{
    var saveFixture = new AccountState
    {
        Schema = 7,
        QualifyingEarned = 1234,
        SavingsPrincipal = 567,
        SavingsInterest = 89,
        CreditTier = (int)CreditTier.VisaMiddleClass,
        CreditDebt = 321,
        BankAccountOpened = 1,
        ButtSalesCount = 4,
    };
    string saveJson = JsonSerializer.Serialize(
        saveFixture,
        accountStateTypeInfo);
    var restoredFixture = (AccountState?)JsonSerializer.Deserialize(
        saveJson,
        accountStateTypeInfo);
    Expect(
        restoredFixture is not null
        && restoredFixture.Schema == saveFixture.Schema
        && restoredFixture.QualifyingEarned
            == saveFixture.QualifyingEarned
        && restoredFixture.SavingsPrincipal
            == saveFixture.SavingsPrincipal
        && restoredFixture.SavingsInterest
            == saveFixture.SavingsInterest
        && restoredFixture.CreditTier == saveFixture.CreditTier
        && restoredFixture.CreditDebt == saveFixture.CreditDebt
        && restoredFixture.BankAccountOpened
            == saveFixture.BankAccountOpened
        && restoredFixture.ButtSalesCount
            == saveFixture.ButtSalesCount,
        "TDLib's AccountState JSON metadata failed a value round trip.");
}

Type serializablePlayerType = AccessTools.TypeByName(
    "MegaCrit.Sts2.Core.Saves.Runs.SerializablePlayer")
    ?? throw new TypeLoadException(
        "The public-beta SerializablePlayer type is unavailable.");
JsonTypeInfo? serializablePlayerTypeInfo =
    serializerContext.GetTypeInfo(serializablePlayerType);
JsonTypeInfo? serializablePlayerTypeInfoAgain =
    serializerContext.GetTypeInfo(serializablePlayerType);
int legacyBankPropertyCount =
    serializablePlayerTypeInfoAgain?.Properties.Count(property =>
        property.Name
            == "save_dict_TDBank.TDBankCode.Banking.AccountState")
    ?? 0;
JsonPropertyInfo? legacyBankProperty =
    serializablePlayerTypeInfo?.Properties.FirstOrDefault(property =>
        property.Name
            == "save_dict_TDBank.TDBankCode.Banking.AccountState");
Expect(
    legacyBankProperty is not null
    && legacyBankPropertyCount == 1,
    "TDLib did not inject TD Bank's legacy JSON property into player saves.");
if (legacyBankProperty is not null)
{
    object serializableFixture =
        RuntimeHelpers.GetUninitializedObject(serializablePlayerType);
    var importedFields = new Dictionary<string, AccountState>(
        StringComparer.Ordinal)
    {
        ["spirefield_Player_TDBank_AccountState"] = new AccountState
        {
            BankAccountOpened = 1,
            CreditDebt = 246,
            SavingsPrincipal = 135,
        },
    };
    legacyBankProperty.Set!(serializableFixture, importedFields);
    var exportedFields =
        (Dictionary<string, AccountState>?)legacyBankProperty.Get!(
            serializableFixture);
    Expect(
        exportedFields is not null
        && exportedFields.Count == 1
        && exportedFields.TryGetValue(
            "spirefield_Player_TDBank_AccountState",
            out AccountState? exportedState)
        && exportedState.BankAccountOpened == 1
        && exportedState.CreditDebt == 246
        && exportedState.SavingsPrincipal == 135,
        "TDLib did not preserve TD Bank's legacy nested player-save entry.");
}




Player unopened = NewPlayer(50, 1, 50, 70);
unopened.Gold += 20;
BankOperationResult unopenedNative =
    BankService.RecordNativeGoldGainAmount(
        unopened,
        20,
        wasStolenBack: false);
_ = BankService.InitializeUnifiedSavings(unopened);
_ = BankService.InitializeQualification(unopened);
_ = BankService.AccrueSavingsInterest(unopened, 101);
_ = BankService.AccrueDebtInterest(unopened, 101);
AccountState unopenedState = BankStateStore.Get(unopened).Clone();
Expect(
    unopenedNative.Success
    && unopenedNative.Amount == 20
    && unopened.Gold == 70
    && unopenedState.BankAccountOpened == 0
    && unopenedState.UnifiedSavingsInitialized == 0
    && unopenedState.QualificationInitialized == 0
    && unopenedState.QualifyingEarned == 0
    && unopenedState.SavingsPrincipal == 0
    && unopenedState.SavingsInterest == 0,
    "Pre-opening gold changed qualification or initialized savings.");
Expect(
    BankService.DepositGold(
        unopened,
        10,
        GoldIncomeSource.NormalGameGold).Error
        == BankErrorCode.OperationUnavailable
    && BankService.RecordGoldEarned(unopened, 10).Error
        == BankErrorCode.OperationUnavailable
    && BankService.TrySpend(unopened, 10).Error
        == BankErrorCode.OperationUnavailable
    && BankService.ApplyForCreditCard(
        unopened,
        CreditTier.VisaPoor).Error
        == BankErrorCode.OperationUnavailable,
    "A deposit, spend, or card application ran before account opening.");
Player preOpenRecipient = OpenPlayer(0, 100);
Expect(
    BankService.ETransfer(unopened, preOpenRecipient, 10).Error
        == BankErrorCode.OperationUnavailable
    && unopened.Gold == 70
    && preOpenRecipient.Gold == 0,
    "e-Transfer moved gold before both players had opened accounts.");
int unopenedHp = unopened.Creature.CurrentHp;
int unopenedMaxHp = unopened.Creature.MaxHp;
Expect(
    (await KkCompoundService.SellKidneys(unopened, 1)).Error
        == BankErrorCode.OperationUnavailable
    && unopened.Creature.CurrentHp == unopenedHp
    && unopened.Creature.MaxHp == unopenedMaxHp
    && unopened.Gold == 70,
    "KK mutated health or gold before account opening.");



BankOperationResult firstOpening = BankService.OpenBankAccount(unopened);
AccountSnapshot openedSnapshot = BankService.GetSnapshot(unopened);
Expect(
    firstOpening.Success
    && firstOpening.Amount == 70
    && openedSnapshot.IsAccountOpened
    && openedSnapshot.SavingsBalance == 70
    && openedSnapshot.SavingsPrincipal == 70
    && openedSnapshot.SavingsInterest == 0
    && openedSnapshot.SavingsInterestEarnedTotal == 0
    && openedSnapshot.QualifyingEarned == 0,
    "OpenBankAccount did not initialize principal and zero qualification.");
AccountSnapshot beforeSecondOpening = openedSnapshot;
Expect(
    BankService.OpenBankAccount(unopened).Error
        == BankErrorCode.AlreadyProcessed
    && BankService.GetSnapshot(unopened) == beforeSecondOpening,
    "OpenBankAccount was not idempotent.");

Player delayedOpening = NewPlayer(50, 101);
_ = BankService.InitializeQualification(delayedOpening);
delayedOpening.Gold += 100;
_ = BankService.RecordNativeGoldGainAmount(
    delayedOpening,
    100,
    wasStolenBack: false);
delayedOpening.Gold -= 100;
Expect(
    BankService.OpenBankAccount(delayedOpening).Success
    && delayedOpening.Gold == 50
    && BankService.GetSavingsPrincipal(delayedOpening) == 50
    && BankService.GetQualifyingEarned(delayedOpening) == 0,
    "Pre-opening awards leaked into post-opening card qualification.");




Player freshRunPlayerOne = OpenPlayer(75, 102);
Player freshRunPlayerTwo = OpenPlayer(125, 103);
BankStateStore.Get(freshRunPlayerOne).QualifyingEarned = 999;
BankStateStore.Get(freshRunPlayerOne).ButtSalesCount = 4;
BankStateStore.Get(freshRunPlayerTwo).CreditTier =
    (int)CreditTier.VisaPoor;
BankStateStore.Get(freshRunPlayerTwo).CreditDebt = 50;
RunState freshSyntheticRun = AttachSyntheticRunState(
    7,
    freshRunPlayerOne,
    freshRunPlayerTwo);
FreshRunBankAccountResetPatch.ResetFreshRunAccounts(
    freshSyntheticRun);
Expect(
    freshSyntheticRun.Players.Count == 2
    && freshSyntheticRun.Players.All(player =>
        !BankService.IsAccountOpened(player))
    && freshSyntheticRun.Players.All(player =>
        BankStateStore.Get(player).QualifyingEarned == 0
        && BankStateStore.Get(player).CreditTier
            == (int)CreditTier.None
        && BankStateStore.Get(player).CreditDebt == 0
        && BankStateStore.Get(player).ButtSalesCount == 0)
    && freshRunPlayerOne.Gold == 75
    && freshRunPlayerTwo.Gold == 125,
    "Fresh-run reset did not make every player unopened without touching "
    + "their native starting gold.");

Player continuedRunPlayer = OpenPlayer(88, 104);
RunState continuedSyntheticRun =
    AttachSyntheticRunState(9, continuedRunPlayer);
VerifyFreshRunAccountResetPatchCanApply();
Expect(
    continuedSyntheticRun.Players.Single() == continuedRunPlayer
    && BankService.IsAccountOpened(continuedRunPlayer)
    && continuedRunPlayer.Gold == 88,
    "Saved-run fixture lost its open account even though saved setup is not "
    + "a FreshRunBankAccountResetPatch target.");



Player schema1 = NewPlayer(40, 2);
BankStateStore.Set(
    schema1,
    new AccountState
    {
        Schema = 1,
        SavingsPrincipal = 100,
        SavingsInterest = 7,
        SavingsTenths = 5,
        SavingsInterestTurns = 9,
        LastSavingsTurnToken = 123456,
        UnifiedSavingsInitialized = 0,
    });
Expect(
    BankService.InitializeUnifiedSavings(schema1).Success,
    "Schema-1 unified-savings migration failed.");
AccountSnapshot schema1Snapshot = BankService.GetSnapshot(schema1);
Expect(
    schema1.Gold == 147
    && schema1Snapshot.IsAccountOpened
    && schema1Snapshot.SavingsBalance == 147
    && schema1Snapshot.SavingsPrincipal == 140
    && schema1Snapshot.SavingsInterest == 7
    && schema1Snapshot.SavingsTenths == 5,
    "Schema-1 migration lost old chequing or savings money.");
AccountState schema1State = BankStateStore.Get(schema1);
Expect(
    schema1State.Schema == AccountState.CurrentSchema
    && schema1State.BankAccountOpened == 1
    && schema1State.UnifiedSavingsInitialized == 1
    && schema1State.QualifyingEarned == 0
    && schema1State.SavingsInterestEarnedTotal == 7
    && schema1State.DebtGraceUsed == 0
    && schema1State.SavingsInterestTurns == 0
    && schema1State.LastSavingsTurnToken == -1,
    "Schema-1 counters or schema-6 qualification fields were not migrated.");
int schema1GoldAfterMigration = schema1.Gold;
_ = BankService.InitializeUnifiedSavings(schema1);
Expect(
    schema1.Gold == schema1GoldAfterMigration,
    "Schema-1 balances were merged more than once.");


Player schema2 = NewPlayer(25, 3);
BankStateStore.Set(
    schema2,
    new AccountState
    {
        Schema = 2,
        SavingsPrincipal = 80,
        SavingsInterest = 9,
        SavingsTenths = 4,
        SavingsInterestTurns = 3,
        LastSavingsTurnToken = 77,
        UnifiedSavingsInitialized = 0,
    });
_ = BankService.InitializeUnifiedSavings(schema2);
AccountSnapshot schema2Snapshot = BankService.GetSnapshot(schema2);
AccountState schema2State = BankStateStore.Get(schema2);
Expect(
    schema2.Gold == 114
    && schema2Snapshot.SavingsPrincipal == 105
    && schema2Snapshot.SavingsInterest == 9
    && schema2Snapshot.SavingsTenths == 4
    && schema2State.SavingsInterestTurns == 3
    && schema2State.LastSavingsTurnToken == 77
    && schema2State.BankAccountOpened == 1
    && schema2State.UnifiedSavingsInitialized == 1
    && schema2State.QualifyingEarned == 0
    && schema2State.SavingsInterestEarnedTotal == 9
    && schema2State.DebtGraceUsed == 0,
    "Schema-2 balance/floor history migration regressed.");
int schema2GoldAfterMigration = schema2.Gold;
_ = BankService.InitializeUnifiedSavings(schema2);
Expect(
    schema2.Gold == schema2GoldAfterMigration,
    "Schema-2 balances were merged more than once.");



Player legacyClosed = NewPlayer(0, 4);
BankStateStore.Set(
    legacyClosed,
    new AccountState
    {
        Schema = 4,
        QualifyingEarned = BankService.TycoonQualification,
        QualificationInitialized = 1,
        UnifiedSavingsInitialized = 1,
        CreditPermanentlyClosed = 1,
        CreditTier = (int)CreditTier.None,
    });
AccountState migratedClosed = BankStateStore.Get(legacyClosed);
BankOperationResult requalifiedLegacy =
    BankService.RecordGoldEarned(
        legacyClosed,
        BankService.TycoonQualification);
Expect(
    migratedClosed.Schema == AccountState.CurrentSchema
    && migratedClosed.BankAccountOpened == 1
    && migratedClosed.CreditPermanentlyClosed == 0
    && migratedClosed.DebtGraceUsed == 0
    && requalifiedLegacy.Success
    && BankService.ApplyForCreditCard(
        legacyClosed,
        CreditTier.VisaTycoon).Success,
    "A schema<5 retired closure marker incorrectly kept the card closed.");



Player observedGold = OpenPlayer(100, 5);
int beforeNativeGain = observedGold.Gold;
observedGold.Gold = 137;
BankOperationResult nativeGain =
    BankService.RecordNativeGoldGain(observedGold, beforeNativeGain);
Expect(
    nativeGain.Success
    && nativeGain.Amount == 37
    && BankService.GetQualifyingEarned(observedGold) == 37
    && BankService.GetSavingsPrincipal(observedGold) == 137,
    "A completed native gold gain did not update qualification and principal.");
Expect(
    BankService.AccrueSavingsInterest(observedGold, 501).Amount == 2
    && observedGold.Gold == 139
    && BankService.GetSavingsPrincipal(observedGold) == 137
    && BankService.GetSavingsInterest(observedGold) == 2
    && BankService.GetSavingsInterestEarnedTotal(observedGold) == 2
    && BankService.GetQualifyingEarned(observedGold) == 39
    && BankService.GetSavingsTenths(observedGold) == 0,
    "Savings-interest setup for native-loss reconciliation is incorrect.");
int beforeFirstLoss = observedGold.Gold;
observedGold.Gold = 131;
Expect(
    BankService.RecordNativeGoldLoss(observedGold, beforeFirstLoss).Amount == 8
    && BankService.GetSavingsInterest(observedGold) == 0
    && BankService.GetSavingsPrincipal(observedGold) == 131,
    "Native loss did not consume accrued interest before principal.");
int beforeSecondLoss = observedGold.Gold;
observedGold.Gold = 100;
Expect(
    BankService.RecordNativeGoldLoss(observedGold, beforeSecondLoss).Amount == 31
    && BankService.GetSavingsInterest(observedGold) == 0
    && BankService.GetSavingsInterestEarnedTotal(observedGold) == 2
    && BankService.GetSavingsPrincipal(observedGold) == 100
    && BankService.GetSavingsBalance(observedGold) == observedGold.Gold,
    "Native loss did not reconcile unified savings components.");

Player crossedZero = OpenPlayer(-20, 6);
crossedZero.Gold = 5;
Expect(
    BankService.RecordNativeGoldGain(crossedZero, -20).Success
    && BankService.GetQualifyingEarned(crossedZero) == 25
    && BankService.GetSavingsPrincipal(crossedZero) == 5,
    "Only the positive-balance part of a debt-crossing gain became principal.");


Player reentrantGain = OpenPlayer(100, 7);
reentrantGain.Gold += 10;
_ = BankService.RecordNativeGoldGainAmount(
    reentrantGain,
    10,
    wasStolenBack: false);
reentrantGain.Gold += 5;
_ = BankService.RecordNativeGoldGainAmount(
    reentrantGain,
    5,
    wasStolenBack: false);
Expect(
    BankService.GetQualifyingEarned(reentrantGain) == 15
    && BankService.GetSavingsPrincipal(reentrantGain) == 115,
    "Nested exact GainGold awards were counted more or less than once.");


Player stolenGold = OpenPlayer(100, 8);
_ = BankService.AccrueSavingsInterest(stolenGold, 801);
int qualificationBeforeTheft =
    BankService.GetQualifyingEarned(stolenGold);
int beforeTheft = stolenGold.Gold;
stolenGold.Gold -= 50;
_ = BankService.RecordNativeGoldLoss(
    stolenGold,
    beforeTheft,
    MegaCrit.Sts2.Core.Entities.Gold.GoldLossType.Stolen);
AccountState stolenState = BankStateStore.Get(stolenGold);
Expect(
    stolenState.StolenSavingsInterest == 1
    && stolenState.StolenSavingsPrincipal == 49
    && stolenState.SavingsInterest == 0
    && stolenState.SavingsPrincipal == 51,
    "Stolen gold did not preserve removed interest/principal composition.");
stolenGold.Gold += 50;
_ = BankService.RecordNativeGoldGainAmount(
    stolenGold,
    50,
    wasStolenBack: true);
Expect(
    BankService.GetSavingsInterest(stolenGold) == 1
    && BankService.GetSavingsPrincipal(stolenGold) == 100
    && BankService.GetQualifyingEarned(stolenGold)
        == qualificationBeforeTheft
    && stolenState.StolenSavingsInterest == 0
    && stolenState.StolenSavingsPrincipal == 0,
    "Returned stolen gold was laundered into principal or qualification.");

Player escapedTheft = OpenPlayer(100, 9);
_ = BankService.AccrueSavingsInterest(escapedTheft, 901);
int beforeEscapedTheft = escapedTheft.Gold;
escapedTheft.Gold -= 20;
_ = BankService.RecordNativeGoldLoss(
    escapedTheft,
    beforeEscapedTheft,
    MegaCrit.Sts2.Core.Entities.Gold.GoldLossType.Stolen);
_ = BankService.ClearUnreturnedStolenGold(escapedTheft);
int beforeLaterTheft = escapedTheft.Gold;
escapedTheft.Gold -= 10;
_ = BankService.RecordNativeGoldLoss(
    escapedTheft,
    beforeLaterTheft,
    MegaCrit.Sts2.Core.Entities.Gold.GoldLossType.Stolen);
escapedTheft.Gold += 10;
_ = BankService.RecordNativeGoldGainAmount(
    escapedTheft,
    10,
    wasStolenBack: true);
Expect(
    BankService.GetSavingsInterest(escapedTheft) == 0
    && BankService.GetSavingsPrincipal(escapedTheft) == 81
    && BankStateStore.Get(escapedTheft).StolenSavingsInterest == 0
    && BankStateStore.Get(escapedTheft).StolenSavingsPrincipal == 0,
    "An escaped thief's stale composition leaked into a later refund.");

Player fractionalTheft = OpenPlayer(5, 10);
_ = BankService.AccrueSavingsInterest(fractionalTheft, 1001);
int beforeFractionalTheft = fractionalTheft.Gold;
fractionalTheft.Gold = 0;
_ = BankService.RecordNativeGoldLoss(
    fractionalTheft,
    beforeFractionalTheft,
    MegaCrit.Sts2.Core.Entities.Gold.GoldLossType.Stolen);
Expect(
    BankStateStore.Get(fractionalTheft).StolenSavingsTenths == 0,
    "Rounded-down savings unexpectedly retained fractional interest.");
fractionalTheft.Gold = 5;
_ = BankService.RecordNativeGoldGainAmount(
    fractionalTheft,
    5,
    wasStolenBack: true);
Expect(
    BankService.GetSavingsTenths(fractionalTheft) == 0
    && BankService.AccrueSavingsInterest(
        fractionalTheft,
        1002).Amount == 0,
    "Rounded-down savings unexpectedly paid carried fractional interest.");

Expect(
    CreditBackedSpendingPatch.PreserveNegativeBalanceAfterNativeLoss(
        -105,
        0) == -105
    && CreditBackedSpendingPatch.PreserveNegativeBalanceAfterNativeLoss(
        50,
        30) == 30,
    "Native LoseGold negative-balance preservation regressed.");


Player compoundInterest = OpenPlayer(100, 11);
BankOperationResult interestFloorOne =
    BankService.AccrueSavingsInterest(compoundInterest, 1101);
Expect(
    interestFloorOne.Success
    && interestFloorOne.Amount == 1
    && compoundInterest.Gold == 101,
    "The first unique floor did not pay 1.5% compound interest.");
Expect(
    BankService.AccrueSavingsInterest(compoundInterest, 1101).Error
        == BankErrorCode.AlreadyProcessed
    && compoundInterest.Gold == 101,
    "The same savings floor token paid twice.");
BankOperationResult interestFloorTwo =
    BankService.AccrueSavingsInterest(compoundInterest, 1102);
Expect(
    interestFloorTwo.Success
    && interestFloorTwo.Amount == 1
    && compoundInterest.Gold == 102
    && BankService.GetSavingsPrincipal(compoundInterest) == 100
    && BankService.GetSavingsInterest(compoundInterest) == 2
    && BankService.GetSavingsInterestEarnedTotal(compoundInterest) == 2
    && BankService.GetQualifyingEarned(compoundInterest) == 2
    && BankStateStore.Get(compoundInterest).SavingsInterestTurns == 2,
    "Savings interest did not compound on the next real floor.");


Expect(
    CompletedMapFloorBankingPatch.ShouldSettleCompletedMapFloor(
        wasBaseRoom: true,
        isMapRoom: false,
        floorToken: 42,
        historyMatches: true,
        sameActiveRun: true)
    && !CompletedMapFloorBankingPatch.ShouldSettleCompletedMapFloor(
        wasBaseRoom: true,
        isMapRoom: true,
        floorToken: 42,
        historyMatches: true,
        sameActiveRun: true)
    && !CompletedMapFloorBankingPatch.ShouldSettleCompletedMapFloor(
        wasBaseRoom: false,
        isMapRoom: false,
        floorToken: 42,
        historyMatches: true,
        sameActiveRun: true)
    && !CompletedMapFloorBankingPatch.ShouldSettleCompletedMapFloor(
        wasBaseRoom: true,
        isMapRoom: false,
        floorToken: 0,
        historyMatches: true,
        sameActiveRun: true)
    && !CompletedMapFloorBankingPatch.ShouldSettleCompletedMapFloor(
        wasBaseRoom: true,
        isMapRoom: false,
        floorToken: 42,
        historyMatches: false,
        sameActiveRun: true)
    && !CompletedMapFloorBankingPatch.ShouldSettleCompletedMapFloor(
        wasBaseRoom: true,
        isMapRoom: false,
        floorToken: 42,
        historyMatches: true,
        sameActiveRun: false),
    "Completed-floor lifecycle filter accepted a map, nested, stale, or invalid room.");
VerifyCompletedFloorPatchCanApply();
VerifyNextActFloorPatchCanApply();
VerifyExactGainGoldPatchCanApply();
VerifyEventCreditAvailabilityPatchCanApply();

Player eventCredit = OpenCardPlayer(
    CreditTier.VisaPoor,
    9901,
    gold: 25);
Player eventCreditClamp = OpenCardPlayer(
    CreditTier.VisaTycoon,
    9902,
    gold: int.MaxValue);
Expect(
    EventCreditAvailabilityPatch.SpendableGold(eventCredit) == 225
    && EventCreditAvailabilityPatch.SpendableGold(eventCredit)
        == BankService.GetPurchasingPower(eventCredit)
    && EventCreditAvailabilityPatch.SpendableGold(eventCreditClamp)
        == int.MaxValue,
    "Event affordability helper did not include available card credit "
    + "or clamp purchasing power to the vanilla int range.");

Expect(
    BankService.GetCreditLimit(CreditTier.VisaPoor) == 200
    && BankService.GetCreditLimit(CreditTier.VisaMiddleClass) == 700
    && BankService.GetCreditLimit(CreditTier.VisaTycoon) == 1200
    && BankService.GetMaximumDebt(CreditTier.VisaPoor) == 400
    && BankService.GetMaximumDebt(CreditTier.VisaMiddleClass) == 1400
    && BankService.GetMaximumDebt(CreditTier.VisaTycoon) == 2400,
    "Credit limits or the 200% debt ceilings are incorrect.");
Expect(
    BankService.PoorQualification == 150
    && BankService.MiddleClassQualification == 600
    && BankService.TycoonQualification == 1600
    && BankService.GetQualificationThreshold(CreditTier.VisaPoor) == 150
    && BankService.GetQualificationThreshold(
        CreditTier.VisaMiddleClass) == 600
    && BankService.GetQualificationThreshold(
        CreditTier.VisaTycoon) == 1600
    && BankService.GetHighestEligibleTier(149) == CreditTier.None
    && BankService.GetHighestEligibleTier(150) == CreditTier.VisaPoor
    && BankService.GetHighestEligibleTier(599) == CreditTier.VisaPoor
    && BankService.GetHighestEligibleTier(600)
        == CreditTier.VisaMiddleClass
    && BankService.GetHighestEligibleTier(1599)
        == CreditTier.VisaMiddleClass
    && BankService.GetHighestEligibleTier(1600)
        == CreditTier.VisaTycoon,
    "Balanced 150/600/1600 card qualification thresholds regressed.");
AscensionBankBenefits[] expectedAscensionBenefits =
[
    new(
        0,
        150, 600, 1600,
        200, 700, 1200,
        200, 3,
        2199, 2499, 2799,
        150, 10,
        10, 200,
        5, 50,
        100, int.MaxValue),
    new(
        1,
        175, 650, 1700,
        200, 700, 1200,
        200, 3,
        2199, 2499, 2799,
        150, 10,
        10, 200,
        5, 50,
        100, int.MaxValue),
    new(
        2,
        200, 700, 1800,
        200, 650, 1100,
        190, 3,
        2299, 2599, 2899,
        125, 8,
        10, 200,
        5, 50,
        100, int.MaxValue),
    new(
        3,
        225, 800, 2000,
        200, 650, 1100,
        190, 2,
        2299, 2599, 2899,
        125, 8,
        10, 200,
        5, 50,
        100, int.MaxValue),
    new(
        4,
        250, 900, 2200,
        200, 600, 1000,
        180, 2,
        2399, 2699, 2999,
        100, 7,
        10, 200,
        5, 50,
        100, int.MaxValue),
    new(
        5,
        275, 1000, 2400,
        200, 600, 1000,
        180, 2,
        2399, 2699, 2999,
        100, 7,
        10, 200,
        5, 50,
        100, int.MaxValue),
    new(
        6,
        300, 1100, 2600,
        175, 550, 900,
        170, 1,
        2499, 2799, 3099,
        75, 6,
        10, 200,
        5, 50,
        100, int.MaxValue),
    new(
        7,
        325, 1200, 2800,
        175, 550, 900,
        170, 1,
        2499, 2799, 3099,
        75, 6,
        10, 200,
        5, 50,
        100, int.MaxValue),
    new(
        8,
        350, 1300, 3000,
        150, 500, 800,
        160, 1,
        2599, 2899, 3199,
        50, 5,
        10, 200,
        5, 50,
        100, int.MaxValue),
    new(
        9,
        375, 1400, 3200,
        150, 450, 750,
        150, 1,
        2699, 2999, 3299,
        50, 5,
        10, 200,
        5, 50,
        100, int.MaxValue),
    new(
        10,
        400, 1500, 3500,
        150, 450, 750,
        150, 1,
        2699, 2999, 3299,
        50, 5,
        10, 200,
        5, 50,
        100, int.MaxValue),
];
for (var ascension = 0; ascension <= 10; ascension++)
{
    AscensionBankBenefits actual =
        AscensionBankBenefits.ForAscension(ascension);
    Expect(
        actual == expectedAscensionBenefits[ascension],
        $"A{ascension} TD Bank comfort terms differ from the complete "
        + "v2.5 table.");
}
Expect(
    AscensionBankBenefits.ForAscension(-1)
        == expectedAscensionBenefits[0]
    && AscensionBankBenefits.ForAscension(99)
        == expectedAscensionBenefits[10],
    "Ascension benefit lookup no longer clamps outside the A0-A10 range.");
Expect(
    AscensionBankBenefits.ForAscension(0)
        .CalculateSavingsInterest(100) == 1
    && AscensionBankBenefits.ForAscension(0)
        .CalculateSavingsInterest(2000) == 10
    && AscensionBankBenefits.ForAscension(3)
        .CalculateSavingsInterest(100) == 1
    && AscensionBankBenefits.ForAscension(3)
        .CalculateSavingsInterest(2000) == 8
    && AscensionBankBenefits.ForAscension(6)
        .CalculateSavingsInterest(200) == 1
    && AscensionBankBenefits.ForAscension(6)
        .CalculateSavingsInterest(2000) == 6
    && AscensionBankBenefits.ForAscension(9)
        .CalculateSavingsInterest(200) == 1
    && AscensionBankBenefits.ForAscension(9)
        .CalculateSavingsInterest(2000) == 5,
    "Ascension savings rates or per-floor caps regressed.");

BankCreditOffer[] expectedA0UiOffers =
[
    new(BankCreditTier.Starter, 150, 200, 400, 2199),
    new(BankCreditTier.MiddleClass, 600, 700, 1400, 2499),
    new(BankCreditTier.NouveauRiche, 1600, 1200, 2400, 2799),
];
Expect(
    BankUiSnapshot.Empty.CreditOffers.SequenceEqual(expectedA0UiOffers),
    "The UI model's A0 fallback offers no longer preserve the old rules.");

BankUiSnapshot a3Ui =
    UiSnapshotFor(expectedAscensionBenefits[3]);
BankUiSnapshot a10Ui =
    UiSnapshotFor(expectedAscensionBenefits[10], buttSalesCount: 3);
Expect(
    a3Ui.CreditOffers.SequenceEqual(
        new[]
        {
            new BankCreditOffer(
                BankCreditTier.Starter, 225, 200, 380, 2299),
            new BankCreditOffer(
                BankCreditTier.MiddleClass, 800, 650, 1235, 2599),
            new BankCreditOffer(
                BankCreditTier.NouveauRiche, 2000, 1100, 2090, 2899),
        })
    && a10Ui.CreditOffers.SequenceEqual(
        new[]
        {
            new BankCreditOffer(
                BankCreditTier.Starter, 400, 150, 225, 2699),
            new BankCreditOffer(
                BankCreditTier.MiddleClass, 1500, 450, 675, 2999),
            new BankCreditOffer(
                BankCreditTier.NouveauRiche, 3500, 750, 1125, 3299),
        }),
    "BankUiSnapshot/CreditOffers do not carry the A3 or A10 dynamic terms.");

BankUiBridge.Language = BankUiLanguage.SimplifiedChinese;
string a3CreditCopy =
    InvokeOverlayPureMethod<string>(a3Ui, "CreditRules");
string a3SavingsCopy =
    InvokeOverlayPureMethod<string>(a3Ui, "SavingsRules");
string a10OpeningCopy =
    InvokeOverlayPureMethod<string>(a10Ui, "OpeningRules");
Expect(
    a3CreditCopy.Contains("当前 A3", StringComparison.Ordinal)
    && a3CreditCopy.Contains("225 G", StringComparison.Ordinal)
    && a3CreditCopy.Contains("650 G", StringComparison.Ordinal)
    && a3CreditCopy.Contains("2 个完成层", StringComparison.Ordinal)
    && a3CreditCopy.Contains("22.99%", StringComparison.Ordinal)
    && a3CreditCopy.Contains("每 100G", StringComparison.Ordinal)
    && a3CreditCopy.Contains("不封顶", StringComparison.Ordinal)
    && a3SavingsCopy.Contains("1.25%", StringComparison.Ordinal)
    && a3SavingsCopy.Contains("最多 8G", StringComparison.Ordinal)
    && a3SavingsCopy.Contains("1G", StringComparison.Ordinal)
    && a10OpeningCopy.Contains("当前 A10", StringComparison.Ordinal)
    && a10OpeningCopy.Contains("1 层", StringComparison.Ordinal)
    && a10OpeningCopy.Contains("26.99%", StringComparison.Ordinal)
    && a10OpeningCopy.Contains("每 100G", StringComparison.Ordinal)
    && a10OpeningCopy.Contains("不封顶", StringComparison.Ordinal)
    && a10OpeningCopy.Contains("200G", StringComparison.Ordinal)
    && a10OpeningCopy.Contains("第 4 次起越卖越伤", StringComparison.Ordinal),
    "Dynamic A3/A10 UI rules do not explain the effective snapshot terms.");



Player a3Qualification = OpenAscensionPlayer(3, 0, 9803);
Expect(
    a3Qualification.RunState.AscensionLevel == 3
    && AscensionBankBenefits.For(a3Qualification)
        == expectedAscensionBenefits[3]
    && BankService.GetQualificationThreshold(
        a3Qualification,
        CreditTier.VisaPoor) == 225
    && BankService.GetQualificationThreshold(
        a3Qualification,
        CreditTier.VisaMiddleClass) == 800
    && BankService.GetQualificationThreshold(
        a3Qualification,
        CreditTier.VisaTycoon) == 2000
    && BankService.RecordGoldEarned(a3Qualification, 225).Success
    && BankService.ApplyForCreditCard(
        a3Qualification,
        CreditTier.VisaPoor).Success
    && BankService.GetCreditLimit(
        a3Qualification,
        CreditTier.VisaPoor) == 200
    && BankService.GetMaximumDebt(
        a3Qualification,
        CreditTier.VisaPoor) == 380,
    "A3 player did not receive the 225G-qualified 200/380G "
    + "live credit terms.");
Expect(
    BankService.ApplyForCreditCard(
        a3Qualification,
        CreditTier.VisaMiddleClass).Error
        == BankErrorCode.NotEligible
    && BankService.RecordGoldEarned(a3Qualification, 575).Success
    && BankService.ApplyForCreditCard(
        a3Qualification,
        CreditTier.VisaMiddleClass).Success
    && BankService.GetCreditLimit(
        a3Qualification,
        CreditTier.VisaMiddleClass) == 650
    && BankService.GetMaximumDebt(
        a3Qualification,
        CreditTier.VisaMiddleClass) == 1235,
    "A3 live middle-card qualification or limit did not use 800/650/1235G.");

Player a3Debt =
    OpenAscensionCardPlayer(3, CreditTier.VisaPoor, 9804);
Expect(
    BankService.TrySpend(a3Debt, 100).Success
    && BankService.GetCreditDebt(a3Debt) == 100,
    "A3 debt fixture could not charge its instant starter card.");
for (var floor = 1; floor <= 2; floor++)
{
    BankOperationResult graceResult =
        BankService.AccrueDebtInterest(a3Debt, 3000 + floor);
    Expect(
        graceResult.Success
        && graceResult.Amount == 0
        && BankService.GetCreditDebt(a3Debt) == 100,
        $"A3 first debt charged interest on grace floor {floor}.");
}
Expect(
    BankService.AccrueDebtInterest(a3Debt, 3003).Amount == 23
    && BankService.GetCreditDebt(a3Debt) == 123
    && BankService.GetDebtInterestBasisPoints(
        a3Debt,
        CreditTier.VisaPoor) == 2299,
    "A3 debt did not charge ceil(100 * 22.99%) after two free floors.");

Player a3Savings = OpenAscensionPlayer(3, 100, 9805);
BankOperationResult a3Interest =
    BankService.AccrueSavingsInterest(a3Savings, 3101);
Expect(
    a3Interest == BankOperationResult.Ok(1, 0)
    && a3Savings.Gold == 101
    && BankService.GetSavingsInterest(a3Savings) == 1
    && BankService.GetQualifyingEarned(a3Savings) == 1
    && BankService.CalculateNextSavingsInterest(
        a3Savings,
        100,
        0) == 1,
    "A3 live savings did not pay capped qualifying 1.25% interest.");

Player a3Kk = OpenAscensionPlayer(3, 0, 9806, 50, 70);
BankOperationResult a3Kidney =
    await KkCompoundService.SellKidneys(a3Kk, 1);
BankOperationResult a3Butt =
    await KkCompoundService.SellButt(a3Kk);
Expect(
    a3Kidney == BankOperationResult.Ok(200, 0)
    && a3Butt == BankOperationResult.Ok(50, 0)
    && a3Kk.Creature.CurrentHp == 35
    && a3Kk.Creature.MaxHp == 60
    && a3Kk.Gold == 250
    && BankService.GetQualifyingEarned(a3Kk) == 0,
    "A3 live KK terms did not apply the fixed -10/-10/+200G kidney and -5/+50G butt.");

Player a10Qualification = OpenAscensionPlayer(10, 0, 9810);
Expect(
    a10Qualification.RunState.AscensionLevel == 10
    && AscensionBankBenefits.For(a10Qualification)
        == expectedAscensionBenefits[10]
    && BankService.GetQualificationThreshold(
        a10Qualification,
        CreditTier.VisaPoor) == 400
    && BankService.GetQualificationThreshold(
        a10Qualification,
        CreditTier.VisaMiddleClass) == 1500
    && BankService.GetQualificationThreshold(
        a10Qualification,
        CreditTier.VisaTycoon) == 3500
    && BankService.RecordGoldEarned(a10Qualification, 400).Success
    && BankService.ApplyForCreditCard(
        a10Qualification,
        CreditTier.VisaPoor).Success
    && BankService.GetCreditLimit(
        a10Qualification,
        CreditTier.VisaPoor) == 150
    && BankService.GetMaximumDebt(
        a10Qualification,
        CreditTier.VisaPoor) == 225,
    "A10 player did not receive the 400G-qualified 150/225G "
    + "live credit terms.");
Expect(
    BankService.RecordGoldEarned(a10Qualification, 3100).Success
    && BankService.ApplyForCreditCard(
        a10Qualification,
        CreditTier.VisaTycoon).Success
    && BankService.GetCreditLimit(
        a10Qualification,
        CreditTier.VisaTycoon) == 750
    && BankService.GetMaximumDebt(
        a10Qualification,
        CreditTier.VisaTycoon) == 1125,
    "A10 live tycoon qualification or limit did not use 3500/750/1125G.");

Player a10Debt =
    OpenAscensionCardPlayer(10, CreditTier.VisaPoor, 9811);
Expect(
    BankService.TrySpend(a10Debt, 100).Success,
    "A10 debt fixture could not charge its instant starter card.");
for (var floor = 1; floor <= 1; floor++)
{
    BankOperationResult graceResult =
        BankService.AccrueDebtInterest(a10Debt, 10000 + floor);
    Expect(
        graceResult.Success
        && graceResult.Amount == 0
        && BankService.GetCreditDebt(a10Debt) == 100,
        $"A10 first debt charged interest on grace floor {floor}.");
}
Expect(
    BankService.AccrueDebtInterest(a10Debt, 10002).Amount == 27
    && BankService.GetCreditDebt(a10Debt) == 127
    && BankService.GetDebtInterestBasisPoints(
        a10Debt,
        CreditTier.VisaPoor) == 2699,
    "A10 debt did not charge ceil(100 * 26.99%) after one free floor.");

Player a10Savings = OpenAscensionPlayer(10, 2000, 9812);
BankOperationResult a10Interest =
    BankService.AccrueSavingsInterest(a10Savings, 10101);
Expect(
    a10Interest == BankOperationResult.Ok(5, 0)
    && a10Savings.Gold == 2005
    && BankService.GetSavingsInterest(a10Savings) == 5
    && BankService.GetQualifyingEarned(a10Savings) == 5
    && BankService.CalculateNextSavingsInterest(
        a10Savings,
        2000,
        0) == 5,
    "A10 live savings did not respect the capped 0.5%/5G terms.");

Player a10Kk = OpenAscensionPlayer(10, 0, 9813, 50, 70);
BankOperationResult a10Kidney =
    await KkCompoundService.SellKidneys(a10Kk, 1);
BankOperationResult[] a10ButtResults = new BankOperationResult[4];
for (var sale = 0; sale < 3; sale++)
{
    a10ButtResults[sale] =
        await KkCompoundService.SellButt(a10Kk);
}
ButtRiskOutcome a10FourthOutcome =
    KkCompoundService.GetButtRiskOutcomeForNextSale(a10Kk);
a10ButtResults[3] =
    await KkCompoundService.SellButt(a10Kk);
int a10FourthGold =
    a10FourthOutcome == ButtRiskOutcome.Unpaid ? 0 : 30;
int a10FourthHp =
    a10FourthOutcome == ButtRiskOutcome.Hemorrhage ? 16 : 8;
Expect(
    a10Kidney == BankOperationResult.Ok(200, 0)
    && a10ButtResults.Take(3).All(result =>
        result == BankOperationResult.Ok(50, 0))
    && a10ButtResults[3] == BankOperationResult.Ok(
        a10FourthGold,
        0,
        a10FourthOutcome)
    && a10Kk.Creature.CurrentHp == 25 - a10FourthHp
    && a10Kk.Creature.MaxHp == 60
    && a10Kk.Gold == 350 + a10FourthGold
    && BankStateStore.Get(a10Kk).ButtSalesCount == 4
    && BankService.GetQualifyingEarned(a10Kk) == 0,
    "A10 live KK terms or the deterministic fourth-sale risk regressed.");

Player perTransactionLimit =
    OpenCardPlayer(CreditTier.VisaPoor, 9903);
Expect(
    BankService.TrySpend(perTransactionLimit, 201).Error
        == BankErrorCode.CreditLimitExceeded
    && BankService.GetCreditDebt(perTransactionLimit) == 0
    && BankService.TrySpend(perTransactionLimit, 200).Success
    && BankService.GetCreditDebt(perTransactionLimit) == 200
    && BankService.GetAvailableCredit(perTransactionLimit) == 200,
    "A purchase exceeded the nominal 200G per-transaction advance "
    + "or the second 200G tranche was unavailable.");



Player grace = OpenCardPlayer(CreditTier.VisaPoor, 12);
Expect(
    BankService.TrySpend(grace, 100).Success
    && BankService.GetCreditDebt(grace) == 100,
    "Debt-grace fixture could not create 100 debt.");
for (var graceIndex = 1; graceIndex <= 3; graceIndex++)
{
    BankOperationResult free =
        BankService.AccrueDebtInterest(grace, 1200 + graceIndex);
    Expect(
        free.Success
        && free.Amount == 0
        && BankService.GetCreditDebt(grace) == 100
        && BankService.GetDebtCycleFloors(grace) == graceIndex,
        $"Debt floor {graceIndex} was not interest-free.");
}
BankOperationResult poorFloorFour =
    BankService.AccrueDebtInterest(grace, 1204);
Expect(
    poorFloorFour.Success
    && poorFloorFour.Amount == 22
    && BankService.GetCreditDebt(grace) == 122
    && BankService.GetDebtInterestBasisPoints(
        CreditTier.VisaPoor) == 2199,
    "Visa Poor floor four did not charge ceil(100 * 21.99%).");
Expect(
    BankService.AccrueDebtInterest(grace, 1204).Error
        == BankErrorCode.AlreadyProcessed
    && BankService.GetCreditDebt(grace) == 122,
    "Debt interest was not idempotent for one completed-floor token.");
BankOperationResult poorCompound =
    BankService.AccrueDebtInterest(grace, 1205);
Expect(
    poorCompound.Amount == 27
    && BankService.GetCreditDebt(grace) == 149,
    "Visa Poor did not compound ceil(122 * 21.99%).");


_ = BankService.RecordGoldEarned(
    grace,
    BankService.MiddleClassQualification
        - BankService.GetQualifyingEarned(grace));
Expect(
    BankService.ApplyForCreditCard(
        grace,
        CreditTier.VisaMiddleClass).Success,
    "Outstanding debt could not upgrade to Visa Middle-Class.");
BankOperationResult upgradedRate =
    BankService.AccrueDebtInterest(grace, 1206);
Expect(
    upgradedRate.Amount == 38
    && BankService.GetCreditDebt(grace) == 187
    && BankService.GetDebtCycleFloors(grace) == 6
    && BankService.GetDebtInterestBasisPoints(
        CreditTier.VisaMiddleClass) == 2499,
    "An upgraded debt cycle did not use ceil(149 * 24.99%).");
BankOperationResult clearedCycle =
    BankService.DepositGold(
        grace,
        187,
        GoldIncomeSource.Other);
Expect(
    clearedCycle.Success
    && clearedCycle.Amount == 0
    && clearedCycle.SecondaryAmount == 187
    && BankService.GetCreditDebt(grace) == 0
    && BankService.GetDebtCycleFloors(grace) == 0
    && BankStateStore.Get(grace).DebtGraceUsed == 1
    && BankService.GetDebtGraceFloorsRemaining(grace) == 0,
    "Automatic repayment did not clear and reset the debt cycle.");
_ = BankService.TrySpend(grace, 50);
Expect(
    BankService.AccrueDebtInterest(grace, 1207).Amount == 13
    && BankService.GetCreditDebt(grace) == 63
    && BankService.GetDebtCycleFloors(grace) == 4,
    "A later debt cycle incorrectly received a second grace period.");

Player tycoonRate = OpenCardPlayer(CreditTier.VisaTycoon, 13);
_ = BankService.TrySpend(tycoonRate, 100);
_ = BankService.AccrueDebtInterest(tycoonRate, 1301);
_ = BankService.AccrueDebtInterest(tycoonRate, 1302);
_ = BankService.AccrueDebtInterest(tycoonRate, 1303);
Expect(
    BankService.AccrueDebtInterest(tycoonRate, 1304).Amount == 28
    && BankService.GetCreditDebt(tycoonRate) == 128
    && BankService.GetDebtInterestBasisPoints(
        CreditTier.VisaTycoon) == 2799,
    "Visa Tycoon did not charge ceil(100 * 27.99%) on floor four.");



Player incomePriority = OpenCardPlayer(CreditTier.VisaPoor, 14);
_ = BankService.TrySpend(incomePriority, 80);
int incomeQualification =
    BankService.GetQualifyingEarned(incomePriority);
BankOperationResult otherIncome = BankService.DepositGold(
    incomePriority,
    30,
    GoldIncomeSource.Other);
BankOperationResult normalIncome = BankService.DepositGold(
    incomePriority,
    60,
    GoldIncomeSource.NormalGameGold);
Expect(
    otherIncome == BankOperationResult.Ok(0, 30)
    && normalIncome == BankOperationResult.Ok(10, 50)
    && incomePriority.Gold == 10
    && BankService.GetCreditDebt(incomePriority) == 0
    && BankService.GetQualifyingEarned(incomePriority)
        == incomeQualification + 60,
    "Incoming gold did not pay debt first or qualification used the wrong source.");

Player nativeDebt = OpenCardPlayer(CreditTier.VisaPoor, 15);
_ = BankService.TrySpend(nativeDebt, 80);
int nativeDebtQualification =
    BankService.GetQualifyingEarned(nativeDebt);
nativeDebt.Gold += 30;
BankOperationResult observedDebtPayment =
    BankService.RecordNativeGoldGainAmount(
        nativeDebt,
        30,
        wasStolenBack: false);
Expect(
    observedDebtPayment == BankOperationResult.Ok(0, 30)
    && nativeDebt.Gold == 0
    && BankService.GetCreditDebt(nativeDebt) == 50
    && BankService.GetQualifyingEarned(nativeDebt)
        == nativeDebtQualification + 30,
    "Native GainGold was not intercepted for automatic repayment.");



Player interestPaysDebt = NewPlayer(100, 16);
BankStateStore.Set(
    interestPaysDebt,
    new AccountState
    {
        Schema = AccountState.CurrentSchema,
        BankAccountOpened = 1,
        UnifiedSavingsInitialized = 1,
        QualificationInitialized = 1,
        QualifyingEarned = 300,
        SavingsPrincipal = 100,
        CreditTier = (int)CreditTier.VisaMiddleClass,
        CreditDebt = 30,
    });
BankOperationResult debtPaidByInterest =
    BankService.AccrueSavingsInterest(interestPaysDebt, 1601);
Expect(
    debtPaidByInterest == BankOperationResult.Ok(0, 1)
    && interestPaysDebt.Gold == 100
    && BankService.GetCreditDebt(interestPaysDebt) == 29
    && BankService.GetSavingsInterest(interestPaysDebt) == 0,
    "Savings interest did not automatically repay debt first.");
Expect(
    BankService.GetQualifyingEarned(interestPaysDebt) == 301
    && BankService.GetSavingsInterestEarnedTotal(interestPaysDebt) == 1,
    "Debt-paid savings interest did not count as issued interest and qualification.");




Player directCeiling = OpenCardPlayer(CreditTier.VisaPoor, 17);
BankOperationResult directCeilingFirst =
    BankService.TrySpend(directCeiling, 200);
BankOperationResult directCeilingSpend =
    BankService.TrySpend(directCeiling, 200);
AccountSnapshot directClosed = BankService.GetSnapshot(directCeiling);
Expect(
    directCeilingFirst.Success
    && directCeilingSpend.Success
    && directCeiling.Gold == 0
    && directClosed.CreditDebt == 0
    && directClosed.CreditTier == CreditTier.None
    && directClosed.IsBankrupt
    && BankStateStore.Get(directCeiling).CreditPermanentlyClosed == 1
    && BankService.GetPendingRelicLiquidationDebt(directCeiling) == 400
    && BankService.ApplyForCreditCard(
        directCeiling,
        CreditTier.VisaPoor).Error
        == BankErrorCode.CreditPermanentlyClosed,
    "Direct TrySpend at the 200% ceiling did not settle without negative gold and permanently close.");
Expect(
    BankService.CompletePendingRelicLiquidation(
        directCeiling,
        399).Error == BankErrorCode.InvalidAmount
    && BankService.GetPendingRelicLiquidationDebt(directCeiling) == 400,
    "A stale relic-liquidation acknowledgement cleared the current quote.");
Expect(
    BankService.CompletePendingRelicLiquidation(
        directCeiling,
        400) == BankOperationResult.Ok(400)
    && BankService.GetPendingRelicLiquidationDebt(directCeiling) == 0
    && BankService.CompletePendingRelicLiquidation(
        directCeiling,
        400).Error == BankErrorCode.AlreadyProcessed,
    "Relic-liquidation acknowledgement was not exact and idempotent.");

Player legacyNegativeForeclosure = NewPlayer(-800, 1701);
BankStateStore.Set(
    legacyNegativeForeclosure,
    new AccountState
    {
        Schema = AccountState.CurrentSchema,
        BankAccountOpened = 1,
        UnifiedSavingsInitialized = 1,
        QualificationInitialized = 1,
        CreditPermanentlyClosed = 1,
    });
BankOperationResult legacyRepair =
    BankService.RepairLegacyNegativeForeclosureBalance(
        legacyNegativeForeclosure);
Expect(
    legacyRepair == BankOperationResult.Ok(800)
    && legacyNegativeForeclosure.Gold == 0
    && BankService.RepairLegacyNegativeForeclosureBalance(
        legacyNegativeForeclosure) == BankOperationResult.Ok(),
    "Legacy foreclosure repair did not restore a negative wallet to zero idempotently.");


Player interestCeiling = NewPlayer(0, 18);
BankStateStore.Set(
    interestCeiling,
    new AccountState
    {
        Schema = AccountState.CurrentSchema,
        BankAccountOpened = 1,
        UnifiedSavingsInitialized = 1,
        QualificationInitialized = 1,
        QualifyingEarned = 200,
        CreditTier = (int)CreditTier.VisaPoor,
        CreditDebt = 390,
        DebtCycleFloors = 3,
        DebtGraceUsed = 1,
    });
BankOperationResult interestClosure =
    BankService.AccrueDebtInterest(interestCeiling, 1801);
Expect(
    interestClosure.Success
    && interestClosure.Amount == 10
    && interestClosure.SecondaryAmount == 400
    && interestCeiling.Gold == 0
    && BankService.IsBankrupt(interestCeiling)
    && BankService.GetPendingRelicLiquidationDebt(
        interestCeiling) == 400,
    "Interest reaching 200% did not cap, settle without negative gold, and permanently close.");



Player nativeCeiling = OpenCardPlayer(CreditTier.VisaPoor, 19);
_ = BankService.TrySpend(nativeCeiling, 200);
BankOperationResult nativeAdvance =
    BankService.AdvanceCreditForPurchase(nativeCeiling, 200);
Expect(
    nativeAdvance.Success
    && nativeCeiling.Gold == 200
    && BankStateStore.Get(nativeCeiling).CreditDebt == 400
    && BankStateStore.Get(nativeCeiling).CreditCeilingPending == 1
    && BankStateStore.Get(nativeCeiling).CreditPermanentlyClosed == 0,
    "Native credit advance did not defer an exact-ceiling collection.");
int beforeNativePurchase = nativeCeiling.Gold;
nativeCeiling.Gold = 0;
BankOperationResult nativeFinalization =
    BankService.RecordNativeGoldLoss(
        nativeCeiling,
        beforeNativePurchase);
Expect(
    nativeFinalization.Success
    && nativeFinalization.SecondaryAmount == 400
    && nativeCeiling.Gold == 0
    && BankService.IsBankrupt(nativeCeiling)
    && BankStateStore.Get(nativeCeiling).CreditCeilingPending == 0
    && BankService.GetPendingRelicLiquidationDebt(nativeCeiling) == 400,
    "Native LoseGold did not finalize pending ceiling settlement without negative gold.");



(int Debt, int Relics)[] relicBoundaries =
{
    (0, 0),
    (1, 1),
    (99, 1),
    (100, 1),
    (149, 1),
    (150, 2),
    (199, 2),
    (200, 2),
    (249, 2),
    (250, 3),
    (2000, 20),
    (4000, 40),
};
Expect(
    relicBoundaries.All(pair =>
        CreditCeilingRelicLiquidationService.CalculateRelicsRequested(
            pair.Debt)
            == pair.Relics),
    "Ceiling relic-seizure nearest-hundred boundary rules are incorrect.");

Player nextFloorWarning = OpenAscensionPlayer(0, 0, 20001);
BankStateStore.Set(
    nextFloorWarning,
    new AccountState
    {
        Schema = AccountState.CurrentSchema,
        BankAccountOpened = 1,
        UnifiedSavingsInitialized = 1,
        QualificationInitialized = 1,
        CreditTier = (int)CreditTier.VisaPoor,
        CreditDebt = 390,
        DebtCycleFloors = 3,
        DebtGraceUsed = 1,
    });
CreditCeilingWarning? warning =
    CreditCeilingRelicLiquidationService.GetNextFloorWarning(
        nextFloorWarning,
        nextFloorWarning.RunState);
BankStateStore.Get(nextFloorWarning).CreditDebt = 300;
CreditCeilingWarning? safeNextFloor =
    CreditCeilingRelicLiquidationService.GetNextFloorWarning(
        nextFloorWarning,
        nextFloorWarning.RunState);
BankStateStore.Get(nextFloorWarning).CreditDebt = 390;
BankStateStore.Get(nextFloorWarning).DebtCycleFloors = 2;
CreditCeilingWarning? graceNextFloor =
    CreditCeilingRelicLiquidationService.GetNextFloorWarning(
        nextFloorWarning,
        nextFloorWarning.RunState);
Expect(
    warning == new CreditCeilingWarning(400, 4)
    && safeNextFloor is null
    && graceNextFloor is null,
    "The next-floor ceiling warning did not match interest, grace, "
    + "or relic-seizure rules.");



Player sender = OpenPlayer(100, 21);
Player recipient = OpenCardPlayer(CreditTier.VisaPoor, 22);
_ = BankService.TrySpend(recipient, 50);
int senderQualification =
    BankService.GetQualifyingEarned(sender);
int recipientQualification =
    BankService.GetQualifyingEarned(recipient);
BankOperationResult transferIntoDebt =
    BankService.ETransfer(sender, recipient, 25);
BankOperationResult transferPastDebt =
    BankService.ETransfer(sender, recipient, 40);
Expect(
    transferIntoDebt == BankOperationResult.Ok(0, 25)
    && transferPastDebt == BankOperationResult.Ok(15, 25)
    && sender.Gold == 35
    && recipient.Gold == 15
    && BankService.GetCreditDebt(recipient) == 0
    && BankService.GetQualifyingEarned(sender) == senderQualification
    && BankService.GetQualifyingEarned(recipient)
        == recipientQualification,
    "e-Transfer did not repay recipient debt first or changed qualification.");

Player compositionSender = OpenPlayer(100, 23);
Player compositionRecipient = OpenPlayer(0, 24);
_ = BankService.AccrueSavingsInterest(compositionSender, 2301);
_ = BankService.ETransfer(
    compositionSender,
    compositionRecipient,
    15);
Expect(
    BankService.GetSavingsInterest(compositionSender) == 0
    && BankService.GetSavingsPrincipal(compositionSender) == 86
    && BankService.GetSavingsInterest(compositionRecipient) == 1
    && BankService.GetSavingsPrincipal(compositionRecipient) == 14,
    "e-Transfer laundered already-earned interest into principal.");
MethodInfo[] eTransferMethods = typeof(BankService)
    .GetMethods(BindingFlags.Public | BindingFlags.Static)
    .Where(method => method.Name == nameof(BankService.ETransfer))
    .ToArray();
Expect(
    eTransferMethods.Length == 1
    && eTransferMethods[0]
        .GetParameters()
        .Select(parameter => parameter.ParameterType)
        .SequenceEqual(new[] { typeof(Player), typeof(Player), typeof(int) }),
    "e-Transfer service API must contain only sender, recipient, and amount.");
string[] eTransferRequestProperties = typeof(BankETransferRequest)
    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
    .Select(property => property.Name)
    .OrderBy(name => name, StringComparer.Ordinal)
    .ToArray();
Expect(
    eTransferRequestProperties.SequenceEqual(
        new[] { "Amount", "RecipientId" }),
    "The e-Transfer UI request must contain only amount and recipient.");



Player kidney = OpenPlayer(0, 25, 50, 70);
int kidneyQualification =
    BankService.GetQualifyingEarned(kidney);
BankOperationResult kidneySale =
    await KkCompoundService.SellKidneys(kidney, 1);
Expect(
    kidneySale == BankOperationResult.Ok(200, 0)
    && kidney.Creature.CurrentHp == 40
    && kidney.Creature.MaxHp == 60
    && kidney.Gold == 200
    && BankService.GetQualifyingEarned(kidney)
        == kidneyQualification,
    "50/70 HP selling one kidney did not become 40/60 HP plus 200G.");

Player kidneyEleven = OpenPlayer(0, 26, 11, 11);
Expect(
    (await KkCompoundService.SellKidneys(
        kidneyEleven,
        1)).Success
    && kidneyEleven.Creature.CurrentHp == 1
    && kidneyEleven.Creature.MaxHp == 1,
    "11/11 HP should safely allow exactly one kidney sale.");
Player kidneyTen = OpenPlayer(0, 27, 10, 10);
AccountSnapshot kidneyTenBefore = BankService.GetSnapshot(kidneyTen);
Expect(
    (await KkCompoundService.SellKidneys(
        kidneyTen,
        1)).Error == BankErrorCode.InsufficientHealth
    && kidneyTen.Creature.CurrentHp == 10
    && kidneyTen.Creature.MaxHp == 10
    && BankService.GetSnapshot(kidneyTen) == kidneyTenBefore,
    "10/10 HP kidney refusal was not mutation-free.");

Player buttSix = OpenPlayer(0, 28, 6, 60);
Expect(
    (await KkCompoundService.SellButt(buttSix))
        == BankOperationResult.Ok(50, 0)
    && buttSix.Creature.CurrentHp == 1
    && buttSix.Creature.MaxHp == 60
    && buttSix.Gold == 50
    && BankStateStore.Get(buttSix).ButtSalesCount == 1,
    "6 HP butt sale did not become 1 HP plus 50G.");
Player buttFive = OpenPlayer(0, 29, 5, 60);
AccountSnapshot buttFiveBefore = BankService.GetSnapshot(buttFive);
Expect(
    (await KkCompoundService.SellButt(buttFive)).Error
        == BankErrorCode.InsufficientHealth
    && buttFive.Creature.CurrentHp == 5
    && BankService.GetSnapshot(buttFive) == buttFiveBefore,
    "5 HP butt refusal was not mutation-free.");




MethodInfo afterCurrentHpChanged = AccessTools.DeclaredMethod(
    typeof(Hook),
    nameof(Hook.AfterCurrentHpChanged))
    ?? throw new MissingMethodException(
        typeof(Hook).FullName,
        nameof(Hook.AfterCurrentHpChanged));
const string kkHealthHookOwner = "TDBank.Tests.KkHealthHookIsolation";
var kkHealthHookHarmony = new Harmony(kkHealthHookOwner);
try
{
    kkHealthHookHarmony.Patch(
        afterCurrentHpChanged,
        prefix: new HarmonyMethod(
            typeof(HealthHookProbe),
            nameof(HealthHookProbe.CountCall)));
    HealthHookProbe.CallCount = 0;

    Player isolatedButt = OpenPlayer(0, 2901, 20, 30);
    int buttCurrentHpSignals = 0;
    int buttMaxHpSignals = 0;
    isolatedButt.Creature.CurrentHpChanged += (_, _) =>
        buttCurrentHpSignals++;
    isolatedButt.Creature.MaxHpChanged += (_, _) =>
        buttMaxHpSignals++;
    BankOperationResult isolatedButtResult =
        await KkCompoundService.SellButt(isolatedButt);

    Player isolatedKidney = OpenPlayer(0, 2902, 30, 40);
    int kidneyCurrentHpSignals = 0;
    int kidneyMaxHpSignals = 0;
    isolatedKidney.Creature.CurrentHpChanged += (_, _) =>
        kidneyCurrentHpSignals++;
    isolatedKidney.Creature.MaxHpChanged += (_, _) =>
        kidneyMaxHpSignals++;
    BankOperationResult isolatedKidneyResult =
        await KkCompoundService.SellKidneys(isolatedKidney, 1);

    Player isolatedHemorrhage = OpenFourthButtSaleFixture(
        ButtRiskOutcome.Hemorrhage,
        ascensionLevel: 0,
        gold: 0,
        currentHp: 20,
        maxHp: 30,
        firstNetId: 29030);
    int hemorrhageCurrentHpSignals = 0;
    isolatedHemorrhage.Creature.CurrentHpChanged += (_, _) =>
        hemorrhageCurrentHpSignals++;
    BankOperationResult isolatedHemorrhageResult =
        await KkCompoundService.SellButt(isolatedHemorrhage);

    Expect(
        isolatedButtResult == BankOperationResult.Ok(50, 0)
        && isolatedButt.Creature.CurrentHp == 15
        && buttCurrentHpSignals == 1
        && buttMaxHpSignals == 0
        && isolatedKidneyResult == BankOperationResult.Ok(200, 0)
        && isolatedKidney.Creature.CurrentHp == 20
        && isolatedKidney.Creature.MaxHp == 30
        && kidneyCurrentHpSignals == 1
        && kidneyMaxHpSignals == 1
        && isolatedHemorrhageResult == BankOperationResult.Ok(
            30,
            0,
            ButtRiskOutcome.Hemorrhage)
        && isolatedHemorrhage.Creature.CurrentHp == 4
        && hemorrhageCurrentHpSignals == 1
        && HealthHookProbe.CallCount == 0,
        "KK health costs triggered gameplay HP hooks or failed to notify "
        + "HUD/checksum value-change listeners.");

    Player nativeHpControl = OpenPlayer(0, 2903, 20, 30);
    bool nativeHpControlUnavailable = false;
    try
    {
        await CreatureCmd.SetCurrentHp(
            nativeHpControl.Creature,
            19);
    }
    catch (NullReferenceException)
    {
        nativeHpControlUnavailable = true;
    }
    Expect(
        HealthHookProbe.CallCount == 1
        || (nativeHpControlUnavailable
            && HealthHookProbe.CallCount == 0),
        "The HP-hook isolation probe did not observe a native "
        + "CreatureCmd.SetCurrentHp control call.");
}
finally
{
    kkHealthHookHarmony.UnpatchAll(kkHealthHookOwner);
}




MethodInfo nativeGainGold = AccessTools.DeclaredMethod(
    typeof(PlayerCmd),
    nameof(PlayerCmd.GainGold),
    [typeof(decimal), typeof(Player), typeof(bool)])
    ?? throw new MissingMethodException(
        typeof(PlayerCmd).FullName,
        nameof(PlayerCmd.GainGold));
MethodInfo nativeLoseGold = AccessTools.DeclaredMethod(
    typeof(PlayerCmd),
    nameof(PlayerCmd.LoseGold),
    [typeof(decimal), typeof(Player), typeof(GoldLossType)])
    ?? throw new MissingMethodException(
        typeof(PlayerCmd).FullName,
        nameof(PlayerCmd.LoseGold));
const string bankGoldHookOwner = "TDBank.Tests.BankGoldCommandIsolation";
var bankGoldHookHarmony = new Harmony(bankGoldHookOwner);
try
{
    bankGoldHookHarmony.Patch(
        nativeGainGold,
        prefix: new HarmonyMethod(
            typeof(NativeGoldCommandProbe),
            nameof(NativeGoldCommandProbe.CountGainAndSkip)));
    bankGoldHookHarmony.Patch(
        nativeLoseGold,
        prefix: new HarmonyMethod(
            typeof(NativeGoldCommandProbe),
            nameof(NativeGoldCommandProbe.CountLossAndSkip)));
    NativeGoldCommandProbe.GainCallCount = 0;
    NativeGoldCommandProbe.LossCallCount = 0;

    Player bankOnlyGold = OpenPlayer(100, 2904, 20, 30);
    Player transferTarget = OpenPlayer(0, 2905, 20, 30);
    BankOperationResult internalDeposit = BankService.DepositGold(
        bankOnlyGold,
        20,
        GoldIncomeSource.OrganSale);
    BankOperationResult internalInterest =
        BankService.AccrueSavingsInterest(bankOnlyGold, 2904);
    BankOperationResult internalButt =
        await KkCompoundService.SellButt(bankOnlyGold);
    BankOperationResult internalTransfer =
        BankService.ETransfer(
            bankOnlyGold,
            transferTarget,
            10);
    Player internalCeiling =
        OpenCardPlayer(CreditTier.VisaPoor, 2906);
    BankOperationResult internalFirstCharge =
        BankService.TrySpend(internalCeiling, 200);
    BankOperationResult internalCeilingCharge =
        BankService.TrySpend(internalCeiling, 200);

    Expect(
        internalDeposit.Success
        && internalInterest.Success
        && internalButt.Success
        && internalTransfer.Success
        && internalFirstCharge.Success
        && internalCeilingCharge.Success
        && BankService.IsBankrupt(internalCeiling)
        && NativeGoldCommandProbe.GainCallCount == 0
        && NativeGoldCommandProbe.LossCallCount == 0,
        "TD-created gold, transfers, interest, or debt collection entered "
        + "the native GainGold/LoseGold command pipeline.");

    await PlayerCmd.GainGold(1m, bankOnlyGold);
    await PlayerCmd.LoseGold(
        1m,
        bankOnlyGold,
        GoldLossType.Lost);
    Expect(
        NativeGoldCommandProbe.GainCallCount == 1
        && NativeGoldCommandProbe.LossCallCount == 1,
        "The native-gold isolation probes did not observe their positive "
        + "control command calls.");
}
finally
{
    bankGoldHookHarmony.UnpatchAll(bankGoldHookOwner);
}

int[][] expectedHemorrhageByBand =
{
    [10, 15, 20, 25],
    [9, 13, 17, 21],
    [8, 11, 15, 19],
    [7, 10, 13, 16],
    [5, 8, 10, 13],
};
int[] riskBandAscensions = [0, 3, 5, 7, 9];
for (var band = 0; band < riskBandAscensions.Length; band++)
{
    for (var riskIndex = 0; riskIndex < 4; riskIndex++)
    {
        int completedSales = 3 + riskIndex;
        ButtRiskProfile profile =
            KkCompoundService.GetButtRiskProfile(
                completedSales,
                riskBandAscensions[band]);
        int expectedUnpaid = 20 + riskIndex * 10;
        int expectedHemorrhage =
            expectedHemorrhageByBand[band][riskIndex];
        Expect(
            profile.UnpaidPercent == expectedUnpaid
            && profile.HemorrhagePercent == expectedHemorrhage
            && profile.NormalPercent
                == 100 - expectedUnpaid - expectedHemorrhage,
            $"A{riskBandAscensions[band]} risk profile is wrong "
            + $"for sale {completedSales + 1}.");
        Expect(
            KkCompoundService.ResolveButtRiskOutcome(
                completedSales,
                riskBandAscensions[band],
                expectedUnpaid - 1)
                == ButtRiskOutcome.Unpaid
            && KkCompoundService.ResolveButtRiskOutcome(
                completedSales,
                riskBandAscensions[band],
                expectedUnpaid)
                == ButtRiskOutcome.Hemorrhage
            && KkCompoundService.ResolveButtRiskOutcome(
                completedSales,
                riskBandAscensions[band],
                expectedUnpaid + expectedHemorrhage - 1)
                == ButtRiskOutcome.Hemorrhage
            && KkCompoundService.ResolveButtRiskOutcome(
                completedSales,
                riskBandAscensions[band],
                expectedUnpaid + expectedHemorrhage)
                == ButtRiskOutcome.Normal,
            $"A{riskBandAscensions[band]} risk thresholds overlap "
            + $"or leave a gap for sale {completedSales + 1}.");
    }
}
Expect(
    KkCompoundService.GetButtRiskProfile(0, 0) == default
    && KkCompoundService.GetButtRiskProfile(2, 10) == default
    && Enumerable.Range(0, 100).All(roll =>
        KkCompoundService.ResolveButtRiskOutcome(2, 10, roll)
            == ButtRiskOutcome.Normal),
    "The first three butt sales are not guaranteed safe.");
Expect(
    KkCompoundService.CalculateButtHpCost(5, 2) == 5
    && KkCompoundService.CalculateButtHpCost(5, 3) == 8
    && KkCompoundService.CalculateButtHpCost(5, 6) == 12
    && KkCompoundService.CalculateButtHpCost(5, 9) == 17
    && KkCompoundService.CalculateButtHpCost(5, 10) == 19
    && KkCompoundService.CalculateButtGoldValue(50, 2) == 50
    && KkCompoundService.CalculateButtGoldValue(50, 3) == 30
    && KkCompoundService.CalculateButtGoldValue(50, 6) == 17
    && KkCompoundService.CalculateButtGoldValue(50, 9) == 10
    && KkCompoundService.CalculateButtGoldValue(80, 3) == 48
    && KkCompoundService.CalculateButtGoldValue(80, 6) == 28
    && KkCompoundService.CalculateButtGoldValue(80, 9) == 12,
    "Repeated butt-sale HP escalation or payout decay regressed.");

Player repeatCustomer =
    OpenAscensionPlayer(0, 0, 30, 32, 60);
BankOperationResult[] repeatCustomerResults = new BankOperationResult[4];
for (var sale = 0; sale < 3; sale++)
{
    Expect(
        KkCompoundService.GetButtGoldValueForNextSale(
            repeatCustomer) == 50,
        $"A0 butt-sale preview is wrong before sale {sale + 1}.");
    repeatCustomerResults[sale] =
        await KkCompoundService.SellButt(repeatCustomer);
}
ButtRiskOutcome repeatFourthOutcome =
    KkCompoundService.GetButtRiskOutcomeForNextSale(repeatCustomer);
ButtRiskOutcome repeatedPreviewOutcome =
    KkCompoundService.GetButtRiskOutcomeForNextSale(repeatCustomer);
IReadOnlyDictionary<string, int> gameRngCountersBefore =
    SnapshotRunRngCounters(repeatCustomer.RunState);
repeatCustomerResults[3] =
    await KkCompoundService.SellButt(repeatCustomer);
IReadOnlyDictionary<string, int> gameRngCountersAfter =
    SnapshotRunRngCounters(repeatCustomer.RunState);
int repeatFourthGold =
    repeatFourthOutcome == ButtRiskOutcome.Unpaid ? 0 : 30;
int repeatFourthHp =
    repeatFourthOutcome == ButtRiskOutcome.Hemorrhage ? 16 : 8;
Expect(
    repeatCustomer.Creature.CurrentHp == 17 - repeatFourthHp
    && repeatCustomer.Gold == 150 + repeatFourthGold
    && repeatCustomerResults.Take(3).All(result =>
        result == BankOperationResult.Ok(50, 0))
    && repeatCustomerResults[3] == BankOperationResult.Ok(
        repeatFourthGold,
        0,
        repeatFourthOutcome)
    && repeatFourthOutcome == repeatedPreviewOutcome
    && SameRunRngCounters(
        gameRngCountersBefore,
        gameRngCountersAfter)
    && BankStateStore.Get(repeatCustomer).ButtSalesCount == 4,
    "The A0 fourth sale did not apply its fixed private-RNG outcome "
    + "without consuming a game RNG stream.");

Player unpaidCustomer = OpenFourthButtSaleFixture(
    ButtRiskOutcome.Unpaid,
    ascensionLevel: 0,
    gold: 0,
    currentHp: 20,
    maxHp: 60,
    firstNetId: 30000);
AccountState unpaidReloadState =
    BankStateStore.Get(unpaidCustomer).Clone();
ButtRiskOutcome unpaidBeforeReload =
    KkCompoundService.GetButtRiskOutcomeForNextSale(unpaidCustomer);
BankStateStore.Set(unpaidCustomer, unpaidReloadState);
ButtRiskOutcome unpaidAfterReload =
    KkCompoundService.GetButtRiskOutcomeForNextSale(unpaidCustomer);
BankOperationResult unpaidSale =
    await KkCompoundService.SellButt(unpaidCustomer);
Expect(
    unpaidBeforeReload == ButtRiskOutcome.Unpaid
    && unpaidAfterReload == ButtRiskOutcome.Unpaid
    && unpaidSale == BankOperationResult.Ok(
        0,
        0,
        ButtRiskOutcome.Unpaid)
    && unpaidCustomer.Creature.CurrentHp == 12
    && unpaidCustomer.Gold == 0
    && BankStateStore.Get(unpaidCustomer).ButtSalesCount == 4,
    "The unpaid event changed after state reload, paid gold, or failed "
    + "to charge normal HP and count the valid sale.");

Player hemorrhageCustomer = OpenFourthButtSaleFixture(
    ButtRiskOutcome.Hemorrhage,
    ascensionLevel: 0,
    gold: 0,
    currentHp: 17,
    maxHp: 60,
    firstNetId: 40000);
BankOperationResult hemorrhageSale =
    await KkCompoundService.SellButt(hemorrhageCustomer);
Expect(
    hemorrhageSale == BankOperationResult.Ok(
        30,
        0,
        ButtRiskOutcome.Hemorrhage)
    && hemorrhageCustomer.Creature.CurrentHp == 1
    && hemorrhageCustomer.Gold == 30
    && BankStateStore.Get(hemorrhageCustomer).ButtSalesCount == 4,
    "The hemorrhage event did not charge double HP, preserve payout, "
    + "leave one HP, and count the valid sale.");

Player riskTooWeak = OpenAscensionPlayer(
    0,
    0,
    50000,
    currentHp: 10,
    maxHp: 60);
BankStateStore.Get(riskTooWeak).ButtSalesCount = 3;
AccountSnapshot riskTooWeakBefore =
    BankService.GetSnapshot(riskTooWeak);
Expect(
    !KkCompoundService.CanSafelySellButt(riskTooWeak)
    && (await KkCompoundService.SellButt(riskTooWeak)).Error
        == BankErrorCode.InsufficientHealth
    && riskTooWeak.Creature.CurrentHp == 10
    && riskTooWeak.Gold == 0
    && BankService.GetSnapshot(riskTooWeak) == riskTooWeakBefore,
    "A fourth-or-later sale bypassed the worst-case double-HP "
    + "preflight or mutated a rejected transaction.");

Player organDebt = OpenCardPlayer(
    CreditTier.VisaTycoon,
    31,
    gold: 0,
    currentHp: 50,
    maxHp: 70);
_ = BankService.TrySpend(organDebt, 220);
int organQualification =
    BankService.GetQualifyingEarned(organDebt);
BankOperationResult kidneyPaysDebt =
    await KkCompoundService.SellKidneys(organDebt, 1);
BankOperationResult buttPaysDebt =
    await KkCompoundService.SellButt(organDebt);
Expect(
    kidneyPaysDebt == BankOperationResult.Ok(0, 200)
    && buttPaysDebt == BankOperationResult.Ok(30, 20)
    && organDebt.Gold == 30
    && BankService.GetCreditDebt(organDebt) == 0
    && organDebt.Creature.CurrentHp == 35
    && organDebt.Creature.MaxHp == 60
    && BankService.GetQualifyingEarned(organDebt)
        == organQualification,
    "KK proceeds did not automatically repay a 220G debt before entering gold.");

Player overflowKidney = OpenPlayer(0, 32, 50, 70);
AccountSnapshot overflowKidneyBefore =
    BankService.GetSnapshot(overflowKidney);
Expect(
    (await KkCompoundService.SellKidneys(
        overflowKidney,
        int.MaxValue)).Error == BankErrorCode.ArithmeticOverflow
    && overflowKidney.Creature.CurrentHp == 50
    && overflowKidney.Creature.MaxHp == 70
    && BankService.GetSnapshot(overflowKidney)
        == overflowKidneyBefore,
    "Overflowing kidney quantity caused a partial mutation.");
Player overflowDeposit = OpenPlayer(int.MaxValue, 33);
AccountSnapshot overflowDepositBefore =
    BankService.GetSnapshot(overflowDeposit);
Expect(
    BankService.DepositGold(
        overflowDeposit,
        1,
        GoldIncomeSource.NormalGameGold).Error
        == BankErrorCode.ArithmeticOverflow
    && BankService.GetSnapshot(overflowDeposit)
        == overflowDepositBefore,
    "Overflowing gold deposit caused a partial mutation.");



var accountPacketSource = new AccountState
{
    Schema = AccountState.CurrentSchema,
    QualifyingEarned = 601,
    QualificationInitialized = 1,
    SavingsPrincipal = 123,
    SavingsInterest = 45,
    SavingsTenths = 6,
    SavingsInterestTurns = 7,
    CreditTier = (int)CreditTier.VisaMiddleClass,
    CreditDebt = 123,
    LastDebtInterestCharge = 31,
    LastSavingsTurnToken = 987,
    LastDebtFloorToken = 986,
    DebtCycleFloors = 2,
    CreditCeilingPending = 1,
    BankAccountOpened = 1,
    ButtSalesCount = 9,
    UnifiedSavingsInitialized = 1,
    CreditPermanentlyClosed = 0,
    StolenSavingsPrincipal = 8,
    StolenSavingsInterest = 4,
    StolenSavingsTenths = 3,
    SavingsInterestEarnedTotal = 88,
    DebtGraceUsed = 1,
    PendingRelicLiquidationDebt = 800,
    Revision = 77,
};
var accountWriter = new PacketWriter();
accountPacketSource.Serialize(accountWriter);
var accountReader = new PacketReader();
accountReader.Reset(accountWriter.Buffer);
var accountPacketCopy = new AccountState();
accountPacketCopy.Deserialize(accountReader);
PropertyInfo[] accountIntProperties = typeof(AccountState)
    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
    .Where(property =>
        property.PropertyType == typeof(int)
        && property.CanRead
        && property.CanWrite)
    .ToArray();
Expect(
    accountPacketCopy.Schema == AccountState.CurrentSchema
    && accountIntProperties.All(property =>
        Equals(
            property.GetValue(accountPacketSource),
            property.GetValue(accountPacketCopy))),
    "Schema-6 account packet did not round-trip every persisted field.");



string[] operationNames = Enum.GetNames<BankOperationKind>()
    .OrderBy(name => name, StringComparer.Ordinal)
    .ToArray();
Expect(
    operationNames.SequenceEqual(
        new[]
        {
            "ApplyCard",
            "ETransfer",
            "OpenAccount",
            "SellButt",
            "SellKidneys",
        }),
    "The network operation enum contains a removed or missing operation.");

TDBankNetOperationAction[] operationPayloads =
{
    new()
    {
        Kind = BankOperationKind.ApplyCard,
        Tier = CreditTier.VisaPoor,
        LifecycleEpoch = BankNetwork.CurrentLifecycleEpoch,
        Amount = 0,
        RecipientId = 0,
        ExecutionType = GameActionType.NonCombat,
        RequestId = 9001,
        HostAuthorized = true,
    },
    new()
    {
        Kind = BankOperationKind.ETransfer,
        Tier = CreditTier.None,
        LifecycleEpoch = BankNetwork.CurrentLifecycleEpoch,
        Amount = 23,
        RecipientId = recipient.NetId,
        ExecutionType = GameActionType.CombatPlayPhaseOnly,
        RequestId = 9002,
        HostAuthorized = true,
    },
    new()
    {
        Kind = BankOperationKind.OpenAccount,
        Tier = CreditTier.None,
        LifecycleEpoch = BankNetwork.CurrentLifecycleEpoch,
        Amount = 0,
        RecipientId = 0,
        ExecutionType = GameActionType.NonCombat,
        RequestId = 9003,
        HostAuthorized = true,
    },
    new()
    {
        Kind = BankOperationKind.SellKidneys,
        Tier = CreditTier.None,
        LifecycleEpoch = BankNetwork.CurrentLifecycleEpoch,
        Amount = 2,
        RecipientId = 0,
        ExecutionType = GameActionType.NonCombat,
        RequestId = 9004,
        HostAuthorized = true,
    },
    new()
    {
        Kind = BankOperationKind.SellButt,
        Tier = CreditTier.None,
        LifecycleEpoch = BankNetwork.CurrentLifecycleEpoch,
        Amount = 1,
        RecipientId = 0,
        ExecutionType = GameActionType.CombatPlayPhaseOnly,
        RequestId = 9005,
        HostAuthorized = true,
    },
};
foreach (TDBankNetOperationAction payload in operationPayloads)
{
    ExpectSamePayload(
        payload,
        RoundTrip(payload),
        $"{payload.Kind} network payload did not round-trip.");
}
var wireWriter = new PacketWriter();
operationPayloads[3].Serialize(wireWriter);
Expect(
    wireWriter.BytePosition == 45,
    $"TDBC request packets must retain the 45-byte frame shape, actual {wireWriter.BytePosition}.");
var wireReader = new PacketReader();
wireReader.Reset(wireWriter.Buffer);
_ = wireReader.ReadInt();
_ = wireReader.ReadInt();
Expect(
    wireReader.ReadInt() == TDBankNetOperationAction.ProtocolMagic
    && TDBankNetOperationAction.ProtocolMagic == 0x54444243
    && wireReader.ReadInt() == BankNetwork.CurrentLifecycleEpoch,
    "The operation packet does not carry TDBC and the lifecycle epoch.");

Player threePeerHost = OpenAscensionPlayer(
    0,
    99,
    92001,
    currentHp: 80,
    maxHp: 80);
BankStateStore.Get(threePeerHost).ButtSalesCount = 3;
TDBankAuthoritativePlayerState threePeerHostState =
    TDBankAuthoritativePlayerState.Capture(threePeerHost);
ButtRiskOutcome threePeerOutcome =
    KkCompoundService.GetButtRiskOutcomeForNextSale(threePeerHost);
var threePeerPayload = RoundTrip(
    new TDBankNetOperationAction
    {
        Kind = BankOperationKind.SellButt,
        LifecycleEpoch = BankNetwork.CurrentLifecycleEpoch,
        Amount = 1,
        ExecutionType = GameActionType.NonCombat,
        RequestId = 92002,
        HostAuthorized = true,
        HasAuthoritativeState = true,
        ActorState = threePeerHostState,
        AuthoritativeButtOutcome = threePeerOutcome,
    });
Player threePeerClientOne = OpenAscensionPlayer(
    0,
    1,
    92001,
    currentHp: 30,
    maxHp: 60);
Player threePeerClientTwo = OpenAscensionPlayer(
    0,
    999,
    92001,
    currentHp: 70,
    maxHp: 90);
threePeerPayload.ActorState!.Apply(threePeerClientOne);
threePeerPayload.ActorState.Apply(threePeerClientTwo);
BankOperationResult threePeerHostResult =
    await KkCompoundService.SellButt(
        threePeerHost,
        threePeerOutcome);
BankOperationResult threePeerClientOneResult =
    await KkCompoundService.SellButt(
        threePeerClientOne,
        threePeerPayload.AuthoritativeButtOutcome);
BankOperationResult threePeerClientTwoResult =
    await KkCompoundService.SellButt(
        threePeerClientTwo,
        threePeerPayload.AuthoritativeButtOutcome);
Expect(
    threePeerHostResult == threePeerClientOneResult
    && threePeerHostResult == threePeerClientTwoResult
    && SameAuthoritativePlayerState(
        TDBankAuthoritativePlayerState.Capture(threePeerHost),
        TDBankAuthoritativePlayerState.Capture(threePeerClientOne))
    && SameAuthoritativePlayerState(
        TDBankAuthoritativePlayerState.Capture(threePeerHost),
        TDBankAuthoritativePlayerState.Capture(threePeerClientTwo)),
    "A host-authoritative butt sale did not converge on all three peers.");

Player transferHostSender = OpenPlayer(500, 92101, 80, 80);
Player transferHostRecipient = OpenPlayer(10, 92102, 80, 80);
var transferPayload = RoundTrip(
    new TDBankNetOperationAction
    {
        Kind = BankOperationKind.ETransfer,
        LifecycleEpoch = BankNetwork.CurrentLifecycleEpoch,
        Amount = 100,
        RecipientId = transferHostRecipient.NetId,
        ExecutionType = GameActionType.NonCombat,
        RequestId = 92103,
        HostAuthorized = true,
        HasAuthoritativeState = true,
        ActorState =
            TDBankAuthoritativePlayerState.Capture(transferHostSender),
        RecipientState =
            TDBankAuthoritativePlayerState.Capture(
                transferHostRecipient),
    });
Player transferClientSender = OpenPlayer(5, 92101, 40, 50);
Player transferClientRecipient = OpenPlayer(900, 92102, 60, 70);
transferPayload.ActorState!.Apply(transferClientSender);
transferPayload.RecipientState!.Apply(transferClientRecipient);
BankOperationResult transferHostResult = BankService.ETransfer(
    transferHostSender,
    transferHostRecipient,
    100);
BankOperationResult transferClientResult = BankService.ETransfer(
    transferClientSender,
    transferClientRecipient,
    100);
Expect(
    transferHostResult == transferClientResult
    && SameAuthoritativePlayerState(
        TDBankAuthoritativePlayerState.Capture(transferHostSender),
        TDBankAuthoritativePlayerState.Capture(transferClientSender))
    && SameAuthoritativePlayerState(
        TDBankAuthoritativePlayerState.Capture(transferHostRecipient),
        TDBankAuthoritativePlayerState.Capture(
            transferClientRecipient)),
    "A host-authoritative e-Transfer did not converge sender and recipient.");

Player actionOwner = NewPlayer(55, 34);
var openAction = new TDBankOperationGameAction(
    actionOwner,
    BankOperationKind.OpenAccount,
    executionType: GameActionType.NonCombat,
    requestId: 9101,
    hostAuthorized: true);
ExpectSamePayload(
    operationPayloads[2] with { RequestId = 9101 },
    (TDBankNetOperationAction)openAction.ToNetAction(),
    "OpenAccount GameAction.ToNetAction did not preserve its payload.");
Expect(
    (await openAction.ExecuteLedgerOperationAsync()).Success
    && BankService.IsAccountOpened(actionOwner),
    "ExecuteLedgerOperationAsync did not execute an authorized OpenAccount.");

(bool Authorized, GameActionType ExecutionType) hostRejection =
    TDBankNetOperationAction.ResolveAuthorizationForPeer(
        NetGameType.Host,
        ActionSynchronizerCombatState.EndTurnPhaseOne,
        BankOperationKind.SellButt,
        GameActionType.CombatPlayPhaseOnly,
        payloadAuthorization: true);
AccountSnapshot beforeRejectedAction =
    BankService.GetSnapshot(actionOwner);
var rejectedAction = new TDBankOperationGameAction(
    actionOwner,
    BankOperationKind.SellButt,
    executionType: hostRejection.ExecutionType,
    requestId: 9102,
    hostAuthorized: hostRejection.Authorized);
Expect(
    hostRejection == (false, GameActionType.Any)
    && rejectedAction.ActionType == GameActionType.Any
    && (await rejectedAction.ExecuteLedgerOperationAsync()).Error
        == BankErrorCode.OperationUnavailable
    && BankService.GetSnapshot(actionOwner)
        == beforeRejectedAction,
    "A host-rejected operation was not a mutation-free Any no-op.");

(bool Authorized, GameActionType ExecutionType) combatButtRejection =
    TDBankNetOperationAction.ResolveAuthorizationForPeer(
        NetGameType.Host,
        ActionSynchronizerCombatState.PlayPhase,
        BankOperationKind.SellButt,
        GameActionType.CombatPlayPhaseOnly,
        payloadAuthorization: true);
(bool Authorized, GameActionType ExecutionType) combatKidneyRejection =
    TDBankNetOperationAction.ResolveAuthorizationForPeer(
        NetGameType.Client,
        ActionSynchronizerCombatState.PlayPhase,
        BankOperationKind.SellKidneys,
        GameActionType.CombatPlayPhaseOnly,
        payloadAuthorization: true);
(bool Authorized, GameActionType ExecutionType) mapButtAuthorization =
    TDBankNetOperationAction.ResolveAuthorizationForPeer(
        NetGameType.Host,
        ActionSynchronizerCombatState.NotInCombat,
        BankOperationKind.SellButt,
        GameActionType.NonCombat,
        payloadAuthorization: true);
Expect(
    combatButtRejection == (false, GameActionType.Any)
    && combatKidneyRejection == (false, GameActionType.Any)
    && mapButtAuthorization
        == (true, GameActionType.NonCombat),
    "Organ sales were not rejected during combat or allowed outside it.");

BankOperationKind unknownKind = (BankOperationKind)999;
(bool Authorized, GameActionType ExecutionType) unknownAuthorization =
    TDBankNetOperationAction.ResolveAuthorizationForPeer(
        NetGameType.Host,
        ActionSynchronizerCombatState.NotInCombat,
        unknownKind,
        GameActionType.NonCombat,
        payloadAuthorization: true);
var unknownAction = new TDBankOperationGameAction(
    actionOwner,
    unknownKind,
    executionType: GameActionType.NonCombat,
    requestId: 9103,
    hostAuthorized: true);
Expect(
    !TDBankNetOperationAction.IsSupportedOperation(unknownKind)
    && unknownAuthorization == (false, GameActionType.Any)
    && (await unknownAction.ExecuteLedgerOperationAsync()).Error
        == BankErrorCode.InvalidAccount,
    "The host did not reject an unknown network operation kind.");

Expect(
    TDBankOperationGameAction.ShouldExecuteAuthorizedOperation(
        hostAuthorized: true,
        actionLifecycleEpoch: 8,
        currentLifecycleEpoch: 8)
    && !TDBankOperationGameAction.ShouldExecuteAuthorizedOperation(
        hostAuthorized: true,
        actionLifecycleEpoch: 8,
        currentLifecycleEpoch: 9)
    && !TDBankOperationGameAction.ShouldExecuteAuthorizedOperation(
        hostAuthorized: false,
        actionLifecycleEpoch: 8,
        currentLifecycleEpoch: 8),
    "Lifecycle epoch/authorization did not guard synchronized execution.");

(bool Authorized, GameActionType ExecutionType) transitionRequest =
    TDBankNetOperationAction.ResolveAuthorizationForPeer(
        NetGameType.Host,
        ActionSynchronizerCombatState.NotInCombat,
        BankOperationKind.ETransfer,
        GameActionType.NonCombat,
        payloadAuthorization: true,
        protocolCompatible: true,
        transitionActive: true);
(bool Authorized, GameActionType ExecutionType) inactiveRunRequest =
    TDBankNetOperationAction.ResolveAuthorizationForPeer(
        NetGameType.Host,
        ActionSynchronizerCombatState.NotInCombat,
        BankOperationKind.OpenAccount,
        GameActionType.NonCombat,
        payloadAuthorization: true,
        protocolCompatible: true,
        transitionActive: false,
        runActive: false);
Expect(
    transitionRequest == (false, GameActionType.Any)
    && inactiveRunRequest == (false, GameActionType.Any),
    "The host accepted a transition-time or inactive-run bank request.");

BankNetwork.ResetRunState();
Expect(
    BankNetwork.TryAcceptHostRequest(81, 90001)
    && !BankNetwork.TryAcceptHostRequest(81, 90001)
    && BankNetwork.TryAcceptHostRequest(82, 90001)
    && !BankNetwork.TryAcceptHostRequest(81, 0),
    "Host request IDs were not deduplicated per actor and run.");
int oldLifecycleEpoch = BankNetwork.CurrentLifecycleEpoch;
int nextLifecycleEpoch = BankNetwork.AdvanceLifecycleEpoch();
Expect(
    nextLifecycleEpoch == oldLifecycleEpoch + 1
    && !TDBankOperationGameAction.ShouldExecuteAuthorizedOperation(
        hostAuthorized: true,
        actionLifecycleEpoch: oldLifecycleEpoch,
        currentLifecycleEpoch: nextLifecycleEpoch)
    && TDBankOperationGameAction.ShouldExecuteAuthorizedOperation(
        hostAuthorized: true,
        actionLifecycleEpoch: nextLifecycleEpoch,
        currentLifecycleEpoch: nextLifecycleEpoch),
    "Advancing the synchronized lifecycle did not invalidate old-floor actions.");
Expect(
    BankNetwork.DeriveLifecycleEpoch(
        totalFloor: 12,
        new[]
        {
            new AccountState
            {
                LastSavingsTurnToken = 11,
                LastDebtFloorToken = 11,
            },
            new AccountState
            {
                LastSavingsTurnToken = 10,
                LastDebtFloorToken = 9,
            },
        }) == 12
    && BankNetwork.DeriveLifecycleEpoch(
        totalFloor: 12,
        new[]
        {
            new AccountState
            {
                LastSavingsTurnToken = 12,
                LastDebtFloorToken = 12,
            },
        }) == 13,
    "Reconnect could not reconstruct the lifecycle epoch from save state.");
BankNetwork.ResetRunState();

FloorTransitionGate.Reset();
Expect(
    FloorTransitionGate.TryBegin(7301)
    && FloorTransitionGate.IsActive
    && FloorTransitionGate.TryBegin(7301)
    && !FloorTransitionGate.TryBegin(7302),
    "Floor-transition gate token/idempotency failed.");
FloorTransitionGate.HoldUntilActEntered(7301);
FloorTransitionGate.End(7301);
Expect(
    FloorTransitionGate.IsActive,
    "A cross-act transition gate ended at asynchronous room-exit timing.");
FloorTransitionGate.ReleaseAfterActEntered();
Expect(
    !FloorTransitionGate.IsActive,
    "Floor settlement transition gate did not reopen.");
Expect(
    !Enum.GetNames<BankOperationKind>().Contains(
        "SettleFloor",
        StringComparer.Ordinal)
    && typeof(BankNetwork).GetMethod(
        "EnqueueFloorSettlement",
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        is null,
    "Floor settlement must not enqueue behind map travel.");

Type[] legacyCustomMessageTypes = typeof(BankNetwork).Assembly
    .GetTypes()
    .Where(type => type.GetInterfaces().Any(contract =>
        string.Equals(
            contract.FullName,
            "BaseLib.Abstracts.ICustomMessage",
            StringComparison.Ordinal)))
    .ToArray();
Expect(
    legacyCustomMessageTypes.Length == 0,
    "TD Bank reintroduced an out-of-queue ICustomMessage channel: "
    + string.Join(", ", legacyCustomMessageTypes.Select(type => type.FullName)));



Expect(
    AbandonRunCompatibilityPatch.ShouldTreatAsCombatForDeathGuard(
        combatInProgress: false,
        isAbandoned: true)
    && AbandonRunCompatibilityPatch.ShouldTreatAsCombatForDeathGuard(
        combatInProgress: true,
        isAbandoned: false)
    && !AbandonRunCompatibilityPatch.ShouldTreatAsCombatForDeathGuard(
        combatInProgress: false,
        isAbandoned: false),
    "Abandon-run death-guard compatibility predicate regressed.");
VerifyAbandonPatchCanApply();
VerifyAllTDBankPatchesCanApply();

var nearCeilingUi = new BankUiSnapshot
{
    CreditBalance = -190,
    MaximumDebt = 200,
    DebtGraceFloorsRemaining = 0,
    DebtInterestRateBasisPoints = BankService.PoorDebtInterestBasisPoints,
};
Expect(
    nearCeilingUi.EstimatedNextDebtInterest == 10,
    "The credit page estimates uncapped interest above the 200% debt ceiling.");




string runtimeSource = File.ReadAllText(
    Path.Combine(
        "TDBankCode",
        "Integration",
        "BankRuntime.cs"));
string uninstallHandoffSource = File.ReadAllText(
    Path.Combine(
        "TDBankCode",
        "Integration",
        "UninstallSaveHandoffBridge.cs"));
Expect(
    !uninstallHandoffSource.Contains(
        "ValidateHistoryCloudCapacity",
        StringComparison.Ordinal)
    && uninstallHandoffSource.Contains(
        "ForgetFilesInDirectoryBeforeWritingIfNecessary",
        StringComparison.Ordinal)
    && uninstallHandoffSource.Contains(
        "cloud.IsFilePersisted(relative)",
        StringComparison.Ordinal)
    && uninstallHandoffSource.Contains(
        "Remote rollback persistence mismatch",
        StringComparison.Ordinal),
    "Uninstall handoff can block quota-managed local history or lose cloud persistence state.");
int nativeGainStart = runtimeSource.IndexOf(
    "internal static void RecordNativeGoldGain(",
    StringComparison.Ordinal);
int nativeLossStart = runtimeSource.IndexOf(
    "internal static void RecordNativeGoldLoss(",
    StringComparison.Ordinal);
int completedFloorStart = runtimeSource.IndexOf(
    "internal static bool OnCompletedMapFloor(",
    StringComparison.Ordinal);
string nativeGainSource = runtimeSource[nativeGainStart..nativeLossStart];
string nativeLossSource = runtimeSource[nativeLossStart..completedFloorStart];
Expect(
    !nativeGainSource.Contains(
        "\"credit_ceiling_closed_notice\"",
        StringComparison.Ordinal)
    && nativeLossSource.Contains(
        "\"credit_ceiling_closed_notice\"",
        StringComparison.Ordinal),
    "Native automatic repayment is confused with 200% ceiling closure.");

string networkSource = File.ReadAllText(
    Path.Combine(
        "TDBankCode",
        "Networking",
        "BankNetwork.cs"));
int reportResultStart = networkSource.IndexOf(
    "private void ReportResult(",
    StringComparison.Ordinal);
int reportResultEnd = networkSource.IndexOf(
    "private string SuccessMessage(",
    reportResultStart,
    StringComparison.Ordinal);
string reportResultSource =
    networkSource[reportResultStart..reportResultEnd];
Expect(
    reportResultSource.IndexOf(
        "BankUiBridge.Refresh();",
        StringComparison.Ordinal)
    < reportResultSource.IndexOf(
        "BankUiBridge.Notify(",
        StringComparison.Ordinal),
    "Network result notification is immediately erased by a later UI refresh.");




string overlaySource = File.ReadAllText(
    Path.Combine(
        "TDBankCode",
        "UI",
        "BankOverlay.cs"));
string notificationHostSource = File.ReadAllText(
    Path.Combine(
        "TDBankCode",
        "UI",
        "BankNotificationHost.cs"));
string uiBridgeSource = File.ReadAllText(
    Path.Combine(
        "TDBankCode",
        "UI",
        "BankUiBridge.cs"));
string uiAssetsSource = File.ReadAllText(
    Path.Combine(
        "TDBankCode",
        "UI",
        "BankUiAssets.cs"));
string topBarSource = File.ReadAllText(
    Path.Combine(
        "TDBankCode",
        "UI",
        "TopBarBankPatch.cs"));
int cursorRestoreStart = uiBridgeSource.IndexOf(
    "cursors!.ZIndex = originalZIndex;",
    StringComparison.Ordinal);
int cursorReparentRestore = uiBridgeSource.IndexOf(
    "cursors.Reparent(originalParent!, keepGlobalTransform: true);",
    cursorRestoreStart,
    StringComparison.Ordinal);
int cursorIndexRestore = uiBridgeSource.IndexOf(
    "originalParent.MoveChild(",
    cursorRestoreStart,
    StringComparison.Ordinal);
int cursorLeaseClearAfterRestore = uiBridgeSource.IndexOf(
    "ClearRemoteCursorLease();",
    cursorRestoreStart,
    StringComparison.Ordinal);
Expect(
    overlaySource.Contains(
        "riskPrefix.AutowrapMode = TextServer.AutowrapMode.Off;",
        StringComparison.Ordinal)
    && overlaySource.Contains(
        "riskPrefix.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;",
        StringComparison.Ordinal)
    && overlaySource.Contains(
        "SizeFlagsHorizontal = SizeFlags.ExpandFill",
        StringComparison.Ordinal),
    "The clickable risk hint can collapse into one Chinese character per line.");
Expect(
    notificationHostSource.Contains(
        "_modalRoot.AddChild(_modalBackdrop);",
        StringComparison.Ordinal)
    && notificationHostSource.Contains(
        "_modalRoot.AddChild(panel);",
        StringComparison.Ordinal)
    && !notificationHostSource.Contains(
        "_modalBackdrop.AddChild(panel);",
        StringComparison.Ordinal)
    && notificationHostSource.Contains(
        "_modalDismiss.Pressed += HideModal;",
        StringComparison.Ordinal)
    && notificationHostSource.Contains(
        "inputEvent.IsActionPressed(\"ui_accept\")",
        StringComparison.Ordinal)
    && notificationHostSource.Contains(
        "IsDismissPoint(mouse.Position)",
        StringComparison.Ordinal)
    && notificationHostSource.Contains(
        "IsDismissPoint(touch.Position)",
        StringComparison.Ordinal)
    && notificationHostSource.Contains(
        "MouseFilter = MouseFilterEnum.Stop;",
        StringComparison.Ordinal)
    && notificationHostSource.Contains(
        "MouseFilter = MouseFilterEnum.Ignore;",
        StringComparison.Ordinal),
    "The notification backdrop can intercept the modal dismiss button.");
Expect(
    notificationHostSource.Contains(
        "_modalAction.Pressed += ExecuteModalAction;",
        StringComparison.Ordinal)
    && notificationHostSource.Contains(
        "IsActionPoint(actionMouse.Position)",
        StringComparison.Ordinal)
    && notificationHostSource.Contains(
        "IsActionPoint(actionTouch.Position)",
        StringComparison.Ordinal)
    && notificationHostSource.Contains(
        "&& _modalAction.HasFocus();",
        StringComparison.Ordinal),
    "The update modal action is not reachable by mouse, touch, or keyboard.");
Expect(
    BankUiBridge.TryGetNewerVersion(
        Encoding.UTF8.GetBytes("""{"tag_name":"v0.1.4"}"""),
        out string newerVersion)
    && newerVersion == "v0.1.4"
    && !BankUiBridge.TryGetNewerVersion(
        Encoding.UTF8.GetBytes("""{"tag_name":"v0.1.3"}"""),
        out _)
    && uiBridgeSource.Contains(
        "private static bool _updateCheckStarted;",
        StringComparison.Ordinal)
    && uiBridgeSource.Contains(
        "Timeout = 5",
        StringComparison.Ordinal)
    && uiBridgeSource.Contains(
        "&& _overlay!.Visible",
        StringComparison.Ordinal)
    && !uiBridgeSource.Contains(
        "override void _Process",
        StringComparison.Ordinal)
    && !overlaySource.Contains(
        "override void _Process",
        StringComparison.Ordinal),
    "Update checking or hidden UI refresh can add continuous host-side work.");
Expect(
    uiBridgeSource.Contains(
        "cursors.Reparent(_layer!, keepGlobalTransform: true);",
        StringComparison.Ordinal)
    && uiBridgeSource.Contains(
        "cursors.ZIndex = 200;",
        StringComparison.Ordinal)
    && uiBridgeSource.Contains(
        "cursors.Reparent(originalParent!, keepGlobalTransform: true);",
        StringComparison.Ordinal)
    && uiBridgeSource.Contains(
        "originalParent.MoveChild(",
        StringComparison.Ordinal)
    && uiBridgeSource.Contains(
        "cursors.ForceUpdateAllCursors();",
        StringComparison.Ordinal)
    && overlaySource.Contains(
        "BankUiBridge.OnOverlayOpened();",
        StringComparison.Ordinal)
    && overlaySource.Contains(
        "BankUiBridge.OnOverlayClosed();",
        StringComparison.Ordinal)
    && notificationHostSource.Contains(
        "BankUiBridge.OnImportantModalOpened();",
        StringComparison.Ordinal)
    && notificationHostSource.Contains(
        "BankUiBridge.OnImportantModalClosed();",
        StringComparison.Ordinal)
    && uiBridgeSource.Contains(
        "_overlaySurfaceVisible || _importantModalVisible",
        StringComparison.Ordinal)
    && uiBridgeSource.Contains(
        "if (cursors.GetParent() != _layer)",
        StringComparison.Ordinal)
    && uiBridgeSource.Contains(
        "\"TD Bank could not re-lift multiplayer cursors:",
        StringComparison.Ordinal)
    && notificationHostSource.Contains(
        "ActiveScreenContext.Instance.Update();",
        StringComparison.Ordinal)
    && cursorRestoreStart >= 0
    && cursorReparentRestore > cursorRestoreStart
    && cursorIndexRestore > cursorReparentRestore
    && cursorLeaseClearAfterRestore > cursorIndexRestore,
    "Multiplayer hand cursors can render below TD or remain reparented after closing.");
Expect(
    uiBridgeSource.Contains(
        "if (IsNativeTargetingActive())",
        StringComparison.Ordinal)
    && uiBridgeSource.Contains(
        "CancelNativeTargeting();",
        StringComparison.Ordinal)
    && topBarSource.Contains(
        "targetManager.TargetingBegan += OnTargetingBegan;",
        StringComparison.Ordinal)
    && topBarSource.Contains(
        "targetManager.TargetingEnded += OnTargetingEnded;",
        StringComparison.Ordinal)
    && topBarSource.Contains(
        "targetManager.TargetingBegan -= OnTargetingBegan;",
        StringComparison.Ordinal)
    && BankUiText.Get("finish_targeting_first").Contains(
        "药水",
        StringComparison.Ordinal),
    "Opening TD or a late bank modal can interfere with native potion targeting.");




Type bankUiAssetsType = typeof(BankUiBridge).Assembly.GetType(
    "TDBank.TDBankCode.UI.BankUiAssets",
    throwOnError: true)!;
MethodInfo localizedCardFileName =
    bankUiAssetsType.GetMethod(
        "LocalizedCardFileName",
        BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new MissingMethodException(
        bankUiAssetsType.FullName,
        "LocalizedCardFileName");
var localizedCardFiles = new[]
{
    (BankCreditTier.Starter, "visa_broke_zh.png", "visa_broke_en.png"),
    (BankCreditTier.MiddleClass, "visa_middle_zh.png", "visa_middle_en.png"),
    (BankCreditTier.NouveauRiche, "visa_rich_zh.png", "visa_rich_en.png"),
};
Expect(
    localizedCardFiles.All(expected =>
        string.Equals(
            localizedCardFileName.Invoke(
                null,
                new object[] { expected.Item1, true }) as string,
            expected.Item2,
            StringComparison.Ordinal)
        && string.Equals(
            localizedCardFileName.Invoke(
                null,
                new object[] { expected.Item1, false }) as string,
            expected.Item3,
            StringComparison.Ordinal))
    && uiAssetsSource.Contains(
        "LoadTexture(localizedFileName)",
        StringComparison.Ordinal)
    && uiBridgeSource.Contains(
        "_overlay!.Refresh(ReadSnapshot());",
        StringComparison.Ordinal)
    && overlaySource.Contains(
        "var cardTexture = BankUiAssets.Card(spec.Tier);",
        StringComparison.Ordinal),
    "Localized credit-card artwork or live language refresh regressed.");



BankUiBridge.Language = BankUiLanguage.SimplifiedChinese;
BankUiSnapshot a0Ui =
    UiSnapshotFor(expectedAscensionBenefits[0], buttSalesCount: 3);
string chineseOpening =
    InvokeOverlayPureMethod<string>(a0Ui, "OpeningRules");
string chineseSavingsRules =
    InvokeOverlayPureMethod<string>(a0Ui, "SavingsRules");
string chineseCreditRules =
    InvokeOverlayPureMethod<string>(a0Ui, "CreditRules");
string chineseCreditExample =
    InvokeOverlayPureMethod<string>(
        a0Ui,
        "CreditExample",
        "credit_interest_example");
string chineseOpeningCreditExample =
    InvokeOverlayPureMethod<string>(
        a0Ui,
        "CreditExample",
        "opening_credit_example");
int a0RepeatButtGold =
    InvokeOverlayPureMethod<int>(a0Ui, "ButtRepeatGoldValue");
int a10RepeatButtGold =
    InvokeOverlayPureMethod<int>(a10Ui, "ButtRepeatGoldValue");
string chineseKkRules = BankUiText.Get(
    "kk_rules",
    a0Ui.AscensionLevel,
    a0Ui.KidneyHpCost,
    a0Ui.KidneyGoldValue,
    a0Ui.ButtHpCost,
    a0Ui.ButtGoldValue,
    a0RepeatButtGold);
string chineseKidneyRules = BankUiText.Get(
    "kidney_rules",
    a0Ui.KidneyHpCost,
    a0Ui.KidneyGoldValue,
    50 - a0Ui.KidneyHpCost,
    70 - a0Ui.KidneyHpCost);
string chineseButtRules = BankUiText.Get(
    "butt_rules",
    a0Ui.ButtHpCost,
    a0Ui.ButtGoldValue,
    a0Ui.ButtSalesCount + 1,
    InvokeOverlayPureMethod<int>(a0Ui, "ButtSaleHpCost"),
    a0RepeatButtGold);
string chineseCreditLocked = BankUiText.Get(
    "credit_locked_blurb",
    a0Ui.AscensionLevel,
    "200 G");
string chineseClosedCap = BankUiText.Get(
    "credit_closed_cap",
    a0Ui.AscensionLevel,
    BankUiText.Get(
        "relic_seizure_unlimited",
        a0Ui.RelicGoldPerSeizure));
Expect(
    BankUiText.Get("open_account_title").Contains(
        "开户",
        StringComparison.Ordinal)
    && BankUiText.Get("brand_tagline") ==
        "把今天的金币，变成明天的财务问题。"
    && BankUiText.Get("open_account_first_step").Contains(
        "申请开户",
        StringComparison.Ordinal)
    && BankUiText.Get("open_account_welcome").Contains(
        "本局第一次",
        StringComparison.Ordinal)
    && BankUiText.Get("open_account_checkbox").Contains(
        "我已读完并无条件同意以上霸王条款",
        StringComparison.Ordinal)
    && BankUiText.Get("open_account_checkbox_again").Contains(
        "再次确认",
        StringComparison.Ordinal)
    && BankUiText.Get("open_account_forced_agree") ==
        "被迫同意并开户"
    && chineseOpening.Contains("当前 A0", StringComparison.Ordinal)
    && chineseOpening.Contains("1.5%", StringComparison.Ordinal)
    && chineseOpening.Contains("每层最多 10G", StringComparison.Ordinal)
    && chineseOpening.Contains(
        "150 G / 600 G / 1,600 G",
        StringComparison.Ordinal)
    && chineseOpening.Contains(
        "400 G / 1,400 G / 2,400 G",
        StringComparison.Ordinal)
    && chineseOpening.Contains("免息 3 层", StringComparison.Ordinal)
    && chineseOpening.Contains(
        "游戏事件损失金币不会制造债务",
        StringComparison.Ordinal)
    && chineseOpening.Contains("21.99%", StringComparison.Ordinal)
    && chineseOpening.Contains("24.99%", StringComparison.Ordinal)
    && chineseOpening.Contains("27.99%", StringComparison.Ordinal)
    && chineseOpening.Contains("原生付费事件", StringComparison.Ordinal)
    && chineseOpening.Contains("每 100G", StringComparison.Ordinal)
    && chineseOpening.Contains("不封顶", StringComparison.Ordinal)
    && chineseOpening.Contains(
        "每份肾 -10 当前及最大生命换 200G",
        StringComparison.Ordinal)
    && chineseOpening.Contains(
        "前三次每次 -5 当前生命换 50G",
        StringComparison.Ordinal)
    && chineseOpeningCreditExample.Contains(
        "法外狂徒张三",
        StringComparison.Ordinal)
    && chineseOpeningCreditExample.Contains(
        "第 4 层",
        StringComparison.Ordinal)
    && !chineseOpening.Contains("Boss", StringComparison.OrdinalIgnoreCase)
    && !BankUiText.Get("open_account_first_step").Contains(
        "下一页会用人话讲完规则",
        StringComparison.Ordinal),
    "Chinese opening flow does not present the current rules and two confirmations.");
Expect(
    overlaySource.Contains(
        "BankUiText.Get(\"brand_tagline\")",
        StringComparison.Ordinal),
    "The account-opening page does not display the TD Bank tagline.");
Expect(
    typeof(BankOverlay).GetMethod(
        "BuildOnboarding",
        BindingFlags.Instance | BindingFlags.NonPublic) is not null
    && typeof(BankUiBridge).GetEvent(
        "OpenAccountRequested",
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        is not null,
    "The first-open application/rules/acceptance flow is not wired to the account operation.");
Expect(
    chineseSavingsRules.Contains(
        "100G",
        StringComparison.Ordinal)
    && chineseSavingsRules.Contains(
        "1G",
        StringComparison.Ordinal)
    && chineseSavingsRules.Contains(
        "每个新地图层开始时",
        StringComparison.Ordinal)
    && !chineseSavingsRules.Contains(
        "结束时",
        StringComparison.Ordinal)
    && chineseSavingsRules.Contains(
        "计入办卡累计",
        StringComparison.Ordinal)
    && BankUiText.Get("interest_turns") == "已发息层数"
    && BankUiText.Get("savings_interest_notice", 10, 0, "1.5", 10)
        == "TD储蓄利息：+10G\n当前进阶 A0：1.5%，本层最多 10G"
    && chineseCreditExample.Contains(
        "张三",
        StringComparison.Ordinal)
    && chineseCreditExample.Contains(
        "第 4 层",
        StringComparison.Ordinal)
    && chineseCreditExample.Contains(
        "本局第一次欠 100G",
        StringComparison.Ordinal)
    && chineseCreditRules.Contains(
        "还清再欠没有第二次免息",
        StringComparison.Ordinal)
    && chineseCreditRules.Contains(
        "游戏事件损失金币不会制造债务",
        StringComparison.Ordinal)
    && chineseCreditRules.Contains(
        "400 G / 1,400 G / 2,400 G",
        StringComparison.Ordinal)
    && chineseCreditRules.Contains(
        "只有开户后的原生游戏金币和储蓄利息",
        StringComparison.Ordinal)
    && chineseCreditLocked.Contains(
        "累计从开户后的 0G 开始",
        StringComparison.Ordinal)
    && chineseClosedCap.StartsWith(
        "你把信用卡刷爆了，TD 决定清算你全部财产并且抄了你的家。",
        StringComparison.Ordinal)
    && BankUiText.Get("etransfer_rules").Contains(
        "已经开户",
        StringComparison.Ordinal)
    && chineseKkRules.Contains(
        "不算办卡资格",
        StringComparison.Ordinal)
    && chineseKkRules.Contains(
        "不计入战斗受伤/失血记录",
        StringComparison.Ordinal)
    && chineseKkRules.Contains(
        "不触发原生“获得金币”效果",
        StringComparison.Ordinal)
    && chineseKidneyRules.Contains(
        "当前生命 -10",
        StringComparison.Ordinal)
    && chineseKidneyRules.Contains(
        "换 200G",
        StringComparison.Ordinal)
    && chineseKidneyRules.Contains(
        "两种生命都必须大于 0",
        StringComparison.Ordinal)
    && chineseButtRules.Contains(
        "第 4 次",
        StringComparison.Ordinal)
    && chineseButtRules.Contains(
        "前三次每次 -5 当前生命换 50G",
        StringComparison.Ordinal)
    && chineseButtRules.Contains(
        "当前第 4 次报价：-8 HP / +30G",
        StringComparison.Ordinal)
    && chineseButtRules.Contains(
        "当前生命必须大于 0",
        StringComparison.Ordinal)
    && BankUiText.Get("kidney_fatal").Contains(
        "暴毙",
        StringComparison.Ordinal)
    && BankUiText.Get("butt_risk_hint") == "卖多了可能触发"
    && BankUiText.Get("credit_ceiling_warning_title") == "TD短信"
    && BankUiText.Get("credit_ceiling_warning_message", 4)
        == "温馨提醒您下回合不还钱就要抄家了（扣除 4 件圣遗物）！"
    && BankUiText.Get("credit_liquidation_title") == "TD 抄家清算单"
    && BankUiText.Get("credit_liquidation_result", 800, 8, 5)
        .Contains("实际没收 5 件", StringComparison.Ordinal)
    && BankUiText.Get("legacy_foreclosure_repaired", 800)
        .Contains("返还 800G", StringComparison.Ordinal)
    && BankUiText.Get("butt_risk_link") == "菊部风控"
    && BankUiText.Get("butt_risk_explanation")
        == "卖多了可能触发特殊事件。\n黑市有风险，卖屁股需谨慎。"
    && !BankUiText.Get("butt_risk_explanation").Contains(
        "%",
        StringComparison.Ordinal)
    && typeof(BankUiBridge).GetMethod(
        "NotifyButtFreeloader",
        BindingFlags.Static | BindingFlags.Public) is not null
    && typeof(BankUiBridge).GetMethod(
        "NotifyButtHemorrhage",
        BindingFlags.Static | BindingFlags.Public) is not null
    && BankUiText.Get("butt_repeat_page_warning").Contains(
        "怎么又是你",
        StringComparison.Ordinal)
    && BankUiText.Get("kidney_quantity").Contains(
        "只能正整数",
        StringComparison.Ordinal)
    && BankUiText.Get("sell_kidney_button", 600) ==
        "卖肾获得 600G"
    && a0RepeatButtGold == 30
    && a10RepeatButtGold == 30
    && BankUiText.Get(
        "sell_butt_button_repeat",
        4,
        InvokeOverlayPureMethod<int>(a0Ui, "ButtSaleHpCost"),
        a0RepeatButtGold)
        == "第 4 次卖屁股：-8 HP / +30G"
    && BankUiText.Get(
        "sell_butt_button_repeat",
        4,
        InvokeOverlayPureMethod<int>(a10Ui, "ButtSaleHpCost"),
        a10RepeatButtGold)
        == "第 4 次卖屁股：-8 HP / +30G"
    && !string.Join(
        "\n",
        chineseSavingsRules,
        chineseCreditRules,
        BankUiText.Get("etransfer_rules"),
        chineseKkRules).Contains(
        "人话版",
        StringComparison.Ordinal)
    && !string.Join(
        "\n",
        chineseSavingsRules,
        BankUiText.Get("etransfer_rules")).Contains(
        "简单说：",
        StringComparison.Ordinal)
    && !chineseCreditRules.Contains(
        "继续借钱",
        StringComparison.Ordinal)
    && !chineseCreditExample.Contains(
        "昨天的利息",
        StringComparison.Ordinal)
    && !string.Join(
        "\n",
        chineseOpening,
        chineseCreditRules,
        chineseCreditLocked).Contains(
        "开户前后",
        StringComparison.Ordinal)
    && !string.Join(
        "\n",
        chineseOpening,
        chineseCreditRules,
        chineseCreditLocked).Contains(
        "本局起始金币",
        StringComparison.Ordinal)
    && !string.Join(
        "\n",
        chineseOpening,
        chineseCreditRules,
        chineseCreditLocked).Contains(
        "奖励",
        StringComparison.Ordinal)
    && !chineseKkRules.Contains(
        "而且是真的扣血",
        StringComparison.Ordinal)
    && !string.Join(
        "\n",
        chineseKkRules,
        chineseKidneyRules,
        chineseButtRules).Contains(
        "先还债",
        StringComparison.Ordinal),
    "A Chinese feature page is missing its current rule explanation, dynamic action copy, or joke.");
Expect(
    typeof(BankUiBridge).GetEvent(
        "RepaymentRequested",
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        is null
    && typeof(BankOverlay).GetMethod(
        "BuildRepayment",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        is null
    && !Enum.GetNames<BankOperationKind>().Any(name =>
        name.Contains("Repay", StringComparison.OrdinalIgnoreCase)),
    "A manual repayment UI/network path still exists.");

BankUiBridge.Language = BankUiLanguage.English;
string englishOpening =
    InvokeOverlayPureMethod<string>(a0Ui, "OpeningRules");
string englishSavings =
    InvokeOverlayPureMethod<string>(a0Ui, "SavingsRules");
string englishCredit =
    InvokeOverlayPureMethod<string>(a0Ui, "CreditRules");
string englishCreditExample =
    InvokeOverlayPureMethod<string>(
        a0Ui,
        "CreditExample",
        "credit_interest_example");
string englishKkRules = BankUiText.Get(
    "kk_rules",
    a0Ui.AscensionLevel,
    a0Ui.KidneyHpCost,
    a0Ui.KidneyGoldValue,
    a0Ui.ButtHpCost,
    a0Ui.ButtGoldValue,
    a0RepeatButtGold);
string englishKidneyRules = BankUiText.Get(
    "kidney_rules",
    a0Ui.KidneyHpCost,
    a0Ui.KidneyGoldValue,
    50 - a0Ui.KidneyHpCost,
    70 - a0Ui.KidneyHpCost);
string englishButtRules = BankUiText.Get(
    "butt_rules",
    a0Ui.ButtHpCost,
    a0Ui.ButtGoldValue,
    a0Ui.ButtSalesCount + 1,
    InvokeOverlayPureMethod<int>(a0Ui, "ButtSaleHpCost"),
    a0RepeatButtGold);
string englishCreditLocked = BankUiText.Get(
    "credit_locked_blurb",
    a0Ui.AscensionLevel,
    "200 G");
string englishClosedCap = BankUiText.Get(
    "credit_closed_cap",
    a0Ui.AscensionLevel,
    BankUiText.Get(
        "relic_seizure_unlimited",
        a0Ui.RelicGoldPerSeizure));
Expect(
    BankUiText.Get("savings") == "Savings"
    && BankUiText.Get("chequing") == "chequing"
    && BankUiText.Get("open_account_title").Contains(
        "Open",
        StringComparison.Ordinal)
    && BankUiText.Get("open_account_welcome").Contains(
        "each run",
        StringComparison.OrdinalIgnoreCase)
    && BankUiText.Get("open_account_welcome").Length < 70
    && BankUiText.Get("open_account_first_step").Length < 60
    && BankUiText.Get("open_account_apply") == "Apply"
    && BankUiText.Get("kk_tab") == "KK Compound"
    && englishOpening.Contains(
        "first three",
        StringComparison.OrdinalIgnoreCase)
    && englishOpening.Contains("21.99%", StringComparison.Ordinal)
    && englishOpening.Contains("24.99%", StringComparison.Ordinal)
    && englishOpening.Contains("27.99%", StringComparison.Ordinal)
    && englishOpening.Contains(
        "400 G / 1,400 G / 2,400 G",
        StringComparison.Ordinal)
    && englishOpening.Contains(
        "relic",
        StringComparison.OrdinalIgnoreCase)
    && englishOpening.Contains(
        "automatically",
        StringComparison.OrdinalIgnoreCase)
    && englishOpening.Contains(
        "qualification starts at 0G",
        StringComparison.OrdinalIgnoreCase)
    && englishCredit.Contains(
        "no second grace period",
        StringComparison.OrdinalIgnoreCase)
    && englishOpening.Contains(
        "native paid events",
        StringComparison.OrdinalIgnoreCase)
    && englishOpening.Contains(
        "150 G / 600 G / 1,600 G",
        StringComparison.Ordinal)
    && englishSavings.Contains(
        "compound interest",
        StringComparison.OrdinalIgnoreCase)
    && englishSavings.Contains(
        "start of each new map floor",
        StringComparison.OrdinalIgnoreCase)
    && !englishSavings.Contains(
        "map-floor end",
        StringComparison.OrdinalIgnoreCase)
    && BankUiText.Get("interest_turns") == "Floors paid"
    && BankUiText.Get("savings_interest_notice", 10, 0, "1.5", 10)
        == "TD savings interest: +10G\nAscension A0: 1.5%, up to 10G this floor"
    && englishCredit.Contains(
        "Only native-game gold and savings interest earned after opening",
        StringComparison.OrdinalIgnoreCase)
    && englishCredit.Contains(
        "Gold lost to game events cannot create debt",
        StringComparison.OrdinalIgnoreCase)
    && BankUiText.Get("etransfer_rules").Contains(
        "has opened a TD account",
        StringComparison.Ordinal)
    && englishKkRules.Contains(
        "does not count toward qualification",
        StringComparison.OrdinalIgnoreCase)
    && englishKkRules.Contains(
        "do not enter combat damage/HP-loss history",
        StringComparison.OrdinalIgnoreCase)
    && englishKkRules.Contains(
        "does not trigger native “gain Gold” effects",
        StringComparison.OrdinalIgnoreCase)
    && englishCreditExample.Contains(
        "Zhang San",
        StringComparison.Ordinal)
    && englishCreditExample.Contains(
        "first debt is 100G",
        StringComparison.OrdinalIgnoreCase)
    && englishCredit.Contains(
        "no second grace period",
        StringComparison.OrdinalIgnoreCase)
    && englishClosedCap.StartsWith(
        "You maxed out the card,",
        StringComparison.Ordinal)
    && BankUiText.Get("kidney_fatal").Contains(
        "kill",
        StringComparison.OrdinalIgnoreCase)
    && BankUiText.Get("credit_ceiling_warning_title") == "TD SMS"
    && BankUiText.Get("credit_ceiling_warning_message", 4).Contains(
        "4 relics",
        StringComparison.OrdinalIgnoreCase)
    && BankUiText.Get("credit_liquidation_title")
        == "TD Foreclosure Statement"
    && BankUiText.Get("credit_liquidation_result", 800, 8, 5)
        .Contains("actually seized 5", StringComparison.OrdinalIgnoreCase)
    && BankUiText.Get("legacy_foreclosure_repaired", 800)
        .Contains("800G was restored", StringComparison.OrdinalIgnoreCase)
    && englishKidneyRules.Contains(
        "for 200G",
        StringComparison.Ordinal)
    && englishKidneyRules.Contains(
        "both HP values must remain above 0",
        StringComparison.OrdinalIgnoreCase)
    && englishButtRules.Contains(
        "first three sales cost 5 current HP for 50G",
        StringComparison.OrdinalIgnoreCase)
    && englishButtRules.Contains(
        "Current sale #4: -8 HP / +30G",
        StringComparison.OrdinalIgnoreCase)
    && BankUiText.Get("butt_risk_explanation").Contains(
        "special events",
        StringComparison.OrdinalIgnoreCase)
    && BankUiText.Get("butt_risk_explanation").Contains(
        "Sell your butt with caution",
        StringComparison.OrdinalIgnoreCase)
    && BankUiText.Get("butt_repeat_page_warning").Contains(
        "You again",
        StringComparison.OrdinalIgnoreCase)
    && BankUiText.Get("kidney_quantity").Contains(
        "positive whole numbers only",
        StringComparison.OrdinalIgnoreCase)
    && BankUiText.Get("sell_kidney_button", 600).Contains(
        "600G",
        StringComparison.Ordinal)
    && !string.Join(
        "\n",
        englishOpening,
        englishCredit,
        englishSavings,
        BankUiText.Get("etransfer_rules"),
        englishKkRules).Contains(
        "plain language",
        StringComparison.OrdinalIgnoreCase)
    && !englishCredit.Contains(
        "Borrowing more",
        StringComparison.OrdinalIgnoreCase)
    && !englishCreditExample.Contains(
        "yesterday's interest",
        StringComparison.OrdinalIgnoreCase)
    && !string.Join(
        "\n",
        englishOpening,
        englishCredit,
        englishCreditLocked).Contains(
        "before or after opening",
        StringComparison.OrdinalIgnoreCase)
    && !string.Join(
        "\n",
        englishOpening,
        englishCredit,
        englishCreditLocked).Contains(
        "starting gold",
        StringComparison.OrdinalIgnoreCase)
    && !string.Join(
        "\n",
        englishOpening,
        englishCredit,
        englishCreditLocked).Contains(
        "reward",
        StringComparison.OrdinalIgnoreCase),
    "English opening or per-feature current rules are incomplete.");


const string pristineProgress = """
{
  "ancient_stats": [],
  "architect_damage": 0,
  "card_stats": [],
  "character_stats": [{
    "badges": [],
    "best_win_streak": 0,
    "current_streak": 0,
    "fastest_win_time": -1,
    "id": "CHARACTER.IRONCLAD",
    "max_ascension": 0,
    "playtime": 0,
    "preferred_ascension": 0,
    "total_losses": 0,
    "total_wins": 0
  }],
  "current_score": 0,
  "discovered_acts": [],
  "discovered_cards": [
    "CARD.STRIKE_IRONCLAD",
    "CARD.DEFEND_IRONCLAD",
    "CARD.BASH"
  ],
  "discovered_events": [],
  "discovered_potions": [],
  "discovered_relics": ["RELIC.BURNING_BLOOD"],
  "encounter_stats": [],
  "enemy_stats": [],
  "epochs": [],
  "floors_climbed": 0,
  "max_multiplayer_ascension": 0,
  "pending_character_unlock": "NONE.NONE",
  "preferred_multiplayer_ascension": 0,
  "schema_version": 22,
  "test_subject_kills": 0,
  "total_playtime": 0,
  "total_unlocks": 0,
  "unlocked_achievements": [],
  "wongo_points": 0
}
""";
Expect(
    MigrationProgressClassifier.Classify(pristineProgress)
        == MigrationProgressClassifier.Result.Pristine,
    "A schema-v22 starter profile should be classified as pristine.");
var latestPristine = JsonNode.Parse(pristineProgress)!.AsObject();
latestPristine["schema_version"] = 21;
latestPristine["character_stats"] = new JsonArray();
Expect(
    MigrationProgressClassifier.Classify(latestPristine.ToJsonString())
        == MigrationProgressClassifier.Result.Pristine,
    "A schema-v21 starter profile should be classified as pristine.");
Expect(
    GameApiCompatibility.IsSupportedProgressSchema(21)
    && GameApiCompatibility.IsSupportedProgressSchema(22)
    && GameApiCompatibility.IsSupportedProgressSchema(23)
    && GameApiCompatibility.IsSupportedProgressSchema(24)
    && !GameApiCompatibility.IsSupportedProgressSchema(20)
    && !GameApiCompatibility.IsSupportedProgressSchema(25),
    "The LTS progress-schema allowlist regressed.");
Expect(
    MigrationProgressClassifier.Classify(
        pristineProgress.Replace(
            "\"floors_climbed\": 0",
            "\"floors_climbed\": 1",
            StringComparison.Ordinal))
        == MigrationProgressClassifier.Result.Substantive,
    "Any climbed floor should make remote progress substantive.");
Expect(
    MigrationProgressClassifier.Classify(
        pristineProgress.Replace(
            "\"schema_version\": 22",
            "\"schema_version\": 25",
            StringComparison.Ordinal))
        == MigrationProgressClassifier.Result.Unknown,
    "An unfamiliar progress schema must fail closed.");
Expect(
    MigrationProgressClassifier.Classify("{ definitely not json")
        == MigrationProgressClassifier.Result.Unknown,
    "Malformed remote progress must fail closed.");
Expect(
    MigrationCloudFileRules.IsCloudManagedTarget(
        "modded/profile1/saves/current_run.save")
    && MigrationCloudFileRules.IsCloudManagedTarget(
        "modded/profile3/saves/history/123456.run")
    && !MigrationCloudFileRules.IsCloudManagedTarget(
        "modded/profile1/saves/progress.save.backup")
    && !MigrationCloudFileRules.IsCloudManagedTarget(
        "modded/profile1/replays/latest.mcr"),
    "Save migration cloud-target allowlist regressed.");

if (SmokeFailures.Messages.Count > 0)
{
    throw new InvalidOperationException(
        $"{SmokeFailures.Messages.Count} TD Bank smoke test(s) failed:\n- "
        + string.Join("\n- ", SmokeFailures.Messages));
}

Console.WriteLine("TD Bank logic smoke tests passed.");

internal static class SmokeFailures
{
    internal static List<string> Messages { get; } = new();
}

internal static class HealthHookProbe
{
    internal static int CallCount { get; set; }

    internal static void CountCall()
    {
        CallCount++;
    }
}

internal static class NativeGoldCommandProbe
{
    internal static int GainCallCount { get; set; }
    internal static int LossCallCount { get; set; }

    internal static bool CountGainAndSkip(ref Task __result)
    {
        GainCallCount++;
        __result = Task.CompletedTask;
        return false;
    }

    internal static bool CountLossAndSkip(ref Task __result)
    {
        LossCallCount++;
        __result = Task.CompletedTask;
        return false;
    }
}

internal static class BaseLibSavePatchProbe
{
    internal static void AdjustPropArray(
        JsonSerializerOptions options,
        ref JsonPropertyInfo[] __result)
    {
    }
}
