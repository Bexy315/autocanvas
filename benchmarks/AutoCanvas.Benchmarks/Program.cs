using BenchmarkDotNet.Running;

// Run all:      dotnet run -c Release --project benchmarks/AutoCanvas.Benchmarks -- --filter '*'
// Quick check:  add --job short
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
