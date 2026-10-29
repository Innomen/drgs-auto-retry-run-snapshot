namespace DRGSAutoRetry;

// Stage identity owns restart vs resume, never checkpoint presence.
internal static class RetryStartup
{
    internal static bool RestartsFromBeginning(int rawStageIndex)
    {
        if (rawStageIndex < 0) throw new InvalidOperationException("Unknown lost stage.");
        return rawStageIndex == 0;
    }

    internal static void Prepare(bool restart, Action clearMidRunData, Func<bool> restoreCheckpoint)
    {
        if (restart) clearMidRunData();
        else if (!restoreCheckpoint())
            throw new InvalidOperationException("Checkpoint startup rejected.");
    }
}
