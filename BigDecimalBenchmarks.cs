using BenchmarkDotNet.Attributes;

namespace BigDecimal;

[MemoryDiagnoser]
public class BigDecimalBenchmarks {
    [Benchmark]
    public BigDecimal Parse() => BigDecimal.Parse("3.14159265359");
}