﻿using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Rust;
using UnityEngine;
using Oxide.Game.Rust;
using System.Globalization;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using UnityEngine.SceneManagement;
using Facepunch;
using ProtoBuf;
namespace Oxide.Plugins

{
    [Info("BotSpawn", "Steenamaroo", "1.3.3", ResourceId = 2580)]
    
    [Description("Spawn Bots with kits at monuments.")]
//population fix
	
    class BotSpawn : RustPlugin
    {
        [PluginReference]
        Plugin Vanish, Kits;
     
        const string permAllowed = "botspawn.allowed";
        bool HasPermission(string id, string perm) => permission.UserHasPermission(id, perm);
        
        int no_of_AI = 0;
        System.Random rnd = new System.Random();
        static System.Random random = new System.Random();
        bool isInAir;

        #region Data
        class StoredData
        {
            public Dictionary<string, MonumentSettings> CustomProfiles = new Dictionary<string, MonumentSettings>();
            public StoredData()
            {
            }
        }

        StoredData storedData;
        #endregion
                
        public double GetRandomNumber(double minimum, double maximum)
        { 
            return random.NextDouble() * (maximum - minimum) + minimum;
        }
        
        void Init()
        {
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings {
            Formatting = Newtonsoft.Json.Formatting.Indented,
            ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore
            };
            var filter = RustExtension.Filter.ToList(); // Thanks Fuji. :)
            filter.Add("cover points");
            filter.Add("resulted in a conflict");
            RustExtension.Filter = filter.ToArray();
            no_of_AI = 0;
            Wipe();
            LoadConfigVariables();
            if (configData.Options.Cull_Default_Population)
            Scientist.Population = 0;
        }
        
        void OnServerInitialized()
        {
            FindMonuments();
        }

        void Loaded()
        {
            lang.RegisterMessages(messages, this);
            permission.RegisterPermission(permAllowed, this);
            storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("BotSpawn");
            if (configData.Options.Reset)
            timer.Repeat(configData.Options.Reset_Timer, 0, () => cmdBotRespawn());
        }
        
        void Unload()
        {
            var filter = RustExtension.Filter.ToList();
            filter.Remove("OnServerInitialized");
            filter.Remove("cover points");
            filter.Remove("resulted in a conflict");
            RustExtension.Filter = filter.ToArray();
            Wipe();
        }
        
        void Wipe()
        {
	    foreach (var bot in TempRecord.NPCPlayers)
	    {
            if (bot.Value.bot != null)
            bot.Value.bot.Kill();
            else
            continue;
	    }
	    TempRecord.NPCPlayers.Clear();            
        }

        bool isAuth(BasePlayer player)
        {
            if (player.net.connection != null)
                if (player.net.connection.authLevel < 2)
                    return false;
                    return true;
        }
    
    
	void OnPlayerDropActiveItem(BasePlayer player, Item item)
	{
	    if (player as NPCPlayer != null)
            {
                NPCPlayerApex botapex = player as NPCPlayerApex;
                
            if (TempRecord.NPCPlayers.ContainsKey(botapex) && !configData.Options.Bots_Drop_Weapons)
            {
                item.Remove(0f);
                return;
                }
            }   
	}
    
        object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
	//bool chute;                       //commented awaiting changes in Rust.
	//NPCPlayerApex botapex;
	//if (entity is NPCPlayer)
	//{
	//    botapex = entity.GetComponent<NPCPlayerApex>();
	//	if (TempRecord.NPCPlayers.ContainsKey(botapex))
	//	{
	//	    if (TempRecord.NPCPlayers[botapex].invincible)
	//	    foreach (var child in botapex.children)
	//	    if (child.ToString().Contains("parachute"))
	//	    return true;
	//	}
	//}
            
            if (entity is NPCPlayer && info.Initiator is BasePlayer)
            {
                var canNetwork = Vanish?.Call("IsInvisible", info.Initiator); //bots wont retaliate to vanished players
                    if ((canNetwork is bool))
                    if ((bool)canNetwork)
                    {
                        info.Initiator = null;
                    }

                    if (configData.Options.Peace_Keeper)
                    {
                    var heldMelee = info.Weapon as BaseMelee;
                    var heldTorchWeapon = info.Weapon as TorchWeapon; 
                    if (heldMelee != null || heldTorchWeapon != null)
                    info.damageTypes.ScaleAll(0);
                    }//prevent melee farming with peacekeeper on

            }

            if (entity is NPCPlayer && info.Initiator is NPCPlayer && !(configData.Options.NPC_Retaliate)) //bots wont retaliate to friendly fire
            {
                info.Initiator = null;
            }
            
            if (info?.Initiator is NPCPlayer && entity is BasePlayer)
            {
                var attacker = info?.Initiator as NPCPlayer;
            
                foreach (var bot in TempRecord.NPCPlayers)
                {
                    if (bot.Value.botID == attacker.userID)
                    {
                    System.Random rnd = new System.Random();
                    int rand = rnd.Next(1, 10);
                        if (bot.Value.accuracy < rand)
                        {
                        return true;
                        }
                        else
                        {
                        info.damageTypes.ScaleAll(bot.Value.damage);
                        return null;
                        }
                    }
                }
            }
            return null;
        }
        
        void OnEntityDeath(BaseEntity entity)
        {
            string respawnLocationName = "";
            NPCPlayerApex Scientist = null;
            if (entity is NPCPlayerApex)
            {
                foreach (var bot in TempRecord.NPCPlayers)
                {
		    Scientist = entity as NPCPlayerApex;
                    if (bot.Value.botID == Scientist.userID)
                        {
                            no_of_AI--;
                            respawnLocationName = bot.Value.monumentName;
                            TempRecord.DeadNPCPlayerIds.Add(bot.Value.botID);
                            if (TempRecord.MonumentProfiles[respawnLocationName].Disable_Radio == true)
                            Scientist.DeathEffect = new GameObjectRef();
                        }
                }
                if(TempRecord.dontRespawn.Contains(Scientist.userID))
                {
                UpdateRecords(Scientist);
                return;
                }
                foreach (var profile in TempRecord.MonumentProfiles)
                {
                    if(profile.Key == respawnLocationName)
                    {
                        timer.Once(profile.Value.Respawn_Timer, () => SpawnSci(profile.Key, profile.Value, null));
                        UpdateRecords(Scientist);
                    }
                }
            }
            else
            {
                return;
            }
        }
      
