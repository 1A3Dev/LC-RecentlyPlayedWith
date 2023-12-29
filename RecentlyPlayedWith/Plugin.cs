using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using Steamworks;

namespace LobbyInviteOnly;

[BepInPlugin(modGUID, "RecentlyPlayedWith", modVersion)]
internal class PluginLoader : BaseUnityPlugin
{
    internal const string modGUID = "Dev1A3.RecentlyPlayedWith";
    internal const string modVersion = "1.0.0";

    private readonly Harmony harmony = new Harmony(modGUID);

    private static bool initialized;

    public static PluginLoader Instance { get; private set; }

    private void Awake()
    {
        if (initialized)
        {
            return;
        }
        initialized = true;
        Instance = this;
        Assembly patches = Assembly.GetExecutingAssembly();
        harmony.PatchAll(patches);

        PlayedWithConfig.InitConfig();
    }

    public void BindConfig<T>(ref ConfigEntry<T> config, string section, string key, T defaultValue, string description = "")
    {
        config = Config.Bind<T>(section, key, defaultValue, description);
    }
}
internal class PlayedWithConfig
{
    internal static SteamId LobbyId;

    internal static ManualLogSource logSource;

    internal static ConfigEntry<bool> IncludeOrbit;
    internal static void InitConfig()
    {
        logSource = Logger.CreateLogSource(PluginLoader.modGUID);

        PluginLoader.Instance.BindConfig(ref IncludeOrbit, "Settings", "Enable In Orbit", false, "Should players be classed as recently played with whilst you are in orbit? Disabling this will only add people whilst the ship is landed.");
    }

    internal static HashSet<ulong> PlayerList = new HashSet<ulong>();
    internal static void SetPlayedWith(ulong playerSteamId, string debugType)
    {
        if (!PlayerList.Contains(playerSteamId) && playerSteamId != 0f && playerSteamId != StartOfRound.Instance.localPlayerController.playerSteamId)
        {
            PlayerList.Add(playerSteamId);
            SteamFriends.SetPlayedWith(playerSteamId);
            logSource.LogInfo($"Set recently played with {playerSteamId} ({debugType})");
        }
    }
}

[HarmonyPatch]
internal static class SetPlayedWith_Patch
{
    [HarmonyPatch(typeof(StartOfRound), "openingDoorsSequence")]
    [HarmonyPostfix]
    private static void openingDoorsSequence(ref StartOfRound __instance)
    {
        foreach (PlayerControllerB plyCon in __instance.allPlayerScripts)
        {
            if (plyCon.isPlayerControlled)
            {
                PlayedWithConfig.SetPlayedWith(plyCon.playerSteamId, "shipLanded");
            }
        }
    }

    [HarmonyPatch(typeof(PlayerControllerB), "SendNewPlayerValuesClientRpc")]
    [HarmonyPostfix]
    private static void StartClient(ref ulong[] playerSteamIds)
    {
        if (StartOfRound.Instance != null && (StartOfRound.Instance.shipHasLanded || (StartOfRound.Instance.inShipPhase && PlayedWithConfig.IncludeOrbit.Value)))
        {
            string debugType = "otherJoined";
            if (GameNetworkManager.Instance.currentLobby.HasValue && GameNetworkManager.Instance.currentLobby.Value.Id != PlayedWithConfig.LobbyId)
            {
                PlayedWithConfig.LobbyId = GameNetworkManager.Instance.currentLobby.Value.Id;
                debugType = "selfJoined";
                if (PlayedWithConfig.PlayerList.Count > 0)
                {
                    PlayedWithConfig.PlayerList.Clear();
                    PlayedWithConfig.logSource.LogInfo($"Cleared recently played with...");
                }
            }

            foreach (ulong steamId in playerSteamIds)
            {
                PlayedWithConfig.SetPlayedWith(steamId, debugType);
            }
        }
    }
}