#nullable enable
namespace BazaarPlusPlus.Core.GameState;

internal interface IBazaarGameEnvironmentProbe
{
    BazaarGameEnvironment CurrentEnvironment { get; }

    bool IsTournamentEnvironment();

    bool IsOfficialEnvironment();
}
