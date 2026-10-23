using System;
using PreLuna.MaxConsumables;
using Player;
using LevelGeneration;
using SNetwork;

static class Tests
{
    static int checks;
    static void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
    sealed class Port:IMergePort {
        public bool Valid=true,Consumed;public float Inventory=4,World=3;public string Fail;public int Writes,Publishes;
        public bool IdentityValid=>Valid;public float InventoryAmount=>Inventory;public float WorldAmount=>World;
        void Throw(string name){if(Fail==name){Fail=null;throw new Exception(name);}}
        public void WriteWorld(float n){Throw("world-before");World=n;Consumed=false;Writes++;Throw("world-after");}
        public void WriteInventory(float n){Throw("inventory-before");Inventory=n;Writes++;Throw("inventory-after");}
        public void PublishInventory(float n){Throw("publish-before");Publishes++;Throw("publish-after");}
        public void ConsumeEmptyWorld(){Throw("consume-before");Consumed=true;Throw("consume-after");}
        public bool WorldConsumed=>Consumed;
    }
    sealed class Fixture {
        public PlayerBackpack Bag=new();public SNet_Player Owner;public Item Held;
        public ItemInLevel Target;public LG_PickupItem_Sync Sync=new();public pPickupItemInteraction Interaction;
        public Fixture(float before,float world,bool consumable=false){
            NativeMerge.Clear();PlayerBackpackManager.Packets.Clear();PlayerBackpackManager.PublishHook=null;SNet.IsMaster=true;
            var slot=consumable?InventorySlot.Consumable:InventorySlot.ResourcePack;
            Held=new(){Data=new(){slot=slot,itemID_gearCRC=12,replicatorRef=new(){keyPlusOne=9},custom=new(){ammo=before,byteId=2,byteState=4}}};
            Bag.Held=new(){Instance=Held};Bag.AmmoStorage.SetAmmo(consumable?AmmoType.CurrentConsumable:AmmoType.ResourcePackRel,before);
            Owner=new(){Backpack=Bag};Target=consumable?new ConsumablePickup_Core():new Gear.ResourcePackPickup();
            Target.Data=new(){slot=slot,itemID_gearCRC=12,replicatorRef=new(){keyPlusOne=17},custom=new(){ammo=world}};
            Target.container=new();Sync.Setup(Target);Interaction=new(){type=ePickupItemInteractionType.Pickup,pPlayer=new(){Player=Owner}};
        }
        public bool Pick()=>Patch.AttemptInteract_Prefix(Sync,Interaction);
        public float Count=>Bag.AmmoStorage.GetAmmoInPack(Held.Data.slot==InventorySlot.ResourcePack?AmmoType.ResourcePackRel:AmmoType.CurrentConsumable);
    }
    static void Main(){
        Arithmetic();Transactions();NativeAdapter();InventoryScope();
        Console.WriteLine($"PASS {checks} assertions; production merge/adapter source compiled against controlled APIs; native networking not executed.");
    }
    static void Arithmetic(){
        foreach(int boundary in new[]{5,40,64,900})for(int a=0;a<=boundary;a++)foreach(float b in new[]{1f,3f,5f,60f,950f}){
            bool ok=MergePlan.TryCreate(a,b,out var p);
            Check(ok,"旧上限不限制累计");
            Check(p.InventoryAfter==a+b&&p.WorldAfter==0,"全部数量进入背包");
            Check(MergePlan.Same(a+b,p.InventoryAfter+p.WorldAfter),"算术数量守恒");
        }
        Check(MergePlan.TryCreate(4,3,out var full)&&full.InventoryAfter==7&&full.WorldAfter==0,"4+3全量进入背包7");
        Check(MergePlan.TryCreate(900,60,out full)&&full.InventoryAfter==960,"旧900上限也能继续累计");
        Check(!MergePlan.TryCreate(float.NaN,3,out _)&&!MergePlan.TryCreate(4,float.PositiveInfinity,out _),"拒绝非有限量");
        Check(!MergePlan.TryCreate(-1,3,out _)&&!MergePlan.TryCreate(4,0,out _),"拒绝负数或空来源");
        Check(!MergePlan.TryCreate(float.MaxValue,float.MaxValue,out _)&&!MergePlan.TryCreate(16777216,1,out _),"浮点溢出及丢失增量时拒绝而不消耗来源");
    }
    static void Transactions(){
        foreach(string failure in new[]{null,"world-before","world-after","inventory-before","inventory-after","publish-before","publish-after","consume-before","consume-after"}){
            var port=new Port{Fail=failure};MergePlan.TryCreate(4,3,out var plan);
            var result=MergeTransaction.Execute(port,plan);
            bool commit=failure==null||failure=="consume-after";
            Check(result.Outcome==(commit?MergeOutcome.Committed:MergeOutcome.RolledBack),"故障结果 "+failure);
            Check(port.Inventory+port.World==7,"失败及成功均守恒 "+failure);
            Check(port.Inventory==(commit?plan.InventoryAfter:4)&&port.World==(commit?plan.WorldAfter:3),"失败按读回值恢复 "+failure);
        }
        var invalid=new Port{Valid=false};MergePlan.TryCreate(4,3,out var p);
        Check(MergeTransaction.Execute(invalid,p).Outcome==MergeOutcome.Uncertain&&invalid.Writes==0,"身份不确定不回写");
        var changed=new Port{Inventory=8};Check(MergeTransaction.Execute(changed,p).Outcome==MergeOutcome.Uncertain&&changed.Writes==0,"出现第三种数量不覆盖");
    }
    static void NativeAdapter(){
        var f=new Fixture(80,60);Check(!f.Pick()&&f.Count==140,"4+3直接变7，不走原版换包");
        Check(f.Bag.Held.Instance==f.Held&&f.Held.Data.replicatorRef.keyPlusOne==9,"保留旧背包实例与复制器");
        Check(f.Held.Data.custom.byteId==2&&f.Held.Data.custom.byteState==4,"不覆盖物品其他状态");
        Check(f.Sync.State.status==ePickupItemStatus.PickedUp&&f.Sync.State.custom.ammo==0&&!f.Target.gameObject.Active,"取空地面包通过同步状态消耗");
        Check(f.Sync.NativeInventoryCallbacks==0&&f.Target.container.Removed==1,"无主人消耗不调用原版换包，并清理容器记录");
        Check(LG_LevelInteractionManager.Removed>0,"取空资源包清除终端登记");
        Check(!f.Pick()&&f.Count==140&&PlayerBackpackManager.Packets.Count==1,"重复拾取不重复计算");
        var packet=PlayerBackpackManager.Packets[0];Check(packet.data.custom.ammo==140&&packet.owningPlayer.Player==f.Owner,"发送实际持有人及原背包身份");
        SNet.IsMaster=false;f.Bag.AmmoStorage.SetAmmo(AmmoType.ResourcePackRel,80);Patch.InventoryCustomData_Postfix(packet);
        Check(f.Count==140,"客机正确应用库存自定义数据");packet.data.replicatorRef.keyPlusOne=18;packet.data.custom.ammo=800;Patch.InventoryCustomData_Postfix(packet);
        Check(f.Count==140,"迟到旧物品包不能覆盖新物品库存");
        f=new Fixture(880,60);Check(!f.Pick()&&f.Count==940&&f.Sync.GetCustomData().ammo==0,"44+3全部进背包47");
        Check(!f.Target.gameObject.Active&&f.Target.container.Removed==1&&f.Bag.Held.Instance==f.Held,"全量转移确认后取空并保留旧背包实例");
        Check(!f.Pick()&&f.Count==940,"重复请求不能重复转移已取空物品");
        f=new Fixture(900,60);Check(!f.Pick()&&f.Count==960&&PlayerBackpackManager.Packets.Count==1,"超过原900仍累计到960");
        f=new Fixture(39,3,true);Check(!f.Pick()&&f.Count==42&&f.Sync.GetCustomData().ammo==0,"消耗品超过原×8上限全量合并");
        f=new Fixture(4,3,true);Check(!f.Pick()&&f.Count==7&&f.Sync.State.status==ePickupItemStatus.PickedUp,"消耗品完全合并保持原实例");
        f=new Fixture(80,60);f.Sync.m_stateReplicator=null;Check(!f.Pick()&&f.Count==80&&f.Bag.Held.Instance==f.Held,"缺失同步对象时不删除旧包");
        f=new Fixture(80,60);f.Target.Data.itemID_gearCRC=99;Check(f.Pick()&&f.Count==80,"不同物品走原版，无合并副作用");
        f=new Fixture(0,60);f.Bag.Held=null;Check(f.Pick(),"空槽走原版正常拾取");
        f=new Fixture(80,60);SNet.IsMaster=false;Check(f.Pick()&&f.Count==80,"客机不重复执行主机转移");
        f=new Fixture(80,60);f.Sync.WorldHook=()=>{throw new Exception("native world callback after write");};
        Check(!f.Pick()&&f.Count==80&&f.Sync.GetCustomData().ammo==60,"适配器地面写后异常恢复总量");
        f=new Fixture(80,60);bool nestedAllowed=true;f.Sync.WorldHook=()=>nestedAllowed=f.Pick();
        Check(!f.Pick()&&!nestedAllowed&&f.Count==140,"同一物品回调重入抑制");
        f=new Fixture(80,60);f.Bag.AmmoStorage.WriteHook=()=>{throw new Exception("inventory callback after write");};
        Check(!f.Pick()&&f.Count==80&&f.Sync.GetCustomData().ammo==60,"适配器库存写后异常恢复总量");
        f=new Fixture(float.MaxValue,60);Check(!f.Pick()&&f.Count==float.MaxValue&&f.Sync.GetCustomData().ammo==60,"增量不可表示时原背包和来源都保留");
    }
    static void InventoryScope(){
        foreach(var type in new[]{AmmoType.Standard,AmmoType.Special,AmmoType.Class,AmmoType.ResourcePackRel,AmmoType.CurrentConsumable}){
            var bag=new PlayerAmmoStorage();var slot=bag.GetInventorySlotAmmo(type);slot.AmmoInPack=100;slot.AmmoMaxCap=100;float result=0;
            Check(!Patch.UpdateAmmoInPack_Override_Prefix(bag,type,20,ref result)&&result==120,"不改变原插件弹药类型扩容范围 "+type);
            slot.AmmoInPack=799;Check(!Patch.UpdateBulletsInPack_Override_Prefix(bag,type,200,ref result)&&result==(Capacity.Uncapped(type)?999:800),"仅资源和消耗品解除容量上限 "+type);
            if(Capacity.Uncapped(type)){
                slot.AmmoInPack=999;Patch.UpdateAmmoInPack_Override_Prefix(bag,type,-1,ref result);Check(result==998,"使用一次不截断超限库存 "+type);
                slot.AmmoInPack=998;Patch.UpdateBulletsInPack_Override_Prefix(bag,type,-1,ref result);Check(result==997,"扣除消耗品弹数不截断库存 "+type);
                slot.AmmoInPack=4;Patch.UpdateAmmoInPack_Override_Prefix(bag,type,-10,ref result);Check(result==0,"扣量仍不能产生负库存 "+type);
                slot.AmmoInPack=10;Patch.UpdateAmmoInPack_Override_Prefix(bag,type,float.NaN,ref result);Check(result==10,"非法增量不污染库存 "+type);
            }
        }
    }
}
