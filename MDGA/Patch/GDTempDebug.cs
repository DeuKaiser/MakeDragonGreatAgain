using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Blueprints.Classes;
using Kingmaker; // for Game.Instance input fallback

namespace MDGA.Patch
{
    // ��ʱ�����ȼ�������Ϸ�а� F8 �������/��ʹ�񻰷�������Ϣ
    [DefaultExecutionOrder(9999)]
    internal class GDTempDebug : MonoBehaviour
    {
        private static bool _installed;
        private KeyCode _key = KeyCode.F8;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (_installed) return; _installed = true;
            try
            {
                // 仅在详细日志模式下安装临时调试组件
                if (!Main.Enabled || Main.Settings == null || !Main.Settings.VerboseLogging) return;
                var go = new GameObject("MDGA_GDTempDebug");
                GameObject.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.DontSave;
                go.AddComponent<GDTempDebug>();
                Main.Log("[GDTest] Temp debug hotkey (F8) installed.");
            }
            catch (Exception ex)
            {
                Main.Log("[GDTest] Install exception: " + ex.Message);
            }
        }

        void Update()
        {
            try
            {
                // ��ͨ�����䳢�Ծɰ� Input�����ֲü������Ƴ���ֱ�����÷��ţ�
                bool pressed = false;
                // 仅在详细日志模式下响应 F8，避免普通会话刷屏
                if (!Main.Enabled || Main.Settings == null || !Main.Settings.VerboseLogging) return;
                try
                {
                    var inputType = Type.GetType("UnityEngine.Input, UnityEngine.IMGUIModule");
                    if (inputType == null) inputType = Type.GetType("UnityEngine.Input, UnityEngine.CoreModule");
                    if (inputType != null)
                    {
                        var m = inputType.GetMethod("GetKeyDown", BindingFlags.Static | BindingFlags.Public, null, new Type[] { typeof(KeyCode) }, null);
                        if (m != null)
                        {
                            pressed = (bool)m.Invoke(null, new object[] { _key });
                        }
                    }
                }
                catch { }
                if (!pressed)
                {
                    // ���ˣ���� Event.current������ OnGUI ��Ч�������
                }
                if (pressed) Run();
            }
            catch { }
        }

        private static void Run()
        {
            try
            {
                var mergeGuid = BlueprintGuid.Parse("5bf6f5d4d2e04a1a9f4b4f4b6a9a1111"); // �Զ����������
                var angelListGuid = BlueprintGuid.Parse("deaffb4218ccf2f419ffd6e41603131a"); // ��ʹ�񻰷�����
                var mergeFeat = ResourcesLibrary.TryGetBlueprint<BlueprintFeatureSelectMythicSpellbook>(mergeGuid);
                var angelList = ResourcesLibrary.TryGetBlueprint<BlueprintSpellList>(angelListGuid);
                var fld = typeof(BlueprintFeatureSelectMythicSpellbook).GetField("m_MythicSpellList", BindingFlags.Instance | BindingFlags.NonPublic);
                var refObj = fld?.GetValue(mergeFeat) as BlueprintSpellListReference;
                var list = refObj?.Get();

                bool GetIsMythic(BlueprintSpellList l)
                {
                    if (l == null) return false;
                    var f = typeof(BlueprintSpellList).GetField("m_IsMythic", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (f != null && f.FieldType == typeof(bool)) return (bool)f.GetValue(l);
                    var p = typeof(BlueprintSpellList).GetProperty("IsMythic", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p != null && p.PropertyType == typeof(bool) && p.CanRead) return (bool)p.GetValue(l, null);
                    return false;
                }

                Main.Log($"[GDTest] MergeFeat MythicList GUID={(list == null ? "null" : list.AssetGuid.ToString())} (IsAngelList={(list!=null && list.AssetGuid==angelListGuid)})");
                Main.Log($"[GDTest] AngelList IsMythic={GetIsMythic(angelList)} L8={angelList?.SpellsByLevel?[8]?.SpellsRefs?.Count}");
                Main.Log($"[GDTest] CurrentList IsMythic={GetIsMythic(list)} Levels={(list?.SpellsByLevel?.Length ?? -1)} L8={list?.SpellsByLevel?[8]?.SpellsRefs?.Count} L9={list?.SpellsByLevel?[9]?.SpellsRefs?.Count} L10={list?.SpellsByLevel?[10]?.SpellsRefs?.Count}");

                string[] goldIds = {
                    "8af93e33","bfc6aa5b","1e42ecaa","a508fd48",
                    "d35b16ed","59d08b90","f7bc6e97","51b498f1",
                    "cb127670","cff9e3bf"
                };
                if (list?.SpellsByLevel != null)
                {
                    for (int lv = 8; lv <= 10; lv++)
                    {
                        var sl = list.SpellsByLevel[lv];
                        if (sl == null) { Main.Log($"[GDTest] L{lv} null"); continue; }
                        var have = sl.SpellsRefs.Select(r => r.Guid.ToString().Replace("-", "").Substring(0, 8)).ToHashSet();
                        var miss = goldIds.Where(g => !have.Contains(g)).ToArray();
                        Main.Log($"[GDTest] L{lv} Count={sl.SpellsRefs.Count} MissingGold={(miss.Length==0 ? "NONE" : string.Join("|", miss))}");
                    }
                }
                else
                {
                    Main.Log("[GDTest] Current list SpellsByLevel null");
                }
            }
            catch (Exception ex)
            {
                Main.Log("[GDTest] Run exception: " + ex.Message);
            }
        }
    }
}
