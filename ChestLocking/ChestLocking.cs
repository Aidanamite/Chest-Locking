using UnityEngine;
using HarmonyLib;
using System.Collections.Generic;
using Steamworks;
using UnityEngine.UI;
using HMLLibrary;
using RaftModLoader;

public class ChestLocking : Mod
{
    Harmony harmony;
    static ModData entry;
    public static ChestLocking instance;
    public static Dictionary<uint, ulong> ChestLocks;
    public static ulong localPlayerID = SteamUser.GetSteamID().m_SteamID;
    static string lockKey;
    static string publicKey;
    static Keybind lockKeyBind;
    static Keybind publicKeyBind;
    public static bool LockKey => ExtraSettingsAPI_Loaded ? MyInput.GetButtonDown(lockKey) : Input.GetKeyDown(KeyCode.L);
    public static bool PublicKey => ExtraSettingsAPI_Loaded ? MyInput.GetButtonDown(publicKey) : Input.GetKeyDown(KeyCode.K);
    public static KeyCode LockMainKey => ExtraSettingsAPI_Loaded ? lockKeyBind.MainKey : KeyCode.L;
    public static KeyCode PublicMainKey => ExtraSettingsAPI_Loaded ? publicKeyBind.MainKey : KeyCode.K;
    static Button unloadButton;
    static Button.ButtonClickedEvent eventStore = null;
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
        entry = modlistEntry;
        unloadButton = entry.modinfo.unloadBtn.GetComponent<Button>();
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

    public void Update()
    {
        var message = RAPI.ListenForNetworkMessagesOnChannel(MessageType.ChannelID);
        while (message != null && message.message != null && message.message is Message_InitiateConnection && message.message.Type == MessageType.MessageID)
        {
            var m = (Message_InitiateConnection)message.message;
            if (m.appBuildID == MessageType.SetLock)
            {
                var msg = new Message_Storage_SetLock(m);
                if (Raft_Network.IsHost && !CanChangeLock(msg.ObjectIndex, message.steamid.m_SteamID))
                    return;
                msg.Use();
                if (Raft_Network.IsHost)
                    m.Broadcast();
            }
            else if(m.appBuildID == MessageType.RequstLocks)
            {
                if (Raft_Network.IsHost)
                    Message_Storage_RequestLocks.Use(message.steamid);
            }
            else if (m.appBuildID == MessageType.AllLocks)
            {
                var msg = new Message_Storage_AllLocks(m);
                msg.Use();
            }
            message = RAPI.ListenForNetworkMessagesOnChannel(MessageType.ChannelID);

        }
    }

    public static bool CanChangeLock(uint objectIndex, ulong playerId)
    {
        if (!ChestLocks.ContainsKey(objectIndex))
            return false;
        var l = ChestLocks[objectIndex];
        return l == 0 || l == playerId;
    }

    public override void WorldEvent_WorldUnloaded()
    {
        ChestLocks.Clear();
    }

