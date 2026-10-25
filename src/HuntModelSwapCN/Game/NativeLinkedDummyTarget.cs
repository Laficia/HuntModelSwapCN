using System.Runtime.InteropServices;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using HuntModelSwapCN.Core;

namespace HuntModelSwapCN.Game;

public sealed unsafe class NativeLinkedDummyTarget : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool HardTargetDelegate(TargetSystem* system, GameObject* target,
        [MarshalAs(UnmanagedType.I1)] bool ignoreTargetModes, [MarshalAs(UnmanagedType.I1)] bool a4, int a5);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SoftTargetDelegate(TargetSystem* system, GameObject* target);

    private readonly IGameInteropProvider interop;
    private readonly IPluginLog log;
    private readonly LinkedDummyTarget redirect;
    private readonly HardTargetDelegate hardDetour;
    private readonly SoftTargetDelegate softDetour;
    private Hook<HardTargetDelegate>? hardHook;
    private Hook<SoftTargetDelegate>? softHook;
    private bool reported;

    public NativeLinkedDummyTarget(IGameInteropProvider interop, IPluginLog log, LinkedDummyTarget redirect)
    {
        this.interop = interop; this.log = log; this.redirect = redirect;
        hardDetour = SetHardTarget; softDetour = SetSoftTarget;
    }

    // Install only on an explicit furnishing swap, before creating a clickable
    // visual. Both strong delegate roots survive until the hooks are disposed.
    public void EnsureEnabled()
    {
        if (hardHook is not null && softHook is not null) return;
        try
        {
            var hard = TargetSystem.Addresses.SetHardTarget.Value;
            var soft = TargetSystem.Addresses.SetSoftTarget.Value;
            if (hard == 0 || soft == 0) throw new InvalidOperationException("当前客户端未解析木人选中接口。");
            hardHook = interop.HookFromAddress<HardTargetDelegate>(hard, hardDetour);
            softHook = interop.HookFromAddress<SoftTargetDelegate>(soft, softDetour);
            hardHook.Enable(); softHook.Enable();
        }
        catch { Dispose(); throw; }
    }

    private GameObject* Resolve(GameObject* target)
    {
        try { return (GameObject*)redirect.Redirect((nint)target); }
        catch (Exception ex)
        {
            if (!reported) { reported = true; log.Error(ex, "木人选中映射核验失败，已放行原选中操作"); }
            return target;
        }
    }
    private bool SetHardTarget(TargetSystem* system, GameObject* target, bool ignoreTargetModes, bool a4, int a5)
        => hardHook!.Original(system, Resolve(target), ignoreTargetModes, a4, a5);
    private bool SetSoftTarget(TargetSystem* system, GameObject* target)
        => softHook!.Original(system, Resolve(target));

    public void Dispose()
    {
        try { softHook?.Dispose(); }
        finally
        {
            try { hardHook?.Dispose(); }
            finally
            {
                softHook = null; hardHook = null;
                GC.KeepAlive(softDetour); GC.KeepAlive(hardDetour);
            }
        }
    }
}
