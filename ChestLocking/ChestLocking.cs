using UnityEngine;
using HarmonyLib;
using System.Collections.Generic;
using Steamworks;
using UnityEngine.UI;
using HMLLibrary;
using RaftModLoader;
using System;
using static UnityEngine.InputSystem.InputRemoting;
using System.Text;
using System.Linq;
using System.Runtime.CompilerServices;

public class ChestLocking : Mod
{
    Harmony harmony;
    public static ChestLocking instance;
    public static Dictionary<uint, ulong> ChestLocks;
    static string lockKey;
    static string publicKey;
    static Keybind lockKeyBind;
    static Keybind publicKeyBind;
    public static bool LockKey => ExtraSettingsAPI_Loaded ? MyInput.GetButtonDown(lockKey) : Input.GetKeyDown(KeyCode.L);
    public static bool PublicKey => ExtraSettingsAPI_Loaded ? MyInput.GetButtonDown(publicKey) : Input.GetKeyDown(KeyCode.K);
    public static KeyCode LockMainKey => ExtraSettingsAPI_Loaded ? lockKeyBind.MainKey : KeyCode.L;
    public static KeyCode PublicMainKey => ExtraSettingsAPI_Loaded ? publicKeyBind.MainKey : KeyCode.K;
    public override bool CanUnload(ref string message)
    {
        if (!Raft_Network.InMenuScene)
        {
            message = "Mod must be unloaded on the main menu";
            return false;
        }
        return base.CanUnload(ref message);
    }
    public void Start()
    {
	    instance = this;
        ChestLocks = new Dictionary<uint, ulong>();
        harmony = new Harmony("com.aidanamite.ChestLocking");
        harmony.PatchAll();
        Log("Mod has been loaded!");
    }

    public void OnModUnload()
    {
        harmony.UnpatchAll(harmony.Id);
        Log("Mod has been unloaded!");
    }

    public override bool OnNetworkMessage(object message, Network_UserId from, string modslug)
    {
        if (message is CustomMessage msg)
        {
            msg.Execute(from);
            return true;
        }
        return false;
    }

    public static bool CanChangeLock(uint objectIndex, ulong playerId)
    {
        if (!ChestLocks.ContainsKey(objectIndex))
            return false;
        var l = ChestLocks[objectIndex];
        return l == 0 || l == playerId;
    }

    public override void Event_ReturnToMainMenu()
    {
        ChestLocks.Clear();
    }

    public override void WorldEvent_WorldLoaded()
    {
        if (!Raft_Network.IsHost)
            new Message_Storage_RequestLocks().Send(ComponentManager<Raft_Network>.Value.HostID);
    }

    public void ExtraSettingsAPI_WorldLoad()
    {
        if (!Raft_Network.IsHost)
            return;
        SetLockFromData(ExtraSettingsAPI_GetDataValue("locks", "data"));
        string str = "World loaded with locks:";
        foreach (var lockData in ChestLocks)
            str += "\n - " + lockData.Key + " locked by " + lockData.Value;
        Debug.Log(str);
    }

    public void ExtraSettingsAPI_Load()
    {
        lockKey = ExtraSettingsAPI_GetKeybindName("lock");
        publicKey = ExtraSettingsAPI_GetKeybindName("public");
        lockKeyBind = ExtraSettingsAPI_GetKeybind("lock");
        publicKeyBind = ExtraSettingsAPI_GetKeybind("public");
    }
    

    public static bool ExtraSettingsAPI_Loaded = false;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public string ExtraSettingsAPI_GetKeybindName(string SettingName) => "";
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Keybind ExtraSettingsAPI_GetKeybind(string SettingName) => null;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public string ExtraSettingsAPI_GetDataValue(string SettingName, string subname) => "";
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void ExtraSettingsAPI_SetDataValue(string SettingName, string subname, string value) { }

    public static string GetLockData() => ChestLocks.Join(x => x.Key + "," + x.Value, "|");
    public static void SetLockFromData(string data) => ChestLocks = new Dictionary<uint, ulong>(data.Split('|').Select(x =>
    {
        var ind = x.IndexOf(',');
        if (ind != -1 && uint.TryParse(x.Remove(ind), out var ui) && ulong.TryParse(x.Substring(ind + 1), out var ul))
            return new KeyValuePair<uint, ulong>(ui, ul);
        return default;
    }).Where(x => x.Key != 0));
}

static class ExtentionMethods
{
    public static bool IsLocked(this Storage_Small storage)
    {
        var lockOwner = ChestLocking.ChestLocks[storage.ObjectIndex];
        return lockOwner != 0 && lockOwner != ulong.MaxValue && lockOwner != ComponentManager<Raft_Network>.Value.localSteamID;
    }
    public static void SetLock(this Storage_Small storage, Network_UserId userID)
    {
        var msg = new Message_Storage_SetLock(storage, userID);
        if (Raft_Network.IsHost)
        {
            msg.Execute(userID);
            msg.Broadcast();
        } else
            msg.Send(ComponentManager<Raft_Network>.Value.HostID);
    }

    public static void Broadcast(this Message message, NetworkChannel channel = NetworkChannel.Channel_Game) => ComponentManager<Raft_Network>.Value.RPC(message, Target.Other, EP2PSend.k_EP2PSendReliable, channel);
    public static void Send(this Message message, Network_UserId userId, NetworkChannel channel = NetworkChannel.Channel_Game) => ComponentManager<Raft_Network>.Value.SendP2P(userId, message, EP2PSend.k_EP2PSendReliable, channel);
    
