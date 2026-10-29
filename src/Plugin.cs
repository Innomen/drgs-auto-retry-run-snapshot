using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace DRGSAutoRetry;

[BepInPlugin("local.drgs.autoretry", "Checkpoint Auto Retry", "0.1.5")]
public sealed class Plugin : BasePlugin
{
    internal static Plugin Instance = null!;
    internal static RetryDriver Driver = null!;
    public override void Load()
    {
        BuildGuard.Check();
        Instance = this;
        Driver = AddComponent<RetryDriver>();
        new Harmony("local.drgs.autoretry").PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo("Checkpoint auto retry hooks installed.");
    }
}

public sealed class RetryDriver : MonoBehaviour
{
    public RetryDriver(IntPtr pointer) : base(pointer) { }
    private GameController? controller;
    private bool restartFirstStage;
    private MidRunSaveData? checkpoint;
    private UniTask<MidRunSaveData>.Awaiter? loading;
    private int phase;
    private float deadline;
    private float nextDiagnostic;
    internal bool WalletRestorePending { get; private set; }
    private bool walletRestoreFailed;

    internal bool Begin()
    {
        if (phase != 0 && phase != 5) return true;
        WalletRestorePending = false;
        walletRestoreFailed = false;
        loading = null;
        controller = UnityEngine.Object.FindObjectOfType<GameController>();
        phase = 1;
        deadline = Time.realtimeSinceStartup + 60;
        nextDiagnostic = Time.realtimeSinceStartup + 5;
        Time.timeScale = 0;
        Plugin.Instance.Log.LogInfo("Run loss intercepted; preparing retry.");
        return true;
    }

    internal void Cancel()
    {
        phase = 0;
        WalletRestorePending = false;
        walletRestoreFailed = false;
        loading = null;
        checkpoint = null;
        restartFirstStage = false;
    }

    internal bool BlocksGameplay => phase != 0;
    internal bool Failed => phase == 5;

    internal bool PrepareNativeStart()
    {
        if (phase != 3) return !Failed;
        try
        {
            // Native retry startup has completed its fresh-run preparation.
            // Select fresh or checkpoint startup before OnCoreRunStarted subscribers
            // initialize wallet, player health, skills or the resumed shop.
            controller = UnityEngine.Object.FindObjectOfType<GameController>();
            if (controller == null)
                throw new InvalidOperationException("Retry controller unavailable.");
            RetryStartup.Prepare(restartFirstStage,
                () => controller.runSettingsManager.ClearMidRunData(),
                () => checkpoint != null && controller.runSettingsManager.LoadFromMidRunData(checkpoint));
            if (controller.runSettingsManager.IsMidRunSave == restartFirstStage)
                throw new InvalidOperationException("Retry startup mode mismatch.");
            WalletRestorePending = true;
            walletRestoreFailed = false;
            phase = 4;
            Plugin.Instance.Log.LogInfo("Retry phase: native startup prepared.");
            return true;
        }
        catch (Exception error)
        {
            StopRetry(error);
            return false;
        }
    }

    private void StopRetry(Exception error)
    {
        Plugin.Instance.Log.LogError("Checkpoint retry stopped in phase " + phase + ": " + error.GetType().Name);
        phase = 5;
        WalletRestorePending = false;
        Time.timeScale = 1;
        if (controller != null && controller.State == GameController.EGameState.CORE)
        {
            controller.PauseCore();
            controller.uiManager.TryShowCorePauseForm();
        }
    }

    internal bool PrepareWalletRestore(Wallet wallet)
    {
        try
        {
            if (wallet.runSettingsManager.IsMidRunSave == restartFirstStage)
                throw new InvalidOperationException("Retry wallet mode mismatch.");
            // Fresh native setup clears run currencies and grants normal starting
            // resources/meta bonuses. Only resume needs our additive-restore reset.
            if (!restartFirstStage)
            {
                if (checkpoint == null) throw new InvalidOperationException("Checkpoint unavailable.");
                RetryWallet.Reset(wallet);
            }
            return true;
        }
        catch (Exception)
        {
            walletRestoreFailed = true;
            return false;
        }
    }

