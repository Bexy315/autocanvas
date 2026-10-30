using BenchmarkDotNet.Running;

// Run all:      dotnet run -c Release --project benchmarks/AutoCanvas.Core.Benchmarks -- --filter '*'
// Quick check:  add --job short
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
