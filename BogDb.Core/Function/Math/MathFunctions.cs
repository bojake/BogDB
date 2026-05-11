using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security.Cryptography;
using BogDb.Core.Common;
using SysMath = global::System.Math;

namespace BogDb.Core.Function.Mathematics;

/// <summary>
/// Additional math utility functions.
/// C++ parity: numeric helper functions spread across src/function/arithmetic*.
/// </summary>
internal static class MathFunctions
{
    internal static void Register(IDictionary<string, Func<object?[], object?>> r)
    {
        r["factorial"] = a => a.Length >= 1 ? (object?)Factorial(TypeCoercionHelper.ToInt64(a[0])) : null;
        r["gcd"]       = a => a.Length >= 2 ? (object?)Gcd(TypeCoercionHelper.ToInt64(a[0]), TypeCoercionHelper.ToInt64(a[1])) : null;
        r["lcm"]       = a => a.Length >= 2 ? (object?)Lcm(TypeCoercionHelper.ToInt64(a[0]), TypeCoercionHelper.ToInt64(a[1])) : null;
        r["hypot"]     = a => a.Length >= 2
            ? (object?)SysMath.Sqrt(
                SysMath.Pow(TypeCoercionHelper.ToDouble(a[0]), 2) +
                SysMath.Pow(TypeCoercionHelper.ToDouble(a[1]), 2))
            : null;
        r["bit_count"] = a => a.Length >= 1
            ? (object?)(long)BitOperations.PopCount((ulong)TypeCoercionHelper.ToInt64(a[0]))
            : null;
        r["xor"]       = a => a.Length >= 2
            ? (object?)(TypeCoercionHelper.ToInt64(a[0]) ^ TypeCoercionHelper.ToInt64(a[1]))
            : null;

        // ── Gamma functions ───────────────────────────────────────────────────
        // C++ parity: src/function/arithmetic/
        r["gamma"]  = a => a.Length >= 1
            ? (object?)GammaFunction(TypeCoercionHelper.ToDouble(a[0])) : null;
        r["lgamma"] = a => a.Length >= 1
            ? (object?)LogGammaFunction(TypeCoercionHelper.ToDouble(a[0])) : null;

        // setseed(x) — sets the random seed; C++ uses this for deterministic rand()
        r["setseed"] = a =>
        {
            // No-op in the C# port — Random.Shared is thread-safe and unseeded.
            // Accepting the call without error maintains Cypher compatibility.
            return null;
        };
    }

    private static long Factorial(long n)
    {
        if (n < 0) throw new ArgumentException("Factorial of negative number.");
        if (n > 20) throw new OverflowException("Factorial overflow for n > 20.");
        long result = 1;
        for (long i = 2; i <= n; i++) result *= i;
        return result;
    }

    private static long Gcd(long a, long b)
    {
        a = SysMath.Abs(a);
        b = SysMath.Abs(b);
        while (b != 0) { var t = b; b = a % b; a = t; }
        return a;
    }

    private static long Lcm(long a, long b)
    {
        if (a == 0 || b == 0) return 0;
        return SysMath.Abs(a / Gcd(a, b) * b);
    }

    /// <summary>Gamma function Γ(x) via Lanczos approximation.</summary>
    private static double GammaFunction(double x)
    {
        // For positive integers, Γ(n) = (n-1)!
        if (x <= 0 && SysMath.Abs(x - SysMath.Round(x)) < 1e-15)
            return double.PositiveInfinity; // poles at non-positive integers

        // Lanczos approximation coefficients (g=7)
        double[] c = { 0.99999999999980993, 676.5203681218851, -1259.1392167224028,
            771.32342877765313, -176.61502916214059, 12.507343278686905,
            -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7 };

        if (x < 0.5)
            return SysMath.PI / (SysMath.Sin(SysMath.PI * x) * GammaFunction(1 - x));

        x -= 1;
        double a = c[0];
        const double g = 7;
        for (int i = 1; i < c.Length; i++)
            a += c[i] / (x + i);

        double t = x + g + 0.5;
        return SysMath.Sqrt(2 * SysMath.PI) * SysMath.Pow(t, x + 0.5) * SysMath.Exp(-t) * a;
    }

    /// <summary>Log-gamma function ln(Γ(x)).</summary>
    private static double LogGammaFunction(double x)
    {
        var g = GammaFunction(x);
        return g > 0 ? SysMath.Log(g) : double.NaN;
    }
}
