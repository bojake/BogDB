using System;
using Xunit;
using BogDb.Core.Function;

namespace BogDb.Tests.Function;

public sealed class ArithmeticFunctionTests
{
    [Fact] public void Sin_ReturnsCorrectValue()
        => Assert.Equal(0.0, (double)FunctionDispatcher.Invoke("sin", [0.0])!, 5);

    [Fact] public void Cos_ReturnsOne()
        => Assert.Equal(1.0, (double)FunctionDispatcher.Invoke("cos", [0.0])!, 5);

    [Fact] public void Tan_ReturnsCorrectValue()
        => Assert.InRange((double)FunctionDispatcher.Invoke("tan", [Math.PI / 4.0])!, 0.999, 1.001);

    [Fact] public void Asin_RoundTrip()
        => Assert.Equal(0.5, (double)FunctionDispatcher.Invoke("asin", [Math.Sin(0.5)])!, 5);

    [Fact] public void Log10_Of100()
        => Assert.Equal(2.0, (double)FunctionDispatcher.Invoke("log", [100.0])!, 10);

    [Fact] public void Ln_OfE()
        => Assert.Equal(1.0, (double)FunctionDispatcher.Invoke("ln", [Math.E])!, 10);

    [Fact] public void Log2_Of8()
        => Assert.Equal(3.0, (double)FunctionDispatcher.Invoke("log2", [8.0])!, 10);

    [Fact] public void Exp_OfZero()
        => Assert.Equal(1.0, (double)FunctionDispatcher.Invoke("exp", [0.0])!, 10);

    [Fact] public void Pi_Constant()
        => Assert.Equal(Math.PI, (double)FunctionDispatcher.Invoke("pi", [])!, 10);

    [Fact] public void E_Constant()
        => Assert.Equal(Math.E, (double)FunctionDispatcher.Invoke("e", [])!, 10);

    [Fact] public void Sign_Negative()
        => Assert.Equal(-1L, FunctionDispatcher.Invoke("sign", [-5.0]));

    [Fact] public void Sign_Positive()
        => Assert.Equal(1L, FunctionDispatcher.Invoke("sign", [3.0]));

    [Fact] public void Degrees_FromRadians()
        => Assert.Equal(180.0, (double)FunctionDispatcher.Invoke("degrees", [Math.PI])!, 5);

    [Fact] public void Radians_FromDegrees()
        => Assert.Equal(Math.PI, (double)FunctionDispatcher.Invoke("radians", [180.0])!, 10);

    [Fact] public void Cbrt_Of27()
        => Assert.Equal(3.0, (double)FunctionDispatcher.Invoke("cbrt", [27.0])!, 10);

    [Fact] public void Round_WithPrecision()
        => Assert.Equal(3.14, (double)FunctionDispatcher.Invoke("round", [3.14159, 2L])!, 5);

    [Fact] public void IsFinite_True()
        => Assert.Equal(true, FunctionDispatcher.Invoke("isfinite", [1.0]));

    [Fact] public void IsInf_True()
        => Assert.Equal(true, FunctionDispatcher.Invoke("isinf", [double.PositiveInfinity]));

    [Fact] public void IsNan_True()
        => Assert.Equal(true, FunctionDispatcher.Invoke("isnan", [double.NaN]));
}
