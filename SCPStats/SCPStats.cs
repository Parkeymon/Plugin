﻿using System;
using System.Linq;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.Loader;
using HarmonyLib;
using MEC;

namespace SCPStats
{
    public class SCPStats : Plugin<Config>
    {
        public override string Name { get; } = "ScpStats";
        public override string Author { get; } = "PintTheDragon";
        public override Version Version { get; } = new Version(1, 1, 7);
        public override PluginPriority Priority { get; } = PluginPriority.Last;

        internal static SCPStats Singleton;

        internal string ID = "";

        private static Harmony harmony;

        internal float waitTime = 10;

        public override void OnEnabled()
        {
            Singleton = this;

            if (Config.Secret == "fill this" || Config.ServerId == "fill this")
            {
                Log.Warn("Config for SCPStats has not been filled out correctly. Disabling!");
                base.OnDisabled();
                return;
            }
            
            harmony = new Harmony("SCPStats-"+Version);
            harmony.PatchAll();
            
            EventHandler.Start();

            Exiled.Events.Handlers.Server.RoundStarted += EventHandler.OnRoundStart;
            Exiled.Events.Handlers.Server.RoundEnded += EventHandler.OnRoundEnd;
            Exiled.Events.Handlers.Server.RestartingRound += EventHandler.OnRoundRestart;
            Exiled.Events.Handlers.Server.WaitingForPlayers += EventHandler.Waiting;
            Exiled.Events.Handlers.Player.Dying += EventHandler.OnKill;
            Exiled.Events.Handlers.Player.ChangingRole += EventHandler.OnRoleChanged;
            Exiled.Events.Handlers.Player.PickingUpItem += EventHandler.OnPickup;
            Exiled.Events.Handlers.Player.DroppingItem += EventHandler.OnDrop;
            Exiled.Events.Handlers.Player.Joined += EventHandler.OnJoin;
            Exiled.Events.Handlers.Player.Left += EventHandler.OnLeave;
            Exiled.Events.Handlers.Player.MedicalItemUsed += EventHandler.OnUse;
            Exiled.Events.Handlers.Player.ThrowingGrenade += EventHandler.OnThrow;
            Exiled.Events.Handlers.Server.ReloadedRA += EventHandler.OnRAReload;
            Exiled.Events.Handlers.Scp914.UpgradingItems += EventHandler.OnUpgrade;

            if (Config.AutoUpdates) AutoUpdater.RunUpdater(10000);

            Timing.CallDelayed(3f, () =>
            {
                var plugin = Loader.Plugins.FirstOrDefault(pl => pl.Name == "ScpSwap");
                if (plugin == null) return;

                var config = plugin.Assembly.GetType("ScpSwap.Config");
                if (config == null) return;

                var configInstance = plugin.Assembly.GetType("ScpSwap.ScpSwap")?.GetProperty("Config")?.GetValue(plugin);
                if (configInstance == null) return;

                var value = config.GetProperty("SwapTimeout")?.GetValue(configInstance);
                if (value == null) return;

                waitTime += (float) value;
            });
            
            base.OnEnabled();
        }

        public override void OnDisabled()
        {
            harmony.UnpatchAll();
            harmony = null;
            
            Exiled.Events.Handlers.Server.RoundStarted -= EventHandler.OnRoundStart;
            Exiled.Events.Handlers.Server.RoundEnded -= EventHandler.OnRoundEnd;
            Exiled.Events.Handlers.Server.RestartingRound -= EventHandler.OnRoundRestart;
            Exiled.Events.Handlers.Server.WaitingForPlayers -= EventHandler.Waiting;
            Exiled.Events.Handlers.Player.Dying -= EventHandler.OnKill;
            Exiled.Events.Handlers.Player.PickingUpItem -= EventHandler.OnPickup;
            Exiled.Events.Handlers.Player.DroppingItem -= EventHandler.OnDrop;
            Exiled.Events.Handlers.Player.Joined -= EventHandler.OnJoin;
            Exiled.Events.Handlers.Player.Left -= EventHandler.OnLeave;
            Exiled.Events.Handlers.Player.MedicalItemUsed -= EventHandler.OnUse;
            Exiled.Events.Handlers.Player.ThrowingGrenade -= EventHandler.OnThrow;
            Exiled.Events.Handlers.Server.ReloadedRA -= EventHandler.OnRAReload;
            Exiled.Events.Handlers.Scp914.UpgradingItems -= EventHandler.OnUpgrade;

            EventHandler.Reset();
            Hats.Hats.Reset();

            waitTime = 10f;
            
            Singleton = null;

            base.OnDisabled();
        }
    }
}