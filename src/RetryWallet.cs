namespace DRGSAutoRetry;

internal static class RetryWallet
{
    // Match the five values zeroed by the supported native fresh-run branch.
    // Resume adds Resources instead; an in-process retry must establish the
    // same empty run wallet. Never clear persistent credits/materials.
    internal static readonly ECurrency[] RunCurrencies =
    {
        ECurrency.GOLD, ECurrency.MORKITE, ECurrency.NITRA,
        ECurrency.ARTIFACT_REROLL, ECurrency.MUTATOR_REROLL
    };

    internal static void Reset(Wallet wallet)
    {
        foreach (var currency in RunCurrencies)
            wallet.Clear(currency);
    }
}
