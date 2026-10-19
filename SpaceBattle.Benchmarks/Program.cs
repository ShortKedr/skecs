using SpaceBattle.Benchmarks;

return args.Length > 0 && args[0] == "measure"
    ? MeasurementProgram.Run(args[1..])
    : await BattleCli.RunAsync(args);
