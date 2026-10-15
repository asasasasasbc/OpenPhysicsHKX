// Mechanically extracted from the hash-pinned C# delivery. See docs/PROVENANCE.md.
// PyNum -- a correctly-rounded decimal-literal -> double reader, for the C# port.
//
// WHY THIS EXISTS (measured in round 4, tools/.stage/cs-s4-parse-fix.py): on 7498 real literals from
// these input files, three available strategies disagree with CPython's strtod:
//     A  JavaScriptSerializer + Convert.ToDouble    378 / 7498 wrong (5.04%)
//     B  decimal.Parse -> (double)                  378 / 7498 wrong (identical to A, because the
//                                                   serializer takes the decimal path)
//     C  double.Parse(lit, NumberStyles.Float)        3 / 7498 wrong
// and a type probe showed the failing path is decimal: JavaScriptSerializer produces a Decimal for these
// literals, and .NET Framework's decimal->double conversion is NOT correctly rounded. The literal
// -0.21947240829467773 comes out one ULP low that way. Since about 5% of the floats in these files are
// such literals, the port started every run with a scattering of one-ULP-wrong inputs -- which is exactly
// the profile the end-to-end comparison showed (about 3 ULP after the first frame on both games).
//
// HOW IT WORKS, and why it is provably correct rather than merely better:
//   1. The literal is turned into an EXACT rational: value = sign * mantissa * 10^exp, with mantissa an
//      arbitrary-precision integer, so no precision is lost in reading it.
//   2. double.Parse gives a candidate that is within one ULP of the true value (measured: 3 wrong out of
//      7498, all by one ULP).
//   3. The candidate is decomposed EXACTLY as M * 2^E (M a 53-bit integer), so its two neighbours' exact
//      midpoints are (2M-1) * 2^(E-1) and (2M+1) * 2^(E-1).
//   4. The exact rational is compared against those midpoints with INTEGER arithmetic only -- by clearing
//      the negative powers of ten and two across the comparison -- so the comparison is exact, not
//      approximate.
//   5. If the value lies outside the interval, the candidate is moved one ULP and re-tested; a tie is
//      broken by round-half-to-even.
// Because every step is exact, the result is the correctly rounded double for the literal, which is what
// CPython's json does. The only assumption is (2), and the code VERIFIES it by checking that the value
// falls inside the outer interval; if it ever did not, it reports rather than silently guessing.
using System;
using System.Globalization;
using System.Numerics;

namespace OpenPhysicsHKX.Internal
{
internal static class PyNum
{
    public static bool Trace;                     // set to report every correction, for the tests
    public static int Corrections;                // how many literals double.Parse got wrong
    public static int Unverified;                 // how many fell outside the one-ULP window

    static BigInteger Pow10(int n)
    {
        BigInteger r = BigInteger.One;
        BigInteger b = 10;
        while (n > 0)
        {
            if ((n & 1) != 0) r *= b;
            b *= b;
            n >>= 1;
        }
        return r;
    }

    static BigInteger Pow2(int n)
    {
        return BigInteger.One << n;
    }

    /// <summary>Exact comparison of m*10^e against N*2^F, both with m,N >= 0.</summary>
    /// <returns>-1, 0 or 1.</returns>
    static int CompareDecToDyadic(BigInteger m, int e, BigInteger N, int F)
    {
        BigInteger lhs = m, rhs = N;
        if (e > 0) lhs *= Pow10(e); else if (e < 0) rhs *= Pow10(-e);
        if (F > 0) rhs *= Pow2(F); else if (F < 0) lhs *= Pow2(-F);
        return lhs.CompareTo(rhs);
    }

    /// <summary>Exact decomposition of a positive finite double as M * 2^E.</summary>
    static void Decompose(double d, out BigInteger M, out int E)
    {
        long bits = BitConverter.DoubleToInt64Bits(d);
        int exp = (int)((bits >> 52) & 0x7FF);
        long frac = bits & 0xFFFFFFFFFFFFFL;
        if (exp == 0) { M = frac; E = -1074; }                  // subnormal
        else { M = frac | (1L << 52); E = exp - 1075; }
    }

