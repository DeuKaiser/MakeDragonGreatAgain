using System;
using System.Linq;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.Utility;
using MDGA.Loc;
using Kingmaker.Localization;
using UnityEngine;
using System.Collections;

using BlueprintCore.Blueprints.CustomConfigurators.Classes;
using BlueprintCore.Blueprints.CustomConfigurators.UnitLogic.Abilities;
using BlueprintCore.Blueprints.CustomConfigurators.UnitLogic.Buffs;
using BlueprintCore.Utils;
using BlueprintCore.Blueprints.Configurators.UnitLogic.ActivatableAbilities;
using BlueprintCore.Utils.Assets;

namespace MDGA.Mythic
{
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class TrueDragonModify
    {
        private static bool _done;
        private static bool _injected;

        // Dark Rites references (source pattern)
        private static readonly BlueprintGuid DarkRitesToggleGuid = BlueprintGuid.Parse("c511a005670280c44b6975a2a18a8a7b");
        private static readonly BlueprintGuid DarkRitesFeatureGuid = BlueprintGuid.Parse("9703d79082dc75e4aaaa4387b9c95229");
        private static readonly BlueprintGuid DarkRitesAreaBuffGuid = BlueprintGuid.Parse("793b138567d79624b97e78969d239307");
        private static readonly BlueprintGuid DarkRitesAreaEffectGuid = BlueprintGuid.Parse("2dce35c38b3c01041aff62e9d395af76");
        private static readonly BlueprintGuid DarkRitesEffectBuffGuid = BlueprintGuid.Parse("c8a852e1ca98b364198d28de555b6788");

        // Icon source: OverwhelmingPresence (神威如岳)
        private static readonly BlueprintGuid OverwhelmingPresenceGuid = BlueprintGuid.Parse("41cf93453b027b94886901dbfc680cb9");

        // True Dragon mythic feature (fill with the GUID you found)
        // Example: BlueprintGuid.Parse("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")
        private static readonly BlueprintGuid TrueDragonFeatureGuid = BlueprintGuid.Parse("2342307641fd42dcabd1441cc2fae0d9");

        // New blueprint GUIDs (mod-owned)
        private const string DragonMajestyFeatureGuid = "d6e1b4e0a3c14f6a9a6d6b6b002a8a01";
        private const string DragonMajestyToggleGuid = "d6e1b4e0a3c14f6a9a6d6b6b002a8a02";
        private const string DragonMajestyAreaBuffGuid = "d6e1b4e0a3c14f6a9a6d6b6b002a8a03";
        private const string DragonMajestyAreaEffectGuid = "d6e1b4e0a3c14f6a9a6d6b6b002a8a04";
        private const string DragonMajestyEffectBuffGuid = "d6e1b4e0a3c14f6a9a6d6b6b002a8a05";

        // Use an existing in-game icon for stability (avoid custom asset bundle dependency)
        private const string DragonMajestyIconAssetId = "";

        [HarmonyPostfix]
        private static void Postfix()
        {
            // Important: BlueprintsCache.Init happens very early. If we bail out while disabled and set _done,
            // the patch would never run again in this session.
            if (_done) return;
            if (!Main.Enabled) return;
            _done = true;

            try
            {
                Main.Log("[TrueDragonModify] BlueprintsCache.Init postfix entered.");
                EnsureDragonMajestyBlueprints();
                ScheduleInjection();
            }
            catch (Exception ex)
            {
                Main.Log("[TrueDragonModify] Exception: " + ex);
            }
        }

        private static void EnsureDragonMajestyBlueprints()
        {
            try
            {
                // If already created (or provided by another source), skip.
                var existingToggle = ResourcesLibrary.TryGetBlueprint<BlueprintActivatableAbility>(BlueprintGuid.Parse(DragonMajestyToggleGuid));
                if (existingToggle != null) return;

                var loc = GetDragonMajestyLoc();

                // Register keys so UI doesn't show "unknown key" and so our LocalizationInjector can re-inject
                // after QuickLocalization or other mods replace the pack.
                try
                {
                    RegisterDragonMajestyLocalization(loc);
                }
                catch (Exception ex)
                {
                    Main.Log("[TrueDragonModify] RegisterDragonMajestyLocalization error: " + ex.Message);
                }

                // Icon: always reuse OverwhelmingPresence icon to avoid missing-bundle warnings (Small.png)
                Sprite iconSprite = null;
                try
                {
                    var bp = ResourcesLibrary.TryGetBlueprint(OverwhelmingPresenceGuid);
                    if (bp is BlueprintFeature feat) iconSprite = feat.Icon;
                    else if (bp is BlueprintBuff buff) iconSprite = buff.Icon;
                    else if (bp is BlueprintAbility ab) iconSprite = ab.Icon;
                    else if (bp is Kingmaker.Blueprints.Items.BlueprintItem item) iconSprite = item.Icon;
                }
                catch (Exception ex)
                {
                    Main.Log("[TrueDragonModify] OverwhelmingPresence icon lookup failed: " + ex);
                    iconSprite = null;
                }

                // Minimal, save-stable implementation:
                // Feature grants an activatable ability. The activatable applies a simple buff.
                // More complex area effects can be layered in later.
                var effectBuff = BuffConfigurator.New("MDGA_TD_DragonMajesty_EffectBuff", DragonMajestyEffectBuffGuid)
                    .SetDisplayName(loc.NameKey)
                    .SetDescription(loc.DescKey)
                    .SetIcon(iconSprite)
                    .Configure();

                // Implement the actual effect: caster level +2 while the buff is active.
                try
                {
                    AddCasterLevelBonusComponent(effectBuff, 2);
                    // Defensive: ensure all components have non-empty names to avoid save-time null key issues.
                    effectBuff.ComponentsArray = EnsureComponentNames(effectBuff.ComponentsArray);
                }
                catch (Exception ex)
                {
                    Main.Log("[TrueDragonModify] Failed to add caster level bonus component: " + ex.Message);
                }

                // Ensure blueprint has bound keys/text as a fallback even if the pack isn't ready at this exact moment.
                try
                {
                    LocalizationInjector.BindKeyAndText(effectBuff, "m_DisplayName", loc.NameKey, GetDragonMajestyNameText(loc));
                    LocalizationInjector.BindKeyAndText(effectBuff, "m_Description", loc.DescKey, GetDragonMajestyDescText(loc));
                }
                catch { }

                var toggle = BlueprintCore.Blueprints.Configurators.UnitLogic.ActivatableAbilities.ActivatableAbilityConfigurator.New("MDGA_TD_DragonMajesty_Toggle", DragonMajestyToggleGuid)
                    .SetDisplayName(loc.NameKey)
                    .SetDescription(loc.DescKey)
                    .SetBuff(effectBuff)
                    .SetIcon(iconSprite)
                    .Configure();

                try
                {
                    LocalizationInjector.BindKeyAndText(toggle, "m_DisplayName", loc.NameKey, GetDragonMajestyNameText(loc));
                    LocalizationInjector.BindKeyAndText(toggle, "m_Description", loc.DescKey, GetDragonMajestyDescText(loc));
                }
                catch { }

                // NOTE: The DragonMajesty feature GUID is kept reserved, but we inject the toggle directly
                // onto the True Dragon feature so it shows up as an ability in UI.

                Main.Log("[TrueDragonModify] Created Dragon Majesty blueprints (Toggle+Buff).");
            }
            catch (Exception ex)
            {
                Main.Log("[TrueDragonModify] EnsureDragonMajestyBlueprints error: " + ex);
            }
        }

        private static void ScheduleInjection()
        {
            // Route B (save-safe): Avoid runtime cloning / cache injection.
            // We only attach an existing, stable blueprint (Dark Rites toggle) onto the DC True Dragon feature.
            // Also delay execution because DC creates the feature during its own patching, which may be after our Init postfix.
            try
            {
                var runner = Kingmaker.Utility.CoroutineRunner.Instance;
                runner.StartCoroutine(DelayedInject());
            }
            catch (Exception ex)
            {
                Main.Log("[TrueDragonModify] Failed to start delayed injection coroutine: " + ex.Message);
            }
        }

        private static IEnumerator DelayedInject()
        {
            // wait a bit to ensure DC finished creating mythic blueprints
            yield return null;
            yield return null;
            yield return new WaitForSecondsRealtime(1.0f);

            TryInjectDragonMajestyToTrueDragon();
        }

        private static void TryInjectDragonMajestyToTrueDragon()
        {
            if (_injected) return;
            if (!Main.Enabled) return;

            try
            {
                var trueDragon = TryResolveBlueprintFeature(TrueDragonFeatureGuid);
                if (trueDragon == null)
                {
                    Main.Log($"[TrueDragonModify] True Dragon feature not found; cannot inject Dragon Majesty: {TrueDragonFeatureGuid}");
                    return;
                }

                var dragonMajestyToggle = ResourcesLibrary.TryGetBlueprint<BlueprintActivatableAbility>(BlueprintGuid.Parse(DragonMajestyToggleGuid));
                if (dragonMajestyToggle == null)
                {
                    Main.Log($"[TrueDragonModify] Dragon Majesty toggle not found: {DragonMajestyToggleGuid}");
                    return;
                }

                AttachToggleToTrueDragon(dragonMajestyToggle);
                _injected = true;
                Main.Log($"[TrueDragonModify] Injected Dragon Majesty toggle into True Dragon: {trueDragon.name} ({trueDragon.AssetGuid})");
            }
            catch (Exception ex)
            {
                Main.Log("[TrueDragonModify] TryInjectDragonMajestyToTrueDragon error: " + ex);
            }
        }

        private static void AttachToggleToTrueDragon(BlueprintActivatableAbility toggle)
        {
            try
            {
                var trueDragon = TryResolveBlueprintFeature(TrueDragonFeatureGuid);
                if (trueDragon == null)
                {
                    Main.Log("[TrueDragonModify] TrueDragon feature not found; skip toggle injection.");
                    return;
                }

                var addFacts = trueDragon.GetComponent<AddFacts>();
                if (addFacts == null)
                {
                    addFacts = CreateComponentSafe<AddFacts>();
                    var existing = trueDragon.ComponentsArray ?? Array.Empty<BlueprintComponent>();
                    existing = EnsureComponentNames(existing);
                    trueDragon.ComponentsArray = existing.Concat(new BlueprintComponent[] { addFacts }).ToArray();
                }

                var facts = addFacts.m_Facts?.ToList() ?? new System.Collections.Generic.List<BlueprintUnitFactReference>();
                var r = toggle.ToReference<BlueprintUnitFactReference>();
                if (!facts.Any(x => x.Guid == r.Guid))
                {
                    facts.Add(r);
                    addFacts.m_Facts = facts.ToArray();
                }
            }
            catch (Exception ex)
            {
                Main.Log("[TrueDragonModify] AttachToggleToTrueDragon error: " + ex.Message);
            }
        }

        private static void TryInjectDarkRitesToggleToTrueDragon()
        {
            // Legacy path (Dark Rites reuse) is intentionally disabled.
            // Plan A uses a mod-owned, static blueprint (Dragon Majesty) instead.
        }

        private const string DragonMajestyZhNameKey = "MDGA_TD_DragonMajesty_Name_ZH";
        private const string DragonMajestyZhDescKey = "MDGA_TD_DragonMajesty_Desc_ZH";
        private const string DragonMajestyEnNameKey = "MDGA_TD_DragonMajesty_Name_EN";
        private const string DragonMajestyEnDescKey = "MDGA_TD_DragonMajesty_Desc_EN";

        private struct DragonMajestyLoc
        {
            public string NameKey;
            public string DescKey;
        }

        private static DragonMajestyLoc GetDragonMajestyLoc()
        {
            try
            {
                // Prefer Unity language when available
                var lang = Application.systemLanguage;
                if (lang == SystemLanguage.Chinese || lang == SystemLanguage.ChineseSimplified || lang == SystemLanguage.ChineseTraditional)
                {
                    return new DragonMajestyLoc { NameKey = DragonMajestyZhNameKey, DescKey = DragonMajestyZhDescKey };
                }

                // Fallback: check locale name/id via reflection
                var locale = LocalizationManager.CurrentLocale;
                if (locale != null)
                {
                    var s = locale.ToString();
                    if (!string.IsNullOrEmpty(s) && s.IndexOf("zh", StringComparison.OrdinalIgnoreCase) >= 0)
                        return new DragonMajestyLoc { NameKey = DragonMajestyZhNameKey, DescKey = DragonMajestyZhDescKey };
                }
            }
            catch { }

            return new DragonMajestyLoc { NameKey = DragonMajestyEnNameKey, DescKey = DragonMajestyEnDescKey };
        }

        private static void RegisterDragonMajestyLocalization(DragonMajestyLoc loc)
        {
            // We keep separate keys per language; choose which pair to register based on loc.
            var name = GetDragonMajestyNameText(loc);
            var desc = GetDragonMajestyDescText(loc);
            LocalizationInjector.RegisterDynamicKey(loc.NameKey, name);
            LocalizationInjector.RegisterDynamicKey(loc.DescKey, desc);

            // Proactively reinject (safe even if packs aren't fully loaded yet; injector handles it)
            LocalizationInjector.EnsureInjected();
        }

        private static string GetDragonMajestyNameText(DragonMajestyLoc loc)
        {
            if (loc.NameKey == DragonMajestyZhNameKey) return "龙族威仪";
            return "Draconic Dignity";
        }

        private static string GetDragonMajestyDescText(DragonMajestyLoc loc)
        {
            if (loc.DescKey == DragonMajestyZhDescKey)
                return "真龙和30英尺半径内的同伴施法时，施法者等级视为高2级。";
            return "In 30-foot radius, allies cast as if caster level was 2 levels higher.";
        }

        private static void AttachToTrueDragon(BlueprintFeature addFeature)
        {
            try
            {
                BlueprintFeature trueDragon = null;
                if (!TrueDragonFeatureGuid.Equals(BlueprintGuid.Empty))
                    trueDragon = TryResolveBlueprintFeature(TrueDragonFeatureGuid);

                if (trueDragon == null)
                {
                    Main.Log("[TrueDragonModify] TrueDragon feature not found; skip injection.");
                    return;
                }

                var addFacts = trueDragon.GetComponent<AddFacts>();
                if (addFacts == null)
                {
                    addFacts = CreateComponentSafe<AddFacts>();
                    var existing = trueDragon.ComponentsArray ?? Array.Empty<BlueprintComponent>();
                    existing = EnsureComponentNames(existing);
                    trueDragon.ComponentsArray = existing.Concat(new BlueprintComponent[] { addFacts }).ToArray();
                }

                var facts = addFacts.m_Facts?.ToList() ?? new System.Collections.Generic.List<BlueprintUnitFactReference>();
                var r = addFeature.ToReference<BlueprintUnitFactReference>();
                if (!facts.Any(x => x.Guid == r.Guid))
                {
                    facts.Add(r);
                    addFacts.m_Facts = facts.ToArray();
                }
            }
            catch (Exception ex)
            {
                Main.Log("[TrueDragonModify] AttachToTrueDragon error: " + ex.Message);
            }
        }

        private static T CreateComponentSafe<T>() where T : BlueprintComponent
        {
            try
            {
                var obj = Activator.CreateInstance(typeof(T)) as T;
                if (obj != null && string.IsNullOrEmpty(obj.name))
                    obj.name = typeof(T).Name;
                return obj;
            }
            catch
            {
                try
                {
                    var obj = (T)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(T));
                    if (obj != null && string.IsNullOrEmpty(obj.name))
                        obj.name = typeof(T).Name;
                    return obj;
                }
                catch
                {
                    return null;
                }
            }
        }

