﻿/*
 *    ▄████████ ▀████    ▐████▀    ▄███████▄  ▄█        ▄██████▄     ▄████████  ▄█   ▄█    █▄     ▄████████         ▄████████  ▄██████▄  ███    █▄  ███▄▄▄▄   ████████▄     ▄████████
 *   ███    ███   ███▌   ████▀    ███    ███ ███       ███    ███   ███    ███ ███  ███    ███   ███    ███        ███    ███ ███    ███ ███    ███ ███▀▀▀██▄ ███   ▀███   ███    ███
 *   ███    █▀     ███  ▐███      ███    ███ ███       ███    ███   ███    █▀  ███▌ ███    ███   ███    █▀         ███    ███ ███    ███ ███    ███ ███   ███ ███    ███   ███    █▀
 *  ▄███▄▄▄        ▀███▄███▀      ███    ███ ███       ███    ███   ███        ███▌ ███    ███  ▄███▄▄▄           ▄███▄▄▄▄██▀ ███    ███ ███    ███ ███   ███ ███    ███   ███
 *  ▀▀███▀▀▀        ████▀██▄     ▀█████████▀  ███       ███    ███ ▀███████████ ███▌ ███    ███ ▀▀███▀▀▀          ▀▀███▀▀▀▀▀   ███    ███ ███    ███ ███   ███ ███    ███ ▀███████████
 *    ███    █▄    ▐███  ▀███      ███        ███       ███    ███          ███ ███  ███    ███   ███    █▄       ▀███████████ ███    ███ ███    ███ ███   ███ ███    ███          ███
 *    ███    ███  ▄███     ███▄    ███        ███▌    ▄ ███    ███    ▄█    ███ ███  ███    ███   ███    ███        ███    ███ ███    ███ ███    ███ ███   ███ ███   ▄███    ▄█    ███
 *    ██████████ ████       ███▄  ▄████▀      █████▄▄██  ▀██████▀   ▄████████▀  █▀    ▀██████▀    ██████████        ███    ███  ▀██████▀  ████████▀   ▀█   █▀  ████████▀   ▄████████▀
 *                                            ▀                                                                     ███    ███
 */

using System;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("ExplosiveRounds", "redBDGR", "1.0.1")]
    [Description("Give explosive bullets impact explosions")]
    class ExplosiveRounds : RustPlugin
    {
        bool Changed = false;
        public const string permissionName = "explosiverounds.use";
       /*
        *   [ BEANCAN ]                 assets/prefabs/weapons/beancan grenade/effects/beancan_grenade_explosion.prefab
        *   [ GRENADE EXPLOSION ]       assets/prefabs/weapons/f1 grenade/effects/f1grenade_explosion.prefab
        *   [ C4 EXPLOSION ]            assets/prefabs/tools/c4/effects/c4_explosion.prefab
        *   [ HELI EXPLOSION ]          assets/prefabs/npc/patrol helicopter/effects/heli_explosion.prefab
        *   [ WATER ]                   assets/prefabs/weapons/waterbucket/effects/waterimpact_explosion.prefab
        */
        public string explosioneffectuse = "assets/prefabs/weapons/beancan grenade/effects/beancan_grenade_explosion.prefab";

        static List<object> AmmoTypes()
        {
            var at = new List<object>();
            at.Add("ammo.rifle.explosive");
            return at;
        }
        List<object> ammotypes;

        void Init()
        {
            permission.RegisterPermission(permissionName, this);
            LoadVariables();
        }

        protected override void LoadDefaultConfig()
        {
            Config.Clear();
            LoadVariables();
        }

        void LoadVariables()
        {
            explosioneffectuse = Convert.ToString(GetConfig("Settings", "Explosion Effect", "assets/prefabs/weapons/beancan grenade/effects/beancan_grenade_explosion.prefab"));
            ammotypes = (List<object>)GetConfig("Settings", "Ammo Types", AmmoTypes());

            if (!Changed) return;
            SaveConfig();
            Changed = false;
        }

        void OnPlayerAttack(BasePlayer player, HitInfo info)
        {
            if (permission.UserHasPermission(player.UserIDString, permissionName))
            {
                if (ammotypes.Contains(info.Weapon.GetEntity().GetComponent<BaseProjectile>()?.primaryMagazine.ammoType.shortname))
                    Effect.server.Run(explosioneffectuse, info.HitPositionWorld);
            }
        }

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
    }
}
