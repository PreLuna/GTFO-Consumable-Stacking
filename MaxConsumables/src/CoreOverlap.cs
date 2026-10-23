using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LevelGeneration;

namespace PreLuna.MaxConsumables
{
    // Govern only the duplicate Core stack. Do not rewrite saved framework settings.
    internal static class CoreOverlap
    {
        const string FeatureName = "Hikaria.Core.Features.Accessibility.ResourceStack";
        static Type feature, manager, internalType;
        static MethodInfo getFeature, disable, enable, internalEnable, create;
        static PropertyInfo instance, enabled;
        static FieldInfo internalFeature;
        static Harmony owner;
        static bool dirty = true, hooks, owning;
        static string lastDiagnostic, lastGate;
        static object restoreOnFailure;
        static long probedAt;
        static bool cacheValid, cachedBlocked;
        static CoreOverlap() { AppDomain.CurrentDomain.AssemblyLoad += (_, _) => { dirty = true; cacheValid = false; }; }
        static Type Find(string assembly, string name)
        {
            var matches = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == assembly).ToArray();
            if (matches.Length > 1) throw new InvalidOperationException("重复程序集：" + assembly);
            return matches.SingleOrDefault()?.GetType(name, false);
        }
        internal static void Install(Harmony harmony)
        {
            owner = harmony; owning = true; dirty = true; cacheValid = false;
            Resolve();
            var current = feature == null || getFeature == null ? null : getFeature.Invoke(null, new object[] { feature });
            if (current != null)
            {
                if ((bool)enabled.GetValue(current)) restoreOnFailure = current;
                Disable(current, "启动接管");
            }
            if (Blocked()) throw new InvalidOperationException("唯一堆叠接管未完成，详见 [堆叠状态] 日志");
        }
        internal static void Release()
        {
            owning = false; hooks = false; owner = null; dirty = true; cacheValid = false;
            if (restoreOnFailure != null)
            {
                try { enable.Invoke(instance.GetValue(null), new object[] { restoreOnFailure, false }); }
                catch (Exception ex) { Plugin.Logger?.LogWarning("堆叠启动失败后恢复Core原状态失败：" + ex.GetBaseException().Message); }
                restoreOnFailure = null;
            }
        }
        static void Resolve()
        {
            if (!dirty) return;
            dirty = false;
            feature = Find("Hikaria.Core", FeatureName);
            manager ??= Find("TheArchive.Core", "TheArchive.Core.FeaturesAPI.FeatureManager");
            internalType ??= Find("TheArchive.Core", "TheArchive.Core.FeaturesAPI.FeatureInternal");
            if (manager == null || internalType == null) return;
            var baseFeature = manager.Assembly.GetType("TheArchive.Core.FeaturesAPI.Feature", true);
            getFeature ??= manager.GetMethod("GetByType", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(Type) }, null);
            instance ??= manager.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            enabled ??= baseFeature.GetProperty("Enabled");
            disable ??= manager.GetMethod("DisableFeature", new[] { baseFeature, typeof(bool) });
            enable ??= manager.GetMethod("EnableFeature", new[] { baseFeature, typeof(bool) });
            internalEnable ??= internalType.GetMethod("Enable", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(bool) }, null);
            internalFeature ??= internalType.GetField("_feature", BindingFlags.NonPublic | BindingFlags.Instance);
            create ??= internalType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .SingleOrDefault(m => m.Name == "CreateAndAssign" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == baseFeature);
            if (getFeature == null || instance == null || enabled == null || disable == null || enable == null
                || internalEnable == null || internalFeature == null || create == null)
                throw new MissingMethodException("TheArchive 的定向堆叠启停接口不完整");
            if (owner == null || hooks) return;
            owner.Patch(enable, prefix: new HarmonyMethod(typeof(CoreOverlap), nameof(EnableFeature_Prefix)) { priority = Priority.First });
            owner.Patch(internalEnable, prefix: new HarmonyMethod(typeof(CoreOverlap), nameof(InternalEnable_Prefix)) { priority = Priority.First });
            owner.Patch(create, postfix: new HarmonyMethod(typeof(CoreOverlap), nameof(Created_Postfix)));
            hooks = true;
        }
        static bool IsStack(object value) => value != null && value.GetType().FullName == FeatureName
            && value.GetType().Assembly.GetName().Name == "Hikaria.Core";
        static void Gate(string phase)
        {
            if (lastGate == phase) return;
            lastGate = phase;
            Plugin.Logger?.LogWarning("[堆叠所有权] phase=" + phase
                + " Core请求启用ResourceStack已拒绝；当前唯一实现=PreLuna.MaxConsumables；其他Core功能正常；未改写保存配置。");
        }
        static bool EnableFeature_Prefix(object __0)
        {
            if (!owning || !IsStack(__0)) return true;
            cacheValid = false;
            Disable(__0, "菜单启用请求"); Gate("菜单启用请求"); return false;
        }
        static bool InternalEnable_Prefix(object __instance, ref bool __result)
        {
            if (!owning || !IsStack(internalFeature.GetValue(__instance))) return true;
            cacheValid = false;
            __result = false; Gate("初始化或内部启用请求"); return false;
        }
        static void Created_Postfix(object __0)
        {
            if (owning && IsStack(__0)) { cacheValid = false; Disable(__0, "Core初始化完成"); }
        }
        static void Disable(object current, string phase)
        {
            if (!(bool)enabled.GetValue(current)) return;
            // false means do not persist a setting. Use the framework's normal
            // disable path to unpatch this feature and update its actual state.
            disable.Invoke(instance.GetValue(null), new object[] { current, false });
            Plugin.Logger?.LogInfo("[堆叠所有权] phase=" + phase + " CoreRuntimeBefore=true CoreRuntimeAfter="
                + enabled.GetValue(current) + "；调用原框架DisableFeature；未删除Core或修改强化剂。");
        }
        static bool HasGuard(MethodInfo original, string handler, bool postfix = false)
        {
            var info = original == null ? null : Harmony.GetPatchInfo(original);
            var patches = postfix ? info?.Postfixes : info?.Prefixes;
            var expected = typeof(CoreOverlap).GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Static);
            return patches != null && patches.Count(p => p.owner == Plugin.Guid && p.PatchMethod == expected) == 1;
        }
        internal static bool Blocked()
        {
            long now = Environment.TickCount64;
            if (!dirty && cacheValid && now - probedAt >= 0 && now - probedAt < 500) return cachedBlocked;
            bool blocked; string state;
            try
            {
                Resolve();
                if (feature == null) { blocked = false; state = "CoreType=absent；未发现Core资源堆叠类型"; }
                else if (manager == null || getFeature == null) { blocked = true; state = "CoreType=present CoreRuntime=unknown；框架状态接口缺失"; }
                else
                {
                    var current = getFeature.Invoke(null, new object[] { feature });
                    bool? runtime = current == null ? null : (bool)enabled.GetValue(current);
                    var patch = feature.GetNestedType("LG_PickupItem_Sync__AttemptInteract__Patch", BindingFlags.Public | BindingFlags.NonPublic)?.GetMethod("Prefix");
                    var target = AccessTools.Method(typeof(LG_PickupItem_Sync), "AttemptInteract", new[] { typeof(pPickupItemInteraction) });
                    int corePatches = Harmony.GetPatchInfo(target)?.Prefixes.Count(p => p.PatchMethod == patch) ?? 0;
                    bool guarded = owning && hooks && HasGuard(enable, nameof(EnableFeature_Prefix))
                        && HasGuard(internalEnable, nameof(InternalEnable_Prefix)) && HasGuard(create, nameof(Created_Postfix), true);
                    // Unregistered is safe only if initial and later enable paths
                    // are both guarded. Unknown is never silently treated as off.
                    blocked = runtime == true || corePatches != 0 || (owning && !guarded) || (runtime == null && !guarded);
                    state = "CoreRegistered=" + (current != null) + " CoreRuntime=" + (runtime?.ToString() ?? "pending")
                        + " CorePickupPrefixes=" + corePatches + " OwnershipGuards=" + guarded;
                }
            }
            catch (Exception ex) { blocked = true; state = "CoreRuntime=unknown exception=" + ex.GetBaseException().GetType().Name + ": " + ex.GetBaseException().Message; }
            string message = "[堆叠状态] " + (blocked ? "暂停" : "允许全量累计") + "；" + state;
            if (message != lastDiagnostic)
            {
                lastDiagnostic = message;
                if (blocked) Plugin.Logger?.LogWarning(message); else Plugin.Logger?.LogInfo(message);
            }
            cachedBlocked = blocked; probedAt = now; cacheValid = true;
            return blocked;
        }
    }
}
