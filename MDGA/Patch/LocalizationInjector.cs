using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit; // 用于构造动态方法以适配不同的 OnLocaleChanged 签名
using HarmonyLib;
using Kingmaker;
using Kingmaker.Blueprints; // 基础蓝图类型（SimpleBlueprint / BlueprintScriptableObject）
using Kingmaker.Blueprints.Classes; // 职业/特性蓝图（BlueprintFeature 等）
using Kingmaker.Localization;
using UnityEngine;

namespace MDGA.Patch
{
    internal static partial class LocalizationInjectorExtension { }
    internal static class LocalizationInjector
    {
        private const string WisNameKey = "MDGA.DD.WisdomBonus.Name";
        private const string WisDescKey = "MDGA.DD.WisdomBonus.Desc"; // 旧版描述 Key（保留兼容，实际多级描述使用动态注册）
        private const string ChaNameKey = "MDGA.DD.CharismaBonus.Name";
        private const string ChaDescKey = "MDGA.DD.CharismaBonus.Desc"; // 旧版描述 Key（同上，与多级版本已分离）

        // 基础静态条目：早期简单+2 感知 / +2 魅力占位。后续真正显示用动态条目覆盖，这里仍注入以保证 fallback。
        private static readonly (string key,string text)[] Entries = new (string,string)[]
        {
            // 仅作兜底占位：正常情况下会被 RegisterFeatureLocalization 的动态条目覆盖
            (WisNameKey, "属性增强：感知+2"),
            (ChaNameKey, "属性增强：魅力+2")
        };

        // 动态条目集合：为每个克隆/新建的特性生成唯一 key（例如 MDGA_DD_<guid>_m_DisplayName），文本在运行期注册。
        private static readonly Dictionary<string,string> DynamicEntries = new();

        private static bool _installedWatcher;
        private static HashSet<string> _applied = new HashSet<string>();
        private static object _lastPack;
        private static int _injectAttempts;
        private static bool _delayStarted;
        private static bool _completedInjection; // 注入完成标记：所有动态 key 均已落地且不再需要轮询
        private static bool _localeHookInstalled;

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
        private static bool _arcanaScaled;

