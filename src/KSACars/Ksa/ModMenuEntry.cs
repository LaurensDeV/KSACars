namespace KSACars;

/// <summary>
/// A copy of MrJeranimo's <c>ModMenuEntryAttribute</c>, so <b>ModMenu</b> lists this mod under its
/// shared <c>Mods</c> menu when a player has it installed.
///
/// <para><b>A copy on purpose, not a dependency.</b> ModMenu scans every loaded assembly and matches
/// the attribute by its type's name alone, so declaring it here is enough to be found, and it is inert
/// when ModMenu is absent.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class ModMenuEntryAttribute(string menuName, string? isModMenuActivePropertyName = null)
    : Attribute
{
    public string MenuName { get; } = menuName;

    public string? IsModMenuActivePropertyName { get; } = isModMenuActivePropertyName;
}
