﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Exiled.API.Features;
using Exiled.Events.EventArgs;
using Exiled.Loader;
using Exiled.Permissions.Extensions;
using MEC;
using SCPStats.Hats;
using UnityEngine;
using WebSocketSharp;
using Object = UnityEngine.Object;

namespace SCPStats
{
#pragma warning disable 4014
    internal class EventHandler
    {
        private static bool DidRoundEnd = false;
        private static bool Restarting = false;
        private static List<string> Players = new List<string>();

        private static bool firstJoin = true;

        private static bool StartGrace = false;

        private static Dictionary<string, string> PocketPlayers = new Dictionary<string, string>();

        internal static bool RanServer = false;

        public static bool PauseRound = false;

        private static List<CoroutineHandle> coroutines = new List<CoroutineHandle>();
        private static List<string> SpawnsDone = new List<string>();

        internal static void Reset()
        {
            Timing.KillCoroutines(coroutines.ToArray());
            coroutines.Clear();
            
            StatHandler.Stop();
            
            SpawnsDone.Clear();

            PauseRound = false;
        }

        internal static void Start()
        {
            firstJoin = true;
            
            StatHandler.Start();
        }

        private static IEnumerator<float> ClearPlayers()
        {
            yield return Timing.WaitForSeconds(30f);

            for (var i = 0; i < Players.Count; i++)
            {
                var player = Players[i];
                if (Player.List.Any(p => p != null && !p.IsHost && p.RawUserId == player)) continue;
                
                StatHandler.SendRequest(RequestType.Leave, "{\"playerid\": \"" + Helper.HandleId(player) + "\"}");

                Players.Remove(player);
            }
        }

        internal static void OnRAReload()
        {
            Timing.RunCoroutine(RAReloaded());
        }

        private static IEnumerator<float> RAReloaded()
        {
            yield return Timing.WaitForSeconds(1.5f);

            foreach (var player in Player.List)
            {
                if (player == null || player.IsHost || player.IPAddress == "127.0.0.WAN" || player.IPAddress == "127.0.0.1") continue;
                StatHandler.SendRequest(RequestType.UserData, Helper.HandleId(player));
            }
        }

        private static bool IsGamemodeRunning()
        {
            var gamemodeManager = Loader.Plugins.FirstOrDefault(pl => pl.Name == "Gamemode Manager");
            if (gamemodeManager == null) return false;
            
            var pluginType = gamemodeManager.Assembly.GetType("Plugin");
            if (pluginType == null) return false;
            
            var queueHandler = gamemodeManager.Assembly.GetType("QueueHandler");
            if (queueHandler == null) return false;

            var queueHandlerInstance = pluginType.GetField("QueueHandler")?.GetValue(gamemodeManager);
            if (queueHandlerInstance == null) return false;

            return (bool) (queueHandler.GetProperty("IsAnyGamemodeActive")?.GetValue(queueHandlerInstance) ?? false);
        }

        internal static void OnRoundStart()
        {
            StartGrace = true;
            Restarting = false;
            DidRoundEnd = false;

            if (IsGamemodeRunning())
            {
                PauseRound = true;
            }
            
            Timing.CallDelayed(SCPStats.Singleton.waitTime, () =>
            {
                StartGrace = false;
            });

            StatHandler.SendRequest(RequestType.RoundStart);
            
            foreach (var player in Player.List)
            {
                if (player?.UserId == null || !player.IsVerified || player.IsHost || player.IPAddress == "127.0.0.WAN" || player.IPAddress == "127.0.0.1") continue;

                StatHandler.SendRequest(RequestType.UserData, Helper.HandleId(player));
            }
        }
        
        internal static void OnRoundEnd(RoundEndedEventArgs ev)
        {
            DidRoundEnd = true;
            StartGrace = false;
            
            HatCommand.HatPlayers.Clear();

            SendRoundEnd();
            
            Timing.KillCoroutines(coroutines.ToArray());
            coroutines.Clear();
            
            SpawnsDone.Clear();
        }
        
