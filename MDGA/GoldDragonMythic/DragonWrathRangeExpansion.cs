using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.Utility; // Feet

namespace MDGA.GoldDragonMythic
{
    // 将“龙族之怒（DragonWrath）”的范围从30尺提升到60尺
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class DragonWrathRangeExpansion
    {
        private static bool _done;
        // base 龙族之怒（自我施放，内部 AbilityTargetsAround 敌人30尺）
        private static readonly BlueprintGuid DragonWrathGuid = BlueprintGuid.Parse("59d08b909d684b91a137766ab22f4b1a");

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (_done) return; _done = true;
            if (!Main.Enabled) return;
            try
            {
                var ability = ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(DragonWrathGuid);
                if (ability == null) { Main.Log("[DragonWrathRange] Ability blueprint not found."); return; }
                EnsureTargetsAroundEnemies60ft(ability);
                Main.Log("[DragonWrathRange] Updated radius to 60 ft.");
            }
            catch (Exception ex)
            {
                Main.Log("[DragonWrathRange] Exception: " + ex.Message);
            }
        }

        private static void EnsureTargetsAroundEnemies60ft(BlueprintAbility ability)
        {
            try
            {
                var comps = GetComponentsArray(ability) ?? Array.Empty<BlueprintComponent>();
                foreach (var a in comps.OfType<AbilityTargetsAround>())
                {
                    // 半径从30改为60，目标类型保持 Enemy
                    SetFieldOrProp(a, "m_Radius", new Feet(60f));
                    SetFieldOrProp(a, "Radius", new Feet(60f));
                }
                SetComponentsArray(ability, comps);
            }
            catch (Exception ex)
            {
                Main.Log("[DragonWrathRange] EnsureTargetsAround error: " + ex.Message);
            }
        }

        private static BlueprintComponent[] GetComponentsArray(BlueprintScriptableObject bp)
        {
            var t = typeof(BlueprintScriptableObject);
            var compField = t.GetField("Components", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                           ?? t.GetField("m_Components", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (compField != null)
            {
                var value = compField.GetValue(bp) as BlueprintComponent[];
                if (value != null) return value;
            }
            var pi = t.GetProperty("ComponentsArray", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return (pi?.GetValue(bp) as BlueprintComponent[]);
        }

        private static void SetComponentsArray(BlueprintScriptableObject bp, BlueprintComponent[] comps)
        {
            var t = typeof(BlueprintScriptableObject);
            var compField = t.GetField("Components", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                           ?? t.GetField("m_Components", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (compField != null) { compField.SetValue(bp, comps); return; }
            var pi = t.GetProperty("ComponentsArray", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (pi != null && pi.CanWrite) pi.SetValue(bp, comps);
        }

        private static bool SetFieldOrProp(object obj, string name, object value)
        {
            if (obj == null) return false;
            var t = obj.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var f = t.GetField(name, flags);
            if (f != null)
            {
                try { f.SetValue(obj, value); return true; } catch { }
            }
            var p = t.GetProperty(name, flags);
            if (p != null && p.CanWrite)
            {
                try { p.SetValue(obj, value, null); return true; } catch { }
            }
            return false;
        }
    }
}
