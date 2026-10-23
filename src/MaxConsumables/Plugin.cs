using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System.Reflection;

[assembly: AssemblyVersion("1.0.4.0")]
[assembly: AssemblyFileVersion("1.0.4.0")]

namespace PreLuna.MaxConsumables
{
    [BepInPlugin(Guid, "MaxConsumables·直接合并修复", "1.0.4")]
    [BepInIncompatibility("INF.MaxConsumables")]
    [BepInIncompatibility("Hikaria.ResourceStack")]
    [BepInDependency("Hikaria.Core", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("dev.AuriRex.gtfo.TheArchive", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BasePlugin
    {
        public const string Guid = "PreLuna.MaxConsumables";
        internal static ManualLogSource Logger;
        public override void Load()
        {
            Logger = Log;
            var harmony = new Harmony(Guid);
            try
            {
                CoreOverlap.Install(harmony);
                harmony.PatchAll(typeof(Plugin).Assembly);
                Log.LogInfo("本地直接合并补丁 1.0.4 已加载；唯一堆叠接管已建立，Core重复ResourceStack运行时关闭；资源和普通消耗品全量累计，保留数量守恒；武器和工具弹药保留原×8规则。");
            }
            catch (System.Exception ex)
            {
                harmony.UnpatchSelf(); CoreOverlap.Release();
                Log.LogError("唯一堆叠启动失败，已撤回本插件全部补丁：" + ex);
                throw;
            }
        }
    }
}
