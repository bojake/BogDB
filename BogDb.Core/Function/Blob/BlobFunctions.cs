using System;
using System.Collections.Generic;
using System.Text;

namespace BogDb.Core.Function.Blob;

/// <summary>
/// Blob scalar functions.
/// C++ parity: src/function/vector_blob_functions.cpp
/// </summary>
internal static class BlobFunctions
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal static void Register(IDictionary<string, Func<object?[], object?>> r)
    {
        r["octet_length"] = a =>
        {
            if (a.Length < 1 || a[0] == null) return null;
            var bytes = ToBytes(a[0]);
            return bytes != null ? (object?)(long)bytes.Length : null;
        };

        r["encode"] = a =>
        {
            if (a.Length < 1 || a[0] == null) return null;
            return (object?)ToBytes(a[0]);
        };

        r["decode"] = a =>
        {
            if (a.Length < 1 || a[0] == null) return null;
            var bytes = ToBytes(a[0]);
            if (bytes == null) return null;
            try
            {
                return (object?)StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                throw new InvalidOperationException(
                    "Failure in decode: could not convert blob to UTF8 string, " +
                    "the blob contained invalid UTF8 characters");
            }
        };
    }

    private static byte[]? ToBytes(object? value)
    {
        return value switch
        {
            byte[] b => b,
            string s => Encoding.UTF8.GetBytes(s),
            _ => null
        };
    }
}