    public override void WorldEvent_WorldLoaded()
    {
        if (!Raft_Network.IsHost)
            Message_Storage_RequestLocks.Message.Send(ComponentManager<Raft_Network>.Value.HostID);
        else if (ExtraSettingsAPI_Loaded)
            ChestLocks.CopyFrom(ExtraSettingsAPI_GetDataValue("locks", "data").Bytes());
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
    static Traverse ExtraSettingsAPI_Traverse;
    public static bool ExtraSettingsAPI_Loaded = false;
    public string ExtraSettingsAPI_GetKeybindName(string SettingName)
    {
        if (ExtraSettingsAPI_Loaded)
            return ExtraSettingsAPI_Traverse.Method("getKeybindName", new object[] { this, SettingName }).GetValue<string>();
        return "";
    }
    public Keybind ExtraSettingsAPI_GetKeybind(string SettingName)
    {
        if (ExtraSettingsAPI_Loaded)
            return ExtraSettingsAPI_Traverse.Method("getKeybind", new object[] { this, SettingName }).GetValue<Keybind>();
        return null;
    }
    public string ExtraSettingsAPI_GetDataValue(string SettingName, string subname)
    {
        if (ExtraSettingsAPI_Loaded)
            return ExtraSettingsAPI_Traverse.Method("getDataValue", new object[] { this, SettingName, subname }).GetValue<string>();
        return "";
    }
    public void ExtraSettingsAPI_SetDataValue(string SettingName, string subname, string value)
    {
        if (ExtraSettingsAPI_Loaded)
            ExtraSettingsAPI_Traverse.Method("setDataValue", new object[] { this, SettingName, subname, value }).GetValue();
    }
}

static class ExtentionMethods
{
    public static bool IsLocked(this Storage_Small storage)
    {
        var lockOwner = ChestLocking.ChestLocks[storage.ObjectIndex];
        return lockOwner != 0 && lockOwner != ulong.MaxValue && lockOwner != ChestLocking.localPlayerID;
    }
    public static void SetLock(this Storage_Small storage, ulong steamID)
    {
        var msg = new Message_Storage_SetLock(storage, steamID);
        if (Raft_Network.IsHost)
        {
            msg.Use();
            msg.Message.Broadcast();
        } else
            msg.Message.Send(ComponentManager<Raft_Network>.Value.HostID);
    }
    public static string String(this byte[] bytes, int length = -1, int offset = 0)
    {
        if (bytes.Length % 2 == 1)
        {
            var n = new byte[bytes.Length + 1];
            bytes.CopyTo(n, 0);
            bytes = n;
        }
        string str = "";
        if (length == -1)
            length = (bytes.Length - offset) / 2;
        while (str.Length < length)
        {
            str += System.BitConverter.ToChar(bytes, offset + str.Length * 2);
        }
        return str;

    }
    public static string String(this List<byte> bytes) => bytes.ToArray().String();
    public static byte[] Bytes(this string str)
    {
        var data = new List<byte>();
        foreach (char chr in str)
            data.AddRange(System.BitConverter.GetBytes(chr));
        return data.ToArray();
    }
    public static uint UInteger(this byte[] bytes, int offset = 0) => System.BitConverter.ToUInt32(bytes, offset);
    public static ulong ULong(this byte[] bytes, int offset = 0) => System.BitConverter.ToUInt64(bytes, offset);
    public static void CopyFrom(this Dictionary<uint, ulong> store, byte[] value)
    {
        for (var i = 0; i < value.Length; i += 12)
        {
            var key = value.UInteger(i);
            if (store.ContainsKey(key))
                store[key] = value.ULong(i + 4);
        }
    }
    public static byte[] Bytes(this uint value) => System.BitConverter.GetBytes(value);
    public static byte[] Bytes(this ulong value) => System.BitConverter.GetBytes(value);
    public static byte[] Bytes(this Dictionary<uint, ulong> value)
    {
        var temp = new List<byte>();
        foreach (var v in value)
        {
            temp.AddRange(v.Key.Bytes());
            temp.AddRange(v.Value.Bytes());
        }
        return temp.ToArray();
    }

    public static void Broadcast(this Message message, NetworkChannel channel = (NetworkChannel)MessageType.ChannelID) => ComponentManager<Raft_Network>.Value.RPC(message, Target.Other, EP2PSend.k_EP2PSendReliable, channel);
    public static void Send(this Message message, CSteamID steamID) => ComponentManager<Raft_Network>.Value.SendP2P(steamID, message, EP2PSend.k_EP2PSendReliable, (NetworkChannel)MessageType.ChannelID);
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
    static void OnDestroy(Storage_Small __instance)
    {
        if (ChestLocking.ChestLocks.ContainsKey(__instance.ObjectIndex))
            ChestLocking.ChestLocks.Remove(__instance.ObjectIndex);
    }