    public static string Join<T>(this IEnumerable<T> values, Func<T, string> converter = null, string delimeter = ", ")
    {
        var str = new StringBuilder();
        bool first = false;
        foreach (var v in values)
        {
            if (first)
                first = false;
            else
                str.Append(delimeter);
            if (converter == null)
                str.Append(v);
            else
                str.Append(converter(v));
        }
        return str.ToString();
    }
}

[HarmonyPatch(typeof(Storage_Small))]
public class Patch_Storage_Small
{
    [HarmonyPatch("IsOpen", MethodType.Getter)]
    [HarmonyPostfix]
    static void IsOpen(Storage_Small __instance, ref bool __result)
    {
        if (!__result && ChestLocking.ChestLocks.ContainsKey(__instance.ObjectIndex))
            __result = __instance.IsLocked();
    }


    [HarmonyPatch("OnFinishedPlacement")]
    [HarmonyPostfix]
    static void OnFinishedPlacement(Storage_Small __instance) => ChestLocking.ChestLocks.Add(__instance.ObjectIndex, 0);


    [HarmonyPatch("OnDestroy")]
    [HarmonyPostfix]
    static void OnDestroy(Storage_Small __instance) => ChestLocking.ChestLocks.Remove(__instance.ObjectIndex);


    [HarmonyPatch("OnIsRayed")]
    [HarmonyPostfix]
    static void OnIsRayed(Storage_Small __instance, CanvasHelper ___canvas)
    {
        if (CanvasHelper.ActiveMenu == MenuType.None && !PlayerItemManager.IsBusy && ___canvas.CanOpenMenu && Helper.LocalPlayerIsWithinDistance(__instance.transform.position, Player.UseDistance + 0.5f))
        {
            var c = ChestLocking.CanChangeLock(__instance.ObjectIndex, ComponentManager<Raft_Network>.Value.localSteamID);
            if (c)
                ___canvas.displayTextManager.ShowText(ChestLocking.ChestLocks[__instance.ObjectIndex] == 0 ? "Lock Chest" : "Unlock Chest", ChestLocking.LockMainKey, 1, 0, false);
            else if (Raft_Network.IsHost)
                ___canvas.displayTextManager.ShowText("Force Unlock Chest", ChestLocking.LockMainKey, 1, 0, false);
            if (ChestLocking.ChestLocks[__instance.ObjectIndex] == 0 && c)
                ___canvas.displayTextManager.ShowText("Set as Public Chest", ChestLocking.PublicMainKey, 2, 0, false);
            if ((c || Raft_Network.IsHost) && ChestLocking.LockKey)
            {
                ___canvas.displayTextManager.HideDisplayTexts();
                __instance.SetLock((c && ChestLocking.ChestLocks[__instance.ObjectIndex] == 0) ? ComponentManager<Raft_Network>.Value.localSteamID : 0);
            }
            else if (c && ChestLocking.ChestLocks[__instance.ObjectIndex] == 0 && ChestLocking.PublicKey)
            {
                ___canvas.displayTextManager.HideDisplayTexts();
                __instance.SetLock(ulong.MaxValue);
            }
        }
    }
}

[Serializable]
public abstract class CustomMessage
{
    public abstract void Execute(Network_UserId from);

    public void Broadcast() => ChestLocking.instance.SendNetworkMessage(this);
    public void Send(Network_UserId userId) => ChestLocking.instance.SendNetworkMessageToPlayer(this, userId);
}

public class Message_Storage_SetLock : CustomMessage
{
    uint objectIndex;
    ulong playerId;
    public Message_Storage_SetLock(Storage_Small box, ulong playerID)
    {
        objectIndex = box.ObjectIndex;
        playerId = playerID;
    }

    public override void Execute(Network_UserId from)
    {
        if (Raft_Network.IsHost && !ChestLocking.CanChangeLock(objectIndex, playerId))
            return;
        ChestLocking.ChestLocks[objectIndex] = playerId;
        Debug.Log("Set object " + objectIndex + " with lock " + playerId);
        if (Raft_Network.IsHost && ChestLocking.ExtraSettingsAPI_Loaded)
            ChestLocking.instance.ExtraSettingsAPI_SetDataValue("locks", "data", ChestLocking.GetLockData());
        var sM = RAPI.GetLocalPlayer().StorageManager;
        if (sM.currentStorage != null && sM.currentStorage.ObjectIndex == objectIndex)
            sM.CloseStorage(sM.currentStorage);
        if (Raft_Network.IsHost)
            Broadcast();
    }
}

public class Message_Storage_RequestLocks : CustomMessage
{
    public override void Execute(Network_UserId from)
    {
        if (Raft_Network.IsHost)
            new Message_Storage_AllLocks().Send(from);
    }
}

public class Message_Storage_AllLocks : CustomMessage
{
    Dictionary<uint,ulong> data;
    public Message_Storage_AllLocks() => data = new Dictionary<uint, ulong>(ChestLocking.ChestLocks);

    public override void Execute(Network_UserId from)
    {
        if (!Raft_Network.IsHost)
            ChestLocking.ChestLocks = new Dictionary<uint, ulong>(data);
    }
}