        internal static void OnRoundRestart()
        {
            Restarting = true;
            StartGrace = false;
            HatCommand.HatPlayers.Clear();
            if (DidRoundEnd) return;

            SendRoundEnd();
            
            Timing.KillCoroutines(coroutines.ToArray());
            coroutines.Clear();
            
            SpawnsDone.Clear();
        }

        private static void SendRoundEnd()
        {
            StatHandler.SendRequest(RequestType.RoundEnd);

            foreach (var player in Player.List)
            {
                if (player?.UserId == null || player.IsHost || !player.IsVerified || player.DoNotTrack || player.IPAddress == "127.0.0.WAN" || player.IPAddress == "127.0.0.1" || !Helper.IsPlayerValid(player)) continue;
                
                StatHandler.SendRequest(RequestType.RoundEndPlayer, "{\"playerID\": \"" + Helper.HandleId(player) + "\"}");
            }
        }

        internal static void Waiting()
        {
            coroutines.Add(Timing.RunCoroutine(ClearPlayers()));
            
            Restarting = false;
            DidRoundEnd = false;
            StartGrace = false;
            PauseRound = false;
        }
        
        internal static void OnKill(DyingEventArgs ev)
        {
            if (ev.Target?.UserId == null || ev.Target.IsHost || !ev.Target.IsVerified || PauseRound || !ev.IsAllowed || !Helper.IsPlayerValid(ev.Target, false) || !RoundSummary.RoundInProgress()) return;

            if (!ev.Target.DoNotTrack && ev.Target.IPAddress != "127.0.0.WAN" && ev.Target.IPAddress != "127.0.0.1")
            {
                StatHandler.SendRequest(RequestType.Death, "{\"playerid\": \""+Helper.HandleId(ev.Target)+"\", \"killerrole\": \""+(ev.Killer == null ? ((int) ev.Target.Role).ToString() : ((int) ev.Killer.Role).ToString())+"\", \"playerrole\": \""+((int) ev.Target.Role).ToString()+"\", \"damagetype\": \""+DamageTypes.ToIndex(ev.HitInformation.GetDamageType()).ToString()+"\"}");
            }

            if (ev.HitInformation.GetDamageType() == DamageTypes.Pocket && PocketPlayers.TryGetValue(Helper.HandleId(ev.Target), out var killer))
            {
                StatHandler.SendRequest(RequestType.Kill, "{\"playerid\": \""+killer+"\", \"targetrole\": \""+((int) ev.Target.Role).ToString()+"\", \"playerrole\": \""+((int) RoleType.Scp106).ToString()+"\", \"damagetype\": \""+DamageTypes.ToIndex(ev.HitInformation.GetDamageType()).ToString()+"\"}");
                return;
            }
            
            if (ev.Killer?.UserId == null || ev.Killer.IsHost || !ev.Killer.IsVerified || ev.Killer.IPAddress == "127.0.0.WAN" || ev.Killer.IPAddress == "127.0.0.1" || ev.Killer.RawUserId == ev.Target.RawUserId || ev.Killer.DoNotTrack || !Helper.IsPlayerValid(ev.Killer, false)) return;

            StatHandler.SendRequest(RequestType.Kill, "{\"playerid\": \""+Helper.HandleId(ev.Killer)+"\", \"targetrole\": \""+((int) ev.Target.Role).ToString()+"\", \"playerrole\": \""+((int) ev.Killer.Role).ToString()+"\", \"damagetype\": \""+DamageTypes.ToIndex(ev.HitInformation.GetDamageType()).ToString()+"\"}");
        }