    [HarmonyPatch("OnIsRayed")]
    [HarmonyPostfix]
    static void OnIsRayed(Storage_Small __instance, CanvasHelper ___canvas)
    {
        if (CanvasHelper.ActiveMenu == MenuType.None && !PlayerItemManager.IsBusy && ___canvas.CanOpenMenu && Helper.LocalPlayerIsWithinDistance(__instance.transform.position, Player.UseDistance + 0.5f))
        {
            var c = ChestLocking.CanChangeLock(__instance.ObjectIndex, ChestLocking.localPlayerID);
            if (c)
                ___canvas.displayTextManager.ShowText(ChestLocking.ChestLocks[__instance.ObjectIndex] == 0 ? "Lock Chest" : "Unlock Chest", ChestLocking.LockMainKey, 1, 0, false);
            else if (Raft_Network.IsHost)
                ___canvas.displayTextManager.ShowText("Force Unlock Chest", ChestLocking.LockMainKey, 1, 0, false);
            if (ChestLocking.ChestLocks[__instance.ObjectIndex] == 0 && c)
                ___canvas.displayTextManager.ShowText("Set as Public Chest", ChestLocking.PublicMainKey, 2, 0, false);
            if ((c || Raft_Network.IsHost) && ChestLocking.LockKey)
            {
                ___canvas.displayTextManager.HideDisplayTexts();
                __instance.SetLock((c && ChestLocking.ChestLocks[__instance.ObjectIndex] == 0) ? ChestLocking.localPlayerID : 0);
            }
            else if (c && ChestLocking.ChestLocks[__instance.ObjectIndex] == 0 && ChestLocking.PublicKey)
            {
                ___canvas.displayTextManager.HideDisplayTexts();
                __instance.SetLock(ulong.MaxValue);
            }
        }
    }
}

public struct MessageType
{
    public const int ChannelID = 123;
    public const Messages MessageID = (Messages)321;
    public const int SetLock = 0;
    public const int RequstLocks = 1;
    public const int AllLocks = 2;
}

class Message_Storage_SetLock
{
    public Message_InitiateConnection Message
    {
        get
        {
            var data = new List<byte>();
            data.AddRange(objectIndex.Bytes());
            data.AddRange(playerId.Bytes());
            return new Message_InitiateConnection(MessageType.MessageID, MessageType.SetLock, data.String());
        }
    }
    public uint ObjectIndex => objectIndex;
    public ulong PlayerId => playerId;
    uint objectIndex;
    ulong playerId;
    public Message_Storage_SetLock(Storage_Small box, ulong playerID)
    {
        objectIndex = box.ObjectIndex;
        playerId = playerID;
    }
    public Message_Storage_SetLock(Message_InitiateConnection message)
    {
        var data = message.password.Bytes();
        objectIndex = data.UInteger(0);
        playerId = data.ULong(4);
    }

    public void Use()
    {
        ChestLocking.ChestLocks[objectIndex] = playerId;
        Debug.Log("Set object " + objectIndex + " with lock " + playerId);
        if (Raft_Network.IsHost && ChestLocking.ExtraSettingsAPI_Loaded)
            ChestLocking.instance.ExtraSettingsAPI_SetDataValue("locks", "data", ChestLocking.ChestLocks.Bytes().String());
        var sM = RAPI.GetLocalPlayer().StorageManager;
        if (sM.currentStorage != null && sM.currentStorage.ObjectIndex == objectIndex)
            sM.CloseStorage(sM.currentStorage);
    }
}

class Message_Storage_RequestLocks
{
    public static Message_InitiateConnection Message => new Message_InitiateConnection(MessageType.MessageID, MessageType.RequstLocks, "");

    public static void Use(CSteamID steamID) => new Message_Storage_AllLocks().Message.Send(steamID);
}

class Message_Storage_AllLocks
{
    public Message_InitiateConnection Message => new Message_InitiateConnection(MessageType.MessageID, MessageType.AllLocks, data.String());
    byte[] data;
    public Message_Storage_AllLocks() => data = ChestLocking.ChestLocks.Bytes();
    public Message_Storage_AllLocks(Message_InitiateConnection message) => data = message.password.Bytes();

    public void Use() => ChestLocking.ChestLocks.CopyFrom(data);
}