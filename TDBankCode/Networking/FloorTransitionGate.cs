namespace TDBank.TDBankCode.Networking;

internal static class FloorTransitionGate
{
    private static int _activeTransitionToken = -1;
    private static int _holdUntilActEntered;

    internal static bool IsActive =>
        Volatile.Read(ref _activeTransitionToken) > 0;

    internal static bool TryBegin(int floorToken)
    {
        if (floorToken <= 0)
        {
            return false;
        }

        int previous = Interlocked.CompareExchange(
            ref _activeTransitionToken,
            floorToken,
            -1);
        return previous == -1 || previous == floorToken;
    }

    internal static void End(int floorToken)
    {
        if (Volatile.Read(ref _holdUntilActEntered) != 0)
        {
            return;
        }

        Interlocked.CompareExchange(
            ref _activeTransitionToken,
            -1,
            floorToken);
    }

    internal static void HoldUntilActEntered(int floorToken)
    {
        if (Volatile.Read(ref _activeTransitionToken) == floorToken)
        {
            Volatile.Write(ref _holdUntilActEntered, 1);
        }
    }

    internal static void ReleaseAfterActEntered()
    {
        Volatile.Write(ref _holdUntilActEntered, 0);
        Interlocked.Exchange(ref _activeTransitionToken, -1);
    }

    internal static void Reset()
    {
        Volatile.Write(ref _holdUntilActEntered, 0);
        Interlocked.Exchange(ref _activeTransitionToken, -1);
    }
}
