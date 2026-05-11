using System;
using System.Collections.Generic;

namespace BogDb.Core.Function.Uuid;

/// <summary>
/// C++ parity: <c>src/function/uuid/gen_random_uuid.cpp</c>
///
/// UUID scalar functions:
///   gen_random_uuid()       → new random UUID string (RFC 4122 v4)
///   uuid()                  → alias for gen_random_uuid()
///   uuid_to_string(bytes)   → convert binary UUID to string form
///   string_to_uuid(str)     → parse UUID string, return canonical lowercase string
///   uuid_version(str)       → extract version nibble (1-7)
/// </summary>
public static class UuidFunctions
{
    public static void Register(Dictionary<string, Func<object?[], object?>> funcs)
    {
        // gen_random_uuid() — standard RFC 4122 v4
        funcs["gen_random_uuid"] = _ => Guid.NewGuid().ToString();
        funcs["uuid"]            = _ => Guid.NewGuid().ToString();

        // uuid_to_string(bytes | string) — normalise to lowercase canonical form
        funcs["uuid_to_string"] = args =>
        {
            if (args.Length == 0 || args[0] == null) return null;
            var s = args[0]!.ToString()!;
            if (Guid.TryParse(s, out var g)) return g.ToString();
            return s.ToLowerInvariant();
        };

        // string_to_uuid(str) — parse and return canonical lower-case UUID
        funcs["string_to_uuid"] = args =>
        {
            if (args.Length == 0 || args[0] == null) return null;
            return Guid.TryParse(args[0]!.ToString(), out var g) ? g.ToString() : null;
        };

        // uuid_version(str) — extract version nibble from UUID string
        funcs["uuid_version"] = args =>
        {
            if (args.Length == 0 || args[0] == null) return null;
            if (!Guid.TryParse(args[0]!.ToString(), out var g)) return null;
            // Version nibble is the first hex digit of the 3rd group
            var bytes = g.ToByteArray();
            // In .NET, Guid layout: bytes 6-7 carry version in high nibble of byte 7
            return (long)((bytes[7] >> 4) & 0xF);
        };
    }
}
