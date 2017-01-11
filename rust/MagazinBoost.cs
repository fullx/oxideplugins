using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;
using ProtoBuf;
using Network;
using System.Net;
using Facepunch.Steamworks;
using Rust;
using Facepunch;

namespace Oxide.Plugins
{
    [Info("MagazinBoost", "Fujikura", "1.5.1", ResourceId = 1962)]
    [Description("Can change magazines, ammo and conditon for most projectile weapons")]
    public class MagazinBoost : RustPlugin
    {	
		bool Changed;
		
		Dictionary <string, object> weaponContainer = new Dictionary <string, object>();
		
		FieldInfo _itemCondition = typeof(Item).GetField("_condition", (BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic));
		FieldInfo _itemMaxCondition = typeof(Item).GetField("_maxCondition", (BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic));

		FieldInfo _guidToPath = typeof(GameManifest).GetField("guidToPath", (BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic));
		Dictionary<string, string> guidToPathCopy = new Dictionary<string, string>();
		
		#region Config
		
		string permissionAll;
		string permissionMaxAmmo;
		string permissionPreLoad;
		string permissionMaxCondition;
		string permissionAmmoType;
		bool checkPermission;
		bool removeSkinIfNoRights;		
		
		object GetConfig(string menu, string datavalue, object defaultValue)
        {
            var data = Config[menu] as Dictionary<string, object>;
            if (data == null)
            {
                data = new Dictionary<string, object>();
                Config[menu] = data;
                Changed = true;
            }
            object value;
            if (!data.TryGetValue(datavalue, out value))
            {
                value = defaultValue;
                data[datavalue] = value;
                Changed = true;
            }
            return value;
        }
		
		void LoadVariables()
        {
			permissionAll = Convert.ToString(GetConfig("Permissions", "permissionAll", "magazinboost.canall"));
			permissionMaxAmmo = Convert.ToString(GetConfig("Permissions", "permissionMaxAmmo", "magazinboost.canmaxammo"));
			permissionPreLoad = Convert.ToString(GetConfig("Permissions", "permissionPreLoad", "magazinboost.canpreload"));
			permissionMaxCondition = Convert.ToString(GetConfig("Permissions", "permissionMaxCondition", "magazinboost.canmaxcondition"));
			permissionAmmoType = Convert.ToString(GetConfig("Permissions", "permissionAmmoType", "magazinboost.canammotype"));
			checkPermission = Convert.ToBoolean(GetConfig("CheckRights", "checkForRightsInBelt", true));
			removeSkinIfNoRights = Convert.ToBoolean(GetConfig("CheckRights", "removeSkinIfNoRights", true));
			weaponContainer = (Dictionary<string, object>)GetConfig("Weapons", "Data", new Dictionary<string, object>());
            
			if (!Changed) return;
            SaveConfig();
            Changed = false;
        }
		
        protected override void LoadDefaultConfig()
        {
            Config.Clear();
            LoadVariables();
        }
		
		#endregion Config
				
