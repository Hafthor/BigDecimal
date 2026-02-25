using BenchmarkDotNet.Running;
using BigDecimal;

var pi = new BigDecimal.BigDecimal(Math.PI);
Console.WriteLine(pi.ToString());
var d = double.Parse(pi.ToString());
Console.WriteLine(d);

var two = new BigDecimal.BigDecimal(2);
Console.WriteLine(two);

var twoDollars = new BigDecimal.BigDecimal(200, 2);
Console.WriteLine(twoDollars);
twoDollars = twoDollars.Normalize();
Console.WriteLine(twoDollars);

var hundred = new BigDecimal.BigDecimal(100);
Console.WriteLine(hundred);
hundred = hundred.Normalize();
Console.WriteLine(hundred);

BenchmarkRunner.Run<BigDecimalBenchmarks>();