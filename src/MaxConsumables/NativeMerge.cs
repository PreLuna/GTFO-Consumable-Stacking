using System;
using System.Collections.Generic;
using LevelGeneration;
using Player;
using SNetwork;

namespace PreLuna.MaxConsumables
{
    internal static class NativeMerge
    {
        static readonly HashSet<IntPtr> busyWorld = new(), busyInventory = new(), uncertain = new();
        internal static void Clear() { busyWorld.Clear(); busyInventory.Clear(); uncertain.Clear(); }
        // true: ordinary pickup of an empty slot / different item; false: this
        // merge (or rejection) owns the interaction and must not also swap items.
        internal static bool TryMerge(LG_PickupItem_Sync sync, pPickupItemInteraction interaction)
        {
            try
            {
                var target = sync?.item?.TryCast<ItemInLevel>();
                if (target == null || !interaction.pPlayer.TryGetPlayer(out var player) || player == null) return true;
                var data = target.Get_pItemData();
                if (data.slot != InventorySlot.ResourcePack && data.slot != InventorySlot.Consumable) return true;
                if (uncertain.Contains(sync.Pointer) || busyWorld.Contains(sync.Pointer)) return false;
                if (sync.GetCurrentState().status != ePickupItemStatus.PlacedInLevel) return false;
                if (!PlayerBackpackManager.TryGetBackpack(player, out var backpack) || backpack == null
                    || !backpack.TryGetBackpackItem(data.slot, out var carried) || carried?.Instance == null) return true;
                var held = carried.Instance;
                if (held.Get_pItemData().itemID_gearCRC != data.itemID_gearCRC) return true;
                if (busyInventory.Contains(held.Pointer)) return false;
                if (sync.m_stateReplicator == null || target.ItemDataBlock == null
                    || held.Get_pItemData().replicatorRef.keyPlusOne == 0) return false;
                // The owner-less consumed state is verified for these two native
                // pickup callbacks. Unsupported custom pickups reject same-item
                // merges; empty-slot and different-item pickups remain native.
                if (target.TryCast<Gear.ResourcePackPickup>() == null && target.TryCast<ConsumablePickup_Core>() == null) return false;
                var ammo = data.slot == InventorySlot.ResourcePack ? AmmoType.ResourcePackRel : AmmoType.CurrentConsumable;
                float before = backpack.AmmoStorage.GetAmmoInPack(ammo);
                float world = sync.GetCustomData().ammo;
                // Same-item resources and consumables accumulate the entire bag.
                // Invalid or unrepresentable totals are rejected without swapping.
                if (!MergePlan.TryCreate(before, world, out var plan))
                {
                    Plugin.Logger?.LogWarning($"拒绝全量合并：数量无效、溢出或增量无法准确表示；背包={before}，地面={world}");
                    return false;
                }
                busyWorld.Add(sync.Pointer); busyInventory.Add(held.Pointer);
                try
                {
                    var port = new NativePort(sync, target, backpack, player, held, ammo);
                    var result = MergeTransaction.Execute(port, plan);
                    if (result.Outcome == MergeOutcome.Uncertain) uncertain.Add(sync.Pointer);
                    if (result.Diagnostic != null)
                        Plugin.Logger?.LogWarning($"合并 {result.Outcome}：{result.Diagnostic}；旧背包={before}，地面={world}，计划背包={plan.InventoryAfter}，余量={plan.WorldAfter}");
                    else Plugin.Logger?.LogDebug($"直接合并：{before}+{world} → 背包{plan.InventoryAfter}，地面{plan.WorldAfter}；保留原背包实例");
                }
                finally { busyWorld.Remove(sync.Pointer); busyInventory.Remove(held.Pointer); }
                return false;
            }
            catch (Exception ex)
            {
                // Never fall through to native swapping after an unknown outcome.
                if (sync != null) uncertain.Add(sync.Pointer);
                Plugin.Logger?.LogWarning("拒绝本次合并，未回退到换包：" + ex.GetBaseException().Message);
                return false;
            }
        }
        internal static void ReceiveInventory(pItemData_WithOwner packet)
        {
            // Host executes the authoritative transfer. Clients need this because
            // native custom-data reception changes the item, not AmmoStorage.
            if (SNet.IsMaster) return;
            try
            {
                var data = packet.data;
                if (data.slot != InventorySlot.ResourcePack && data.slot != InventorySlot.Consumable
                    || !float.IsFinite(data.custom.ammo) || data.custom.ammo < 0 || data.replicatorRef.keyPlusOne == 0) return;
                if (!packet.owningPlayer.TryGetPlayer(out var player) || player == null
                    || !PlayerBackpackManager.TryGetBackpack(player, out var backpack) || backpack == null
                    || !backpack.TryGetBackpackItem(data.slot, out var carried) || carried?.Instance == null) return;
                var current = carried.Instance.Get_pItemData();
                if (current.itemID_gearCRC != data.itemID_gearCRC || current.slot != data.slot
                    || current.replicatorRef.keyPlusOne != data.replicatorRef.keyPlusOne) return;
                var type = data.slot == InventorySlot.ResourcePack ? AmmoType.ResourcePackRel : AmmoType.CurrentConsumable;
                backpack.AmmoStorage.SetAmmo(type, data.custom.ammo);
            }
            catch (Exception ex) { Plugin.Logger?.LogWarning("合并库存同步失败：" + ex.GetBaseException().Message); }
        }
    }

