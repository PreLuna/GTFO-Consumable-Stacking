using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using PreLuna.MaxConsumables;
using TheArchive.Core.FeaturesAPI;
namespace LevelGeneration {
    public struct pPickupItemInteraction {}
    public class LG_PickupItem_Sync {
        [MethodImpl(MethodImplOptions.NoInlining)] public void AttemptInteract(pPickupItemInteraction interaction){}
    }
}
namespace PreLuna.MaxConsumables {
    internal sealed class Logger {public List<string> Lines=new();public void LogWarning(string s){Lines.Add(s);Console.WriteLine(s);}public void LogInfo(string s){Lines.Add(s);Console.WriteLine(s);} }
    internal static class Plugin {public const string Guid="PreLuna.MaxConsumables";public static Logger Logger=new();}
}
static class OwnershipTests {
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static Feature LoadFeature(string dll,string name)=> (Feature)Activator.CreateInstance(Assembly.LoadFrom(dll).GetType(name,true));
    static void Main(string[] args) {
        AssemblyLoadContext.Default.Resolving+=(_,n)=>{
            string path=Path.Combine(args[0],n.Name+".dll");return File.Exists(path)?AssemblyLoadContext.Default.LoadFromAssemblyPath(path):null;};
        Run(args);
    }
    [MethodImpl(MethodImplOptions.NoInlining)] static void Run(string[] args) {
        bool late=args[2]=="late";
        Feature resource=null;
        if(!late){resource=LoadFeature(args[1],"Hikaria.Core.Features.Accessibility.ResourceStack");FeatureInternal.CreateAndAssign(resource,null);Check(resource.Enabled,"已启用Core夹具");}
        var owner=new Harmony(Plugin.Guid);
        CoreOverlap.Install(owner);
        Check(!CoreOverlap.Blocked(),"接管允许唯一堆叠");
        if(late){resource=LoadFeature(args[1],"Hikaria.Core.Features.Accessibility.ResourceStack");Check(!CoreOverlap.Blocked(),"晚加载且尚未注册，仅有真实保护才放行");FeatureInternal.CreateAndAssign(resource,null);}
        Check(!resource.Enabled,"初始与晚加载都关闭Core堆叠");
        Check(!CoreOverlap.Blocked(),"初始化后状态可核验");
        int enabledCalls=FeatureManager.Instance.EnabledCalls,internalCalls=resource.FeatureInternal.EnableCalls;
        FeatureManager.Instance.EnableFeature(resource,true);
        Check(!resource.Enabled&&FeatureManager.Instance.EnabledCalls==enabledCalls,"菜单启用被精确拦截");
        Check(!resource.FeatureInternal.Enable(true)&&resource.FeatureInternal.EnableCalls==internalCalls,"内部直接启用被精确拦截");
        var other=LoadFeature(args[1],"Hikaria.Core.Features.Accessibility.OtherFeature");FeatureInternal.CreateAndAssign(other,null);
        Check(other.Enabled,"其他Core功能初始化正常");
        FeatureManager.Instance.DisableFeature(other,false);FeatureManager.Instance.EnableFeature(other,true);
        Check(other.Enabled&&FeatureManager.Instance.SavedChanges==1,"其他功能菜单与保存权限不变");
        Check(FeatureManager.Instance.DisabledCalls==(late?1:2),"原框架停用调用只影响原来启用项及主动其他测试项");
        var before=Plugin.Logger.Lines.Count;for(int i=0;i<50;i++)Check(!CoreOverlap.Blocked(),"重复检查保持允许");
        Check(Plugin.Logger.Lines.Count==before,"稳定状态不刷诊断");
        FeatureManager.QueryFails=true;Invalidate();Check(CoreOverlap.Blocked(),"查询异常不伪装为关闭");
        Check(Plugin.Logger.Lines.Last().Contains("mock-query-failure"),"保留查询异常根因诊断");
        FeatureManager.QueryFails=false;Invalidate();Check(!CoreOverlap.Blocked(),"查询恢复后可继续");
        // A leftover actual Core patch must be treated as a conflict even with
        // a false Enabled flag. Assembly load invalidates the state cache.
        var prefix=resource.GetType().GetNestedType("LG_PickupItem_Sync__AttemptInteract__Patch").GetMethod("Prefix");
        var target=typeof(LevelGeneration.LG_PickupItem_Sync).GetMethod("AttemptInteract");
        var conflict=new Harmony("test-core-stack");conflict.Patch(target,prefix:new HarmonyMethod(prefix));
        Invalidate();Check(CoreOverlap.Blocked(),"运行标志关闭但实际Core前缀残留，仍暂停而不双重合并");
        conflict.UnpatchSelf();Invalidate();Check(!CoreOverlap.Blocked(),"冲突移除后恢复允许");
        // Removal of ownership guards is not silently accepted for an unregistered feature.
        owner.UnpatchSelf();Invalidate();Check(CoreOverlap.Blocked(),"Core关闭但所有权保护被移除时拒绝");
        FeatureManager.Instance.RegisteredFeatures.Remove(resource);
        Invalidate();Check(CoreOverlap.Blocked(),"未注册且保护被移除，状态无法确认时拒绝");
        FeatureManager.Instance.RegisteredFeatures.Add(resource);
        CoreOverlap.Release();
        Check(resource.Enabled==!late,"启动失败撤回时恢复之前启用的Core；晚加载时保留原保存配置未改");
        FeatureManager.Instance.EnableFeature(resource,false);Check(resource.Enabled,"移除本地保护后Core可独立启用");
        Console.WriteLine("PASS "+checks+" ownership assertions ("+args[2]+"); real Harmony managed detours, controlled framework fixture; native game not run.");
    }
    static void Invalidate(){typeof(CoreOverlap).GetField("cacheValid",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,false);}
}