    internal void VerifyWalletRestore(Wallet wallet)
    {
        try
        {
            // Native SetupCoreCurrencies adds the snapshot; verify replacement,
            // never print amounts or snapshot contents.
            if (wallet.runSettingsManager.IsMidRunSave == restartFirstStage)
                throw new InvalidOperationException("Retry wallet mode mismatch.");
            foreach (var currency in restartFirstStage ? Array.Empty<ECurrency>() : RetryWallet.RunCurrencies)
            {
                int expected = 0;
                checkpoint!.Resources.TryGetValue(currency, out expected);
                if (wallet.GetAmount(currency) != expected)
                    throw new InvalidOperationException("Checkpoint wallet mismatch.");
            }
            Plugin.Instance.Log.LogInfo(restartFirstStage ? "Fresh retry wallet initialized." : "Checkpoint run-currency restore verified.");
        }
        catch (Exception)
        {
            walletRestoreFailed = true;
        }
        WalletRestorePending = false;
    }

    public void Update()
    {
        if (phase == 0) return;
        if (phase == 5)
        {
            // Failure is a stopped transaction, never a playable immortal run.
            if (controller != null && (controller.State == GameController.EGameState.CORE ||
                controller.State == GameController.EGameState.SHOP)) Time.timeScale = 0;
            return;
        }
        try
        {
            if (walletRestoreFailed)
                throw new InvalidOperationException("Checkpoint wallet restoration failed.");
            if (Time.realtimeSinceStartup > deadline)
                throw new TimeoutException("Checkpoint retry did not complete within 60 seconds.");
            if (controller == null) controller = UnityEngine.Object.FindObjectOfType<GameController>();
            if (controller == null) return;
            if ((phase == 3 || phase == 4) && Time.realtimeSinceStartup >= nextDiagnostic)
            {
                nextDiagnostic = Time.realtimeSinceStartup + 5;
                Plugin.Instance.Log.LogInfo("Retry startup wait: phase=" + phase + "; state=" + controller.State +
                    "; fading=" + controller.uiManager.IsFading);
            }
            if (phase == 1)
            {
                // Decide from the lost stage, before looking at any persisted
                // checkpoint. A stale slot must never turn stage one into Resume.
                restartFirstStage = RetryStartup.RestartsFromBeginning(controller.runStateManager.GetRawStageIndex());
                checkpoint = null;
                Plugin.Instance.Log.LogInfo(restartFirstStage ? "Retry phase: restart first stage." : "Retry phase: load checkpoint.");
                if (!restartFirstStage)
                    loading = controller.midRunSaveManager.LoadCurrentSlot().GetAwaiter();
                phase = 2;
            }
            if (phase == 2 && (restartFirstStage || loading!.IsCompleted))
            {
                if (!restartFirstStage)
                {
                    checkpoint = loading!.GetResult();
                    loading = null;
                    if (checkpoint == null)
                        throw new InvalidOperationException("No checkpoint available; run kept paused.");
                }
                phase = 3;
                Time.timeScale = 1;
                // Vanilla death activates this coroutine host before its menu
                // button is reachable. We suppress vanilla death, so explicitly
                // activate the host without showing the reward/end-run workflow.
                // Native OnMenuButton calls KillAll(true), which completes active
                // tween callbacks. During a loss we can still have gameplay
                // tweens alive; cancel them without executing completion effects.
                DG.Tweening.DOTween.KillAll(false);
                var endScreen = controller.uiManager.EndScreen;
                endScreen.gameObject.SetActive(true);
                if (!endScreen.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("Reload coroutine host is inactive.");
                Plugin.Instance.Log.LogInfo("Retry phase: native retry reload; coroutine host active.");
                // Use the native startup branch, not an assumed MENU rendezvous.
                // Its normal retry preparation is replaced at the run-start event.
                Assets.Scripts.StaticVars.IsRetryRun = true;
                endScreen.OnMenuButton();
            }
            if (RetryCompletion.CanComplete(phase == 4, WalletRestorePending, walletRestoreFailed,
                RetryCompletion.IsInteractiveDestination((int)controller.State)))
            {
                phase = 0;
                checkpoint = null;
                Plugin.Instance.Log.LogInfo("Automatic retry completed.");
            }
        }
        catch (Exception error)
        {
            // Exception messages and checkpoint data stay off diagnostics.
            StopRetry(error);
        }
    }
}

internal static class BuildGuard
{
    internal static void Check()
    {
        var expected = new Dictionary<string, string>
        {
            ["GameAssembly.dll"] = "250bbb4bb0491dee3c9d7f312276d9ecfce04cd98fda14cca1ab67cb289829ba",
            ["UnityPlayer.dll"] = "37ab16d3b22cff3282dbf995b6919e591b2d16689f041b97048332e0c0c556bd",
            ["DRG Survivor_Data/il2cpp_data/Metadata/global-metadata.dat"] = "49202e38cb7e400c6895ce6188d81bbf2676af29a90e71493bd03513178a10d8"
        };
        foreach (var pair in expected)
        {
            using var stream = File.OpenRead(Path.Combine(Paths.GameRootPath, pair.Key));
            using var sha = System.Security.Cryptography.SHA256.Create();
            if (Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant() != pair.Value)
                throw new InvalidOperationException("Unsupported DRGS build; Auto Retry hooks were not installed.");
        }
    }
}

[HarmonyPatch(typeof(GameController), nameof(GameController.OnPlayerDeath))]
internal static class PlayerDeathPatch
{
    static bool Prefix() => !Plugin.Driver.Begin();
}

[HarmonyPatch(typeof(CoreGameEvents), nameof(CoreGameEvents.TriggerCoreRunOutro))]
internal static class RunLossPatch
{
    static bool Prefix(ECoreRunEndCondition condition)
    {
        if (condition is ECoreRunEndCondition.DIE or ECoreRunEndCondition.MISSED_DROPPOD)
            return !Plugin.Driver.Begin();
        Plugin.Driver.Cancel(); // Explicit abort and victory keep vanilla behavior.
        return true;
    }
}

// Scope the cold-wallet precondition to our retry, not normal shops or resumes.
[HarmonyPatch(typeof(Wallet), nameof(Wallet.SetupCoreCurrencies))]
internal static class RetryWalletPatch
{
    static bool Prefix(Wallet __instance, out bool __state)
    {
        __state = Plugin.Driver.WalletRestorePending;
        return !__state || Plugin.Driver.PrepareWalletRestore(__instance);
    }

