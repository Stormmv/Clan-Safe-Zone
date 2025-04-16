using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("ClanSafeZone", "Stormmv", "1.0.2")]
    [Description("Clans can create a safe zone using a UI button in the Tool Cupboard during the first hour after wipe.")]
    public class ClanSafeZone : RustPlugin
    {
        [PluginReference] Plugin Clans, ZoneManager;

        private Dictionary<string, bool> clanUsedProtection = new();
        private HashSet<ulong> interactingPlayers = new();
        private double wipeTime;

        private ConfigData config;

        private class ConfigData
        {
            public float ActivationWindow { get; set; } = 3600f;
            public float ZoneRadius { get; set; } = 50f;
        }

        protected override void LoadDefaultConfig() => config = new ConfigData();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            config = Config.ReadObject<ConfigData>();
        }

        protected override void SaveConfig() => Config.WriteObject(config);

        #region Hooks

        void OnServerInitialized()
        {
            if (Clans == null)
            {
                PrintError("Clans Reborn plugin not found! Please make sure it's installed and loaded.");
            }

            wipeTime = Time.realtimeSinceStartup;
        }

        void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (entity == null || player == null) return;
            if (entity.ShortPrefabName != "cupboard.tool.deployed") return;

            if (Time.realtimeSinceStartup - wipeTime > config.ActivationWindow) return;

            if (!interactingPlayers.Contains(player.userID))
            {
                interactingPlayers.Add(player.userID);
                timer.Once(0.2f, () => ShowUI(player)); // Show the UI when TC is opened
            }
        }

        void OnLootEntityEnd(BasePlayer player, BaseEntity entity)
        {
            if (entity == null || player == null) return;
            if (entity.ShortPrefabName != "cupboard.tool.deployed") return;

            if (interactingPlayers.Contains(player.userID))
            {
                interactingPlayers.Remove(player.userID);
                DestroyUI(player); // Destroy UI when TC is closed
            }
        }

        void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyUI(player);
            }
        }

        #endregion

        #region UI

        private void ShowUI(BasePlayer player)
        {
            DestroyUI(player);

            var container = new CuiElementContainer();
            var panel = container.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.1 0.1 0.0" },
                RectTransform = { AnchorMin = "0.76 0.91", AnchorMax = "0.91 0.96" },
                CursorEnabled = false
            }, "Overlay", "SafeZoneUI");

            container.Add(new CuiButton
            {
                Button = { Color = "0.2 0.8 0.2 0.8", Command = "clansafezone.create", Close = "SafeZoneUI" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Text = { Text = "Create Safe Zone", FontSize = 12, Align = TextAnchor.MiddleCenter }
            }, panel);

            CuiHelper.AddUi(player, container);
        }

        private void DestroyUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "SafeZoneUI");
        }

        #endregion

        #region Commands

        [ConsoleCommand("clansafezone.create")]
        private void CreateSafeZone(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            string clan = GetClan(player);
            Puts($"[DEBUG] Clan tag for {player.displayName}: {clan ?? "null"}");

            if (string.IsNullOrEmpty(clan))
            {
                player.ChatMessage("You must be in a clan to use this feature.");
                return;
            }

            if (clanUsedProtection.ContainsKey(clan))
            {
                player.ChatMessage("Your clan has already used its safe zone.");
                return;
            }

            if (Time.realtimeSinceStartup - wipeTime > config.ActivationWindow)
            {
                player.ChatMessage("The safe zone feature is no longer available.");
                return;
            }

            CreateZoneForClan(player, clan);
            player.ChatMessage("Clan safe zone created. Protection will expire at the end of the first hour of wipe.");
        }

        #endregion

        #region Helpers

        private void CreateZoneForClan(BasePlayer player, string clan)
        {
            string zoneId = $"clansafezone_{clan}";
            Vector3 position = player.transform.position;

            var args = new List<string>
            {
                zoneId,
                position.x.ToString(),
                position.y.ToString(),
                position.z.ToString(),
                config.ZoneRadius.ToString(),
                "nopvp true",
                "noraid true",
                $"enter_message Welcome to {clan}'s Safe Zone!",
                $"leave_message Leaving {clan}'s Safe Zone."
            };

            ZoneManager?.Call("CreateOrUpdateZone", args);
            clanUsedProtection[clan] = true;

            float remainingTime = Mathf.Max(0f, config.ActivationWindow - (float)(Time.realtimeSinceStartup - wipeTime));
            timer.Once(remainingTime, () => ZoneManager?.Call("EraseZone", zoneId));
        }

        private string GetClan(BasePlayer player)
        {
            var result = Clans?.Call("GetClanOf", player.userID);
            return result as string;
        }

        #endregion
    }
}
