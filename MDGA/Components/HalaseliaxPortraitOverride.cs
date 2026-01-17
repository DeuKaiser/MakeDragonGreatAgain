using System;
using System.IO;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker;
using UnityEngine;
using System.Reflection;

namespace MDGA.Components
{
    // 在蓝图加载后，替换哈拉塞利亚斯与泽卡琉斯的肖像为本地 Assets/portraits 下的三张图片。
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class HalaseliaxPortraitOverride
    {
        private static bool _done;
        private static readonly BlueprintGuid HalaseliaxUnitGuid = BlueprintGuid.Parse("a9df515d1e68471abd9cfd482a8dab42");
        private static readonly BlueprintGuid ZachariusUnitGuid = BlueprintGuid.Parse("e006d3f1b8e45ec4587358aa941409b7");

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (_done) return; _done = true;
            if (!Main.Enabled)
            {
                Main.Log("[PortraitOverride] Skipped: mod not enabled.");
                return;
            }
            TryApplyPortrait(HalaseliaxUnitGuid, "Halaseliax");
            TryApplyPortrait(ZachariusUnitGuid, "Zacharius");
        }

        private static void TryApplyPortrait(BlueprintGuid unitGuid, string baseName)
        {
            try
            {
                var unit = ResourcesLibrary.TryGetBlueprint<Kingmaker.Blueprints.BlueprintUnit>(unitGuid);
                if (unit == null)
                {
                    Main.Log($"[PortraitOverride] Unit not found: {baseName} GUID={unitGuid}");
                    return;
                }

                string baseDir = Path.Combine(Main.ModEntry?.Path ?? ".", "Assets", "portraits");
                string smallPath = Path.Combine(baseDir, baseName + "Small.png");
                string mediumPath = Path.Combine(baseDir, baseName + "Medium.png");
                string fullPath = Path.Combine(baseDir, baseName + "Fulllength.png");
                Main.Log($"[PortraitOverride] ({baseName}) BaseDir={baseDir}");

                if (!File.Exists(smallPath) || !File.Exists(mediumPath) || !File.Exists(fullPath))
                {
                    Main.Log($"[PortraitOverride] ({baseName}) Portrait files missing. Expected {baseName}Small/Medium/Fulllength.png");
                    return;
                }

                string tempDir = Path.Combine(baseDir, baseName + "_Temp");
                try { Directory.CreateDirectory(tempDir); } catch (Exception exMkDir) { Main.Log($"[PortraitOverride] ({baseName}) CreateDirectory error: " + exMkDir.Message); }
                string dstSmall = Path.Combine(tempDir, "Small.png");
                string dstMedium = Path.Combine(tempDir, "Medium.png");
                string dstFull = Path.Combine(tempDir, "Fulllength.png");

                try
                {
                    File.Copy(smallPath, dstSmall, true);
                    File.Copy(mediumPath, dstMedium, true);
                    File.Copy(fullPath, dstFull, true);
                    Main.Log($"[PortraitOverride] ({baseName}) Copied files to tempDir: {tempDir}");
                }
                catch (Exception exCopy)
                {
                    Main.Log($"[PortraitOverride] ({baseName}) File.Copy error: " + exCopy.Message);
                    return;
                }

                PortraitData data;
                try
                {
                    data = new PortraitData(tempDir);
                    var pdType = typeof(PortraitData);
                    Sprite sSmall = TryGetSprite(pdType, data, "SmallPortrait");
                    Sprite sMedium = TryGetSprite(pdType, data, "MediumPortrait") ?? TryGetSprite(pdType, data, "HalfLengthPortrait") ?? TryGetSprite(pdType, data, "HalfPortrait");
                    Sprite sFull = TryGetSprite(pdType, data, "FullLengthPortrait") ?? TryGetSprite(pdType, data, "BigPortrait") ?? TryGetSprite(pdType, data, "Portrait");

                    if (sSmall == null || sMedium == null || sFull == null)
                    {
                        Main.Log($"[PortraitOverride] ({baseName}) PortraitData sprites missing: small={(sSmall==null)} medium={(sMedium==null)} full={(sFull==null)}");
                        return;
                    }
                    Main.Log($"[PortraitOverride] ({baseName}) Loaded sprites: S={sSmall.texture?.width}x{sSmall.texture?.height} M={sMedium.texture?.width}x{sMedium.texture?.height} F={sFull.texture?.width}x{sFull.texture?.height}");
                }
                catch (Exception exPd)
                {
                    Main.Log($"[PortraitOverride] ({baseName}) PortraitData ctor error: " + exPd.Message);
                    return;
                }

                try { if (unit.PortraitSafe?.Data?.m_PetEyeImage != null) data.m_PetEyeImage = unit.PortraitSafe.Data.m_PetEyeImage; } catch (Exception exPet) { Main.Log($"[PortraitOverride] ({baseName}) m_PetEyeImage copy error: " + exPet.Message); }

                if (unit.PortraitSafe != null)
                {
                    unit.PortraitSafe.Data = data;
                    Main.Log($"[PortraitOverride] ({baseName}) Applied portrait via PortraitSafe.Data.");
                }
                else
                {
                    Main.Log($"[PortraitOverride] ({baseName}) unit.PortraitSafe is null; skip applying to avoid early crash.");
                }
            }
            catch (Exception ex)
            {
                Main.Log($"[PortraitOverride] ({baseName}) Exception: " + ex.Message);
            }
        }

        private static Sprite TryGetSprite(Type pdType, PortraitData data, string propName)
        {
            try
            {
                var p = pdType.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null) return p.GetValue(data) as Sprite;
                var f = pdType.GetField(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) return f.GetValue(data) as Sprite;
            }
            catch (Exception ex)
            {
                Main.Log("[PortraitOverride] TryGetSprite error on " + propName + ": " + ex.Message);
            }
            return null;
        }
    }
}
