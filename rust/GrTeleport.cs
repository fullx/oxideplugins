﻿using System.Collections.Generic;
using System;
using UnityEngine;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("GrTeleport", "carny666", "1.0.6", ResourceId = 2665)]
    class GrTeleport : RustPlugin
    {
        #region permissions
        private const string adminPermission = "GrTeleport.admin";
        private const string grtPermission = "GrTeleport.use";
        #endregion

        #region local variabls / supporting classes
        GrTeleportData grTeleportData;
        
        int lastGridTested = 0;
        List<SpawnPosition> spawnGrid = new List<SpawnPosition>();
        List<Cooldown> coolDowns = new List<Cooldown>();

        class GrTeleportData
        {
            public int CooldownInSeconds { get; set; }
            public bool AvoidWater { get; set; }

            public bool AllowBuildingBlocked { get; set; }
        }

        class SpawnPosition
        {
            public Vector3 Position;
            public Vector3 GroundPosition;
            public bool aboveWater;
            public string GridReference;

            public SpawnPosition(Vector3 position)
            {
                Position = position;
                aboveWater = PositionAboveWater(Position);
                GroundPosition = GetGroundPosition(new Vector3(position.x, 0, position.z));
            }

            bool PositionAboveWater(Vector3 Position)
            {
                if ((TerrainMeta.HeightMap.GetHeight(Position) - TerrainMeta.WaterMap.GetHeight(Position)) >= 0)
                    return false;
                return true;
            }

            Vector3 GetGroundPosition(Vector3 sourcePos)
            {
                LayerMask GROUND_MASKS = LayerMask.GetMask("Terrain", "World", "Construction");  // TODO: mountain? wtf?
                RaycastHit hitInfo;

                if (Physics.Raycast(sourcePos, Vector3.down, out hitInfo, GROUND_MASKS))
                    sourcePos.y = hitInfo.point.y;
                sourcePos.y = Mathf.Max(sourcePos.y, TerrainMeta.HeightMap.GetHeight(sourcePos));
                
                return sourcePos;
            }

        }

        class Cooldown
        {
            public string name;
            public int cooldownPeriodSeconds;
            public DateTime lastUse;
            public DateTime expirtyDateTime;

            public Cooldown(string PlayerName, int CoolDownInSeconds) 
            {
                name = PlayerName;
                cooldownPeriodSeconds = CoolDownInSeconds;
                lastUse = DateTime.Now;
                expirtyDateTime = lastUse.AddSeconds(cooldownPeriodSeconds);
            }

        }
        #endregion

        #region events

        void Init()
        {
            grTeleportData = Config.ReadObject<GrTeleportData>();
            if (Config["Messages"] != null)
                Config.WriteObject(grTeleportData, true);
        }

        void Loaded()
        {
            try
            {
                permission.RegisterPermission(adminPermission, this);
                permission.RegisterPermission(grtPermission, this);

                spawnGrid = CreateSpawnGrid();

                lang.RegisterMessages(new Dictionary<string, string>
                {
                    { "buildingblocked", "You cannot grTeleport into or out from a building blocked area." },
                    { "nosquares", "Admin must configure set the grid width using setgridwidth ##" },
                    { "noinit", "spawnGrid was not initialized. 0 spawn points available." },
                    { "teleported", "You have GrTeleported to {gridreference}" }, // {playerPosition}
                    { "overwater", "That refernce point is above water." },
                    { "cmdusage", "usage ex: /grt n10  (where n = a-zz and 10=0-60" },
                    { "noaccess", "You do not have sufficient access to execute this command." },
                    { "sgerror", "Error creating spawnpoints, too much water? contact dev." },
                    { "cooldown", "Sorry, your are currently in a {cooldownperiod} second cooldown, you have another {secondsleft} seconds remaining." },
                    { "cooldownreply", "Cooldown has been set to {cooldownperiod} seconds" },
                    { "gridwidthreply", "Gridwidth has been set to {gridwidth}x{gridwidth}" },
                    { "cuboardreply", "Buidling block teleportation is {togglebuildingblocked}" },
                    { "avoidwaterreplay", "Avoid water has been set tp {avoidwater}" },
                    { "cupboard", "Sorry, you cannot teleport within {distance}f of a cupboard." }
                }, this, "en");
            }
            catch (Exception ex)
            {
                throw new Exception($"Loaded {ex.Message}");
            }
        }

        protected override void LoadDefaultConfig()
        {
            var data = new GrTeleportData
            {
                CooldownInSeconds = 30,
                AvoidWater = true,
                AllowBuildingBlocked = false
            };
            Config.WriteObject(data, true);
        }
        #endregion

        #region commands
        [ChatCommand("grt")]
        void chatCommandGrt(BasePlayer player, string command, string[] args)
        {
            try
            {
                if (!CheckAccess(player, command, grtPermission)) return;

                var tmp = GetCooldown(player.displayName);
                if (tmp != null)  
                {
                    PrintToChat(player, lang.GetMessage("cooldown", this, player.UserIDString).Replace("{cooldownperiod}", tmp.cooldownPeriodSeconds.ToString()).Replace("{secondsleft}", tmp.expirtyDateTime.Subtract(DateTime.Now).TotalSeconds.ToString("0")));
                    return;
                }

                if (spawnGrid == null || spawnGrid.Count <= 0)
                    spawnGrid = CreateSpawnGrid();

                if (args.Length > 0)
                {
                    var gr = args[0];
                    var index = GridIndexFromReference(gr);

                    
                    if (player.IsBuildingBlocked(spawnGrid[index].GroundPosition, new Quaternion(0, 0, 0, 0), new Bounds(Vector3.zero, Vector3.zero)) && !grTeleportData.AllowBuildingBlocked) 
                    {
                        PrintToChat(player, lang.GetMessage("buildingblocked", this, player.UserIDString));
                        return;
                    }

                    if (spawnGrid[index].aboveWater && grTeleportData.AvoidWater)
                    {
                        PrintToChat(player, lang.GetMessage("overwater", this, player.UserIDString));
                        return;
                    }
                    else
                    {
                        if (TeleportToGridReference(player, gr, grTeleportData.AvoidWater))                    
                        {
                            PrintToChat(player, lang.GetMessage("teleported", this, player.UserIDString).Replace("{playerPosition}", player.transform.position.ToString()).Replace("{gridreference}", gr.ToUpper()));
                            AddToCoolDown(player.displayName, grTeleportData.CooldownInSeconds);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PrintError($"Error chatCommandGrt:{ex.Message}");
                return;
            }

            PrintToChat(player, lang.GetMessage("cmdusage", this, player.UserIDString));
        }

        [ConsoleCommand("grt.nextspawn")]
        void ccGrtNextspawn(ConsoleSystem.Arg arg)
        {
            try
            {
                if (arg.Player() == null) return;

                BasePlayer player = arg.Player();

                if (spawnGrid.Count <= 0)
                    throw new Exception(lang.GetMessage("noinit", this, player.UserIDString));

                if (!CheckAccess(player, "grt.nextspawn", adminPermission)) return;

                while (spawnGrid[++lastGridTested].aboveWater)
                    if (lastGridTested > 1000) // endless loop               
                        throw new Exception(lang.GetMessage("sgerror", this, player.UserIDString));

                Teleport(player, spawnGrid[lastGridTested].GroundPosition, false);

                PrintToChat(player, lang.GetMessage("teleported", this, player.UserIDString).Replace("{playerPosition}", player.transform.position.ToString()));
            }
            catch (Exception ex)
            {
                throw new Exception($"ccGrtNextspawn {ex.Message}");
            }
        }

        [ChatCommand("setcooldown")]
        void chmSetCooldown(BasePlayer player, string command, string[] args)
        {
            try
            {
                if (!CheckAccess(player, "setcooldown", adminPermission)) return;
                if (args.Length > 0)
                    grTeleportData.CooldownInSeconds = int.Parse(args[0]);

                coolDowns.Clear();

                Config.WriteObject(grTeleportData, true);
                PrintToChat(player, lang.GetMessage("cooldownreply", this, player.UserIDString).Replace("{cooldownperiod}", grTeleportData.CooldownInSeconds.ToString()));
            }
            catch (Exception ex)
            {
                throw new Exception($"chmSetCooldown {ex.Message}");
            }

        }

        [ChatCommand("togglebuildingblocked")]
        void chmtogglebuildingblocked(BasePlayer player, string command, string[] args)
        {
            try
            {
                if (!CheckAccess(player, "togglebuildingblocked", adminPermission)) return;
                grTeleportData.AllowBuildingBlocked = !grTeleportData.AllowBuildingBlocked;
                Config.WriteObject(grTeleportData, true);
                PrintToChat(player, lang.GetMessage("cuboardreply", this, player.UserIDString).Replace("{togglebuildingblocked}", grTeleportData.AllowBuildingBlocked.ToString()));
            }
            catch (Exception ex)
            {
                throw new Exception($"setcupboard {ex.Message}");
            }

        }

        [ChatCommand("avoidwater")]
        void chmSetAvoidWater(BasePlayer player, string command, string[] args)
        {
            try
            {
                if (!CheckAccess(player, "avoidwater", adminPermission)) return;
                if (args.Length > 0)
                    grTeleportData.AvoidWater = bool.Parse(args[0]);
                Config.WriteObject(grTeleportData, true);
                PrintToChat(player, lang.GetMessage("avoidwaterreplay", this, player.UserIDString).Replace("{avoidwater}", grTeleportData.AvoidWater.ToString()));
            }
            catch (Exception ex)
            {
                throw new Exception($"chmSetAvoidWater {ex.Message}");
            }

        }
        #endregion

        #region API
        [HookMethod("TeleportToGridReference")]
        private bool TeleportToGridReference(BasePlayer player, string gridReference, bool avoidWater = true)
        {
            try
            {
                var index = GridIndexFromReference(gridReference);
                if (avoidWater && spawnGrid[index].aboveWater) return false;
                Teleport(player, spawnGrid[index].GroundPosition);
                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"TeleportToGridReference {ex.Message}");
            }

        }

        [HookMethod("IsGridReferenceAboveWater")]
        private bool IsGridReferenceAboveWater(string gridReference)
        {
            try
            {
                var index = GridIndexFromReference(gridReference);
                return spawnGrid[index].aboveWater;
            }
            catch (Exception ex)
            {
                throw new Exception($"IsGridReferenceAboveWater {ex.Message}");
            }

        }
        #endregion

        #region supporting fuctions

        int GridIndexFromReference(string gridReference)
        {
            try
            {
                foreach(SpawnPosition s in spawnGrid)
                {
                    if (gridReference.ToUpper().Trim() == s.GridReference.ToUpper().Trim())
                        return spawnGrid.IndexOf(s);
                }
                throw new Exception($"GridIndexFromReference {gridReference.ToUpper()} was not found in spawnGrid {spawnGrid.Count}");
            }
            catch (Exception ex)
            {
                throw new Exception($"GridIndexFromReference {ex.Message}");
            }

        }

        Cooldown GetCooldown(string playerName)
        {
            try
            {
                var cnt = coolDowns.RemoveAll(x => x.expirtyDateTime <= DateTime.Now);
                var index = coolDowns.FindIndex(x => x.name.ToLower() == playerName.ToLower());
                if (index == -1) return null;

                return coolDowns[index];
            }
            catch (Exception ex)
            {
                throw new Exception($"GetCooldown {ex.Message}", ex);
            }
        }

        List<SpawnPosition> CreateSpawnGrid()
        {
            try
            {
                List<SpawnPosition> retval = new List<SpawnPosition>();

                var worldSize = (ConVar.Server.worldsize);
                float offset = worldSize / 2;
                var gridWidth = (0.0066666666666667f * worldSize);
                float step = worldSize / gridWidth;
                string start = "";

                char letter = 'A';
                int number = 0;

                for (float zz = offset; zz > -offset; zz -= step)
                {
                    for (float xx = -offset; xx < offset; xx += step)
                    {
                        var sp = new SpawnPosition(new Vector3(xx, 0, zz));
                        sp.GridReference = $"{start}{letter}{number}";
                        retval.Add(sp);
                        number++;
                    }

                    number = 0;
                    if (letter.ToString().ToUpper() == "Z")
                    {
                        start = "A";
                        letter = 'A';
                    }
                    else
                    {
                        letter = (char)(((int)letter) + 1);
                    }
                }
                return retval;
            } catch(Exception ex)
            {
                throw new Exception($"CreateSpawnGrid {ex.Message}");
            }
        }

        void StartSleeping(BasePlayer player)
        {
            if (player.IsSleeping())
                return;

            player.SetPlayerFlag(BasePlayer.PlayerFlags.Sleeping, true);

            if (!BasePlayer.sleepingPlayerList.Contains(player))
                BasePlayer.sleepingPlayerList.Add(player);

            player.CancelInvoke("InventoryUpdate");
            //player.inventory.crafting.CancelAll(true);
            //player.UpdatePlayerCollider(true, false);
        }

        void Teleport(BasePlayer player, Vector3 position, bool startSleeping = true)
        {
            if (player.net?.connection != null)
                player.ClientRPCPlayer(null, player, "StartLoading");

            if (startSleeping)
                StartSleeping(player);

            player.MovePosition(position);

            if (player.net?.connection != null)
                player.ClientRPCPlayer(null, player, "ForcePositionTo", position);

            player.SendNetworkUpdate();

            if (player.net?.connection != null)
                player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);

            player.UpdateNetworkGroup();

            player.SendNetworkUpdateImmediate(false);
            if (player.net?.connection == null) return;

            //TODO temporary for potential rust bug
            try { player.ClearEntityQueue(null); } catch { }
            player.SendFullSnapshot();
        }

        bool CheckAccess(BasePlayer player, string command, string sPermission, bool onErrorDisplayMessageToUser = true)
        {
            try
            {
                if (!permission.UserHasPermission(player.UserIDString, sPermission))
                {
                    if (onErrorDisplayMessageToUser)
                        PrintToChat(player, lang.GetMessage("noaccess", this, player.UserIDString));

                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"CheckAccess {ex.Message}");
            }
        }

        void AddToCoolDown(string userName, int seconds)
        {
            coolDowns.Add(new Cooldown(userName.ToLower(), seconds));
        }

        bool AreThereCupboardsWithinDistance(Vector3 position, int distance)
        {
            try
            {
                var spawns = Resources.FindObjectsOfTypeAll<GameObject>();
                foreach (GameObject s in spawns)
                {
                    if (Vector3.Distance(s.transform.position, position) < distance)
                    {
                        if (s.name.Contains("tool_cupboard"))
                            return true;
                    }
                }
                return false;
            } catch(Exception ex)
            {
                throw new Exception($"AreThereCupboardsWithinDistance {ex.Message}");
            }
        }

        
        #endregion

    }
}