        void UpdateRecords(BasePlayer player)
        {
            foreach (var bot in TempRecord.NPCPlayers)
            {
                if (bot.Value.botID == player.userID)
                {
                foreach (Item item in player.inventory.containerBelt.itemList) 
                {
                    item.Remove();
                } 
                TempRecord.NPCPlayers.Remove(bot.Key);
                TempRecord.dontRespawn.Remove(bot.Value.botID);
                return;
                }
            }
        }
	
        // Facepunch.RandomUsernames
        public static string Get(ulong v) //credit fuji.
        {
            return Facepunch.RandomUsernames.Get((int)(v % 2147483647uL));
        }

        BaseEntity InstantiateSci(Vector3 position, Quaternion rotation, bool murd) // Spawn population spam fix - credit Fuji
        {
            string prefabname = "assets/prefabs/npc/scientist/scientist.prefab";
            if (murd == true)
            {
                prefabname ="assets/prefabs/npc/murderer/murderer.prefab";
            }

            var prefab = GameManager.server.FindPrefab(prefabname);
            GameObject gameObject = Instantiate.GameObject(prefab, position, rotation);
            gameObject.name = prefabname;
            SceneManager.MoveGameObjectToScene(gameObject, Rust.Server.EntityScene);
                if (gameObject.GetComponent<Spawnable>())
                    UnityEngine.Object.Destroy(gameObject.GetComponent<Spawnable>());
                if (!gameObject.activeSelf)
                    gameObject.SetActive(true);
			BaseEntity component = gameObject.GetComponent<BaseEntity>();
            return component;
        }

        void SpawnSci(string name, MonumentSettings settings, string type = null)
        {

            var murd = settings.Murderer;
            var pos = new Vector3 (settings.LocationX, settings.LocationY, settings.LocationZ);
            var zone = settings;

                int X = rnd.Next((-zone.Radius/2), (zone.Radius/2));
                int Z = rnd.Next((-zone.Radius/2), (zone.Radius/2));
                int dropX = rnd.Next(5, 10);
                int dropZ = rnd.Next(5, 10);
		int Y = 100;
	    //int Y = rnd.Next(zone.Spawn_Height, (zone.Spawn_Height + 50));
                var CentrePos = new Vector3((pos.x + X),200,(pos.z + Z));    
                Quaternion rot = Quaternion.Euler(0, 0, 0);
                Vector3 newPos = (CalculateGroundPos(CentrePos));
	    //if (zone.Chute)newPos =  newPos + new Vector3(0,Y,0);  //commented awaiting changes in Rust.
	    //if (type == "AirDrop" && zone.Chute) newPos = new Vector3((pos.x + dropX),pos.y,(pos.z + dropZ));
		//NPCPlayer entity = GameManager.server.CreateEntity("assets/prefabs/npc/scientist/scientist.prefab", newPos, rot, true) as NPCPlayer;

                NPCPlayer entity = (NPCPlayer)InstantiateSci(newPos, rot, murd);
                    
                    var botapex = entity.GetComponent<NPCPlayerApex>();
                    botapex.Spawn();

                if (zone.Disable_Radio)
                botapex.GetComponent<FacepunchBehaviour>().CancelInvoke(new Action(botapex.RadioChatter));
                                      
                TempRecord.NPCPlayers.Add(botapex, new botData()
                {
                spawnPoint = newPos,
		//invincible = zone.Invincible_In_Air,
                accuracy = zone.Bot_Accuracy,
                damage = zone.Bot_Damage,
                botID = entity.userID,
                bot = entity,
                monumentName = name,
                });

                int suicInt = rnd.Next((configData.Options.Suicide_Timer), (configData.Options.Suicide_Timer + 10));
                
                if (type == "AirDrop" || type == "Attack")
                {
                TempRecord.dontRespawn.Add(botapex.userID);
                timer.Once(suicInt, () =>
                {
                    if (TempRecord.NPCPlayers.ContainsKey(botapex))
                    {
                        if (botapex != null)
                        {
                        OnEntityDeath(botapex);
                        Effect.server.Run("assets/prefabs/weapons/rocketlauncher/effects/rocket_explosion.prefab", botapex.transform.position);
                        botapex.Kill();
                        }
                        else
                        {
                            TempRecord.dontRespawn.Remove(botapex.userID);
                            TempRecord.NPCPlayers.Remove(botapex);
                            return;
                        }
                    }
                    else return; 
                });
                }
                no_of_AI++;

                if (zone.Kit != "default")
                {
                    object checkKit = (Kits.CallHook("GetKitInfo", zone.Kit, true));
                    if (checkKit == null)
                    {
                        PrintWarning("Kit does not exist - Defaulting to 'Scientist'.");
                        return;
                    }
                    else
                    {
                    entity.inventory.Strip(); 
                    Kits?.Call($"GiveKit", entity, zone.Kit, true);
                    }
                }

                entity.health = zone.BotHealth;
		
                if (zone.BotName == "randomname")
                {
                entity.displayName = Get(entity.userID);
                }
                else
                {
                entity.displayName = zone.BotName;
                }
                SetFiringRange(botapex, zone.Bot_Firing_Range);
	//if (zone.Chute)                   //commented awaiting changes in Rust.
	//{
	//var Chute = GameManager.server.CreateEntity("assets/prefabs/misc/parachute/parachute.prefab", newPos, rot);
	//Chute.gameObject.Identity();
	//Chute.SetParent(botapex, "parachute");
	//Chute.Spawn();
	//float x = Convert.ToSingle(GetRandomNumber(-0.16, 0.16));
	//float z = Convert.ToSingle(GetRandomNumber(-0.16, 0.16));
	//float varySpeed = Convert.ToSingle(GetRandomNumber(0.4, 0.8));
	//Drop(botapex, Chute, zone, x, varySpeed, z);
	//}         

        }