        internal static void OnRoleChanged(ChangingRoleEventArgs ev)
        {
            if (ev.Player?.UserId == null || ev.Player.IsHost || !ev.Player.IsVerified || ev.Player.IPAddress == "127.0.0.WAN" || ev.Player.IPAddress == "127.0.0.1") return;
            
            if (ev.NewRole != RoleType.None && ev.NewRole != RoleType.Spectator)
            {
                Timing.CallDelayed(.5f, () =>
                {
                    if (HatCommand.HatPlayers.ContainsKey(ev.Player.UserId))
                    {
                        HatPlayerComponent playerComponent;

                        if (!ev.Player.GameObject.TryGetComponent(out playerComponent))
                        {
                            playerComponent = ev.Player.GameObject.AddComponent<HatPlayerComponent>();
                        }

                        if (playerComponent.item != null)
                        {
                            Object.Destroy(playerComponent.item.gameObject);
                            playerComponent.item = null;
                        }

                        ev.Player.SpawnHat(HatCommand.HatPlayers[ev.Player.UserId]);
                    }
                });
            }

            if (PauseRound || (!RoundSummary.RoundInProgress() && !StartGrace) || !Helper.IsPlayerValid(ev.Player, true, false)) return;
            
            if (ev.IsEscaped && !ev.Player.DoNotTrack)
            {
                StatHandler.SendRequest(RequestType.Escape, "{\"playerid\": \""+Helper.HandleId(ev.Player)+"\", \"role\": \""+((int) ev.Player.Role).ToString()+"\"}");
            }

            if (ev.NewRole == RoleType.None || ev.NewRole == RoleType.Spectator) return;
            
            if (StartGrace && SpawnsDone.Contains(ev.Player.UserId)) return;
            if(!SpawnsDone.Contains(ev.Player.UserId)) SpawnsDone.Add(ev.Player.UserId);
            
            coroutines.Add(Timing.RunCoroutine(SpawnDelay(ev.Player)));
        }

        private static IEnumerator<float> SpawnDelay(Player p)
        {
            if (StartGrace) yield return Timing.WaitForSeconds(SCPStats.Singleton.waitTime);
            StatHandler.SendRequest(RequestType.Spawn, "{\"playerid\": \""+Helper.HandleId(p)+"\", \"spawnrole\": \""+((int) p.Role).ToString()+"\"}");
        }

        internal static void OnPickup(PickingUpItemEventArgs ev)
        {
            if (!ev.Pickup || !ev.Pickup.gameObject) return;
            
            if (ev.Pickup.gameObject.TryGetComponent<HatItemComponent>(out _))
            {
                ev.IsAllowed = false;
                return;
            }
            
            if (ev.Player?.UserId == null || ev.Player.IsHost || !ev.Player.IsVerified || ev.Player.IPAddress == "127.0.0.WAN" || ev.Player.IPAddress == "127.0.0.1" || PauseRound || !Helper.IsPlayerValid(ev.Player) || !RoundSummary.RoundInProgress() || !ev.IsAllowed) return;

            StatHandler.SendRequest(RequestType.Pickup, "{\"playerid\": \""+Helper.HandleId(ev.Player)+"\", \"itemid\": \""+((int) ev.Pickup.itemId).ToString()+"\"}");
        }

        internal static void OnDrop(DroppingItemEventArgs ev)
        {
            if (ev.Player?.UserId == null || ev.Player.IsHost || !ev.Player.IsVerified || ev.Player.IPAddress == "127.0.0.WAN" || ev.Player.IPAddress == "127.0.0.1" || PauseRound || !Helper.IsPlayerValid(ev.Player) || !RoundSummary.RoundInProgress() || !ev.IsAllowed) return;

            StatHandler.SendRequest(RequestType.Drop, "{\"playerid\": \""+Helper.HandleId(ev.Player)+"\", \"itemid\": \""+((int) ev.Item.id).ToString()+"\"}");
        }

