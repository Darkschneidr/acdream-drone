using AcDream.Core.Items;

namespace AcDream.Runtime.Gameplay;

public static class ItemEquipRules
{
    public const EquipMask AutoWearMask =
        EquipMask.HeadWear
        | EquipMask.ChestWear
        | EquipMask.AbdomenWear
        | EquipMask.UpperArmWear
        | EquipMask.LowerArmWear
        | EquipMask.HandWear
        | EquipMask.UpperLegWear
        | EquipMask.LowerLegWear
        | EquipMask.FootWear
        | EquipMask.ChestArmor
        | EquipMask.AbdomenArmor
        | EquipMask.UpperArmArmor
        | EquipMask.LowerArmArmor
        | EquipMask.UpperLegArmor
        | EquipMask.LowerLegArmor
        | EquipMask.Cloak;

    public static bool IsAutoWearItem(ClientObject item)
        => (item.ValidLocations & AutoWearMask) != EquipMask.None;

    /// <summary>Whether a drop on a paperdoll slot is an off-hand weapon wield:
    /// the shield slot also takes any item that can go in the melee weapon
    /// slot, although the item's own valid locations do not name the shield
    /// slot. Whether the character may dual wield is the server's decision,
    /// not the drop's.</summary>
    public static bool IsOffHandWeaponDrop(ClientObject item, EquipMask targetMask)
        => targetMask == EquipMask.Shield
            && (item.ValidLocations & EquipMask.MeleeWeapon) != EquipMask.None;

    /// <summary>The locations a paperdoll slot may take the item in: its own
    /// valid locations, plus the shield slot for an off-hand weapon drop.</summary>
    public static EquipMask PaperdollSlotValidLocations(ClientObject item, EquipMask targetMask)
        => IsOffHandWeaponDrop(item, targetMask)
            ? item.ValidLocations | EquipMask.Shield
            : item.ValidLocations;

    public static EquipMask ResolvePaperdollDropWieldMask(ClientObject item, EquipMask targetMask)
    {
        if ((item.ValidLocations & targetMask) == EquipMask.None)
            return EquipMask.None;

        return IsAutoWearItem(item)
            ? item.ValidLocations
            : item.ValidLocations & targetMask;
    }
}
