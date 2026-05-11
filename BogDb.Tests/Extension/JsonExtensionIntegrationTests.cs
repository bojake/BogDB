using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using BogDb.Core.Main;
using BogDb.Core.Extension;
using BogDb.Extensions.Json;
using BogDb.Extensions.HttpFS;

namespace BogDb.Tests.Extension
{
    /// <summary>
    /// Integration tests that validate the end-to-end extension wiring:
    ///   ExtensionManager.LoadExtension → FunctionRegistry.Register
    ///   BogConnection.Query("LOAD FROM 'path' RETURN *") → QueryResult rows
    ///
    /// These are the C# equivalent of the C++ scan_json.test -CASE TinySNBSubset assertions.
    /// </summary>
    public class JsonExtensionIntegrationTests
    {
        private sealed class MockHttpJsonMessageHandler : HttpMessageHandler
        {
            private readonly byte[] _jsonBytes;

            public MockHttpJsonMessageHandler(string content)
            {
                _jsonBytes = Encoding.UTF8.GetBytes(content);
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(Send(request, cancellationToken));

            protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.Method == HttpMethod.Head)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    response.Content = new ByteArrayContent(Array.Empty<byte>());
                    response.Content.Headers.ContentLength = _jsonBytes.Length;
                    return response;
                }

                if (request.Method == HttpMethod.Get)
                {
                    return new HttpResponseMessage(HttpStatusCode.PartialContent)
                    {
                        Content = new ByteArrayContent(_jsonBytes)
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }
        // ── helpers ──────────────────────────────────────────────────────────────

        private static string ResolvePath(string relative)
        {
            var normalised = relative.Replace('\\', Path.DirectorySeparatorChar);
            var segments = normalised.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            var tail = string.Join(Path.DirectorySeparatorChar.ToString(),
                segments.SkipWhile(s => s == ".."));

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, tail);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return relative;
        }

        private static BogDatabase CreateDbWithJson()
        {
            var db = BogDatabase.Open(":memory:");
            new JsonExtension().Load(db);  // register directly (simulates ExtensionManager.LoadExtension)
            return db;
        }

        // ── registration tests ────────────────────────────────────────────────────

        [Fact]
        public void LoadExtension_RegistersScanJsonArrayFunction()
        {
            var db = BogDatabase.Open(":memory:");
            Assert.False(db.FunctionRegistry.Contains("scan_json_array"),
                "Function should not be registered before Load()");
            Assert.False(db.StandaloneTableFunctionRegistry.Contains("scan_json_array"));
            Assert.False(db.ScalarFunctionRegistry.Contains("json_valid"));

            new JsonExtension().Load(db);

            Assert.True(db.FunctionRegistry.Contains("scan_json_array"),
                "scan_json_array should be registered after JsonExtension.Load()");
            Assert.True(db.StandaloneTableFunctionRegistry.Contains("scan_json_array"));
            Assert.True(db.ScalarFunctionRegistry.Contains("json_valid"));
        }

        // ── LOAD FROM query tests ─────────────────────────────────────────────────

        [Fact]
        public void LoadFrom_ViaQuery_VMovies_ReturnsThreeRows()
        {
            // C++ equivalent: LOAD FROM "tinysnb_json/vMovies.json" RETURN *;  ---- 3
            using var db = CreateDbWithJson();
            using var conn = new BogConnection(db);

            var path = ResolvePath("../../../../dataset/tinysnb_json/vMovies.json");
            var result = conn.Query($"LOAD FROM '{path}' RETURN *");

            Assert.True(result.IsSuccess, $"Query failed: {result.ErrorMessage}");

            var rows = new System.Collections.Generic.List<BogDb.Core.Main.QueryResult.BogRow>();
            while (result.HasNext()) rows.Add(result.GetNext());

            Assert.Equal(3, rows.Count);
        }

        [Fact]
        public void LoadFrom_ViaQuery_VMovies_FirstRowHasNameKey()
        {
            // C++ equivalent: first result row has field "name"
            using var db = CreateDbWithJson();
            using var conn = new BogConnection(db);

            var path = ResolvePath("../../../../dataset/tinysnb_json/vMovies.json");
            var result = conn.Query($"LOAD FROM '{path}' RETURN *");

            Assert.True(result.IsSuccess, $"Query failed: {result.ErrorMessage}");
            Assert.True(result.HasNext());

            var firstRow = result.GetNext().GetAsDictionary();
            Assert.True(firstRow.ContainsKey("name"), $"Expected 'name' key; got: {string.Join(", ", firstRow.Keys)}");
            Assert.NotNull(firstRow["name"]);
        }

