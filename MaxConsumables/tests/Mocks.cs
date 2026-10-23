using System;
using System.Collections.Generic;
namespace HarmonyLib {
    public class HarmonyMethod {public int priority; public HarmonyMethod(Type t,string name){} }
    public class Patch {public string owner; public System.Reflection.MethodInfo PatchMethod;}
    public class Patches {public List<Patch> Prefixes=new(),Postfixes=new();}
    public class Harmony {public Harmony(string id){}public void Patch(System.Reflection.MethodInfo m,HarmonyMethod prefix=null,HarmonyMethod postfix=null){}public static Patches GetPatchInfo(System.Reflection.MethodBase m)=>null;}
    public static class AccessTools {public static System.Reflection.MethodInfo Method(Type t,string name,Type[] p)=>t.GetMethod(name,p);}
    public class HarmonyPatch:Attribute {public HarmonyPatch(){}public HarmonyPatch(Type t,string name){} }
    public class HarmonyPrefix:Attribute {} public class HarmonyPostfix:Attribute {}
    public class HarmonyPriority:Attribute {public HarmonyPriority(int v){} }
    public static class Priority {public const int First=800;}
}
namespace Il2CppInterop.Runtime.InteropTypes {
    public class Il2CppObjectBase {static long next;public IntPtr Pointer=(IntPtr)(++next);public T TryCast<T>()where T:class=>this as T;}
}
namespace UnityEngine {
    public class GameObject {public bool Active=true;}
    public static class Mathf {public static float Clamp(float v,float min,float max)=>Math.Clamp(v,min,max);}
}
namespace Il2CppSystem {public class Action<T>{public System.Action<T> Callback;public void Invoke(T v)=>Callback?.Invoke(v);} }
namespace GameData {public class ItemDataBlock {public int ConsumableAmmoMax{get;set;}=5;} }
public class Item:Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase {
    public Player.pItemData Data;public GameData.ItemDataBlock ItemDataBlock{get;set;}=new();
    public virtual Player.pItemData Get_pItemData()=>Data;
    public virtual void SetCustomData(Player.pItemData_Custom custom,bool sync){Data.custom=custom;}
}
public class ConsumablePickup_Core:LevelGeneration.ItemInLevel{}
namespace Gear {public class ResourcePackPickup:LevelGeneration.ItemInLevel {public object m_terminalItem=new();} }
namespace Player {
    public enum InventorySlot {Primary=1,Special=2,ResourcePack=4,Consumable=5}
    public enum AmmoType {Standard=0,Special=1,Class=2,ResourcePackRel=3,CurrentConsumable=5}
    public struct pItemData_Custom {public float ammo;public byte byteId,byteState;}
    public struct pItemData {public pItemData_Custom custom;public uint itemID_gearCRC;public SNetwork.SNetStructs.pReplicator replicatorRef;public InventorySlot slot;}
    public struct pItemData_WithOwner {public SNetwork.SNetStructs.pPlayer owningPlayer;public pItemData data;}
    public class BackpackItem {public Item Instance{get;set;} }
    public class InventorySlotAmmo {
        public float AmmoMaxCap{get;set;}=100;public float AmmoInPack{get;set;}
        public float CostOfBullet{get;set;}=1;public int BulletsInPack=>(int)(AmmoInPack/CostOfBullet);
        public Il2CppSystem.Action<int> OnBulletsUpdateCallback{get;set;}
    }
    public class PlayerAmmoStorage {
        public Dictionary<AmmoType,InventorySlotAmmo> Slots=new();public bool NeedsSync{get;set;}public System.Action WriteHook;
        public InventorySlotAmmo GetInventorySlotAmmo(AmmoType type){if(!Slots.TryGetValue(type,out var s))Slots[type]=s=new();return s;}
        public float GetAmmoInPack(AmmoType type)=>GetInventorySlotAmmo(type).AmmoInPack;
        public void SetAmmo(AmmoType type,float amount){GetInventorySlotAmmo(type).AmmoInPack=amount;NeedsSync=true;WriteHook?.Invoke();}
        public void UpdateSlotAmmoUI(InventorySlotAmmo slot,int count){}
        public void UpdateAmmoInPack(){}public void UpdateBulletsInPack(){}
    }
    public class PlayerBackpack {public BackpackItem Held;public PlayerAmmoStorage AmmoStorage{get;set;}=new();
        public bool TryGetBackpackItem(InventorySlot slot,out BackpackItem item){item=Held;return item!=null&&item.Instance.Data.slot==slot;}}
    public class PlayerBackpackManager {
        public static readonly List<pItemData_WithOwner> Packets=new();public static System.Action PublishHook;
        public static bool TryGetBackpack(SNetwork.SNet_Player player,out PlayerBackpack bag){bag=player.Backpack;return bag!=null;}
        public static void SendCustomData(pItemData data,SNetwork.SNet_Player owner){Packets.Add(new(){data=data,owningPlayer=new(){Player=owner}});PublishHook?.Invoke();}
        public void OnUpdateCustomData(){}public void OnLevelCleanup(){}
    }
}
namespace SNetwork {
    public static class SNet {public static bool IsMaster=true;}
    public class SNet_Player {public Player.PlayerBackpack Backpack;}
    public static class SNetStructs {
        public struct pPlayer {public SNet_Player Player;public bool TryGetPlayer(out SNet_Player player){player=Player;return player!=null;} }
        public struct pReplicator {public ushort keyPlusOne;}
    }
    public class SNet_StateReplicator<S,I> {public System.Action<S> Commit;public void InteractWithState(S state,I interaction)=>Commit(state);}
}
namespace LevelGeneration {
    public static class LG_LevelInteractionManager {public static int Removed;public static void DeregisterTerminalItem(object item){Removed++;} }
    public enum ePickupItemInteractionType {Pickup,Place,UpdateCustomData}
    public enum ePickupItemStatus {PlacedInLevel,PickedUp}
    public struct pPickupPlacement {public bool hasBeenPickedUp;}
    public struct pPickupItemState {public ePickupItemStatus status;public SNetwork.SNetStructs.pPlayer pPlayer;public pPickupPlacement placement;public Player.pItemData_Custom custom;public bool updateCustomDataOnly,forceUpdate;}
    public struct pPickupItemInteraction {public ePickupItemInteractionType type;public SNetwork.SNetStructs.pPlayer pPlayer;}
    public class LG_ResourceContainer_Storage {public int Removed;public void OnItemRemoved(UnityEngine.GameObject go){Removed++;} }
    public class ItemInLevel:Item {
        public LG_ResourceContainer_Storage container;public UnityEngine.GameObject gameObject=new();public LG_PickupItem_Sync Sync;
    }
    public class LG_PickupItem_Sync:Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase {
        public Item item{get;set;}public pPickupItemState State;public int NativeInventoryCallbacks;public System.Action WorldHook;
        public SNetwork.SNet_StateReplicator<pPickupItemState,pPickupItemInteraction> m_stateReplicator{get;set;}
        public void Setup(ItemInLevel target){item=target;target.Sync=this;State=new(){custom=target.Data.custom};m_stateReplicator=new(){Commit=s=>{
            State=s;item.Data.custom=s.custom;
            // Model ONLY the verified callback distinction: ownerless picked-up
            // hides the world item without adding/swapping the carried inventory.
            target.gameObject.Active=s.status==ePickupItemStatus.PlacedInLevel;
            if(s.status==ePickupItemStatus.PickedUp&&s.pPlayer.Player!=null)NativeInventoryCallbacks++;
        }};}
        public pPickupItemState GetCurrentState()=>State;
        public Player.pItemData_Custom GetCustomData()=>State.custom;
        public void SetCustomData(Player.pItemData_Custom custom,bool sync){var state=State;state.custom=custom;state.updateCustomDataOnly=true;m_stateReplicator.InteractWithState(state,default);WorldHook?.Invoke();}
        public void AttemptInteract(){}
    }
}
namespace PreLuna.MaxConsumables {
    internal class FakeLogger {public List<string> Entries=new();public void LogWarning(object v)=>Entries.Add(v.ToString());public void LogInfo(object v)=>Entries.Add(v.ToString());public void LogDebug(object v){} }
    internal static class Plugin {public const string Guid="PreLuna.MaxConsumables";internal static FakeLogger Logger=new();}
}
