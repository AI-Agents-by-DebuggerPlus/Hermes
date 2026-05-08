using System.Globalization;
using System.Linq;
using Hermes.DesktopInteraction;

static int Fail(string msg)
{
    Console.Error.WriteLine(msg);
    return 1;
}

static bool TryParseInt(string s, out int v) =>
    int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

if (args.Length == 0)
{
    Console.WriteLine(
        """
        Hermes.MouseBridge — управление курсором Win32.

        Использование:
          Hermes.MouseBridge move <x> <y>
          Hermes.MouseBridge moveby <dx> <dy>
          Hermes.MouseBridge click [--double|--right]

        Пример из WSL (путь подставьте к своей сборке):
          "/mnt/d/Programming/AI_Agents/Hermes/Hermes.MouseBridge/bin/Release/net8.0-windows/Hermes.MouseBridge.exe" move 500 400
        """);
    return 0;
}

var verb = args[0].Trim().ToLowerInvariant();
try
{
    switch (verb)
    {
        case "move":
        {
            if (args.Length != 3 || !TryParseInt(args[1], out var x) || !TryParseInt(args[2], out var y))
            {
                return Fail("Ожидалось: move <x> <y> — целые координаты экрана (пиксели).");
            }

            DesktopMouse.MoveTo(x, y);
            return 0;
        }
        case "moveby":
        {
            if (args.Length != 3 || !TryParseInt(args[1], out var dx) || !TryParseInt(args[2], out var dy))
            {
                return Fail("Ожидалось: moveby <dx> <dy> — смещение в пикселях от текущей позиции.");
            }

            if (!DesktopMouse.MoveBy(dx, dy))
            {
                return Fail("Не удалось сместить курсор.");
            }

            return 0;
        }
        case "click":
        {
            var dbl = args.Any(static a => string.Equals(a, "--double", StringComparison.OrdinalIgnoreCase));
            var right = args.Any(static a => string.Equals(a, "--right", StringComparison.OrdinalIgnoreCase));
            if (right)
            {
                DesktopMouse.RightClick();
            }
            else
            {
                DesktopMouse.LeftClick(dbl ? 2 : 1);
            }

            return 0;
        }
        default:
            return Fail($"Неизвестная команда: {args[0]}");
    }
}
catch (Exception ex)
{
    return Fail(ex.Message);
}