        internal static void RegisterDynamicKey(string key, string text)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(text)) return;
            if (!DynamicEntries.ContainsKey(key))
            {
                DynamicEntries[key] = text;
                if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] Registered dynamic key " + key);
                // 如果之前认为注入已完成，但现在出现新 key，则重新开启注入流程
                if (_completedInjection && !_applied.Contains(key))
                {
                    _completedInjection = false; // allow EnsureInjected to run again
                }
                EnsureInjected();
            }
        }

        internal static string GetFallback(string key)
        {
            foreach (var (k, t) in Entries)
                if (k == key) return t;
            if (DynamicEntries.TryGetValue(key, out var dyn)) return dyn;
            return null;
        }

        internal static void EnsureInjected()
        {
            // 触发条件：未完成注入 或 存在尚未写入包的动态 key。
            if (_completedInjection && DynamicEntries.Keys.All(k => _applied.Contains(k))) return;
            _injectAttempts++;
            try
            {
                object pack = null;
                var packProp = typeof(LocalizationManager).GetProperty("CurrentPack", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (packProp != null)
                {
                    try { pack = packProp.GetValue(null); } catch { pack = null; }
                }
                if (pack == null)
                {
                    foreach (var f in typeof(LocalizationManager).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                    {
                        try
                        {
                            var ft = f.FieldType;
                            if (ft.Name.Contains("LocalizationPack"))
                            {
                                var val = f.GetValue(null);
                                if (val != null) { pack = val; break; }
                            }
                        }
                        catch { }
                    }
                }
                if (pack == null)
                {
                    if (Main.Settings.VerboseLogging && (_injectAttempts <= 3 || _injectAttempts % 100 == 0))
                        Main.Log("[DD ProgFix][Loc] EnsureInjected: localization pack still null (attempt " + _injectAttempts + ")");
                    return;
                }
                bool packSwapped = !ReferenceEquals(pack, _lastPack) && _lastPack != null;
                _lastPack = pack;

                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                var dictField = pack.GetType().GetField("m_Strings", flags);
                if (dictField == null)
                {
                    if (Main.Settings.VerboseLogging && (_injectAttempts <= 3 || _injectAttempts % 200 == 0))
                        Main.Log("[DD ProgFix][Loc] m_Strings field not found on pack type " + pack.GetType().FullName);
                    return;
                }
                var dict = dictField.GetValue(pack) as System.Collections.IDictionary;
                if (dict == null) return;

                var seType = pack.GetType().GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
                    .FirstOrDefault(t => t.Name.Contains("StringEntry"));
                if (seType == null) return;
                var textField = seType.GetField("Text", flags) ?? seType.GetField("m_Text", flags);
                var traitsField = seType.GetField("Traits", flags);

                int added = 0; int skipped = 0;
                // 注入静态占位条目
                foreach (var (key,text) in Entries)
                {
                    if (TryAdd(dict, seType, textField, traitsField, key, text)) added++; else skipped++;
                }
                // 注入所有动态条目（特性名/描述等）
                foreach (var kv in DynamicEntries)
                {
                    if (TryAdd(dict, seType, textField, traitsField, kv.Key, kv.Value)) added++; else skipped++;
                }
                // Arcana 伤害骰描述扩展（逐级+2/+3/+4）——仅第一次成功才设定 _arcanaScaled
                if (!_arcanaScaled)
                {
                    try { ApplyArcanaScalingDescriptions(dict); } catch (Exception exA) { if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] Arcana scale desc error: " + exA.Message); }
                }
                if (Main.Settings.VerboseLogging && (added > 0 || packSwapped || _injectAttempts <= 2))
                    Main.Log($"[DD ProgFix][Loc] Inject attempt #{_injectAttempts} packSwapped={packSwapped} added={added} skippedExisting={skipped}");
                // 在每次注入后执行挂钩式强制覆写（用于与 QuickLocalization 共存的 key 文本替换）
                DragonMightOverrideHelper.TryOverrideDragonMight(dict);
                // 完成判定：全部动态 key 已出现 + 多次尝试后再无新增 + Arcana 扩展已完成
                bool allPresentNow = DynamicEntries.Keys.All(k => _applied.Contains(k));
                if (allPresentNow && _injectAttempts > 5 && added == 0 && _arcanaScaled)
                {
                    _completedInjection = true;
                    if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] Injection complete; suppressing further attempts.");
                }
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging && (_injectAttempts <= 3 || _injectAttempts % 200 == 0))
                    Main.Log("[DD ProgFix][Loc] EnsureInjected exception: " + ex.Message);
            }
        }

        private static bool TryAdd(System.Collections.IDictionary dict, Type seType, FieldInfo textField, FieldInfo traitsField, string key, string text)
        {
            try
            {
                if (dict.Contains(key)) { _applied.Add(key); return false; }
                var entry = Activator.CreateInstance(seType);
                textField?.SetValue(entry, text);
                if (traitsField != null) { try { traitsField.SetValue(entry, null); } catch { } }
                dict.Add(key, entry);
                _applied.Add(key);
                return true;
            }
            catch { return false; }
        }

        internal static void BindLocalizedStrings(BlueprintFeature wis, BlueprintFeature cha)
        {
            try
            {
                if (wis != null)
                {
                    SetLocKey(wis, "m_DisplayName", WisNameKey);
                    SetLocKey(wis, "m_Description", WisDescKey);
                }
                if (cha != null)
                {
                    SetLocKey(cha, "m_DisplayName", ChaNameKey);
                    SetLocKey(cha, "m_Description", ChaDescKey);
                }
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] BindLocalizedStrings exception: " + ex.Message);
            }
        }

        private static void SetLocKey(object fact, string fieldName, string key)
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            // 向上遍历继承链查找字段（特性可能在基类里声明）
            FieldInfo fi = null; Type t = fact.GetType();
            while (t != null && fi == null) { fi = t.GetField(fieldName, flags); t = t.BaseType; }
            if (fi == null) return;
            var loc = fi.GetValue(fact);
            if (loc == null) return;
            var keyField = loc.GetType().GetField("m_Key", flags);
            if (keyField == null) return;
            keyField.SetValue(loc, key);
        }

        internal static void InstallWatcher()
        {
            if (_installedWatcher) return; _installedWatcher = true;
            try
            {
                var go = new GameObject("MDGA_LocWatcher");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<LocWatcher>();
                InstallLocaleChangedHook();
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] Failed to install watcher: " + ex.Message);
            }
        }

        internal static void StartDelayed()
        {
            if (_delayStarted) return; _delayStarted = true;
            try
            {
                var go = new GameObject("MDGA_LocDelay");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<DelayedInit>();
                InstallLocaleChangedHook();
            }
            catch (Exception ex) { if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] StartDelayed error: " + ex.Message); }
        }

        private class LocWatcher : MonoBehaviour
        {
            private float _timer;
            private int _burstFrames = 30; // 初始高频阶段用于快速覆盖 QuickLocalization 的早期写入
            private int _idleCounter;
            void Update()
            {
                if (_completedInjection) { Destroy(this); return; }
                if (_burstFrames > 0)
                {
                    _burstFrames--;
                    LocalizationInjector.EnsureInjected();
                    return;
                }
                _timer += Time.unscaledDeltaTime;
                if (_timer >= 15f) // 间隔轮询：防止语言包被其他 mod 重新分配后遗漏再注入
                {
                    _timer = 0f;
                    LocalizationInjector.EnsureInjected();
                }
                else if (_idleCounter++ % 600 == 0) // 低频探测 pack 是否被替换
                {
                    var packProp = typeof(LocalizationManager).GetProperty("CurrentPack", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    var pack = packProp?.GetValue(null);
                    if (pack != null && !ReferenceEquals(pack, _lastPack))
                    {
                        if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] Detected pack swap, reinjecting.");
                        _lastPack = pack;
                        _completedInjection = false;
                        LocalizationInjector.EnsureInjected();
                    }
                }
            }
        }

        private class DelayedInit : MonoBehaviour
        {
            private int _frames; // 延迟阶段：等待其他本地化覆盖完成后再最后补一次
            private bool _done;
            void Update()
            {
                if (_done || _completedInjection) { Destroy(this.gameObject); return; }
                _frames++;
                if (_frames < 150) // 前 150 帧内每 50 帧重试一次
                {
                    if (_frames % 50 == 0) LocalizationInjector.EnsureInjected();
                    return;
                }
                LocalizationInjector.EnsureInjected();
                _done = true;
                if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] Delayed injection finished after " + _frames + " frames");
            }
        }

        internal static void DumpState(BlueprintFeature wis = null, BlueprintFeature cha = null)
        {
            if (Main.Settings != null && !Main.Settings.VerboseLogging) return;
            try
            {
                var packProp = typeof(LocalizationManager).GetProperty("CurrentPack", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var pack = packProp?.GetValue(null);
                Main.Log("[DD ProgFix][LocDiag] Pack instance=" + (pack==null?"null":pack.ToString()));
                if (wis != null) LogLocFields(wis, "Wis");
                if (cha != null) LogLocFields(cha, "Cha");
            }
            catch (Exception ex)
            {
                Main.Log("[DD ProgFix][LocDiag] Exception: " + ex.Message);
            }
        }

        private static void LogLocFields(BlueprintFeature feat, string tag)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                FieldInfo fDisp = null, fDesc = null; Type t = feat.GetType();
                while (t != null && (fDisp == null || fDesc == null)) { fDisp ??= t.GetField("m_DisplayName", flags); fDesc ??= t.GetField("m_Description", flags); t = t.BaseType; }
                var disp = fDisp?.GetValue(feat);
                var desc = fDesc?.GetValue(feat);
                string dispKey = disp?.GetType().GetField("m_Key", flags)?.GetValue(disp) as string;
                string descKey = desc?.GetType().GetField("m_Key", flags)?.GetValue(desc) as string;
                Main.Log($"[DD ProgFix][LocDiag] {tag} displayKey={dispKey} descKey={descKey}");
            }
            catch (Exception ex) { Main.Log("[DD ProgFix][LocDiag] LogLocFields error: " + ex.Message); }
        }

        internal static void RegisterFeatureLocalization(BlueprintFeature feat, string display, string description)
        {
            try
            {
                if (feat == null) return;
                var displayKey = "MDGA_DD_" + feat.AssetGuid + "_m_DisplayName";
                var descKey = "MDGA_DD_" + feat.AssetGuid + "_m_Description";
                RegisterDynamicKey(displayKey, display);
                RegisterDynamicKey(descKey, description);

                // 直接将 blueprint 上的 m_DisplayName / m_Description 的 m_Key 指向我们动态 key；
                // 并在本地对象里写入 m_Text 作为兜底，防止 UI 读取时出现 null。
                BindKeyAndText(feat, "m_DisplayName", displayKey, display);
                BindKeyAndText(feat, "m_Description", descKey, description);
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] RegisterFeatureLocalization error: " + ex.Message);
            }
        }

        private static void BindKeyAndText(object blueprint, string fieldName, string key, string text)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                FieldInfo fi = null; Type t = blueprint.GetType();
                while (t != null && fi == null) { fi = t.GetField(fieldName, flags); t = t.BaseType; }
                if (fi == null) return;
                var locObj = fi.GetValue(blueprint);
                if (locObj == null)
                {
                    // 尝试创建新的 LocalizedString 实例
                    var locType = fi.FieldType;
                    try { locObj = Activator.CreateInstance(locType); fi.SetValue(blueprint, locObj); } catch { return; }
                }
                var keyField = locObj.GetType().GetField("m_Key", flags);
                var textField = locObj.GetType().GetField("m_Text", flags);
                if (keyField != null) keyField.SetValue(locObj, key);
                if (textField != null && (textField.GetValue(locObj) as string) == null)
                {
                    // 仅在原文本为空时写入兜底文本，避免覆盖其他 mod 已经写入的内容
                    textField.SetValue(locObj, text);
                }
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] BindKeyAndText error: " + ex.Message);
            }
        }

        private static void ApplyArcanaScalingDescriptions(System.Collections.IDictionary dict)
        {
            string suffixEn = " At 5th level this bonus increases to +2 per die, at 10th level to +3, and at 15th level to +4.";
            string suffixZh = " 在5级时该加值变为每骰+2，在10级为每骰+3，在15级为每骰+4。";
            int patched = 0; int skipped = 0;
            foreach (var guid in ArcanaGuids)
            {
                var feat = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(guid);
                if (feat == null) { skipped++; continue; }
                try
                {
                    var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                    FieldInfo fDesc = null; Type t = feat.GetType();
                    while (t != null && fDesc == null) { fDesc = t.GetField("m_Description", flags); t = t.BaseType; }
                    if (fDesc == null) { skipped++; continue; }
                    var locObj = fDesc.GetValue(feat);
                    if (locObj == null) { skipped++; continue; }
                    var keyField = locObj.GetType().GetField("m_Key", flags);
                    var textField2 = locObj.GetType().GetField("m_Text", flags);
                    string origKey = keyField?.GetValue(locObj) as string;
                    if (!string.IsNullOrEmpty(origKey) && origKey.Contains("MDGAScale")) { skipped++; continue; }

                    string packText = null;
                    if (!string.IsNullOrEmpty(origKey) && dict.Contains(origKey))
                    {
                        var entry = dict[origKey];
                        var et = entry?.GetType();
                        var txtF = et?.GetField("Text", flags) ?? et?.GetField("m_Text", flags);
                        packText = txtF?.GetValue(entry) as string;
                    }
                    string baseText = packText ?? (textField2?.GetValue(locObj) as string ?? string.Empty);

                    bool isChinese = baseText.Any(c => c >= '\u4e00' && c <= '\u9fff');
                    bool alreadyHas = (baseText.Contains("15级") || baseText.Contains("15th level") || baseText.Contains("15th") || baseText.Contains("15级为每骰+4"));
                    string newText = alreadyHas ? baseText : baseText + (isChinese ? suffixZh : suffixEn);

                    string newKeyBase = string.IsNullOrEmpty(origKey) ? ("MDGA_ARCANA_DESC_" + guid) : origKey;
                    string newKey = newKeyBase + "_MDGAScale";
                    keyField?.SetValue(locObj, newKey);
                    textField2?.SetValue(locObj, newText);
                    RegisterDynamicKey(newKey, newText);
                    patched++;
                }
                catch { skipped++; }
            }
            if (patched > 0) _arcanaScaled = true;
            if (Main.Settings.VerboseLogging) Main.Log($"[DD ProgFix][Loc] Arcana scaling descriptions patched: {patched} (skipped {skipped})");
        }

        private static void InstallLocaleChangedHook()
        {
            if (_localeHookInstalled) return;
            try
            {
                var lmType = typeof(LocalizationManager);
                // 首先尝试事件（优先，无侵入）
                var evt = lmType.GetEvent("OnLocaleChanged", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (evt != null)
                {
                    var handlerType = evt.EventHandlerType;
                    var invoke = handlerType.GetMethod("Invoke");
                    var pars = invoke.GetParameters();
                    Delegate del;
                    if (pars.Length == 0)
                    {
                        Action a = () => LocaleChangedCallback();
                        del = Delegate.CreateDelegate(handlerType, a.Target, a.Method);
                    }
                    else if (pars.Length == 1)
                    {
                        try {
                            var p0Type = pars[0].ParameterType;
                            var dm = new DynamicMethod("MDGA_LocaleChangedProxy", typeof(void), new Type[] { p0Type }, typeof(LocalizationInjector), true);
                            var il = dm.GetILGenerator();
                            il.Emit(OpCodes.Call, typeof(LocalizationInjector).GetMethod(nameof(LocaleChangedCallback), BindingFlags.Static | BindingFlags.NonPublic));
                            il.Emit(OpCodes.Ret);
                            del = dm.CreateDelegate(handlerType);
                        } catch {
                            del = Delegate.CreateDelegate(handlerType, typeof(LocalizationInjector).GetMethod(nameof(LocaleChangedCallback), BindingFlags.Static | BindingFlags.NonPublic), true);
                        }
                    }
                    else
                    {
                        Action a = () => LocaleChangedCallback();
                        del = Delegate.CreateDelegate(handlerType, a.Target, a.Method, false);
                    }
                    evt.AddEventHandler(null, del);
                    _localeHookInstalled = true;
                    if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] Installed OnLocaleChanged event hook.");
                }
                else
                {
                    // 若没有事件，则回退：Patch ChangeLanguage / SetLanguage / SetLocale 之类的方法
                    var m = lmType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(mi => mi.Name.Contains("ChangeLanguage") || mi.Name.Contains("SetLanguage") || mi.Name.Contains("SetLocale"));
                    if (m != null)
                    {
                        var harmony = new Harmony("MDGA.LocalizationHook");
                        harmony.Patch(m, postfix: new HarmonyMethod(typeof(LocalizationInjector).GetMethod(nameof(LocaleChangedPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                        _localeHookInstalled = true;
                        if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] Patched method " + m.Name + " for locale change hook.");
                    }
                    else
                    {
                        if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] No locale change event or method found; relying on watcher.");
                    }
                }
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] InstallLocaleChangedHook error: " + ex.Message);
            }
        }

        private static void LocaleChangedCallback()
        {
            try
            {
                if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] Locale changed -> reinjection reset");
                _completedInjection = false;
                _arcanaScaled = false; // force arcana patch re-run
                _injectAttempts = 0;
                EnsureInjected();
                StartDelayed(); // schedule aggressive retries
                // Recreate watcher if it was destroyed
                if (!_installedWatcher) InstallWatcher();
                // 语言切换后再次覆写龙族之力描述
                DragonMightOverrideHelper.MarkDirty();
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging) Main.Log("[DD ProgFix][Loc] LocaleChangedCallback error: " + ex.Message);
            }
        }

        private static void LocaleChangedPostfix()
        {
            LocaleChangedCallback();
        }
    }

    // 针对 DragonMight 描述的原始 key 强制覆写辅助：
    // QC 会在自身加载阶段批量替换 pack 中文本。我们的策略：在每次 EnsureInjected 后调用 TryOverride 再写入一次，确保描述被自定义版本覆盖。
    // 若找不到原始 key（极少数情况），则回退到动态 key 绑定方案（DragonMightExpansion 已注册）。
    internal static class DragonMightOverrideHelper
    {
        private static bool _doneOnce; // 首次成功后标记；再次语言切换或 pack swap 时置脏（_dirty）以重新覆盖。
        private static string _cachedOrigKey; // DragonMight 原始描述 key
        private static readonly BlueprintGuid DragonMightGuid = BlueprintGuid.Parse("bfc6aa5be6bc41f68ca78aef37913e9f");
        private static bool _dirty = true; // 是否需要重新尝试覆盖（语言切换 / pack 被替换）

        internal static void MarkDirty() { _dirty = true; }

        internal static void TryOverrideDragonMight(System.Collections.IDictionary dict)
        {
            try
            {
                if (!_dirty && _doneOnce) return; // 已完成且未脏
                var ability = ResourcesLibrary.TryGetBlueprint<Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility>(DragonMightGuid);
                if (ability == null) return;
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                // 定位能力描述字段 m_Description（LocalizedString）
                FieldInfo fDesc = null; Type t = ability.GetType();
                while (t != null && fDesc == null) { fDesc = t.GetField("m_Description", flags); t = t.BaseType; }
                var loc = fDesc?.GetValue(ability);
                if (loc == null) return;
                var keyField = loc.GetType().GetField("m_Key", flags);
                var textField = loc.GetType().GetField("m_Text", flags);
                string origKey = keyField?.GetValue(loc) as string;
                if (string.IsNullOrEmpty(origKey)) return;
                _cachedOrigKey ??= origKey; // 记录原始 key

                // 新文本（与 DragonMightExpansion 中保持一致）
                string zh = "你可以花费{g|Encyclopedia:Swift_Action}迅捷动作{/g}，令自己和周围30英尺内的盟友每施法者1{g|Encyclopedia:Combat_Round}轮{/g}内造成的{g|Encyclopedia:Damage}伤害{/g}提高50%。";
                string en = "As a {g|Encyclopedia:Swift_Action}swift action{/g}, you and allies within 30 feet deal 50% more {g|Encyclopedia:Damage}damage{/g} for 1 {g|Encyclopedia:Combat_Round}round{/g} per {g|Encyclopedia:Caster_Level}caster level{/g}.";
                bool isZh = LocalizationInjectorExtension_IsChinese();
                string finalText = isZh ? zh : en;

                // 覆写策略：若 pack 包含原始 key => 直接改 entry.Text；否则回退到动态 key。
                if (dict.Contains(origKey))
                {
                    var entry = dict[origKey];
                    var eType = entry?.GetType();
                    var eTextField = eType?.GetField("Text", flags) ?? eType?.GetField("m_Text", flags);
                    if (eTextField != null)
                    {
                        eTextField.SetValue(entry, finalText);
                        _doneOnce = true;
                        _dirty = false;
                        if (Main.Settings.VerboseLogging) Main.Log("[DragonMightOverride] Overrode existing pack key=" + origKey + " textLen=" + finalText.Length + " locale=" + (isZh?"zh":"en"));
                    }
                }
                else
                {
                    // 回退：使用我们动态 key（保持与 DragonMightExpansion 兼容），并设置 m_Key 指向该动态 key
                    string dynKey = isZh ? "MDGA_DragonMight_Desc_zh" : "MDGA_DragonMight_Desc_en";
                    LocalizationInjector.RegisterDynamicKey(dynKey, finalText);
                    keyField?.SetValue(loc, dynKey);
                    textField?.SetValue(loc, finalText);
                    if (Main.Settings.VerboseLogging) Main.Log("[DragonMightOverride] Pack missing origKey; rebound to dynamic key=" + dynKey);
                }
            }
            catch (Exception ex)
            {
                if (Main.Settings.VerboseLogging) Main.Log("[DragonMightOverride] Exception: " + ex.Message);
            }
        }

        // 语言判定：尝试从 LocalizationManager.CurrentLocale 与系统语言推断是否中文
        private static bool LocalizationInjectorExtension_IsChinese()
        {
            try
            {
                var loc = LocalizationManager.CurrentLocale;
                if (loc != null)
                {
                    string locStr = loc.ToString();
                    if (!string.IsNullOrEmpty(locStr) && locStr.IndexOf("zh", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    var langProp = loc.GetType().GetProperty("Language", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var langObj = langProp?.GetValue(loc, null);
                    if (langObj != null && langObj.ToString().ToLower().StartsWith("zh")) return true;
                }
            }
            catch { }
            try
            {
                if (Application.systemLanguage == SystemLanguage.ChineseSimplified || Application.systemLanguage == SystemLanguage.Chinese || Application.systemLanguage == SystemLanguage.ChineseTraditional)
                    return true;
            }
            catch { }
            return false;
        }
    }
}
