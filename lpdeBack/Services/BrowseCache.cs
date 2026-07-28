using Microsoft.Extensions.Caching.Memory;

namespace lpdeBack.Services;

/// <summary>
/// Cle du cache des agregats de la page « Parcourir ». Partagee entre le
/// controleur qui la remplit et les traitements qui la perimenent : une purge
/// laisserait sinon la page annoncer pendant dix minutes un catalogue qui n'existe
/// plus.
/// </summary>
public static class BrowseCache
{
    public static readonly string[] Sections = { "categories", "locations", "contractTypes" };

    public static string Key(string section) => $"browse:{section}";

    public static void Invalidate(IMemoryCache cache)
    {
        foreach (var section in Sections) cache.Remove(Key(section));
    }
}
