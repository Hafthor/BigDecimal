using System.Collections.ObjectModel;
using System.Numerics;
using System.Buffers;

namespace BigDecimal;

public readonly struct BigDecimal { // : IComparable<BigDecimal>, IEquatable<BigDecimal> {
    private readonly BigInteger value; // zero might be special value
    private readonly int exponent; // minvalue=-INF, maxvalue=+INF, minvalue+1=Signaling NaN, maxvalue-1=Quiet NaN

    private const int PosInfExponent = int.MaxValue;
    private const int NegInfExponent = int.MinValue;
    private const int NaNExponent = int.MaxValue - 1;
    private const int SignalingNaNExponent = int.MinValue + 1;
    public static readonly BigDecimal Zero = new(BigInteger.Zero);
    public static readonly BigDecimal One = new(BigInteger.One);
    public static readonly BigDecimal PosInf = new(BigInteger.Zero, PosInfExponent);
    public static readonly BigDecimal NegInf = new(BigInteger.Zero, NegInfExponent);
    public static readonly BigDecimal NaN = new(BigInteger.Zero, NaNExponent);
    public static readonly BigDecimal SignalingNaN = new(BigInteger.Zero, SignalingNaNExponent);

    private static readonly SearchValues<char> ExpChars = SearchValues.Create("DEde");
    
    // Cache for expensive constant calculations - stores only the most precise significand computed so far
    // Each constant has a known exponent: Pi, E, Ln10 are in [1,2) so exponent adjusts to put MSB at precisionBits
    // Ln2 is in [0.5,1) so it's 2^-1 relative to the others
    // We store the significand normalized to have exactly _constPrecisions bits
    private enum Consts {
        Pi,
        E,
        Ln2,
        Ln10
    }

    // private static readonly ReadOnlyCollection<Func<long, BigDecimal>> _constFuncs =
    //     [ComputePi, ComputeE, ComputeLn2, ComputeLn10];

    private static readonly ReadOnlyCollection<object> ConstLocks = [new(), new(), new(), new()];
    private static readonly BigInteger[] ConstValues =
        [BigInteger.Zero, BigInteger.Zero, BigInteger.Zero, BigInteger.Zero];
    private static readonly int[] ConstPrecisions = new int[4];

    public BigDecimal(BigInteger value, int decimalPrecision = 0) {
        this.value = value;
        exponent = -decimalPrecision;
    }

    public BigDecimal(long value, int decimalPrecision = 0) {
        this.value = value;
        exponent = -decimalPrecision;
    }
    
    public BigDecimal(int value, int decimalPrecision = 0) {
        this.value = value;
        exponent = -decimalPrecision;
    }

    public BigDecimal(double value, int decimalPrecision = 15) {
        var v = Parse(value.ToString("E" + decimalPrecision, System.Globalization.CultureInfo.InvariantCulture));
        this.value = v.value;
        exponent = v.exponent;
    }
    
    public BigDecimal(float value, int decimalPrecision = 8) {
        var v = Parse(value.ToString("E" + decimalPrecision, System.Globalization.CultureInfo.InvariantCulture));
        this.value = v.value;
        exponent = v.exponent;
    }
    
    public BigDecimal(Half value, int decimalPrecision = 4) {
        var v = Parse(value.ToString("E" + decimalPrecision, System.Globalization.CultureInfo.InvariantCulture));
        this.value = v.value;
        exponent = v.exponent;
    }

    public static BigDecimal Parse(string value) {
        // Handle special values (case-insensitive)
        if (value.Equals("nan", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("+nan", StringComparison.OrdinalIgnoreCase)) return NaN;
        if (value.Equals("-nan", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("snan", StringComparison.OrdinalIgnoreCase)) return SignalingNaN;
        if (value.Equals("inf", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("+inf", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("infinity", StringComparison.OrdinalIgnoreCase) || 
            value.Equals("+infinity", StringComparison.OrdinalIgnoreCase)) return PosInf;
        if (value.Equals("-inf", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("-infinity", StringComparison.OrdinalIgnoreCase)) return NegInf;

        var v = _Parse(value);
        return new(v.value, -v.exponent);
    }

    private static (BigInteger value, int exponent) _Parse(string value) {
        // // Handle special values (case-insensitive)
        // if (value.Equals("nan", StringComparison.OrdinalIgnoreCase) ||
        //     value.Equals("+nan", StringComparison.OrdinalIgnoreCase)) return (BigInteger.Zero, NaNExponent);
        // if (value.Equals("-nan", StringComparison.OrdinalIgnoreCase) ||
        //     value.Equals("snan", StringComparison.OrdinalIgnoreCase)) return (BigInteger.Zero, SignalingNaNExponent);
        // if (value.Equals("inf", StringComparison.OrdinalIgnoreCase) ||
        //     value.Equals("+inf", StringComparison.OrdinalIgnoreCase) ||
        //     value.Equals("infinity", StringComparison.OrdinalIgnoreCase) ||
        //     value.Equals("+infinity", StringComparison.OrdinalIgnoreCase)) return (BigInteger.Zero, PosInfExponent);
        // if (value.Equals("-inf", StringComparison.OrdinalIgnoreCase) ||
        //     value.Equals("-infinity", StringComparison.OrdinalIgnoreCase)) return (BigInteger.Zero, NegInfExponent);

        ReadOnlySpan<char> valueSpan = value.AsSpan();
        int exp = 0;
        int e = valueSpan.IndexOfAny(ExpChars);
        if (e >= 0) {
            exp = int.Parse(valueSpan[(e + 1)..]);
            valueSpan = valueSpan[..e];
        }
        int dp = valueSpan.IndexOf('.');
        if (dp < 0) return (BigInteger.Parse(valueSpan), exp);

        var tail = valueSpan[(dp + 1)..];
        return (BigInteger.Parse(string.Concat(valueSpan[..dp], tail)), exp - tail.Length);
    }

    public bool IsFinite => exponent is <= int.MaxValue - 1 and >= int.MinValue + 1 || value != BigInteger.Zero;
    public bool IsNaN => exponent is int.MinValue + 1 or int.MaxValue - 1;
    public bool IsInfinity => exponent is int.MinValue or int.MaxValue;
    public bool IsSignalingNaN => exponent is int.MinValue + 1;
    public bool IsQuietNaN => exponent is int.MaxValue - 1;
    public bool IsPositiveInfinity => exponent is int.MaxValue;
    public bool IsNegativeInfinity => exponent is int.MinValue;
    
    public override string ToString() {
        if (!IsFinite) return IsNaN ? IsQuietNaN ? "NaN" : "sNaN" : IsPositiveInfinity ? "Infinity" : "-Infinity";
        string s = value.ToString();
        return exponent == 0 ? s :
            -exponent < s.Length && exponent < 0 ? s.Insert(s.Length + exponent, ".") : s + "E" + exponent;
    }

    private static readonly BigInteger TenNonillion = new(10_000_000_000_000_000_000_000_000_000m);
    private static readonly BigInteger[] PowersOfTen = [new(10_000_000_000_000_000), new(100_000_000), new(10_000), new(100), new(10)];

    public BigDecimal Normalize() {
        if (!IsFinite) return this;
        BigInteger v = value;
        int e = exponent;
        while (v >= TenNonillion && v % TenNonillion == 0) {
            v /= TenNonillion;
            e += 28;
        }
        int adj = 16;
        foreach (var div in PowersOfTen) {
            if (v >= div && v % div == 0) {
                v /= div;
                e += adj;
            }
            adj >>= 1;
        }
        return new(v, -e);
    }
}