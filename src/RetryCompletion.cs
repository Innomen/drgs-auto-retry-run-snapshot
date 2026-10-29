namespace DRGSAutoRetry;

internal static class RetryCompletion
{
    // Supported-build EGameState: intro and mutator selection can wait for
    // player input indefinitely. Neither is an unfinished machine transition.
    internal static bool IsInteractiveDestination(int state) => state is 1 or 2 or 3 or 5;

    internal static bool CanComplete(bool nativeStartPrepared, bool walletPending,
        bool walletFailed, bool playableDestination) =>
        nativeStartPrepared && !walletPending && !walletFailed && playableDestination;
}