    static void Postfix(Wallet __instance, bool __state)
    {
        if (__state) Plugin.Driver.VerifyWalletRestore(__instance);
    }
}

// Never accept shop transactions against a wallet undergoing teardown/restore.
// One shared guard also rejects nested purchase callbacks before granting items.
[HarmonyPatch]
internal static class ShopTransactionPatch
{
    private static readonly TransactionGate Gate = new();
    static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(DRS.UI.UIShopButton), "OnButtonClick");
        yield return AccessTools.Method(typeof(DRS.UI.UIShopScreen), "OnHealButton");
        yield return AccessTools.Method(typeof(DRS.UI.UIShopScreen), "OnRerollButton");
    }
    static bool Prefix(out bool __state)
    {
        __state = Gate.TryEnter(Plugin.Driver.BlocksGameplay);
        return __state;
    }
    static Exception? Finalizer(Exception? __exception, bool __state)
    {
        if (__state) Gate.Exit();
        return __exception;
    }
}

[HarmonyPatch(typeof(GameController), nameof(GameController.UnpauseCore))]
internal static class RetryUnpausePatch
{
    static bool Prefix() => !Plugin.Driver.Failed;
}

// This boundary precedes all native wallet/player/skill initialization.
[HarmonyPatch(typeof(CoreGameEvents), nameof(CoreGameEvents.TriggerCoreRunStarted))]
internal static class NativeRetryStartPatch
{
    static bool Prefix() => Plugin.Driver.PrepareNativeStart();
}
