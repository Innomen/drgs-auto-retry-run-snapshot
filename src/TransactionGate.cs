namespace DRGSAutoRetry;

internal sealed class TransactionGate
{
    private bool entered;
    internal bool TryEnter(bool retryActive)
    {
        if (retryActive || entered) return false;
        entered = true;
        return true;
    }
    internal void Exit() => entered = false;
}
