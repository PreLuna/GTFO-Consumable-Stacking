using System;

namespace PreLuna.MaxConsumables
{
    internal readonly record struct MergePlan(float InventoryBefore, float WorldBefore, float InventoryAfter, float WorldAfter)
    {
        internal static bool TryCreate(float inventory, float world, out MergePlan plan)
        {
            plan = default;
            if (!float.IsFinite(inventory) || !float.IsFinite(world) || inventory < 0 || world <= 0) return false;
            float after = inventory + world;
            // No gameplay capacity: transfer ALL of the source. Reject numeric
            // overflow / lost increments rather than consuming a rounded-away item.
            if (!float.IsFinite(after) || after <= inventory || !Same(after - inventory, world)) return false;
            plan = new(inventory, world, after, 0); return true;
        }
        internal static bool Same(float a, float b) => float.IsFinite(a) && float.IsFinite(b)
            && Math.Abs(a - b) <= Math.Max(.0001f, Math.Abs(a) * .000001f);
    }

    internal interface IMergePort
    {
        bool IdentityValid { get; }
        float InventoryAmount { get; }
        float WorldAmount { get; }
        void WriteWorld(float amount);
        void WriteInventory(float amount);
        void PublishInventory(float amount);
        void ConsumeEmptyWorld();
        bool WorldConsumed { get; }
    }
    internal enum MergeOutcome { Committed, RolledBack, Uncertain }
    internal readonly record struct MergeResult(MergeOutcome Outcome, string Diagnostic);

    // The world item is debited in place; the carried item is never removed.
    // After any failure, inspect what actually happened before compensating.
    internal static class MergeTransaction
    {
        internal static MergeResult Execute(IMergePort port, MergePlan plan)
        {
            bool publicationAttempted = false, publicationConfirmed = false;
            try
            {
                Require(port.IdentityValid && MergePlan.Same(port.InventoryAmount, plan.InventoryBefore)
                    && MergePlan.Same(port.WorldAmount, plan.WorldBefore), "提交前物品或数量已变化");
                port.WriteWorld(plan.WorldAfter);
                Require(port.IdentityValid && MergePlan.Same(port.WorldAmount, plan.WorldAfter)
                    && MergePlan.Same(port.InventoryAmount, plan.InventoryBefore), "地面扣量未确认，或背包已变化");
                port.WriteInventory(plan.InventoryAfter);
                Require(port.IdentityValid && MergePlan.Same(port.InventoryAmount, plan.InventoryAfter)
                    && MergePlan.Same(port.WorldAmount, plan.WorldAfter), "背包加量未确认");
                publicationAttempted = true;
                port.PublishInventory(plan.InventoryAfter);
                publicationConfirmed = true;
                if (plan.WorldAfter == 0) port.ConsumeEmptyWorld();
                Require(port.IdentityValid && MergePlan.Same(port.InventoryAmount, plan.InventoryAfter)
                    && MergePlan.Same(port.WorldAmount, plan.WorldAfter)
                    && (plan.WorldAfter != 0 || port.WorldConsumed), "完成后的数量或空包状态未确认");
                return new(MergeOutcome.Committed, null);
            }
            catch (Exception ex)
            {
                // A callback may throw AFTER the empty item was consumed. If both
                // quantities and the consumed state are confirmed, do not roll back
                // a successful transfer or retry it and count it a second time.
                try
                {
                    if (publicationConfirmed && port.IdentityValid && MergePlan.Same(port.InventoryAmount, plan.InventoryAfter)
                        && MergePlan.Same(port.WorldAmount, plan.WorldAfter)
                        && (plan.WorldAfter != 0 || port.WorldConsumed))
                        return new(MergeOutcome.Committed, "回调异常，但合并数量和状态已确认：" + ex.GetBaseException().Message);
                    return Rollback(port, plan, publicationAttempted, ex);
                }
                catch (Exception recovery)
                {
                    return new(MergeOutcome.Uncertain, "停止此物品的再次合并；无法确认回滚："
                        + ex.GetBaseException().Message + " / " + recovery.GetBaseException().Message);
                }
            }
        }
        static MergeResult Rollback(IMergePort port, MergePlan plan, bool publish, Exception original)
        {
            Require(port.IdentityValid, "原背包物品已被替换，不向新物品回写旧数量");
            float inventory = port.InventoryAmount, world = port.WorldAmount;
            Require(MergePlan.Same(inventory, plan.InventoryBefore) || MergePlan.Same(inventory, plan.InventoryAfter), "背包数量出现第三种值");
            Require(MergePlan.Same(world, plan.WorldBefore) || MergePlan.Same(world, plan.WorldAfter), "地面数量出现第三种值");
            if (!MergePlan.Same(inventory, plan.InventoryBefore))
            {
                try { port.WriteInventory(plan.InventoryBefore); }
                catch { if (!MergePlan.Same(port.InventoryAmount, plan.InventoryBefore)) throw; }
            }
            Require(MergePlan.Same(port.InventoryAmount, plan.InventoryBefore), "背包回滚未确认");
            if (!MergePlan.Same(world, plan.WorldBefore))
            {
                try { port.WriteWorld(plan.WorldBefore); }
                catch { if (!MergePlan.Same(port.WorldAmount, plan.WorldBefore)) throw; }
            }
            Require(port.IdentityValid && MergePlan.Same(port.WorldAmount, plan.WorldBefore)
                && MergePlan.Same(port.InventoryAmount + port.WorldAmount, plan.InventoryBefore + plan.WorldBefore), "回滚后数量不守恒");
            if (publish) port.PublishInventory(plan.InventoryBefore);
            return new(MergeOutcome.RolledBack, "已撤销本次合并：" + original.GetBaseException().Message);
        }
        static void Require(bool ok, string error) { if (!ok) throw new InvalidOperationException(error); }
    }
}