        [Fact]
        public void LoadFrom_ViaQuery_ArrayTest_ReturnsTwoRows()
        {
            // C++ equivalent: LOAD WITH HEADERS (lst INT8[]) FROM "array-test.json" RETURN *;  ---- 2
            using var db = CreateDbWithJson();
            using var conn = new BogConnection(db);

            var path = ResolvePath("../../../../dataset/json-misc/array-test.json");
            var result = conn.Query($"LOAD FROM '{path}' RETURN *");

            Assert.True(result.IsSuccess, $"Query failed: {result.ErrorMessage}");

            var count = 0;
            while (result.HasNext()) { result.GetNext(); count++; }
            Assert.Equal(2, count);
        }

        [Fact]
        public void LoadFrom_ViaQuery_ParameterPath_ReturnsThreeRows()
        {
            using var db = CreateDbWithJson();
            using var conn = new BogConnection(db);

            var path = ResolvePath("../../../../dataset/tinysnb_json/vMovies.json");
            var result = conn.Query(
                "LOAD FROM $path RETURN *",
                new System.Collections.Generic.Dictionary<string, object?> { ["path"] = path });

            Assert.True(result.IsSuccess, $"Query failed: {result.ErrorMessage}");

            var count = 0;
            while (result.HasNext()) { result.GetNext(); count++; }
            Assert.Equal(3, count);
        }

        [Fact]
        public void LoadFrom_ViaQuery_MissingFile_ReturnsError()
        {
            // Extension executes but the file doesn't exist — should return an error result
            using var db = CreateDbWithJson();
            using var conn = new BogConnection(db);

            var result = conn.Query("LOAD FROM '/nonexistent/ghost.json' RETURN *");

            Assert.False(result.IsSuccess, "Expected failure for missing file");
            Assert.Contains("ghost.json", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void LoadFrom_ViaQuery_WithoutExtensionLoaded_ReturnsRegistryError()
        {
            // FunctionRegistry is empty — scan_json_array not registered
            using var db = BogDatabase.Open(":memory:");  // NO extension load
            using var conn = new BogConnection(db);

            var result = conn.Query("LOAD FROM '/any/path.json' RETURN *");

            Assert.False(result.IsSuccess);
            Assert.Contains("scan_json_array", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        [Fact]
        public void ReturnJsonValid_ViaQuery_UsesExtensionScalarRegistry()
        {
            using var db = CreateDbWithJson();
            using var conn = new BogConnection(db);

            var result = conn.Query("RETURN json_valid('{\"a\":55,\"b\":72}')");

            Assert.True(result.IsSuccess, $"Query failed: {result.ErrorMessage}");
            Assert.True(result.HasNext());
            Assert.True(result.GetNext().GetBoolean(0));
        }

        [Fact]
        public void ReturnJsonValid_ViaQuery_InvalidJsonReturnsFalse()
        {
            using var db = CreateDbWithJson();
            using var conn = new BogConnection(db);

            var result = conn.Query("RETURN json_valid('{\"a\":55,\"b\":72,}')");

            Assert.True(result.IsSuccess, $"Query failed: {result.ErrorMessage}");
            Assert.True(result.HasNext());
            Assert.False(result.GetNext().GetBoolean(0));
        }

        [Fact]
        public void CallScanJsonArray_ViaStandaloneRegistry_ReturnsRows()
        {
            using var db = CreateDbWithJson();
            using var conn = new BogConnection(db);

            var path = ResolvePath("../../../../dataset/tinysnb_json/vMovies.json").Replace('\\', '/');
            var result = conn.Query($"CALL scan_json_array('{path}') RETURN *");

            Assert.True(result.IsSuccess, $"Query failed: {result.ErrorMessage}");

            var count = 0;
            while (result.HasNext()) { result.GetNext(); count++; }
            Assert.Equal(3, count);
        }

        [Fact]
        public void LoadFrom_ViaHttpFileSystem_UsesRegisteredFileSystem()
        {
            using var db = CreateDbWithJson();
            db.RegisterFileSystem(
                "http",
                new HttpFileSystem(new HttpClient(new MockHttpJsonMessageHandler(
                    "[{\"id\":1,\"name\":\"Alice\"},{\"id\":2,\"name\":\"Bob\"}]"))));
            using var conn = new BogConnection(db);

            var result = conn.Query("LOAD FROM 'http://example.test/people.json' RETURN *");

            Assert.True(result.IsSuccess, $"Query failed: {result.ErrorMessage}");
            Assert.True(result.HasNext());
            var first = result.GetNext().GetAsDictionary();
            Assert.Equal(1L, first["id"]);
            Assert.Equal("Alice", first["name"]);
            Assert.True(result.HasNext());
            var second = result.GetNext().GetAsDictionary();
            Assert.Equal(2L, second["id"]);
            Assert.Equal("Bob", second["name"]);
        }
    }
}
