using Hermes.DesktopCapture.Models;

namespace Hermes.DesktopCapture;

public static class ScreenRegionCatalog
{
    public static IReadOnlyList<ScreenRegion> NumberAndFinalize(IReadOnlyList<ScreenRegion> regions)
    {
        var list = regions.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var r = list[i];
            list[i] = r with { Index = i + 1 };
        }

        return list;
    }
}
