using HarmonyLib;
using System;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;

namespace PreLuna.MaxConsumables
{
    // Resources and ordinary consumables have no gameplay stacking ceiling.
    // Preserve the downloaded plugin's weapon/tool limits; do not make those unlimited.
    internal static class Capacity
    {
        internal const float StackLimit = float.MaxValue;
        internal static bool Uncapped(AmmoType type) => type == AmmoType.ResourcePackRel || type == AmmoType.CurrentConsumable;
        internal static bool TryAdjust(float amount, float delta, AmmoType type, float originalMax, out float count)
        {
            count = amount;
            if (!float.IsFinite(amount) || amount < 0 || !float.IsFinite(delta)) return false;
            float next = amount + delta;
            if (!float.IsFinite(next)) return false;
            if (Uncapped(type))
            {
                if (delta > 0 && (next <= amount || !MergePlan.Same(next - amount, delta))
                    || delta < 0 && amount > 0 && next == amount) return false;
                count = Math.Max(0, next); return true;
            }
            count = Mathf.Clamp(next, 0, originalMax * 8); return true;
        }
    }
    [HarmonyPatch]
    internal static class Patch
    {
        static bool invalidUpdateDiagnosed;
        static float Adjust(PlayerAmmoStorage storage, InventorySlotAmmo slot, AmmoType type, float delta)
        {
            if (Capacity.TryAdjust(slot.AmmoInPack, delta, type, slot.AmmoMaxCap, out var count)) return count;
            if (!invalidUpdateDiagnosed)
            { invalidUpdateDiagnosed = true; Plugin.Logger?.LogWarning("拒绝库存增减：数值无效或增量无法表示，保留原库存；后续同类诊断本关卡不重复。"); }
            return slot.AmmoInPack;
        }
        [HarmonyPatch(typeof(LG_PickupItem_Sync), nameof(LG_PickupItem_Sync.AttemptInteract))]
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        public static bool AttemptInteract_Prefix(LG_PickupItem_Sync __instance, pPickupItemInteraction interaction)
        {
            if (!SNet.IsMaster || interaction.type != ePickupItemInteractionType.Pickup || CoreOverlap.Blocked()) return true;
            return NativeMerge.TryMerge(__instance, interaction);
        }

        [HarmonyPatch(typeof(PlayerBackpackManager), nameof(PlayerBackpackManager.OnUpdateCustomData))]
        [HarmonyPostfix]
        public static void InventoryCustomData_Postfix(pItemData_WithOwner __0)
        { if (!CoreOverlap.Blocked()) NativeMerge.ReceiveInventory(__0); }

        [HarmonyPatch(typeof(PlayerBackpackManager), nameof(PlayerBackpackManager.OnLevelCleanup))]
        [HarmonyPostfix]
        public static void LevelCleanup_Postfix() { NativeMerge.Clear(); invalidUpdateDiagnosed = false; }

        [HarmonyPatch(typeof(PlayerAmmoStorage), nameof(PlayerAmmoStorage.UpdateAmmoInPack))]
        [HarmonyPrefix]
        public static bool UpdateAmmoInPack_Override_Prefix(PlayerAmmoStorage __instance, AmmoType ammoType, float delta, ref float __result)
        {
            if (CoreOverlap.Blocked()) return true;
            var slot = __instance.GetInventorySlotAmmo(ammoType);
            float count = Adjust(__instance, slot, ammoType, delta);
            slot.AmmoInPack = count;
            slot.OnBulletsUpdateCallback?.Invoke(slot.BulletsInPack);
            __instance.NeedsSync = true;
            __instance.UpdateSlotAmmoUI(slot, 0);
            __result = count;
            return false;
        }

        [HarmonyPatch(typeof(PlayerAmmoStorage), nameof(PlayerAmmoStorage.UpdateBulletsInPack))]
        [HarmonyPrefix]
        public static bool UpdateBulletsInPack_Override_Prefix(PlayerAmmoStorage __instance, AmmoType ammoType, int bulletCount, ref float __result)
        {
            if (CoreOverlap.Blocked()) return true;
            var slot = __instance.GetInventorySlotAmmo(ammoType);
            float delta = bulletCount * slot.CostOfBullet;
            float count = Adjust(__instance, slot, ammoType, delta);
            slot.AmmoInPack = count;
            slot.OnBulletsUpdateCallback?.Invoke(slot.BulletsInPack);
            __instance.NeedsSync = true;
            __instance.UpdateSlotAmmoUI(slot, 0);
            __result = count;
            return false;
        }
    }
}