    internal sealed class NativePort : IMergePort
    {
        readonly LG_PickupItem_Sync sync;
        readonly ItemInLevel target;
        readonly PlayerBackpack backpack;
        readonly SNet_Player owner;
        readonly Item held;
        readonly AmmoType ammo;
        readonly pItemData carriedData;
        readonly pItemData worldData;
        readonly pPickupItemState originalState;
        public NativePort(LG_PickupItem_Sync sync, ItemInLevel target, PlayerBackpack backpack, SNet_Player owner, Item held, AmmoType ammo)
        {
            this.sync = sync; this.target = target; this.backpack = backpack; this.owner = owner;
            this.held = held; this.ammo = ammo; carriedData = held.Get_pItemData(); originalState = sync.GetCurrentState();
            worldData = target.Get_pItemData();
        }
        public bool IdentityValid
        {
            get
            {
                if (!SNet.IsMaster || sync == null || target == null || held == null || sync.item?.Pointer != target.Pointer
                    || !backpack.TryGetBackpackItem(carriedData.slot, out var item) || item?.Instance?.Pointer != held.Pointer) return false;
                var data = item.Instance.Get_pItemData();
                if (data.slot != carriedData.slot || data.itemID_gearCRC != carriedData.itemID_gearCRC
                    || data.replicatorRef.keyPlusOne != carriedData.replicatorRef.keyPlusOne) return false;
                var ground = target.Get_pItemData();
                if (ground.slot != worldData.slot || ground.itemID_gearCRC != worldData.itemID_gearCRC
                    || ground.replicatorRef.keyPlusOne != worldData.replicatorRef.keyPlusOne) return false;
                var state = sync.GetCurrentState();
                return state.status == ePickupItemStatus.PlacedInLevel || WorldConsumed;
            }
        }
        public float InventoryAmount => backpack.AmmoStorage.GetAmmoInPack(ammo);
        public float WorldAmount => sync.GetCurrentState().status == ePickupItemStatus.PlacedInLevel ? sync.GetCustomData().ammo : 0;
        public bool WorldConsumed
        {
            get
            {
                var state = sync.GetCurrentState();
                return state.status == ePickupItemStatus.PickedUp && state.custom.ammo == 0
                    && !state.pPlayer.TryGetPlayer(out _);
            }
        }
        public void WriteWorld(float amount)
        {
            if (WorldConsumed)
            {
                var restored = originalState; restored.custom.ammo = amount;
                restored.updateCustomDataOnly = false; restored.forceUpdate = true;
                sync.m_stateReplicator.InteractWithState(restored, default);
            }
            else
            {
                var custom = sync.GetCustomData(); custom.ammo = amount;
                sync.SetCustomData(custom, true);
            }
        }
        public void WriteInventory(float amount) => backpack.AmmoStorage.SetAmmo(ammo, amount);
        public void PublishInventory(float amount)
        {
            var data = held.Get_pItemData(); data.custom.ammo = amount;
            held.SetCustomData(data.custom, false);
            PlayerBackpackManager.SendCustomData(data, owner);
        }
        public void ConsumeEmptyWorld()
        {
            if (sync.GetCurrentState().status != ePickupItemStatus.PlacedInLevel || sync.GetCustomData().ammo != 0)
                throw new InvalidOperationException("地面包不是已扣空状态");
            var consumed = sync.GetCurrentState();
            consumed.status = ePickupItemStatus.PickedUp;
            consumed.pPlayer = default; // no owner => no native add/drop inventory callback
            consumed.custom.ammo = 0;
            consumed.updateCustomDataOnly = false;
            consumed.forceUpdate = false;
            consumed.placement.hasBeenPickedUp = true;
            sync.m_stateReplicator.InteractWithState(consumed, default);
            if (!WorldConsumed) throw new InvalidOperationException("取空状态未确认");
            var resource = target.TryCast<Gear.ResourcePackPickup>();
            if (resource?.m_terminalItem != null)
                LG_LevelInteractionManager.DeregisterTerminalItem(resource.m_terminalItem);
            // Native OnPickedUp normally performs this container bookkeeping, but
            // that callback is intentionally skipped to keep the carried instance.
            target.container?.OnItemRemoved(target.gameObject);
        }
    }
}
