using System;
using System.Linq;
using UnityEngine;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;

namespace MDGA.Patch
{
    // 迟后修复：在其它本地化/描述修改 Mod（如 QuickLocalization）加载完成后，
    // 再次检查龙族秘法特性描述，若缺少“5/10/15 级每骰加值 +2/+3/+4”说明则追加。
    // 使用延迟挂件方式避免与早期写入冲突。
    internal class ArcanaLateDescriptionFix : MonoBehaviour
    {
        private static readonly BlueprintGuid[] ArcanaGuids = new[] {
            BlueprintGuid.Parse("ac04aa27a6fd8b4409b024a6544c4928"),
            BlueprintGuid.Parse("a8baee8eb681d53438cc17bd1d125890"),
            BlueprintGuid.Parse("153e9b6b5b0f34d45ae8e815838aca80"),
            BlueprintGuid.Parse("5515ae09c952ae2449410ab3680462ed"),
            BlueprintGuid.Parse("caebe2fa3b5a94d4bbc19ccca86d1d6f"),
            BlueprintGuid.Parse("2a8ed839d57f31a4983041645f5832e2"),
            BlueprintGuid.Parse("1af96d3ab792e3048b5e0ca47f3a524b"),
            BlueprintGuid.Parse("456e305ebfec3204683b72a45467d87c"),
            BlueprintGuid.Parse("0f0cb88a2ccc0814aa64c41fd251e84e"),
            BlueprintGuid.Parse("677ae97f60d26474bbc24a50520f9424")
        };
        private int _frames;
        private int _attempts;
        private bool _done;

        public static void Ensure()
        {
            if (GameObject.Find("MDGA_ArcanaLateFix") != null) return;
            var go = new GameObject("MDGA_ArcanaLateFix");
            DontDestroyOnLoad(go);
            go.AddComponent<ArcanaLateDescriptionFix>();
        }

        void Update()
        {
            if (_done) { Destroy(gameObject); return; }
            _frames++;
            // 前置等待：确保其它本地化修改完成（约 5 秒）
            if (_frames < 300) return;
            if (_frames % 60 != 0) return; // 之后每秒尝试一次
            _attempts++;
            try
            {
                int patched = 0; int skipped = 0;
                foreach (var guid in ArcanaGuids)
                {
                    var feat = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(guid);
                    if (feat == null) { skipped++; continue; }
                    try
                    {
                        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
                        System.Reflection.FieldInfo fDesc = null; Type t = feat.GetType();
                        while (t != null && fDesc == null) { fDesc = t.GetField("m_Description", flags); t = t.BaseType; }
                        var locObj = fDesc?.GetValue(feat); if (locObj == null) { skipped++; continue; }
                        var keyField = locObj.GetType().GetField("m_Key", flags);
                        var textField = locObj.GetType().GetField("m_Text", flags);
                        string key = keyField?.GetValue(locObj) as string;
                        string text = textField?.GetValue(locObj) as string ?? string.Empty;
                        bool hasScaling = text.Contains("15级") || text.Contains("15th level") || text.Contains("15th") || text.Contains("15级为每骰+4");
                        if (!hasScaling)
                        {
                            bool zh = text.Any(c => c >= '\u4e00' && c <= '\u9fff');
                            string suffixEn = " At 5th level this bonus increases to +2 per die, at 10th level to +3, and at 15th level to +4.";
                            string suffixZh = " 在5级时该加值变为每骰+2，在10级为每骰+3，在15级为每骰+4。";
                            string newText = text + (zh ? suffixZh : suffixEn);
                            string newKeyBase = string.IsNullOrEmpty(key) ? ("MDGA_ARCANA_DESC_" + guid) : key;
                            if (!newKeyBase.Contains("MDGAScale")) newKeyBase += "_MDGAScaleLate";
                            keyField?.SetValue(locObj, newKeyBase);
                            textField?.SetValue(locObj, newText);
                            LocalizationInjector.RegisterDynamicKey(newKeyBase, newText);
                            patched++;
                        }
                        else skipped++;
                    }
                    catch (Exception exFeat)
                    {
                        Main.Log("[ArcanaLateFix] Feature patch error " + guid + " msg=" + exFeat.Message);
                        skipped++;
                    }
                }
                if (patched > 0) { Main.Log($"[ArcanaLateFix] Patched {patched} (skipped {skipped})"); _done = true; }
                else if (_attempts > 10) { Main.Log("[ArcanaLateFix] Gave up (no targets) after attempts=" + _attempts); _done = true; }
            }
            catch (Exception ex)
            {
                Main.Log("[ArcanaLateFix] Exception: " + ex.Message);
                if (_attempts > 10) _done = true;
            }
        }
    }
}