		void GetWeapons()
		{
			weaponContainer = (Dictionary <string, object>)Config["Weapons", "Data"];
			var weapons = ItemManager.GetItemDefinitions().Where(p => p.category == ItemCategory.Weapon && p.GetComponent<ItemModEntity>() != null);
		
			if (weaponContainer != null && weaponContainer.Count() > 0)
			{
				int countLoadedServerStats = 0;
				foreach (var weapon in weapons)
				{
					if (!guidToPathCopy.ContainsKey(weapon.GetComponent<ItemModEntity>().entityPrefab.guid)) continue;
					if (weapon.GetComponent<ItemModEntity>().entityPrefab.Get().GetComponent<BaseProjectile>() == null) continue;
					
					if (weaponContainer.ContainsKey(weapon.shortname)) 
					{
						if (!guidToPathCopy.ContainsKey(weapon.GetComponent<ItemModEntity>().entityPrefab.guid)) continue;
						Dictionary <string, object> serverDefaults = weaponContainer[weapon.shortname] as Dictionary <string, object>;
						if ((bool)serverDefaults["serveractive"])
						{
							ItemDefinition weaponDef = ItemManager.FindItemDefinition(weapon.shortname);
							weaponDef.GetComponent<ItemModEntity>().entityPrefab.Get().GetComponent<BaseProjectile>().primaryMagazine.definition.builtInSize = (int)serverDefaults["servermaxammo"];
							weaponDef.GetComponent<ItemModEntity>().entityPrefab.Get().GetComponent<BaseProjectile>().primaryMagazine.contents = (int)serverDefaults["serverpreload"];
							ItemDefinition ammo = ItemManager.FindItemDefinition((string)serverDefaults["serverammotype"]);
							if (ammo != null)
								weaponDef.GetComponent<ItemModEntity>().entityPrefab.Get().GetComponent<BaseProjectile>().primaryMagazine.ammoType = ammo;
							weaponDef.condition.max = Convert.ToSingle(serverDefaults["servermaxcondition"]);
							countLoadedServerStats++;
						}
						continue;
					}
					Dictionary <string, object> weaponStats = new Dictionary <string, object>();
					weaponStats.Add("displayname", weapon.displayName.english);
					weaponStats.Add("maxammo", weapon.GetComponent<ItemModEntity>().entityPrefab.Get().GetComponent<BaseProjectile>().primaryMagazine.definition.builtInSize);
					weaponStats.Add("preload", weapon.GetComponent<ItemModEntity>().entityPrefab.Get().GetComponent<BaseProjectile>().primaryMagazine.contents);
					weaponStats.Add("maxcondition", weapon.condition.max);
					weaponStats.Add("ammotype", weapon.GetComponent<ItemModEntity>().entityPrefab.Get().GetComponent<BaseProjectile>().primaryMagazine.ammoType.shortname);
					weaponStats.Add("skinid", 0);
					weaponStats.Add("settingactive", true);
					weaponStats.Add("servermaxammo", weapon.GetComponent<ItemModEntity>().entityPrefab.Get().GetComponent<BaseProjectile>().primaryMagazine.definition.builtInSize);
					weaponStats.Add("serverpreload", weapon.GetComponent<ItemModEntity>().entityPrefab.Get().GetComponent<BaseProjectile>().primaryMagazine.contents);
					weaponStats.Add("serverammotype", weapon.GetComponent<ItemModEntity>().entityPrefab.Get().GetComponent<BaseProjectile>().primaryMagazine.ammoType.shortname);
					weaponStats.Add("servermaxcondition", weapon.condition.max);
					weaponStats.Add("serveractive", false);				
					weaponContainer.Add(weapon.shortname, weaponStats);
					Puts($"Added NEW weapon '{weapon.displayName.english} ({weapon.shortname})' to weapons list");
				}
				if (countLoadedServerStats > 0)
					Puts($"Changed server default values for '{countLoadedServerStats}' weapons");
				Config["Weapons", "Data"] = weaponContainer;
				Config.Save();
				return;
			}
			else
			{
				int counter = 0;
				foreach (var weapon in weapons)
				{
					if (!guidToPathCopy.ContainsKey(weapon.GetComponent<ItemModEntity>().entityPrefab.guid)) continue;
					if (weapon.GetComponent<ItemModEntity>().entityPrefab.Get().GetComponent<BaseProjectile>() == null) continue;
					
					Dictionary <string, object> weaponStats = new Dictionary <string, object>();
					weaponStats.Add("displayname", weapon.displayName.english);
					weaponStats.Add("maxammo", weapon.GetComponent<ItemModEntity>().entityPrefab.Get().GetComponent<BaseProjectile>().primaryMagazine.definition.builtInSize);
					weaponStats.Add("preload", weapon.GetComponent<ItemModEntity>().entityPrefab.Get().GetComponent<BaseProjectile>().primaryMagazine.contents);
					weaponStats.Add("maxcondition", weapon.condition.max);
					weaponStats.Add("ammotype", weapon.GetComponent<ItemModEntity>().entityPrefab.Get().GetComponent<BaseProjectile>().primaryMagazine.ammoType.shortname);
					weaponStats.Add("skinid", 0);
					weaponStats.Add("settingactive", true);
					weaponStats.Add("servermaxammo", weapon.GetComponent<ItemModEntity>().entityPrefab.Get().GetComponent<BaseProjectile>().primaryMagazine.definition.builtInSize);
					weaponStats.Add("serverpreload", weapon.GetComponent<ItemModEntity>().entityPrefab.Get().GetComponent<BaseProjectile>().primaryMagazine.contents);
					weaponStats.Add("serverammotype", weapon.GetComponent<ItemModEntity>().entityPrefab.Get().GetComponent<BaseProjectile>().primaryMagazine.ammoType.shortname);
					weaponStats.Add("servermaxcondition", weapon.condition.max);
					weaponStats.Add("serveractive", false);				
					weaponContainer.Add(weapon.shortname, weaponStats);
					counter++;
				}
				Puts($"Created initial weaponlist with '{counter}' projectile weapons.");
				Config["Weapons", "Data"] = weaponContainer;
				Config.Save();
				return;
			}
		}

		bool hasAnyRight(BasePlayer player)
		{
			if (permission.UserHasPermission(player.UserIDString, permissionAll)) return true;
			if (permission.UserHasPermission(player.UserIDString, permissionMaxAmmo)) return true;
			if (permission.UserHasPermission(player.UserIDString, permissionPreLoad)) return true;
			if (permission.UserHasPermission(player.UserIDString, permissionMaxCondition)) return true;
			if (permission.UserHasPermission(player.UserIDString, permissionAmmoType)) return true;
			return false;
		}
		
		bool hasRight(BasePlayer player, string perm)
		{
			bool right = false;
			switch (perm)
			{
				case "all":
						if (permission.UserHasPermission(player.UserIDString, permissionAll)) {right = true;}
						break;
				case "maxammo":
						if (permission.UserHasPermission(player.UserIDString, permissionMaxAmmo)) {right = true;}
						break;
				case "preload":
						if (permission.UserHasPermission(player.UserIDString, permissionPreLoad)) {right = true;}
						break;
				case "maxcondition":
						if (permission.UserHasPermission(player.UserIDString, permissionMaxCondition)) {right = true;}
						break;
				case "ammotype":
						if (permission.UserHasPermission(player.UserIDString, permissionAmmoType)) {right = true;}
						break;
				default:
						break;
				
			}
			return right;
		}

