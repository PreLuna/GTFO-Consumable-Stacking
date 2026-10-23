using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
namespace TheArchive.Core.FeaturesAPI {
    public class Feature { public bool Enabled {get;set;} public FeatureInternal FeatureInternal; }
    public class FeatureManager {
        public static FeatureManager Instance {get;}=new();
        public List<Feature> RegisteredFeatures=new(); public int SavedChanges,DisabledCalls,EnabledCalls; public static bool QueryFails;
        [MethodImpl(MethodImplOptions.NoInlining)] public static Feature GetByType(Type type){if(QueryFails)throw new InvalidOperationException("mock-query-failure");return Instance.RegisteredFeatures.FirstOrDefault(f=>f.GetType()==type);}
        [MethodImpl(MethodImplOptions.NoInlining)] public void EnableFeature(Feature feature,bool save){EnabledCalls++;if(save)SavedChanges++;feature.FeatureInternal.Enable(true);}
        [MethodImpl(MethodImplOptions.NoInlining)] public void DisableFeature(Feature feature,bool save){DisabledCalls++;if(save)SavedChanges++;feature.FeatureInternal.Disable();}
    }
    public class FeatureInternal {
        readonly Feature _feature; public int EnableCalls,DisableCalls;
        public FeatureInternal(Feature feature){_feature=feature;}
        [MethodImpl(MethodImplOptions.NoInlining)] public bool Enable(bool callback){EnableCalls++;_feature.Enabled=true;return true;}
        [MethodImpl(MethodImplOptions.NoInlining)] public bool Disable(){DisableCalls++;_feature.Enabled=false;return true;}
        [MethodImpl(MethodImplOptions.NoInlining)] public static void CreateAndAssign(Feature feature,object module){feature.FeatureInternal=new(feature);feature.FeatureInternal.Enable(false);FeatureManager.Instance.RegisteredFeatures.Add(feature);}
    }
}
