```

BenchmarkDotNet v0.13.12, Ubuntu 24.04.4 LTS (Noble Numbat) (container)
AMD EPYC 9V74, 1 CPU, 2 logical cores and 1 physical core
.NET SDK 10.0.400
  [Host]     : .NET 9.0.19 (9.0.1926.36724), X64 RyuJIT AVX2
  DefaultJob : .NET 9.0.19 (9.0.1926.36724), X64 RyuJIT AVX2


```
| Method              | Mean               | Error           | StdDev          | Gen0   | Allocated |
|-------------------- |-------------------:|----------------:|----------------:|-------:|----------:|
| CalculateOrderTotal |  50,475,679.293 ns |  38,177.7509 ns |  33,843.5818 ns |      - |    1978 B |
| ValidateOrderAsync  | 141,459,286.767 ns | 361,419.9941 ns | 338,072.4867 ns |      - |    2376 B |
| GetAllProducts      |       3,115.524 ns |      59.5336 ns |     110.3493 ns | 0.0153 |     272 B |
| GetProductById      |           3.784 ns |       0.1588 ns |       0.4156 ns |      - |         - |
| SearchProducts      |       2,832.974 ns |      56.4852 ns |     139.6175 ns | 1.3008 |   21776 B |
| GetLowStockProducts | 221,985,740.222 ns | 331,316.1582 ns | 309,913.3399 ns |      - |    4141 B |
