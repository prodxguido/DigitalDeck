using Xunit;

namespace DigitalDeck.Integrations.Tests;

/// <summary>
/// A test that needs Ableton Live open, with the DigitalDeck Remote Script selected as a Control
/// Surface. Skipped everywhere else, so a normal <c>dotnet test</c> stays green on a machine with
/// no Ableton on it.
/// </summary>
/// <remarks>
/// Section 26 of the blueprint asks for exactly this: the integration rests on undocumented
/// behaviour that Ableton never promised to keep, so there has to be something to run against each
/// new version of Live before telling anyone it is safe to update.
/// <code>
/// $env:DIGITALDECK_LIVE = "1"
/// dotnet test --filter FullyQualifiedName~AbletonSmoke --logger "console;verbosity=detailed"
/// </code>
/// </remarks>
public sealed class LiveFactAttribute : FactAttribute
{
    public const string Switch = "DIGITALDECK_LIVE";

    /// <summary>Guards the handful of checks that change the structure of the open set.</summary>
    public const string DestructiveSwitch = "DIGITALDECK_LIVE_DESTRUCTIVE";

    public LiveFactAttribute(bool destructive = false)
    {
        if (!IsOn(Switch))
        {
            Skip = $"Necesita Ableton Live abierto. Poné {Switch}=1 para correrlo.";
            return;
        }

        if (destructive && !IsOn(DestructiveSwitch))
        {
            Skip =
                $"Crea y elimina tracks en el set abierto. Poné {DestructiveSwitch}=1 " +
                "y abrí un Live Set de prueba antes de correrlo.";
        }
    }

    private static bool IsOn(string name) =>
        string.Equals(Environment.GetEnvironmentVariable(name), "1", StringComparison.Ordinal);
}
