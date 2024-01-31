using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using Steamworks;

namespace RecentlyPlayedWith
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
        internal static ManualLogSource logSource;

        internal static ConfigEntry<bool> IncludeOrbit;
        internal static void InitConfig()
        {
            logSource = Logger.CreateLogSource(PluginLoader.modGUID);

            PluginLoader.Instance.BindConfig(ref IncludeOrbit, "Settings", "Enable In Orbit", true, "Should players be classed as recently played with whilst you are in orbit? Disabling this will only add people whilst the ship is landed.");
        }

        internal static HashSet<ulong> PlayerList = new HashSet<ulong>();
        internal static void SetPlayedWith(ulong[] playerSteamIds, string debugType)
        {
            playerSteamIds = playerSteamIds.Where(x => x != 0f && x != SteamClient.SteamId && (debugType == "generateLevel" || !PlayerList.Contains(x))).ToArray();
            if (playerSteamIds.Length > 0) {
                foreach (ulong playerSteamId in playerSteamIds)
                {
                    if (!PlayerList.Contains(playerSteamId))
                        PlayerList.Add(playerSteamId);
                    SteamFriends.SetPlayedWith(playerSteamId);
                }
                logSource.LogInfo($"Set recently played with ({debugType}) for {playerSteamIds.Length} players.");
                logSource.LogDebug($"Set recently played with ({debugType}): {string.Join(", ", playerSteamIds)}");
            }
        }
    }

    [HarmonyPatch]
    internal static class SetPlayedWith_Patch
    {
        internal static bool initialJoin = true;

        [HarmonyPatch(typeof(RoundManager), "GenerateNewLevelClientRpc")]
        [HarmonyPostfix]
        private static void GenerateNewLevelClientRpc(ref RoundManager __instance)
        {
            PlayedWithConfig.SetPlayedWith(__instance.playersManager.allPlayerScripts.Where(x => x.isPlayerControlled || x.isPlayerDead).Select(x => x.playerSteamId).ToArray(), "generateLevel");
        }

        [HarmonyPatch(typeof(PlayerControllerB), "SendNewPlayerValuesClientRpc")]
        [HarmonyPostfix]
        private static void StartClient(ref ulong[] playerSteamIds)
        {
            if (StartOfRound.Instance != null && (!StartOfRound.Instance.inShipPhase || PlayedWithConfig.IncludeOrbit.Value))
            {
                string debugType = "otherJoined";
                if (initialJoin)
                {
                    initialJoin = false;
                    debugType = "selfJoined";
                }

                PlayedWithConfig.SetPlayedWith(playerSteamIds.ToArray(), debugType);
            }
        }

        [HarmonyPatch(typeof(StartOfRound), "OnPlayerDC")]
        [HarmonyPostfix]
        private static void OnPlayerDC(ref StartOfRound __instance, ref int playerObjectNumber, ulong clientId)
        {
            ulong steamId = __instance.allPlayerScripts[playerObjectNumber].playerSteamId;
            PlayedWithConfig.PlayerList.Remove(steamId);
            PlayedWithConfig.logSource.LogInfo($"Removing {steamId} from recently played with.");
        }

        [HarmonyPatch(typeof(StartOfRound), "OnDestroy")]
        [HarmonyPostfix]
        private static void SORDestroy(ref StartOfRound __instance)
        {
            initialJoin = true;
            if (PlayedWithConfig.PlayerList.Count > 0)
            {
                PlayedWithConfig.PlayerList.Clear();
                PlayedWithConfig.logSource.LogInfo($"Cleared recently played with (OnDestroy)");
            }
        }
    }
}