        internal static void OnJoin(VerifiedEventArgs ev)
        {
            if (ev.Player?.UserId == null || ev.Player.IsHost || !ev.Player.IsVerified || ev.Player.IPAddress == "127.0.0.WAN" || ev.Player.IPAddress == "127.0.0.1") return;
            
            if (firstJoin)
            {
                firstJoin = false;
                Verification.UpdateID();
            }

            StatHandler.SendRequest(RequestType.UserData, Helper.HandleId(ev.Player));
            
            if (!Round.IsStarted && Players.Contains(ev.Player.RawUserId) || ev.Player.DoNotTrack) return;

            StatHandler.SendRequest(RequestType.Join, "{\"playerid\": \""+Helper.HandleId(ev.Player)+"\"}");
            
            Players.Add(ev.Player.RawUserId);
        }

        internal static void OnLeave(DestroyingEventArgs ev)
        {
            if (ev.Player?.UserId == null || ev.Player.IsHost || !ev.Player.IsVerified || ev.Player.IPAddress == "127.0.0.WAN" || ev.Player.IPAddress == "127.0.0.1") return;
            
            if (ev.Player.GameObject.TryGetComponent<HatPlayerComponent>(out var playerComponent) && playerComponent.item != null)
            {
                Object.Destroy(playerComponent.item.gameObject);
                playerComponent.item = null;
            }
            
            if (Restarting || ev.Player.DoNotTrack) return;

            StatHandler.SendRequest(RequestType.Leave, "{\"playerid\": \""+Helper.HandleId(ev.Player)+"\"}");

            if (Players.Contains(ev.Player.RawUserId)) Players.Remove(ev.Player.RawUserId);
        }

        internal static void OnUse(UsedMedicalItemEventArgs ev)
        {
            if (ev.Player?.UserId == null || ev.Player.IsHost || !ev.Player.IsVerified || ev.Player.IPAddress == "127.0.0.WAN" || ev.Player.IPAddress == "127.0.0.1" || PauseRound || !Helper.IsPlayerValid(ev.Player) || !RoundSummary.RoundInProgress()) return;

            StatHandler.SendRequest(RequestType.Use, "{\"playerid\": \""+Helper.HandleId(ev.Player)+"\", \"itemid\": \""+((int) ev.Item).ToString()+"\"}");
        }

        internal static void OnThrow(ThrowingGrenadeEventArgs ev)
        {
            if (ev.Player?.UserId == null || ev.Player.IsHost || !ev.Player.IsVerified || ev.Player.IPAddress == "127.0.0.WAN" || ev.Player.IPAddress == "127.0.0.1" || PauseRound || !Helper.IsPlayerValid(ev.Player) || !RoundSummary.RoundInProgress() || !ev.IsAllowed) return;

            StatHandler.SendRequest(RequestType.Use, "{\"playerid\": \""+Helper.HandleId(ev.Player)+"\", \"itemid\": \""+((int) ev.GrenadeManager.availableGrenades[(int) ev.Type].inventoryID).ToString()+"\"}");
        }

        internal static void OnUpgrade(UpgradingItemsEventArgs ev)
        {
            ev.Items.RemoveAll(pickup => pickup.gameObject.TryGetComponent<HatItemComponent>(out _));
        }

        internal static void OnEnterPocketDimension(EnteringPocketDimensionEventArgs ev)
        {
            if (!ev.IsAllowed || ev.Player?.UserId == null || ev.Player.IsHost || !ev.Player.IsVerified || ev.Player.IPAddress == "127.0.0.WAN" || ev.Player.IPAddress == "127.0.0.1" || !Helper.IsPlayerValid(ev.Player) || ev.Scp106?.UserId == null || ev.Scp106.IsHost || !ev.Scp106.IsVerified || ev.Scp106.IPAddress == "127.0.0.WAN" || ev.Scp106.IPAddress == "127.0.0.1" || !Helper.IsPlayerValid(ev.Scp106) || ev.Player.UserId == ev.Scp106.UserId) return;

            PocketPlayers[Helper.HandleId(ev.Player)] = Helper.HandleId(ev.Scp106);
        }
    }
}
