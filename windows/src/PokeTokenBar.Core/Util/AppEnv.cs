using System.Reflection;

namespace PokeTokenBar.Core.Util;

public static class AppEnv
{
    public static bool IsRealApp => string.Equals(
        Assembly.GetEntryAssembly()?.GetName().Name,
        "PokeTokenBar",
        StringComparison.OrdinalIgnoreCase);
}
