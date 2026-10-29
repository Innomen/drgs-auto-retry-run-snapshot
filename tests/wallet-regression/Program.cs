using DRGSAutoRetry;

// Synthetic wallet only. No game process, save, Steam or GPU access.
var snapshot = new Dictionary<ECurrency, int> {
    [ECurrency.GOLD] = 37, [ECurrency.NITRA] = 21,
    [ECurrency.ARTIFACT_REROLL] = 2, [ECurrency.MUTATOR_REROLL] = 1
};
var wallet = new Wallet();
wallet.Values[ECurrency.CREDITS] = 901;
wallet.Values[ECurrency.BISMOR] = 77;
// Failed-attempt residue, including a currency absent from the snapshot.
wallet.Values[ECurrency.NITRA] = 13;
wallet.Values[ECurrency.MORKITE] = 9;
foreach (int attempt in Enumerable.Range(0, 10)) {
    RetryWallet.Reset(wallet);
    // Native supported-build resume uses Add for every Resources entry.
    foreach (var item in snapshot) wallet.Add(item.Key, item.Value);
    foreach (var currency in RetryWallet.RunCurrencies)
        Check(wallet.GetAmount(currency) == snapshot.GetValueOrDefault(currency));
    Check(wallet.GetAmount(ECurrency.CREDITS) == 901);
    Check(wallet.GetAmount(ECurrency.BISMOR) == 77);
    Check(wallet.CanAfford(ECurrency.NITRA, 21));
    Check(!wallet.CanAfford(ECurrency.NITRA, 22));
    wallet.Spend(ECurrency.NITRA, 20);
    Check(!wallet.CanAfford(ECurrency.NITRA, 2));
    wallet.Add(ECurrency.NITRA, 7); // income later discarded on next retry
    wallet.Add(ECurrency.GOLD, 11);
}
// Empty snapshot and even negative old residue must give a cold run wallet.
wallet.Values[ECurrency.NITRA] = -8;
RetryWallet.Reset(wallet);
foreach (var currency in RetryWallet.RunCurrencies) Check(wallet.GetAmount(currency) == 0);
Check(!wallet.CanAfford(ECurrency.NITRA, 1));
Check(wallet.GetAmount(ECurrency.CREDITS) == 901);
Console.WriteLine("Wallet regression passed: ten restores, purchase budgets, absent keys, negative residue and persistent balances.");
var gate = new TransactionGate();
Check(!gate.TryEnter(true)); // no purchase while restoring or failed
Check(gate.TryEnter(false));
Check(!gate.TryEnter(false)); // nested callback cannot double-spend/grant
// A rejected nested entry must not release the outer transaction.
Check(!gate.TryEnter(false));
gate.Exit();
Check(gate.TryEnter(false));
gate.Exit();
Check(!gate.TryEnter(true));
Console.WriteLine("Transaction regression passed: active/failed retry and nested purchases blocked; next transaction recovers.");
// Regression scenario: a visible SHOP before startup is still blocked. Once
// native startup and wallet restore finish, that same SHOP must accept a
// purchase, debit once, then reject an unaffordable second purchase.
var purchaseGate = new TransactionGate();
bool retryActive = true;
Check(!RetryCompletion.CanComplete(false, false, false, true));
Check(!purchaseGate.TryEnter(retryActive));
Check(!RetryCompletion.CanComplete(true, true, false, true));
Check(!RetryCompletion.CanComplete(true, false, true, true));
Check(!RetryCompletion.CanComplete(true, false, false, false));
RetryWallet.Reset(wallet);
wallet.Add(ECurrency.NITRA,21);
if (RetryCompletion.CanComplete(true,false,false,true)) retryActive=false;
Check(purchaseGate.TryEnter(retryActive));
Check(wallet.CanAfford(ECurrency.NITRA,20));
wallet.Spend(ECurrency.NITRA,20);
purchaseGate.Exit();
Check(wallet.GetAmount(ECurrency.NITRA)==1);
Check(purchaseGate.TryEnter(retryActive));
Check(!wallet.CanAfford(ECurrency.NITRA,20));
purchaseGate.Exit();
Console.WriteLine("Resumed-shop regression passed: blocked partial restore, released verified shop, exact debit and no overspend.");
// Every native interactive destination must release a verified restore before
// any amount of player deliberation can turn it into a timeout failure.
foreach (int destination in new[] { 1, 2, 3, 5 }) {
    Check(RetryCompletion.IsInteractiveDestination(destination));
    Check(RetryCompletion.CanComplete(true, false, false,
        RetryCompletion.IsInteractiveDestination(destination)));
    Check(!RetryCompletion.CanComplete(true, true, false,
        RetryCompletion.IsInteractiveDestination(destination)));
    Check(!RetryCompletion.CanComplete(true, false, true,
        RetryCompletion.IsInteractiveDestination(destination)));
}
foreach (int transition in new[] { -1, 0, 4, 6, 7 })
    Check(!RetryCompletion.IsInteractiveDestination(transition));
Console.WriteLine("Startup-selection regression passed: intro, core, mutator and shop release verified restore; transition states remain blocked.");
// First stage always clears resume state, even with a stale checkpoint. Later
// stages restore checkpoints; absence/rejection fails instead of restarting.
foreach (int stage in new[] { 0, 0, 1, 4, 0 }) {
    bool midRun = true;
    int restores = 0;
    bool restart = RetryStartup.RestartsFromBeginning(stage);
    RetryStartup.Prepare(restart, () => midRun = false, () => { restores++; return true; });
    Check(midRun == !restart);
    Check(restores == (restart ? 0 : 1));
}
bool rejected = false;
try { RetryStartup.Prepare(false, () => throw new Exception("Unexpected fresh start"), () => false); }
catch (InvalidOperationException) { rejected = true; }
Check(rejected);
rejected = false;
try { RetryStartup.RestartsFromBeginning(-1); }
catch (InvalidOperationException) { rejected = true; }
Check(rejected);
Console.WriteLine("Retry-mode regression passed: repeated first-stage deaths restart, stale snapshots ignored, later checkpoints restored and missing checkpoints rejected.");
static void Check(bool ok) { if (!ok) throw new Exception("Synthetic wallet invariant failed."); }

public enum ECurrency { GOLD, MORKITE, NITRA, ARTIFACT_REROLL, MUTATOR_REROLL, CREDITS, BISMOR }
public sealed class Wallet {
    public Dictionary<ECurrency,int> Values = new();
    public int GetAmount(ECurrency c) => Values.GetValueOrDefault(c);
    public void Clear(ECurrency c) => Values[c] = 0;
    public void Add(ECurrency c, int n) => Values[c] = GetAmount(c) + n;
    public bool CanAfford(ECurrency c,int n) => GetAmount(c) >= n;
    public void Spend(ECurrency c,int n) => Values[c] = GetAmount(c) - n;
}