        void SetFiringRange(NPCPlayerApex botapex, int range)
        {
            if (botapex == null)
            {        
                TempRecord.NPCPlayers.Remove(botapex);
                TempRecord.dontRespawn.Remove(botapex.userID);
                return;
            }

            var heldEntity = botapex.GetActiveItem();
            if (botapex.svActiveItemID != 0)
            {
                    List<int> weapons = new List<int>(); //check all their weapons
                    foreach (Item item in botapex.inventory.containerBelt.itemList)
                    {
                        if (item.GetHeldEntity() as BaseProjectile != null || item.GetHeldEntity() as BaseMelee != null || item.GetHeldEntity() as TorchWeapon != null)
                        {
                            weapons.Add(Convert.ToInt16(item.position));
                        }
                    }

                    if (weapons.Count == 0)
                    {
                        Puts("No suitable weapon found in kit.");
                        return;
                    }
                int index = rnd.Next(weapons.Count);
                
                foreach (Item item in botapex.inventory.containerBelt.itemList) //pick one at random
                {
                    
                    if (item.position == weapons[index])
                    {
                    var UID = botapex.inventory.containerBelt.GetSlot(weapons[index]).uid;
                    Item activeItem = item;
                    botapex.svActiveItemID = 0;
                    botapex.inventory.UpdatedVisibleHolsteredItems();
                    HeldEntity held = activeItem.GetHeldEntity() as HeldEntity;
                    botapex.svActiveItemID = UID;
                    botapex.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                    held.SetHeld(true);
                    botapex.svActiveItemID = UID;
                    botapex.inventory.UpdatedVisibleHolsteredItems();
                    }
                }
                
                AttackEntity heldGun = botapex.GetHeldEntity() as AttackEntity;
                if (heldGun != null)
                {
                    var heldMelee = heldGun as BaseMelee;
                    var heldTorchWeapon = heldGun as TorchWeapon;
                    if (heldMelee != null || heldTorchWeapon != null)
                       heldGun.effectiveRange = 1; 
                    else
                        heldGun.effectiveRange = range;
                    return;
                }
		    }
            else
            {
                timer.Once(1, () => SetFiringRange(botapex, range));
            }

        }
	//    void Drop(NPCPlayer bot, BaseEntity Chute, MonumentSettings zone, float x, float varyY, float z)
	//    {
	//	if (bot == null) return;
	//	isInAir = true;
	//	var gnd = (CalculateGroundPos(bot.transform.position));
	//	var botapex = bot.GetComponent<NPCPlayerApex>();
	//	if (bot.transform.position.y > gnd.y)
	//	{
	//	    float logSpeed = ((bot.transform.position.y / 150f) + varyY);
	//	    bot.transform.position = bot.transform.position + new Vector3(x, (-logSpeed), z);
	//	    timer.Once(0.2f, () =>
	//	    {
	//		if (TempRecord.NPCPlayers.ContainsKey(botapex))
	//		Drop(bot, Chute, zone, x, varyY, z);
	//	    });
	//			    
	//	}
	//	else
	//	{
	//	    isInAir = false;
	//	    botapex.Resume();
	//	    bot.RemoveChild(Chute);
	//	    Chute.Kill();
	//	}
	//    }

	void OnEntitySpawned(BaseEntity entity)
	{
        if (entity != null)
        {
            if (entity is DroppedItemContainer)
            {
                NextTick(() =>
                {
                    if (entity == null || entity.IsDestroyed) return;
                    var container = entity as DroppedItemContainer;
                    
                    ulong ownerID = container.playerSteamID;
                    if (ownerID == 0) return;
                    if (configData.Options.Remove_BackPacks)
                    {
                        foreach (var ID in TempRecord.DeadNPCPlayerIds)
                        {
                            if (ID.ToString() == ownerID.ToString())
                            {
                                entity.Kill();
                                TempRecord.DeadNPCPlayerIds.Remove(ownerID);
                                return;
                            }
                        }
                    }

                });
            }
	    
	    
            Vector3 dropLocation = new Vector3(0,0,0);
            if (!(entity.name.Contains("supply_drop")))
            return;
                
            dropLocation = (CalculateGroundPos(entity.transform.position));
            List<BaseEntity> entitiesWithinRadius = new List<BaseEntity>();
            Vis.Entities(dropLocation, 50f, entitiesWithinRadius);
            
            foreach (var BaseEntity in entitiesWithinRadius)
            {
                if (BaseEntity.name.Contains("grenade.smoke.deployed") && !(configData.Options.Supply_Enabled))
                return;
            }
                foreach (var profile in TempRecord.MonumentProfiles)
                {
                    if(profile.Key == "AirDrop" && profile.Value.Activate == true)
                    {
                        timer.Repeat(0f,profile.Value.Bots, () =>
                        {
                        profile.Value.LocationX = entity.transform.position.x;
                        profile.Value.LocationY = entity.transform.position.y;
                        profile.Value.LocationZ = entity.transform.position.z;
                        SpawnSci(profile.Key, profile.Value, "AirDrop");
                        }
                        );
                    }
                }
        }
	}
        
        #region targeting
        