    /// <summary>A correctly-rounded decimal-literal -> double reader.</summary>
    public static double Parse(string lit)
    {
        if (lit == null) return 0.0;
        string s = lit.Trim();
        if (s.Length == 0) return 0.0;

        // ---- 1. exact rational: sign * mantissa * 10^exp
        int i = 0;
        int sign = 1;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) { if (s[i] == '-') sign = -1; i++; }

        var digits = new System.Text.StringBuilder();
        int exp10 = 0;
        bool seenDot = false;
        int fracDigits = 0;
        bool any = false;
        while (i < s.Length)
        {
            char c = s[i];
            if (c >= '0' && c <= '9')
            {
                digits.Append(c);
                if (seenDot) fracDigits++;
                any = true;
                i++;
            }
            else if (c == '.' && !seenDot) { seenDot = true; i++; }
            else break;
        }
        if (!any) return double.NaN;
        exp10 = -fracDigits;
        int esign = 1, eval = 0;
        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            i++;
            if (i < s.Length && (s[i] == '+' || s[i] == '-')) { if (s[i] == '-') esign = -1; i++; }
            bool anyE = false;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') { eval = eval * 10 + (s[i] - '0'); anyE = true; i++; }
            if (!anyE) return double.NaN;
            exp10 += esign * eval;
        }
        if (i != s.Length) return double.NaN;                  // trailing junk: not a bare number

        var m = BigInteger.Parse(digits.ToString(), CultureInfo.InvariantCulture);
        if (m.IsZero) return sign < 0 ? -0.0 : 0.0;

        // ---- 2. candidate from the Framework parser (within one ULP, verified below)
        //
        // FrameworkNote: .NET Framework's double.Parse THROWS OverflowException for a literal beyond the
        // largest finite double (1.7976931348623159e308), whereas CPython's float() and json return inf
        // for the same input. My own hard-case set found this: the reader crashed on that literal and
        // silently produced nine fewer results than inputs. The port must behave like Python here, so the
        // overflow is caught and turned into an infinity with the literal's sign.
        double cand;
        try
        {
            cand = double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            return sign < 0 ? double.NegativeInfinity : double.PositiveInfinity;
        }
        if (double.IsInfinity(cand) || double.IsNaN(cand))
        {
            return sign * cand;
        }
        double a = Math.Abs(cand);

        // ---- 3./4./5. exact midpoint comparison, moving at most a couple of ULPs
        for (int guard = 0; guard < 4; guard++)
        {
            if (a == 0.0) break;
            BigInteger M; int E;
            Decompose(a, out M, out E);
            int lo = CompareDecToDyadic(m, exp10, 2 * M - 1, E - 1);
            int hi = CompareDecToDyadic(m, exp10, 2 * M + 1, E - 1);
            if (lo < 0) { a = StepDown(a); Corrections++; continue; }       // candidate too high
            if (hi > 0) { a = StepUp(a); Corrections++; continue; }          // candidate too low
            if (lo == 0) { a = TieToEven(a, true); }                         // exactly the lower midpoint
            else if (hi == 0) { a = TieToEven(a, false); }                    // exactly the upper midpoint
            if (Trace) Console.Error.WriteLine("PyNum: {0} -> {1:R} (lo={2} hi={3})", lit, sign * a, lo, hi);
            return sign * a;
        }
        Unverified++;
        Console.Error.WriteLine("PyNum: could not verify {0} within the one-ULP window; returning double.Parse", lit);
        try { return sign * double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture); }
        catch (OverflowException) { return sign < 0 ? double.NegativeInfinity : double.PositiveInfinity; }
    }

    static double StepUp(double a)
    {
        long b = BitConverter.DoubleToInt64Bits(a);
        return BitConverter.Int64BitsToDouble(b + 1);
    }

    static double StepDown(double a)
    {
        long b = BitConverter.DoubleToInt64Bits(a);
        return BitConverter.Int64BitsToDouble(b - 1);
    }

    /// <summary>At an exact midpoint, round to the neighbour with an even mantissa.</summary>
    static double TieToEven(double a, bool lowerMidpoint)
    {
        // a is a candidate; if its mantissa is odd, the other neighbour is the even one.
        long bits = BitConverter.DoubleToInt64Bits(a);
        bool odd = ((bits & 1L) != 0);
        if (!odd) return a;
        return lowerMidpoint ? StepDown(a) : StepUp(a);
    }
}
}