        private static BlueprintComponent[] EnsureComponentNames(BlueprintComponent[] components)
        {
            if (components == null || components.Length == 0) return components ?? Array.Empty<BlueprintComponent>();
            foreach (var c in components)
            {
                if (c == null) continue;
                if (string.IsNullOrEmpty(c.name))
                {
                    try { c.name = c.GetType().Name; }
                    catch { }
                }
            }
            return components;
        }

        private static BlueprintFeature TryResolveBlueprintFeature(BlueprintGuid guid)
        {
            // 1) Normal lookup
            var bp = TryGetBlueprintWithGuidFallback<BlueprintFeature>(guid);
            if (bp != null) return bp;

            // 2) Some tools display the raw 32-hex id. Try direct parse from that string.
            try
            {
                var s = guid.ToString();
                var normalized = s.Replace("-", string.Empty);
                if (!string.IsNullOrEmpty(normalized) && normalized.Length == 32)
                {
                    var g2 = BlueprintGuid.Parse(normalized);
                    bp = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(g2);
                    if (bp != null)
                    {
                        Main.Log($"[TrueDragonModify] Resolved target feature via normalized GUID string: {s} -> {normalized}");
                        return bp;
                    }
                }
            }
            catch { }

            // 3) As an extra diagnostic, try cache access if available
            try
            {
                // ResourcesLibrary.BlueprintsCache may expose GetCachedBlueprint for already-loaded assets
                var cache = ResourcesLibrary.BlueprintsCache;
                var method = cache.GetType().GetMethod("GetCachedBlueprint", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (method != null)
                {
                    var obj = method.Invoke(cache, new object[] { guid });
                    bp = obj as BlueprintFeature;
                    if (bp != null)
                    {
                        Main.Log("[TrueDragonModify] Resolved target feature via BlueprintsCache.GetCachedBlueprint.");
                        return bp;
                    }
                }
            }
            catch { }

            return null;
        }

        private static void AddCasterLevelBonusComponent(BlueprintBuff buff, int value)
        {
            // Prefer the vanilla implementation used by many effects (including Dark Rites):
            // Kingmaker.Designers.Mechanics.Facts.AddCasterLevel -> RuleCalculateAbilityParams.AddBonusCasterLevel
            var t = AccessTools.TypeByName("Kingmaker.Designers.Mechanics.Facts.AddCasterLevel")
                    ?? AccessTools.TypeByName("Kingmaker.Designers.Mechanics.EquipmentEnchants.AddCasterLevelEquipment")
                    ?? AccessTools.TypeByName("Kingmaker.UnitLogic.FactLogic.AddCasterLevel")
                    ?? AccessTools.TypeByName("Kingmaker.UnitLogic.FactLogic.AddCasterLevelBonus")
                    ?? AccessTools.TypeByName("Kingmaker.UnitLogic.FactLogic.AddCasterLevelModifier")
                    ?? FindAddCasterLevelTypeByScan();
            if (t == null)
            {
                Main.Log("[TrueDragonModify] No AddCasterLevel component type found; cannot implement CL+" + value + ".");
                return;
            }

            var comp = Activator.CreateInstance(t) as BlueprintComponent;
            if (comp == null)
            {
                Main.Log("[TrueDragonModify] Failed to instantiate caster level component: " + t.FullName);
                return;
            }

            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var fi = t.GetField("Value", flags) ?? t.GetField("m_Value", flags) ?? t.GetField("Bonus", flags) ?? t.GetField("m_Bonus", flags);
            if (fi != null)
            {
                if (fi.FieldType == typeof(int)) fi.SetValue(comp, value);
                else
                {
                    // Some implementations use ContextValue
                    var cvType = AccessTools.TypeByName("Kingmaker.UnitLogic.Mechanics.ContextValue");
                    if (cvType != null && fi.FieldType == cvType)
                    {
                        var cv = Activator.CreateInstance(cvType);
                        var vf = cvType.GetField("Value", flags) ?? cvType.GetField("m_Value", flags);
                        if (vf != null && vf.FieldType == typeof(int)) vf.SetValue(cv, value);
                        fi.SetValue(comp, cv);
                    }
                }
            }

            // Append to components
            var existing = buff.ComponentsArray ?? Array.Empty<BlueprintComponent>();
            buff.ComponentsArray = existing.Concat(new[] { comp }).ToArray();

            Main.Log($"[TrueDragonModify] Added caster level component: {t.FullName} (+{value})");
        }

        private static TBlueprint TryGetBlueprintWithGuidFallback<TBlueprint>(BlueprintGuid guid) where TBlueprint : BlueprintScriptableObject
        {
            var bp = ResourcesLibrary.TryGetBlueprint<TBlueprint>(guid);
            if (bp != null) return bp;

            // Some external dumps/tools provide the 32-hex GUID in a different byte order.
            // Try swapping to the "raw" Guid byte order and reparse.
            try
            {
                var swapped = SwapGuidString32(guid.ToString());
                if (!string.IsNullOrEmpty(swapped))
                {
                    var g2 = BlueprintGuid.Parse(swapped);
                    bp = ResourcesLibrary.TryGetBlueprint<TBlueprint>(g2);
                    if (bp != null)
                    {
                        Main.Log($"[TrueDragonModify] Found blueprint by swapped GUID: {guid} -> {swapped}");
                        return bp;
                    }
                }
            }
            catch { }

            return null;
        }

        private static string SwapGuidString32(string hex32)
        {
            if (string.IsNullOrEmpty(hex32)) return null;
            // Normalize: accept formats like "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx" or "xxxxxxxx-xxxx-..."
            hex32 = hex32.Replace("-", string.Empty);
            if (hex32.Length != 32) return null;
            // Standard Guid byte order swap: 4-2-2 fields are little-endian in string representation.
            // Reverse bytes in first 4 bytes, next 2, next 2; leave remaining 8 bytes.
            static string revPairs(string s)
            {
                // s is even length
                var chars = s.ToCharArray();
                for (int i = 0, j = chars.Length - 2; i < j; i += 2, j -= 2)
                {
                    (chars[i], chars[j]) = (chars[j], chars[i]);
                    (chars[i + 1], chars[j + 1]) = (chars[j + 1], chars[i + 1]);
                }
                return new string(chars);
            }

            var a = revPairs(hex32.Substring(0, 8));
            var b = revPairs(hex32.Substring(8, 4));
            var c = revPairs(hex32.Substring(12, 4));
            var d = hex32.Substring(16, 16);
            return a + b + c + d;
        }

        private static Type FindAddCasterLevelTypeByScan()
        {
            try
            {
                var asm = typeof(AddFacts).Assembly;
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                foreach (var t in asm.GetTypes())
                {
                    if (t == null) continue;
                    if (!t.FullName.StartsWith("Kingmaker.UnitLogic.FactLogic.", StringComparison.Ordinal)) continue;
                    if (t.Name.IndexOf("AddCasterLevel", StringComparison.Ordinal) < 0) continue;
                    if (!typeof(BlueprintComponent).IsAssignableFrom(t)) continue;

                    // must have a writable numeric/int-ish field
                    var fi = t.GetField("Value", flags) ?? t.GetField("m_Value", flags) ?? t.GetField("Bonus", flags) ?? t.GetField("m_Bonus", flags);
                    if (fi != null)
                        return t;
                }
            }
            catch { }
            return null;
        }

        private static T CloneBlueprint<T>(T source, string newName, string newGuid) where T : SimpleBlueprint
        {
            try
            {
                if (source == null) return null;
                // SimpleBlueprint is not a UnityEngine.Object. Clone using MemberwiseClone.
                var m = source.GetType().GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var clone = (T)m.Invoke(source, null);
                clone.name = newName;

                // Assign guid via reflection (BlueprintCore is not reliable here); compatible with publicized Assembly-CSharp.
                var soType = typeof(SimpleBlueprint);
                var fi = soType.GetField("AssetGuid", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (fi != null) fi.SetValue(clone, BlueprintGuid.Parse(newGuid));

                ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(clone.AssetGuid, clone);
                return clone;
            }
            catch (Exception ex)
            {
                Main.Log("[TrueDragonModify] CloneBlueprint failed: " + ex.Message);
                return null;
            }
        }

        private static void ReplaceBuffReferences(SimpleBlueprint blueprint, BlueprintGuid oldBuff, BlueprintGuid newBuff)
        {
            try
            {
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                foreach (var fi in blueprint.GetType().GetFields(flags))
                {
                    var val = fi.GetValue(blueprint);
                    if (val == null) continue;

                    // BlueprintBuffReference
                    if (fi.FieldType.Name.Contains("BlueprintBuffReference"))
                    {
                        var guidField = fi.FieldType.GetField("guid", flags) ?? fi.FieldType.GetField("m_Guid", flags);
                        if (guidField != null)
                        {
                            var g = (BlueprintGuid)guidField.GetValue(val);
                            if (g.Equals(oldBuff)) guidField.SetValue(val, newBuff);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Log("[TrueDragonModify] ReplaceBuffReferences error: " + ex.Message);
            }
        }
    }
}