        object OnNpcPlayerTarget(NPCPlayerApex npcPlayer, BaseEntity entity)//stops bots targetting animals
        {
	    //if ((bool)isInAir && configData.Options.Ai_Falling_Disable)
	    //return 0f;
            BasePlayer victim = null;
            if (entity is BasePlayer)
            {
            victim = entity as BasePlayer; 
                    
                if (victim is NPCPlayer) //stop murderers attacking scientists.
                return 0f;
                if (configData.Options.Peace_Keeper)
                {
                if (victim.svActiveItemID == 0u)
                {
                return 0f;
                }
                else
                {
                    var heldWeapon = victim.GetHeldEntity() as BaseProjectile;
                    var heldFlame = victim.GetHeldEntity() as FlameThrower;
                    if (heldWeapon == null && heldFlame == null)
                    return 0f;
                }                   
                }


            if(!victim.userID.IsSteamId() && configData.Options.Ignore_HumanNPC)
            return 0f;
            }
            if (entity.name.Contains("agents/") && configData.Options.Ignore_Animals)
            return 0f;
            else
            return null;

        }
        
        object CanBradleyApcTarget(BradleyAPC bradley, BaseEntity target)//stops bradley targetting bots
        {
            if (target is NPCPlayer && configData.Options.APC_Safe)
            return false;
            return null;
        }
        
        object OnNpcTarget(BaseNpc npc, BaseEntity entity)//stops animals targetting bots
        {
            if (entity is NPCPlayer && configData.Options.Animal_Safe)
            return 0f;
            return null;
        }

        object CanBeTargeted(BaseCombatEntity player, MonoBehaviour turret)//stops autoturrets targetting bots
        {
            if (player is NPCPlayer && configData.Options.Turret_Safe)
            return false;
            return null;
        }
        
        #endregion
        void AttackPlayer(BasePlayer player, string name, MonumentSettings profile)
        {
        Vector3 location = (CalculateGroundPos(player.transform.position));
    
            timer.Repeat(0f,profile.Bots, () =>
            {
            profile.LocationX = location.x;
            profile.LocationY = location.y;
            profile.LocationZ = location.z;
            SpawnSci(name, profile, "Attack");
            }
            );
        }
    
        static BasePlayer FindPlayerByName(string name)
        {
            BasePlayer result = null;
            foreach (BasePlayer current in BasePlayer.activePlayerList)
            {
            if (current.displayName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                BasePlayer result2 = current;
                return result2;
            }
            if (current.UserIDString.Contains(name, CompareOptions.OrdinalIgnoreCase))
            {
                BasePlayer result2 = current;
                return result2;
            }
            if (current.displayName.Contains(name, CompareOptions.OrdinalIgnoreCase))
            {
                result = current;
            }
            }
            return result;
        }

        static Vector3 CalculateGroundPos(Vector3 sourcePos) // credit Wulf & Nogrod 
        {
            RaycastHit hitInfo;

            if (UnityEngine.Physics.Raycast(sourcePos, Vector3.down, out hitInfo, 800f, LayerMask.GetMask("Terrain", "World", "Construction"), QueryTriggerInteraction.Ignore))
            {
                sourcePos.y = hitInfo.point.y;
            }
            sourcePos.y = Mathf.Max(sourcePos.y, TerrainMeta.HeightMap.GetHeight(sourcePos));
            return sourcePos;
        } 
  
