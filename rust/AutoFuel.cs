﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("AutoFuel", "redBDGR", "1.0.2", ResourceId = 2717)]
    [Description("Automatically fuel lights if there is fuel in the toolcupboards inventory")]

    class AutoFuel : RustPlugin
    {
        private bool Changed;

        private bool useBarbeque;
        private bool useCampfires;
        private bool useCeilingLight;
        private bool useFurnace;
        private bool useJackOLantern;
        private bool useLantern;
        private bool useSearchLight;
        private bool useTunaCanLamp;

        private bool dontRequireFuel;

        private List<string> activeShortNames = new List<string>();

        #region Configuration

        protected override void LoadDefaultConfig()
        {
            Config.Clear();
            LoadVariables();
            DoStartupItemNames();
        }

        private void LoadVariables()
        {
            useBarbeque = Convert.ToBoolean(GetConfig("Types to autofuel", "Use Barbeque", false));
            useCampfires = Convert.ToBoolean(GetConfig("Types to autofuel", "Use Campfire", false));
            useCeilingLight = Convert.ToBoolean(GetConfig("Types to autofuel", "Use Ceiling Light", true));
            useFurnace = Convert.ToBoolean(GetConfig("Types to autofuel", "Use Furnace", false));
            useJackOLantern = Convert.ToBoolean(GetConfig("Types to autofuel", "Use JackOLanterns", true));
            useLantern = Convert.ToBoolean(GetConfig("Types to autofuel", "Use Lantern", true));
            useSearchLight = Convert.ToBoolean(GetConfig("Types to autofuel", "Use Search Light", true));
            useTunaCanLamp = Convert.ToBoolean(GetConfig("Types to autofuel", "Use Tuna Can Lamp", true));
            dontRequireFuel = Convert.ToBoolean(GetConfig("Settings", "Don't require fuel", false));

            if (!Changed) return;
            SaveConfig();
            Changed = false;
        }

        #endregion

        #region Oxide Hooks

        private void Init()
        {
            LoadVariables();
            DoStartupItemNames();
        }

        private void OnEntitySpawned(BaseNetworkable entity)
        {
            if (entity.GetComponent<BaseEntity>()?.ShortPrefabName == "jackolantern.angry" || entity.GetComponent<BaseEntity>()?.ShortPrefabName == "jackolantern.happy")
                entity.GetComponent<BaseOven>().fuelType = ItemManager.FindItemDefinition("wood");
        }

        private Item OnFindBurnable(BaseOven oven)
        {
            if (oven == null)
                return null;
            if (oven.fuelType == null)
            {
                PrintError($"BaseOven FuelType is null {oven.ShortPrefabName} (if you are seeing this error, post it in the support thread http://oxidemod.org/plugins/autofuel.2717/)");
                return null;
            }
            if (!activeShortNames.Contains(oven.ShortPrefabName))
                return null;
            if (HasFuel(oven))
                return null;
            DecayEntity decayEnt = oven.GetComponent<DecayEntity>();
            if (decayEnt == null)
            {
                PrintError("DecayEntity is null (if you are seeing this error, post it in the support thread http://oxidemod.org/plugins/autofuel.2717/)");
                return null;
            }
            BuildingManager.Building building = decayEnt.GetBuilding();
            if (building == null)
            {
                PrintError("BuildingManager.Building is null (if you are seeing this error, post it in the support thread http://oxidemod.org/plugins/autofuel.2717/)");
                return null;
            }
            foreach (BuildingPrivlidge priv in building.buildingPrivileges)
            {
                if (dontRequireFuel)
                    return ItemManager.CreateByName(oven.fuelType.shortname, 1);
                Item fuelItem = GetFuel(priv, oven);
                if (fuelItem == null)
                    continue;
                RemoveItemThink(fuelItem);
                ItemManager.CreateByName(oven.fuelType.shortname, 1).MoveToContainer(oven.inventory);
                return null;
            }
            return null;
        }

        #endregion

        #region Methods

        private bool HasFuel(BaseOven oven)
        {
            if (oven.inventory?.itemList == null || oven.inventory.itemList.Count == 0)
                return false;
            return oven.inventory.itemList.Where(item => item != null).Any(item => item.info == oven.fuelType);
        }

        private Item GetFuel(BuildingPrivlidge priv, BaseOven oven)
        {
            if (priv.inventory?.itemList == null || priv.inventory.itemList.Count == 0)
                return null;
            return priv.inventory.itemList.Where(item => item != null).FirstOrDefault(item => item.info == oven.fuelType);
        }

        private static void RemoveItemThink(Item item)
        {
            if (item == null)
                return;
            if (item.amount == 1)
            {
                item.RemoveFromContainer();
                item.RemoveFromWorld();
                item.Remove();
            }
            else
            {
                item.amount = item.amount - 1;
                item.MarkDirty();
            }
        }

        private void DoStartupItemNames()
        {
            if (useBarbeque)
                activeShortNames.Add("bbq.deployed");
            if (useCampfires)
                activeShortNames.Add("campfire");
            if (useCeilingLight)
                activeShortNames.Add("ceilinglight.deployed");
            if (useFurnace)
                activeShortNames.Add("furnace");
            if (useJackOLantern)
            {
                activeShortNames.Add("jackolantern.angry");
                activeShortNames.Add("jackolantern.happy");
            }
            if (useLantern)
                activeShortNames.Add("lantern.deployed");
            if (useSearchLight)
                activeShortNames.Add("searchlight.deployed");
            if (useTunaCanLamp)
                activeShortNames.Add("tunalight.deployed");
        }

        private object GetConfig(string menu, string datavalue, object defaultValue)
        {
            var data = Config[menu] as Dictionary<string, object>;
            if (data == null)
            {
                data = new Dictionary<string, object>();
                Config[menu] = data;
                Changed = true;
            }
            object value;
            if (data.TryGetValue(datavalue, out value)) return value;
            value = defaultValue;
            data[datavalue] = value;
            Changed = true;
            return value;
        }

        #endregion
    }
}