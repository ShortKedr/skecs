using SpaceBattle.Game;

System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
try
{
    if (args.Contains("--validate-observer")) { SpectatorCameraValidation.Run(); return 0; }
    int frameworkIndex = Array.IndexOf(args, "--framework");
    if (frameworkIndex >= 0 && frameworkIndex + 1 >= args.Length)
        throw new ArgumentException("Missing value for --framework.");
    string framework = frameworkIndex < 0 ? "skecs" : args[frameworkIndex + 1].ToLowerInvariant();
    switch (framework)
    {
        case "skecs":
            using (var app = new SpaceBattle.Game.Skecs.BattleApp(args)) app.Run();
            break;
        case "lite":
        case "leoecslite":
        case "leoecs-lite":
            using (var app = new SpaceBattle.Game.LeoEcsLite.BattleApp(args)) app.Run();
            break;
        case "classic":
        case "leoecs":
        case "leoecsclassic":
        case "leoecs-classic":
            using (var app = new SpaceBattle.Game.LeoEcsClassic.BattleApp(args)) app.Run();
            break;
        case "morpeh":
            using (var app = new SpaceBattle.Game.Morpeh.BattleApp(args)) app.Run();
            break;
        case "static":
        case "staticecs":
        case "static-ecs":
            using (var app = new SpaceBattle.Game.StaticEcs.BattleApp(args)) app.Run();
            break;
        default:
            throw new ArgumentException("Unknown --framework. Use skecs, lite, classic, morpeh or static.");
    }
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
