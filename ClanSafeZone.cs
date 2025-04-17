using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("ClanSafeZone", "Stormmv", "1.1.1")]
    [Description("Clans can create a safe zone using a UI button in the Tool Cupboard during the first hour after wipe.")]

    public class ClanSafeZone : RustPlugin
    {
        [PluginReference] Plugin Clans, ZoneManager;

        private Dictionary<string, bool> clanUsedProtection = new();
        private HashSet<ulong> interactingPlayers = new();

        private ConfigData config;
        private static string dataFileName = "ClanSafeZone";

        private class ConfigData
        {
            public List<string> AllowedClans { get; set; } = new List<string>(); // List of allowed clan names
            public float ZoneRadius { get; set; } = 60f;
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
                PrintError("Clans Reborn plugin not found! Please make sure it's installed and loaded.");

            if (ZoneManager == null)
            {
                PrintWarning("ZoneManager not loaded. Attempting to reload...");
                Server.Command("oxide.reload ZoneManager");
            }

            // Register the permission when the server is initialized
            permission.RegisterPermission("clansafezone.use", this);
        }

        void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (entity == null || player == null) return;

            // Checking for both the default and custom cupboard types
            if (entity.ShortPrefabName != "cupboard.tool.deployed" &&
                entity.ShortPrefabName != "cupboard.tool.shockbyte.deployed" &&
                entity.ShortPrefabName != "cupboard.tool.retro.deployed")
                return;

            if (!interactingPlayers.Contains(player.userID))
            {
                interactingPlayers.Add(player.userID);
                timer.Once(0.2f, () => ShowUI(player));
            }
        }


        void OnLootEntityEnd(BasePlayer player, BaseEntity entity)
        {
            if (entity == null || player == null) return;

            // Check if the entity is one of the specified tool cupboards
            if (entity.ShortPrefabName != "cupboard.tool.deployed" &&
                entity.ShortPrefabName != "cupboard.tool.shockbyte.deployed" &&
                entity.ShortPrefabName != "cupboard.tool.retro.deployed")
                return;

            if (interactingPlayers.Contains(player.userID))
            {
                interactingPlayers.Remove(player.userID);
                DestroyUI(player);
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
            string clan = GetClan(player);

            // Only show UI if player is in an allowed clan AND has the permission
            if (string.IsNullOrEmpty(clan) || !config.AllowedClans.Contains(clan)) return;
            if (!permission.UserHasPermission(player.UserIDString, "clansafezone.use")) return;

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

            // Check if player has the permission AND is in an allowed clan
            if (permission.UserHasPermission(player.UserIDString, "clansafezone.use"))
            {
                string clan = GetClan(player);
                Puts($"[DEBUG] Clan tag for {player.displayName}: {clan ?? "null"}");

                if (string.IsNullOrEmpty(clan))
                {
                    player.ChatMessage("You must be in a clan to use this feature.");
                    return;
                }

                if (!config.AllowedClans.Contains(clan))
                {
                    player.ChatMessage("Your clan is not allowed to create a safe zone.");
                    return;
                }

                // Check if the clan already has a zone
                var zoneIds = ZoneManager?.Call<string[]>("GetZoneIDs") ?? new string[0];
                foreach (var zoneId in zoneIds)
                {
                    // Check if the zone name contains the clan tag (e.g., clansafezone_MRTM)
                    if (zoneId.Contains(clan))
                    {
                        player.ChatMessage("Your clan has already created a safe zone.");
                        return;
                    }
                }

                // Now that we've passed all checks, create the safe zone
                CreateZoneForClan(player, clan);
                player.ChatMessage("Clan safe zone created.");
                Server.Command("oxide.reload ZoneManager");
            }
            else
            {
                player.ChatMessage("You do not have permission to create a safe zone.");
            }
        }

        #endregion

        #region Helpers

        private void CreateZoneForClan(BasePlayer player, string clan)
        {
            if (player == null)
            {
                Puts("[ERROR] Player is null.");
                return;
            }

            Vector3 position = player.transform.position;
            if (position == Vector3.zero)
            {
                Puts("[ERROR] Invalid position: (0, 0, 0). Using fallback.");
                position = new Vector3(100f, 10f, 100f);
            }

            string zoneId = $"clansafezone_{clan}";
            string[] args = new string[] 
            {
                "radius", config.ZoneRadius.ToString(),
                "pvpgod", "true",
                "undestr", "true",
            };

            if (ZoneManager == null)
            {
                Puts("[ERROR] ZoneManager not found or not loaded.");
                return;
            }

            try
            {
                bool? success = ZoneManager?.Call<bool>("CreateOrUpdateZone", zoneId, args, position);
                if (success == true)
                {
                    Puts($"[DEBUG] Zone {zoneId} created successfully at {position}.");
                    clanUsedProtection[clan] = true;
                }
                else
                {
                    Puts($"[DEBUG] Failed to create zone {zoneId}.");
                }
            }
            catch (Exception ex)
            {
                Puts($"[ERROR] Exception creating zone: {ex.Message}");
            }
        }

        private string GetClan(BasePlayer player)
        {
            ulong steamId = player.userID;
            var result = Clans?.Call("GetClanOf", steamId);
            return result as string;
        }

        #endregion
    }
}