        private void FindMonuments() // credit K1lly0u 
        {
            
            TempRecord.MonumentProfiles.Clear();
            var allobjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
            int warehouse = 0;
            int lighthouse = 0;
            int gasstation = 0;
            int spermket = 0;
            foreach (var gobject in allobjects)
            {
                if (gobject.name.Contains("autospawn/monument")) 
                {
                    var pos = gobject.transform.position;
                    if (gobject.name.Contains("powerplant_1"))
                    {
                    TempRecord.MonumentProfiles.Add("PowerPlant", new MonumentSettings
                    {
                        Activate = configData.Zones.Powerplant.Activate,
                        Murderer = configData.Zones.Powerplant.Murderer,
                        Bots = configData.Zones.Powerplant.Bots,
                        BotHealth = configData.Zones.Powerplant.BotHealth,
                        Radius = configData.Zones.Powerplant.Radius,
                        Kit = configData.Zones.Powerplant.Kit,
                        BotName = configData.Zones.Powerplant.BotName,
                        //Chute = configData.Zones.Powerplant.Chute,
                        Bot_Firing_Range = configData.Zones.Powerplant.Bot_Firing_Range,
                        Bot_Accuracy = configData.Zones.Powerplant.Bot_Accuracy,
                        Bot_Damage = configData.Zones.Powerplant.Bot_Damage,
                        Disable_Radio = configData.Zones.Powerplant.Disable_Radio,
                        //Invincible_In_Air = configData.Zones.Powerplant.Invincible_In_Air,
                        Spawn_Height = configData.Zones.Powerplant.Spawn_Height,
                        Respawn_Timer = configData.Zones.Powerplant.Respawn_Timer,
                        LocationX = pos.x,
                        LocationY = pos.y,
                        LocationZ = pos.z,
                    });             
                    continue;
                    }
 
                    if (gobject.name.Contains("airfield_1"))
                    {
                    TempRecord.MonumentProfiles.Add("Airfield", new MonumentSettings
                    {
                        Activate = configData.Zones.Airfield.Activate,
                        Murderer = configData.Zones.Airfield.Murderer,
                        Bots = configData.Zones.Airfield.Bots,
                        BotHealth = configData.Zones.Airfield.BotHealth,
                        Radius = configData.Zones.Airfield.Radius,
                        Kit = configData.Zones.Airfield.Kit,
                        BotName = configData.Zones.Airfield.BotName,
                        //Chute = configData.Zones.Airfield.Chute,
                        Bot_Firing_Range = configData.Zones.Airfield.Bot_Firing_Range,
                        Bot_Accuracy = configData.Zones.Airfield.Bot_Accuracy,
                        Bot_Damage = configData.Zones.Airfield.Bot_Damage,
                        Disable_Radio = configData.Zones.Airfield.Disable_Radio,
                        //Invincible_In_Air = configData.Zones.Airfield.Invincible_In_Air,
                        Spawn_Height = configData.Zones.Airfield.Spawn_Height,
                        Respawn_Timer = configData.Zones.Airfield.Respawn_Timer,
                        LocationX = pos.x,
                        LocationY = pos.y,
                        LocationZ = pos.z,
                    });    
                    continue;
                    }

                    if (gobject.name.Contains("trainyard_1"))
                    {
                    TempRecord.MonumentProfiles.Add("Trainyard", new MonumentSettings
                    {
                        Activate = configData.Zones.Trainyard.Activate,
                        Murderer = configData.Zones.Trainyard.Murderer,
                        Bots = configData.Zones.Trainyard.Bots,
                        BotHealth = configData.Zones.Trainyard.BotHealth,
                        Radius = configData.Zones.Trainyard.Radius,
                        Kit = configData.Zones.Trainyard.Kit,
                        BotName = configData.Zones.Trainyard.BotName,
                        //Chute = configData.Zones.Trainyard.Chute,
                        Bot_Firing_Range = configData.Zones.Trainyard.Bot_Firing_Range,
                        Bot_Accuracy = configData.Zones.Trainyard.Bot_Accuracy,
                        Bot_Damage = configData.Zones.Trainyard.Bot_Damage,
                        Disable_Radio = configData.Zones.Trainyard.Disable_Radio,
                        //Invincible_In_Air = configData.Zones.Trainyard.Invincible_In_Air,
                        Spawn_Height = configData.Zones.Trainyard.Spawn_Height,
                        Respawn_Timer = configData.Zones.Trainyard.Respawn_Timer,
                        LocationX = pos.x,
                        LocationY = pos.y,
                        LocationZ = pos.z,
                    });              
                    continue;
                    }

                    if (gobject.name.Contains("water_treatment_plant_1")) 
                    {
                    TempRecord.MonumentProfiles.Add("Watertreatment", new MonumentSettings
                    {
                        Activate = configData.Zones.Watertreatment.Activate,
                        Murderer = configData.Zones.Watertreatment.Murderer,
                        Bots = configData.Zones.Watertreatment.Bots,
                        BotHealth = configData.Zones.Watertreatment.BotHealth,
                        Radius = configData.Zones.Watertreatment.Radius,
                        Kit = configData.Zones.Watertreatment.Kit,
                        BotName = configData.Zones.Watertreatment.BotName,
                        //Chute = configData.Zones.Watertreatment.Chute,
                        Bot_Firing_Range = configData.Zones.Watertreatment.Bot_Firing_Range,
                        Bot_Accuracy = configData.Zones.Watertreatment.Bot_Accuracy,
                        Bot_Damage = configData.Zones.Watertreatment.Bot_Damage,
                        Disable_Radio = configData.Zones.Watertreatment.Disable_Radio,
                        //Invincible_In_Air = configData.Zones.Watertreatment.Invincible_In_Air,
                        Spawn_Height = configData.Zones.Watertreatment.Spawn_Height,
                        Respawn_Timer = configData.Zones.Watertreatment.Respawn_Timer,
                        LocationX = pos.x,
                        LocationY = pos.y,
                        LocationZ = pos.z,
                    });     
                    continue;
                    }

                    if (gobject.name.Contains("satellite_dish")) 
                    {
                    TempRecord.MonumentProfiles.Add("Satellite", new MonumentSettings
                    {
                        Activate = configData.Zones.Satellite.Activate,
                        Murderer = configData.Zones.Satellite.Murderer,
                        Bots = configData.Zones.Satellite.Bots,
                        BotHealth = configData.Zones.Satellite.BotHealth,
                        Radius = configData.Zones.Satellite.Radius,
                        Kit = configData.Zones.Satellite.Kit,
                        BotName = configData.Zones.Satellite.BotName,
                        //Chute = configData.Zones.Satellite.Chute,
                        Bot_Firing_Range = configData.Zones.Satellite.Bot_Firing_Range,
                        Bot_Accuracy = configData.Zones.Satellite.Bot_Accuracy,
                        Bot_Damage = configData.Zones.Satellite.Bot_Damage,  
                        Disable_Radio = configData.Zones.AirDrop.Disable_Radio,
                        //Invincible_In_Air = configData.Zones.AirDrop.Invincible_In_Air,
                        Spawn_Height = configData.Zones.Satellite.Spawn_Height,
                        Respawn_Timer = configData.Zones.Satellite.Respawn_Timer,
                        LocationX = pos.x,
                        LocationY = pos.y,
                        LocationZ = pos.z,
                    });   
                    continue;
                    } 

                    if (gobject.name.Contains("sphere_tank"))
                    {
                    TempRecord.MonumentProfiles.Add("Dome", new MonumentSettings
                    {
                        Activate = configData.Zones.Dome.Activate,
                        Murderer = configData.Zones.Dome.Murderer,
                        Bots = configData.Zones.Dome.Bots,
                        BotHealth = configData.Zones.Dome.BotHealth,
                        Radius = configData.Zones.Dome.Radius,
                        Kit = configData.Zones.Dome.Kit,
                        BotName = configData.Zones.Dome.BotName,
                        //Chute = configData.Zones.Dome.Chute,
                        Bot_Firing_Range = configData.Zones.Dome.Bot_Firing_Range,
                        Bot_Accuracy = configData.Zones.Dome.Bot_Accuracy,
                        Bot_Damage = configData.Zones.Dome.Bot_Damage,
                        Disable_Radio = configData.Zones.Dome.Disable_Radio,
                        //Invincible_In_Air = configData.Zones.Dome.Invincible_In_Air,
                        Spawn_Height = configData.Zones.Dome.Spawn_Height,
                        Respawn_Timer = configData.Zones.Dome.Respawn_Timer,
                        LocationX = pos.x,
                        LocationY = pos.y,
                        LocationZ = pos.z,
                    }); 
                    continue;
                    }

                    if (gobject.name.Contains("radtown_small_3"))
                    {
                    TempRecord.MonumentProfiles.Add("Radtown", new MonumentSettings
                    {
                        Activate = configData.Zones.Radtown.Activate,
                        Murderer = configData.Zones.Radtown.Murderer,
                        Bots = configData.Zones.Radtown.Bots,
                        BotHealth = configData.Zones.Radtown.BotHealth,
                        Radius = configData.Zones.Radtown.Radius,
                        Kit = configData.Zones.Radtown.Kit,
                        BotName = configData.Zones.Radtown.BotName,
                        //Chute = configData.Zones.Radtown.Chute,
                        Bot_Firing_Range = configData.Zones.Radtown.Bot_Firing_Range,
                        Bot_Accuracy = configData.Zones.Radtown.Bot_Accuracy,
                        Bot_Damage = configData.Zones.Radtown.Bot_Damage,
                        Disable_Radio = configData.Zones.Radtown.Disable_Radio,
                        //Invincible_In_Air = configData.Zones.Radtown.Invincible_In_Air,
                        Spawn_Height = configData.Zones.Radtown.Spawn_Height,
                        Respawn_Timer = configData.Zones.Radtown.Respawn_Timer,
                        LocationX = pos.x,
                        LocationY = pos.y,
                        LocationZ = pos.z,
                    });      
                    continue;
                    }
                    
                    if (gobject.name.Contains("launch_site"))
                    {
                    TempRecord.MonumentProfiles.Add("Launchsite", new MonumentSettings
                    {
                        Activate = configData.Zones.Launchsite.Activate,
                        Murderer = configData.Zones.Launchsite.Murderer,
                        Bots = configData.Zones.Launchsite.Bots,
                        BotHealth = configData.Zones.Launchsite.BotHealth,
                        Radius = configData.Zones.Launchsite.Radius,
                        Kit = configData.Zones.Launchsite.Kit,
                        BotName = configData.Zones.Launchsite.BotName,
                        //Chute = configData.Zones.Launchsite.Chute,
                        Bot_Firing_Range = configData.Zones.Launchsite.Bot_Firing_Range,
                        Bot_Accuracy = configData.Zones.Launchsite.Bot_Accuracy,
                        Bot_Damage = configData.Zones.Launchsite.Bot_Damage,
                        Disable_Radio = configData.Zones.Launchsite.Disable_Radio,
                        //Invincible_In_Air = configData.Zones.Launchsite.Invincible_In_Air,
                        Spawn_Height = configData.Zones.Launchsite.Spawn_Height,
                        Respawn_Timer = configData.Zones.Launchsite.Respawn_Timer,
                        LocationX = pos.x,
                        LocationY = pos.y,
                        LocationZ = pos.z,
                    }); 
                    continue;
                    }

                    if (gobject.name.Contains("military_tunnel_1"))
                    {
                    TempRecord.MonumentProfiles.Add("MilitaryTunnel", new MonumentSettings
                    {
                        Activate = configData.Zones.MilitaryTunnel.Activate,
                        Murderer = configData.Zones.MilitaryTunnel.Murderer,
                        Bots = configData.Zones.MilitaryTunnel.Bots,
                        BotHealth = configData.Zones.MilitaryTunnel.BotHealth,
                        Radius = configData.Zones.MilitaryTunnel.Radius,
                        Kit = configData.Zones.MilitaryTunnel.Kit,
                        BotName = configData.Zones.MilitaryTunnel.BotName,
                        //Chute = configData.Zones.MilitaryTunnel.Chute,
                        Bot_Firing_Range = configData.Zones.MilitaryTunnel.Bot_Firing_Range,
                        Bot_Accuracy = configData.Zones.MilitaryTunnel.Bot_Accuracy,
                        Bot_Damage = configData.Zones.MilitaryTunnel.Bot_Damage,  
                        Disable_Radio = configData.Zones.MilitaryTunnel.Disable_Radio,
                        //Invincible_In_Air = configData.Zones.MilitaryTunnel.Invincible_In_Air,
                        Spawn_Height = configData.Zones.MilitaryTunnel.Spawn_Height,
                        Respawn_Timer = configData.Zones.MilitaryTunnel.Respawn_Timer,
                        LocationX = pos.x,
                        LocationY = pos.y,
                        LocationZ = pos.z,
                    }); 
                    continue;
                    }

                    if (gobject.name.Contains("harbor_1"))
                    { 
                    TempRecord.MonumentProfiles.Add("Harbor1", new MonumentSettings
                    {
                        Activate = configData.Zones.Harbor1.Activate,
                        Murderer = configData.Zones.Harbor1.Murderer,
                        Bots = configData.Zones.Harbor1.Bots,
                        BotHealth = configData.Zones.Harbor1.BotHealth,
                        Radius = configData.Zones.Harbor1.Radius,
                        Kit = configData.Zones.Harbor1.Kit,
                        BotName = configData.Zones.Harbor1.BotName,
                        //Chute = configData.Zones.Harbor1.Chute,
                        Bot_Firing_Range = configData.Zones.Harbor1.Bot_Firing_Range,
                        Bot_Accuracy = configData.Zones.Harbor1.Bot_Accuracy,
                        Bot_Damage = configData.Zones.Harbor1.Bot_Damage,  
                        Disable_Radio = configData.Zones.Harbor1.Disable_Radio,
                        //Invincible_In_Air = configData.Zones.Harbor1.Invincible_In_Air,
                        Spawn_Height = configData.Zones.Harbor1.Spawn_Height,
                        Respawn_Timer = configData.Zones.Harbor1.Respawn_Timer,
                        LocationX = pos.x,
                        LocationY = pos.y,
                        LocationZ = pos.z,
                    });     
                    continue;
                    }

                    if (gobject.name.Contains("harbor_2"))
                    {
                    TempRecord.MonumentProfiles.Add("Harbor2", new MonumentSettings
                    {
                        Activate = configData.Zones.Harbor2.Activate,
                        Murderer = configData.Zones.Harbor2.Murderer,
                        Bots = configData.Zones.Harbor2.Bots,
                        BotHealth = configData.Zones.Harbor2.BotHealth,
                        Radius = configData.Zones.Harbor2.Radius,
                        Kit = configData.Zones.Harbor2.Kit,
                        BotName = configData.Zones.Harbor2.BotName,
                        //Chute = configData.Zones.Harbor2.Chute,
                        Bot_Firing_Range = configData.Zones.Harbor2.Bot_Firing_Range,
                        Bot_Accuracy = configData.Zones.Harbor2.Bot_Accuracy,
                        Bot_Damage = configData.Zones.Harbor2.Bot_Damage,  
                        Disable_Radio = configData.Zones.Harbor2.Disable_Radio,
                        //Invincible_In_Air = configData.Zones.Harbor2.Invincible_In_Air,
                        Spawn_Height = configData.Zones.Harbor2.Spawn_Height,
                        Respawn_Timer = configData.Zones.Harbor2.Respawn_Timer,
                        LocationX = pos.x,
                        LocationY = pos.y,
                        LocationZ = pos.z,
                    });             
                    continue;
                    }
                    
                    if (gobject.name.Contains("gas_station_1") && gasstation == 0)
                    {
                    TempRecord.MonumentProfiles.Add("GasStation", new MonumentSettings
                    {
                        Activate = configData.Zones.GasStation.Activate,
                        Murderer = configData.Zones.GasStation.Murderer,
                        Bots = configData.Zones.GasStation.Bots,
                        BotHealth = configData.Zones.GasStation.BotHealth,
                        Radius = configData.Zones.GasStation.Radius,
                        Kit = configData.Zones.GasStation.Kit,
                        BotName = configData.Zones.GasStation.BotName,
                        //Chute = configData.Zones.GasStation.Chute,
                        Bot_Firing_Range = configData.Zones.GasStation.Bot_Firing_Range,
                        Bot_Accuracy = configData.Zones.GasStation.Bot_Accuracy,
                        Bot_Damage = configData.Zones.GasStation.Bot_Damage,  
                        Disable_Radio = configData.Zones.GasStation.Disable_Radio,
                        //Invincible_In_Air = configData.Zones.GasStation.Invincible_In_Air,
                        Spawn_Height = configData.Zones.GasStation.Spawn_Height,
                        Respawn_Timer = configData.Zones.GasStation.Respawn_Timer,
                        LocationX = pos.x,
                        LocationY = pos.y,
                        LocationZ = pos.z,
                    });
                    gasstation++;
                    continue;
                    }
              
                    if (gobject.name.Contains("gas_station_1") && gasstation == 1)
                    {
                    TempRecord.MonumentProfiles.Add("GasStation1", new MonumentSettings
                    {
                        Activate = configData.Zones.GasStation1.Activate,
                        Murderer = configData.Zones.GasStation1.Murderer,
                        Bots = configData.Zones.GasStation1.Bots,
                        BotHealth = configData.Zones.GasStation1.BotHealth,
                        Radius = configData.Zones.GasStation1.Radius,
                        Kit = configData.Zones.GasStation1.Kit,
                        BotName = configData.Zones.GasStation1.BotName,
                        //Chute = configData.Zones.GasStation1.Chute,
                        Bot_Firing_Range = configData.Zones.GasStation1.Bot_Firing_Range,
                        Bot_Accuracy = configData.Zones.GasStation1.Bot_Accuracy,
                        Bot_Damage = configData.Zones.GasStation1.Bot_Damage,  
                        Disable_Radio = configData.Zones.GasStation1.Disable_Radio,
                        //Invincible_In_Air = configData.Zones.GasStation1.Invincible_In_Air,
                        Spawn_Height = configData.Zones.GasStation1.Spawn_Height,
                        Respawn_Timer = configData.Zones.GasStation1.Respawn_Timer,
                        LocationX = pos.x,
                        LocationY = pos.y,
                        LocationZ = pos.z,
                    });
                    gasstation++;
                    continue;
                    }
                    
                    if (gobject.name.Contains("supermarket_1") && spermket == 0)
                    {
                    TempRecord.MonumentProfiles.Add("SuperMarket", new MonumentSettings
                    {
                        Activate = configData.Zones.SuperMarket.Activate,
                        Murderer = configData.Zones.SuperMarket.Murderer,
                        Bots = configData.Zones.SuperMarket.Bots,
                        BotHealth = configData.Zones.SuperMarket.BotHealth,
                        Radius = configData.Zones.SuperMarket.Radius,
                        Kit = configData.Zones.SuperMarket.Kit,
                        BotName = configData.Zones.SuperMarket.BotName,
                        //Chute = configData.Zones.SuperMarket.Chute,
                        Bot_Firing_Range = configData.Zones.SuperMarket.Bot_Firing_Range,
                        Bot_Accuracy = configData.Zones.SuperMarket.Bot_Accuracy,
                        Bot_Damage = configData.Zones.SuperMarket.Bot_Damage,  
                        Disable_Radio = configData.Zones.SuperMarket.Disable_Radio,
                        //Invincible_In_Air = configData.Zones.SuperMarket.Invincible_In_Air,
                        Spawn_Height = configData.Zones.SuperMarket.Spawn_Height,
                        Respawn_Timer = configData.Zones.SuperMarket.Respawn_Timer,
                        LocationX = pos.x,
                        LocationY = pos.y,
                        LocationZ = pos.z,
                    });
                    spermket++;
                    continue;
                    }
                    
                    if (gobject.name.Contains("supermarket_1") && spermket == 1)
                    {
                    TempRecord.MonumentProfiles.Add("SuperMarket1", new MonumentSettings
                    {
                        Activate = configData.Zones.SuperMarket1.Activate,
                        Murderer = configData.Zones.SuperMarket1.Murderer,
                        Bots = configData.Zones.SuperMarket1.Bots,
                        BotHealth = configData.Zones.SuperMarket1.BotHealth,
                        Radius = configData.Zones.SuperMarket1.Radius,
                        Kit = configData.Zones.SuperMarket1.Kit,
                        BotName = configData.Zones.SuperMarket1.BotName,
                        //Chute = configData.Zones.SuperMarket1.Chute,
                        Bot_Firing_Range = configData.Zones.SuperMarket1.Bot_Firing_Range,
                        Bot_Accuracy = configData.Zones.SuperMarket1.Bot_Accuracy,
                        Bot_Damage = configData.Zones.SuperMarket1.Bot_Damage,  
                        Disable_Radio = configData.Zones.SuperMarket1.Disable_Radio,
                        //Invincible_In_Air = configData.Zones.SuperMarket1.Invincible_In_Air,
                        Spawn_Height = configData.Zones.SuperMarket1.Spawn_Height,
                        Respawn_Timer = configData.Zones.SuperMarket1.Respawn_Timer,
                        LocationX = pos.x,
                        LocationY = pos.y,
                        LocationZ = pos.z,
                    });
                    spermket++;
                    continue;
                    }
                    
                    if (gobject.name.Contains("lighthouse") && lighthouse == 0)
                    {
                    TempRecord.MonumentProfiles.Add("Lighthouse", new MonumentSettings
                    {
                        Activate = configData.Zones.Lighthouse.Activate,
                        Murderer = configData.Zones.Lighthouse.Murderer,
                        Bots = configData.Zones.Lighthouse.Bots,
                        BotHealth = configData.Zones.Lighthouse.BotHealth,
                        Radius = configData.Zones.Lighthouse.Radius,
                        Kit = configData.Zones.Lighthouse.Kit,
                        BotName = configData.Zones.Lighthouse.BotName,
                        //Chute = configData.Zones.Lighthouse.Chute,
                        Bot_Firing_Range = configData.Zones.Lighthouse.Bot_Firing_Range,
                        Bot_Accuracy = configData.Zones.Lighthouse.Bot_Accuracy,
                        Bot_Damage = configData.Zones.Lighthouse.Bot_Damage,
                        Disable_Radio = configData.Zones.Lighthouse.Disable_Radio,
                        //Invincible_In_Air = configData.Zones.Lighthouse.Invincible_In_Air,
                        Spawn_Height = configData.Zones.Lighthouse.Spawn_Height,
                        Respawn_Timer = configData.Zones.Lighthouse.Respawn_Timer,
                        LocationX = pos.x,
                        LocationY = pos.y,
                        LocationZ = pos.z,
                    });             
                    lighthouse++;
                    continue;
                    }
    
                    if (gobject.name.Contains("lighthouse") && lighthouse == 1)
                    {                        
                    TempRecord.MonumentProfiles.Add("Lighthouse1", new MonumentSettings
                    {
                        Activate = configData.Zones.Lighthouse1.Activate,
                        Murderer = configData.Zones.Lighthouse1.Murderer,
                        Bots = configData.Zones.Lighthouse1.Bots,
                        BotHealth = configData.Zones.Lighthouse1.BotHealth,
                        Radius = configData.Zones.Lighthouse1.Radius,
                        Kit = configData.Zones.Lighthouse1.Kit,
                        BotName = configData.Zones.Lighthouse1.BotName,
                        //Chute = configData.Zones.Lighthouse1.Chute,
                        Bot_Firing_Range = configData.Zones.Lighthouse1.Bot_Firing_Range,
                        Bot_Accuracy = configData.Zones.Lighthouse1.Bot_Accuracy,
                        Bot_Damage = configData.Zones.Lighthouse1.Bot_Damage,
                        Disable_Radio = configData.Zones.Lighthouse1.Disable_Radio,
                        //Invincible_In_Air = configData.Zones.Lighthouse1.Invincible_In_Air,
                        Spawn_Height = configData.Zones.Lighthouse1.Spawn_Height,
                        Respawn_Timer = configData.Zones.Lighthouse1.Respawn_Timer,
                        LocationX = pos.x,
                        LocationY = pos.y,
                        LocationZ = pos.z,
                    });     
                    lighthouse++;
                    continue;
                    }
                    
                    if (gobject.name.Contains("lighthouse") && lighthouse == 2)
                    {
                    TempRecord.MonumentProfiles.Add("Lighthouse2", new MonumentSettings
                    {
                        Activate = configData.Zones.Lighthouse2.Activate,
                        Murderer = configData.Zones.Lighthouse2.Murderer,
                        Bots = configData.Zones.Lighthouse2.Bots,
                        BotHealth = configData.Zones.Lighthouse2.BotHealth,
                        Radius = configData.Zones.Lighthouse2.Radius,
                        Kit = configData.Zones.Lighthouse2.Kit,
                        BotName = configData.Zones.Lighthouse2.BotName,
                        //Chute = configData.Zones.Lighthouse2.Chute,
                        Bot_Firing_Range = configData.Zones.Lighthouse2.Bot_Firing_Range,
                        Bot_Accuracy = configData.Zones.Lighthouse2.Bot_Accuracy,
                        Bot_Damage = configData.Zones.Lighthouse2.Bot_Damage,
                        Disable_Radio = configData.Zones.Lighthouse2.Disable_Radio,
                        //Invincible_In_Air = configData.Zones.Lighthouse2.Invincible_In_Air,
                        Spawn_Height = configData.Zones.Lighthouse2.Spawn_Height,
                        Respawn_Timer = configData.Zones.Lighthouse2.Respawn_Timer,
                        LocationX = pos.x,
                        LocationY = pos.y,
                        LocationZ = pos.z,
                    });     
                    lighthouse++;
                    continue;
                    }

                    if (gobject.name.Contains("warehouse") && warehouse == 0)
                    {
                    TempRecord.MonumentProfiles.Add("Warehouse", new MonumentSettings
                    {
                        Activate = configData.Zon