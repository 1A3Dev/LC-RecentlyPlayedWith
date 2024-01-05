using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using Steamworks;

namespace LobbyInviteOnly
{
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
        internal static bool CanSetPlayedWith(ulong playerSteamId)
        {
            return !PlayerList.Contains(playerSteamId) && playerSteamId != 0f && playerSteamId != StartOfRound.Instance.localPlayerController.playerSteamId;
        }
        internal static void SetPlayedWith(ulong[] playerSteamIds, string debugType)
        {
            if (playerSteamIds.Length > 0) {
                foreach (ulong playerSteamId in playerSteamIds)
                {
                    if (CanSetPlayedWith(playerSteamId))
                    {
                        PlayerList.Add(playerSteamId);
                        SteamFriends.SetPlayedWith(playerSteamId);
                    }
                }
                logSource.LogInfo($"Set recently played with ({debugType}) for {playerSteamIds.Length} players.");
                logSource.LogDebug($"Set recently played with ({debugType}): {string.Join(", ", playerSteamIds)}");
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
            PlayedWithConfig.SetPlayedWith(__instance.allPlayerScripts.Where(x => x.isPlayerControlled && PlayedWithConfig.CanSetPlayedWith(x.playerSteamId)).Select(x => x.playerSteamId).ToArray(), "shipLanded");
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

                PlayedWithConfig.SetPlayedWith(playerSteamIds.Where(x => PlayedWithConfig.CanSetPlayedWith(x)).ToArray(), debugType);
            }
        }
    }
}