		void OnServerInitialized()
        {
			LoadVariables();
			guidToPathCopy = (Dictionary<string, string>)_guidToPath.GetValue(GameManifest.Get());
			GetWeapons();
			if (!permission.PermissionExists(permissionAll)) permission.RegisterPermission(permissionAll, this);
			if (!permission.PermissionExists(permissionMaxAmmo)) permission.RegisterPermission(permissionMaxAmmo, this);
			if (!permission.PermissionExists(permissionPreLoad)) permission.RegisterPermission(permissionPreLoad, this);
			if (!permission.PermissionExists(permissionMaxCondition)) permission.RegisterPermission(permissionMaxCondition, this);
			if (!permission.PermissionExists(permissionAmmoType)) permission.RegisterPermission(permissionAmmoType, this);
		}

		void OnItemCraftFinished(ItemCraftTask task, Item item)
		{
			if(!(item.GetHeldEntity() is BaseProjectile)) return;
			if(!hasAnyRight(task.owner)) return;
			Dictionary <string, object> weaponStats = null;
			if (weaponContainer.ContainsKey(item.info.shortname))
				weaponStats = weaponContainer[item.info.shortname] as Dictionary <string, object>;
			if (!(bool)weaponStats["settingactive"]) return;
			if (hasRight(task.owner,"maxammo") || hasRight(task.owner, "all"))
				(item.GetHeldEntity() as BaseProjectile).primaryMagazine.capacity = (int)weaponStats["maxammo"];
			if (hasRight(task.owner,"preload") || hasRight(task.owner, "all"))
				(item.GetHeldEntity() as BaseProjectile).primaryMagazine.contents = (int)weaponStats["preload"];
			if (hasRight(task.owner,"ammotype") || hasRight(task.owner, "all"))
			{
				var ammo = ItemManager.FindItemDefinition((string)weaponStats["ammotype"]);
				if (ammo != null)
					(item.GetHeldEntity() as BaseProjectile).primaryMagazine.ammoType = ammo;
			}
			if (hasRight(task.owner,"maxcondition") || hasRight(task.owner, "all"))
			{
				_itemMaxCondition.SetValue(item, Convert.ToSingle(weaponStats["maxcondition"]));
				_itemCondition.SetValue(item, Convert.ToSingle(weaponStats["maxcondition"]));				
			}
			if((int)weaponStats["skinid"] > 0)
			{
				item.skin = Convert.ToUInt64(weaponStats["skinid"]);
				item.GetHeldEntity().skinID = Convert.ToUInt64(weaponStats["skinid"]);
			}
		}
		
		private void OnItemAddedToContainer(ItemContainer container, Item item)
		{
			if(!checkPermission) return;
			if(item.GetHeldEntity() is BaseProjectile && container.HasFlag(ItemContainer.Flag.Belt))
			{
				Dictionary <string, object> weaponStats = null;
				object checkStats;
				if (weaponContainer.TryGetValue(item.info.shortname, out checkStats))
				{
					weaponStats = checkStats as Dictionary <string, object>;
					if (!(bool)weaponStats["settingactive"]) return;
				}
				else
					return;
				if ((item.GetHeldEntity() as BaseProjectile).primaryMagazine.capacity > item.info.GetComponent<ItemModEntity>().entityPrefab.Get().GetComponent<BaseProjectile>().primaryMagazine.definition.builtInSize && !(hasRight(container.playerOwner, "maxammo") || hasRight(container.playerOwner, "all")))
				{
					(item.GetHeldEntity() as BaseProjectile).primaryMagazine.capacity = item.info.GetComponent<ItemModEntity>().entityPrefab.Get().GetComponent<BaseProjectile>().primaryMagazine.definition.builtInSize;
					if ((item.GetHeldEntity() as BaseProjectile).primaryMagazine.contents > (item.GetHeldEntity() as BaseProjectile).primaryMagazine.capacity)
						(item.GetHeldEntity() as BaseProjectile).primaryMagazine.contents = (item.GetHeldEntity() as BaseProjectile).primaryMagazine.capacity;
				}
				if (item.maxCondition > item.info.condition.max && !(hasRight(container.playerOwner, "maxcondition") || hasRight(container.playerOwner, "all")))
				{
					var newCon = item.condition * (item.info.condition.max / item.maxCondition);
					_itemMaxCondition.SetValue(item, Convert.ToSingle(item.info.condition.max));
					_itemCondition.SetValue(item, Convert.ToSingle(newCon));
				}
				if (removeSkinIfNoRights && !hasAnyRight(container.playerOwner) && item.GetHeldEntity().skinID == Convert.ToUInt64(weaponStats["skinid"]) && item.GetHeldEntity().skinID != 0uL)
				{
					item.skin = 0uL;
					item.GetHeldEntity().skinID = 0uL;
				}
			}
		}
	}
}
