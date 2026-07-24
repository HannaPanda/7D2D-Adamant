using System.Reflection;
using HarmonyLib;

namespace AdamantBlock
{
    // Entry point: TFP's Harmony loader calls InitMod on any IModApi in a mod DLL.
    public class AdamantBlockInit : IModApi
    {
        public void InitMod(Mod _modInstance)
        {
            var harmony = new Harmony("com.hanna.adamantblock");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            UnityEngine.Debug.Log("[AdamantBlock] Harmony patches applied (tool-vs-weapon guard).");
        }
    }

    internal static class AdamantGuard
    {
        // Material id of the Adamant block (see materials.xml).
        public const string MaterialId = "MAdamant_shapes";

        // Vanilla item-tag facts (verified against 3.0.1 items.xml):
        //   Tools   (pickaxe, axe, shovel, auger, nailgun, salvage tool, ...) : tag "tool", NEVER "weapon".
        //   Weapons (guns AND melee weapons: club, knife, sledge, spear, ...) : tag "weapon".
        // So the presence of the "weapon" tag is the exact tool/weapon discriminator.
        private static readonly FastTags<TagGroup.Global> WeaponTag =
            FastTags<TagGroup.Global>.GetTag("weapon");

        // Returns true if this damage event should be BLOCKED entirely.
        //   - non-player source (zombie, animal, explosion, none) -> blocked (as before).
        //   - player holding a WEAPON (gun or melee weapon)        -> blocked (0 damage).
        //   - player holding a TOOL (or bare hands)                -> allowed (can mine it).
        public static bool ShouldBlock(Block block, int entityIdThatDamaged)
        {
            if (block == null || block.blockMaterial == null)
                return false;
            if (block.blockMaterial.id != MaterialId)
                return false;

            var gm = GameManager.Instance;
            var world = gm != null ? gm.World : null;
            if (world == null)
                return true; // no world context -> safest is to block

            // Anything that is not a living entity (explosion, world, unknown) -> block.
            if (!(world.GetEntity(entityIdThatDamaged) is EntityAlive ent))
                return true;

            // Living but not a player (zombie/animal) -> block, keep it indestructible to them.
            if (!(ent is EntityPlayer))
                return true;

            // Player: decide by the item currently in hand.
            var held = ent.inventory != null ? ent.inventory.holdingItem : null;
            if (held == null)
                return false; // bare hands / nothing held -> harmless, let it through

            // Weapon in hand -> block the damage. Tool in hand -> allow it.
            return held.HasAnyTags(WeaponTag);
        }
    }

    // Melee entry point used by ItemActionAttack for entity attacks.
    [HarmonyPatch(typeof(Block), nameof(Block.DamageBlock))]
    internal static class Patch_Block_DamageBlock
    {
        private static bool Prefix(Block __instance, BlockValue _blockValue, int _entityIdThatDamaged, ref int __result)
        {
            if (AdamantGuard.ShouldBlock(__instance, _entityIdThatDamaged))
            {
                __result = _blockValue.damage; // unchanged -> no damage, no destroy, no ping
                return false;                  // skip original
            }
            return true;
        }
    }

    // Central damage sink; covers any path that reaches OnBlockDamaged directly.
    [HarmonyPatch(typeof(Block), nameof(Block.OnBlockDamaged))]
    internal static class Patch_Block_OnBlockDamaged
    {
        private static bool Prefix(Block __instance, BlockValue _blockValue, int _entityIdThatDamaged, ref int __result)
        {
            if (AdamantGuard.ShouldBlock(__instance, _entityIdThatDamaged))
            {
                __result = _blockValue.damage;
                return false;
            }
            return true;
        }
    }
}
