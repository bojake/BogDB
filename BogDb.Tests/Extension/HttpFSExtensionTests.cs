using System;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using BogDb.Core.Main;
using BogDb.Extensions.HttpFS;

namespace BogDb.Tests.Extension
{
    public class HttpFSExtensionTests
    {
        [Fact]
        public void ParseS3Url_TranslatesBucketAndKeyCorrectly()
        {
            string s3Url = "s3://my-bucket/dataset/file.csv";
            
            // Setting a mock environment variable to test Region extraction
            Environment.SetEnvironmentVariable("AWS_REGION", "us-west-2");
            
            string parsed = new HttpFileSystem().ParseS3Url(s3Url);
            
            Assert.Equal("https://my-bucket.s3.us-west-2.amazonaws.com/dataset/file.csv", parsed);
        }

        [Fact]
        public void ParseS3Url_DefaultsToUsEast1WhenNoRegionSet()
        {
            string s3Url = "s3://bogdb-test/data.json";
            Environment.SetEnvironmentVariable("AWS_REGION", null);
            
            string parsed = new HttpFileSystem().ParseS3Url(s3Url);
            
            Assert.Equal("https://bogdb-test.s3.us-east-1.amazonaws.com/data.json", parsed);
        }

        private class MockS3HttpMessageHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(Send(request, cancellationToken));
            }

            protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.Method == HttpMethod.Head)
                {
                    var headResponse = new HttpResponseMessage(HttpStatusCode.OK);
                    headResponse.Content = new StringContent("");
                    headResponse.Content.Headers.ContentLength = 1000; // Mock FileSize 1000 bytes
                    return headResponse;
                }
                
                if (request.Method == HttpMethod.Get && request.Headers.Range != null)
                {
                    // Mock Range Read returning a simulated partial buffer gracefully
                    byte[] mockData = Encoding.UTF8.GetBytes("BOGDB-NG-S3-MOCK-DATA");
                    var getResponse = new HttpResponseMessage(HttpStatusCode.PartialContent);
                    getResponse.Content = new ByteArrayContent(mockData);
                    return getResponse;
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }

        [Fact]
        public void HttpFileInfo_DeterminesFileSizeViaHEADAndReadsPartialRangeSuccessfully()
        {
            var mockClient = new HttpClient(new MockS3HttpMessageHandler());
            
            // Initialize FileInfo which maps HEAD request natively
            var fileInfo = new HttpFileInfo("s3://test-bucket/file.bin", mockClient, new HttpFileSystem());

            Assert.Equal(1000, fileInfo.GetFileSize());

            // Validate Range Read extraction natively wrapping buffers cleanly
            byte[] buffer = new byte[21];
            fileInfo.Read(buffer, 0);

            string extracted = Encoding.UTF8.GetString(buffer);
            Assert.Equal("BOGDB-NG-S3-MOCK-DATA", extracted);
        }

        [Fact]
        public void LoadExtension_RegistersHttpSchemesAndOptions()
        {
            using var db = BogDatabase.CreateInMemory();

            new HttpFSExtension().Load(db);

            Assert.True(db.TryGetFileSystem("http", out var httpFs));
            Assert.True(db.TryGetFileSystem("https", out var httpsFs));
            Assert.True(db.TryGetFileSystem("s3", out var s3Fs));
            Assert.Same(httpFs, httpsFs);
            Assert.Same(httpFs, s3Fs);

            Assert.True(db.TryGetExtensionOption("s3_region", out var regionOption));
            Assert.Equal("us-east-1", db.GetExtensionOptionValue("s3_region"));
            Assert.Equal(BogDb.Core.Common.LogicalTypeID.STRING, regionOption.Type);
            Assert.True(db.TryGetExtensionOption("s3_secret_access_key", out var secretOption));
            Assert.True(secretOption.IsConfidential);
            Assert.Equal(false, db.GetExtensionOptionValue("http_cache_file"));
        }

        [Fact]
        public void CallOptionAssignment_UpdatesHttpfsExtensionOption()
        {
            using var db = BogDatabase.CreateInMemory();
            new HttpFSExtension().Load(db);
            using var conn = new BogConnection(db);

            var result = conn.Query("CALL s3_region='eu-central-1'");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal("eu-central-1", db.GetExtensionOptionValue("s3_region"));
        }

        private sealed class MockHttpScriptMessageHandler : HttpMessageHandler
        {
            private readonly byte[] _scriptBytes = Encoding.UTF8.GetBytes("CALL s3_region='ap-south-1';");

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(Send(request, cancellationToken));

            protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.Method == HttpMethod.Head)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    response.Content = new ByteArrayContent(Array.Empty<byte>());
                    response.Content.Headers.ContentLength = _scriptBytes.Length;
                    return response;
                }

                if (request.Method == HttpMethod.Get)
                {
                    return new HttpResponseMessage(HttpStatusCode.PartialContent)
                    {
                        Content = new ByteArrayContent(_scriptBytes)
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }

        [Fact]
        public void ExecuteScript_UsesRegisteredHttpFileSystem()
        {
            using var db = BogDatabase.CreateInMemory();
            new HttpFSExtension().Load(db);
            db.RegisterFileSystem("http", new HttpFileSystem(new HttpClient(new MockHttpScriptMessageHandler())));
            using var conn = new BogConnection(db);

            conn.ExecuteScript("http://example.test/script.cypher");
            Assert.Equal("ap-south-1", db.GetExtensionOptionValue("s3_region"));
        }

        private sealed class MockHttpCsvMessageHandler : HttpMessageHandler
        {
            private readonly byte[] _csvBytes = Encoding.UTF8.GetBytes(
                "id,name,age\n1,Alice,30\n2,Bob,25\n3,Charlie,35\n");

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(Send(request, cancellationToken));

            protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.Method == HttpMethod.Head)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    response.Content = new ByteArrayContent(Array.Empty<byte>());
                    response.Content.Headers.ContentLength = _csvBytes.Length;
                    return response;
                }

                if (request.Method == HttpMethod.Get)
                {
                    return new HttpResponseMessage(HttpStatusCode.PartialContent)
                    {
                        Content = new ByteArrayContent(_csvBytes)
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }

        private sealed class MockHttpMapMessageHandler : HttpMessageHandler
        {
            private readonly System.Collections.Generic.Dictionary<string, byte[]> _responses;

            public MockHttpMapMessageHandler(System.Collections.Generic.Dictionary<string, string> responses)
            {
                _responses = new System.Collections.Generic.Dictionary<string, byte[]>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (var (path, content) in responses)
                    _responses[path] = Encoding.UTF8.GetBytes(content);
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(Send(request, cancellationToken));

            protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var path = request.RequestUri?.AbsolutePath ?? "/";
                if (!_responses.TryGetValue(path, out var bytes))
                    return new HttpResponseMessage(HttpStatusCode.NotFound);

                if (request.Method == HttpMethod.Head)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    response.Content = new ByteArrayContent(Array.Empty<byte>());
                    response.Content.Headers.ContentLength = bytes.Length;
                    return response;
                }

                if (request.Method == HttpMethod.Get)
                {
                    return new HttpResponseMessage(HttpStatusCode.PartialContent)
                    {
                        Content = new ByteArrayContent(bytes)
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }

        [Fact]
        public void LoadFromCsv_UsesRegisteredHttpFileSystem()
        {
            using var db = BogDatabase.CreateInMemory();
            new HttpFSExtension().Load(db);
            db.RegisterFileSystem("http", new HttpFileSystem(new HttpClient(new MockHttpCsvMessageHandler())));
            using var conn = new BogConnection(db);

            var result = conn.Query(
                "LOAD WITH HEADERS (id INT64, name STRING, age INT64) " +
                "FROM 'http://example.test/people.csv' RETURN id, name ORDER BY id");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(3UL, result.GetNumTuples());
            Assert.True(result.HasNext());
            var first = result.GetNext();
            Assert.Equal(1L, first.GetInt64(0));
            Assert.Equal("Alice", first.GetString(1));
        }

        [Fact]
        public void CopyFromNodeTable_UsesRegisteredHttpFileSystem()
        {
            using var db = BogDatabase.CreateInMemory();
            new HttpFSExtension().Load(db);
            db.RegisterFileSystem(
                "http",
                new HttpFileSystem(new HttpClient(new MockHttpMapMessageHandler(
                    new System.Collections.Generic.Dictionary<string, string>
                    {
                        ["/people.csv"] = "id,name,age\n1,Alice,30\n2,Bob,25\n"
                    }))));

            using var conn = new BogConnection(db);
            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new System.Collections.Generic.Dictionary<string, BogDb.Core.Common.LogicalTypeID>
            {
                ["id"] = BogDb.Core.Common.LogicalTypeID.INT64,
                ["name"] = BogDb.Core.Common.LogicalTypeID.STRING,
                ["age"] = BogDb.Core.Common.LogicalTypeID.INT64
            });
            conn.Commit();

            var copyResult = conn.Query("COPY Person FROM 'http://example.test/people.csv'");
            Assert.True(copyResult.IsSuccess, copyResult.ErrorMessage);

            var queryResult = conn.Query("MATCH (p:Person) RETURN p.id, p.name ORDER BY p.id");
            Assert.True(queryResult.IsSuccess, queryResult.ErrorMessage);
            var first = queryResult.GetNext();
            Assert.Equal(1L, first.GetInt64(0));
            Assert.Equal("Alice", first.GetString(1));
            var second = queryResult.GetNext();
            Assert.Equal(2L, second.GetInt64(0));
            Assert.Equal("Bob", second.GetString(1));
        }

        [Fact]
        public void CopyFromRelTable_UsesRegisteredHttpFileSystem()
        {
            using var db = BogDatabase.CreateInMemory();
            new HttpFSExtension().Load(db);
            db.RegisterFileSystem(
                "http",
                new HttpFileSystem(new HttpClient(new MockHttpMapMessageHandler(
                    new System.Collections.Generic.Dictionary<string, string>
                    {
                        ["/people.csv"] = "id,name,age\n1,Alice,30\n2,Bob,25\n3,Charlie,35\n",
                        ["/knows.csv"] = "from_id,to_id,since\n1,2,2020\n2,3,2021\n"
                    }))));

            using var conn = new BogConnection(db);
            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new System.Collections.Generic.Dictionary<string, BogDb.Core.Common.LogicalTypeID>
            {
                ["id"] = BogDb.Core.Common.LogicalTypeID.INT64,
                ["name"] = BogDb.Core.Common.LogicalTypeID.STRING,
                ["age"] = BogDb.Core.Common.LogicalTypeID.INT64
            });
            conn.EnsureRelTable("Knows", "Person", "Person", new System.Collections.Generic.Dictionary<string, BogDb.Core.Common.LogicalTypeID>
            {
                ["since"] = BogDb.Core.Common.LogicalTypeID.INT64
            });
            conn.Commit();

            var nodeCopyResult = conn.Query("COPY Person FROM 'http://example.test/people.csv'");
            Assert.True(nodeCopyResult.IsSuccess, nodeCopyResult.ErrorMessage);

            var relCopyResult = conn.Query("COPY Knows FROM 'http://example.test/knows.csv'");
            Assert.True(relCopyResult.IsSuccess, relCopyResult.ErrorMessage);

            var queryResult = conn.Query(
                "MATCH (a:Person)-[k:Knows]->(b:Person) RETURN a.id, b.id, k.since ORDER BY a.id");
            Assert.True(queryResult.IsSuccess, queryResult.ErrorMessage);
            var first = queryResult.GetNext();
            Assert.Equal(1L, first.GetInt64(0));
            Assert.Equal(2L, first.GetInt64(1));
            Assert.Equal(2020L, first.GetInt64(2));
            var second = queryResult.GetNext();
            Assert.Equal(2L, second.GetInt64(0));
            Assert.Equal(3L, second.GetInt64(1));
            Assert.Equal(2021L, second.GetInt64(2));
        }

        // ── Retry tests ────────────────────────────────────────────────────────

        private sealed class TransientFailureHandler : HttpMessageHandler
        {
            private int _callCount;
            public int CallCount => _callCount;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(Send(request, cancellationToken));

            protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                _callCount++;
                // Fail first 2 attempts with 503, succeed on 3rd
                if (_callCount <= 2)
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

                if (request.Method == HttpMethod.Head)
                {
                    var resp = new HttpResponseMessage(HttpStatusCode.OK);
                    resp.Content = new ByteArrayContent(Array.Empty<byte>());
                    resp.Content.Headers.ContentLength = 42;
                    return resp;
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes("RETRY_SUCCESS"))
                };
            }
        }

        [Fact]
        public void SendWithRetry_RetriesOnTransientErrors()
        {
            var handler = new TransientFailureHandler();
            var config = new HttpFSConfig
            {
                MaxRetries = 3,
                RetryBaseDelay = TimeSpan.FromMilliseconds(10) // fast for tests
            };
            var fs = new HttpFileSystem(new HttpClient(handler), config);

            var request = new HttpRequestMessage(HttpMethod.Head, "https://example.com/test");
            var response = fs.SendWithRetry(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(3, handler.CallCount); // 2 failures + 1 success
        }

        [Fact]
        public void SendWithRetry_ThrowsAfterMaxRetries()
        {
            var handler = new AlwaysFailHandler();
            var config = new HttpFSConfig
            {
                MaxRetries = 2,
                RetryBaseDelay = TimeSpan.FromMilliseconds(10)
            };
            var fs = new HttpFileSystem(new HttpClient(handler), config);

            var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/test");
            // 3 total attempts (1 initial + 2 retries) all return 500
            var response = fs.SendWithRetry(request);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(3, handler.CallCount);
        }

        private sealed class AlwaysFailHandler : HttpMessageHandler
        {
            public int CallCount;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(Send(request, cancellationToken));

            protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                CallCount++;
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }
        }

        // ── S3 Auth tests ──────────────────────────────────────────────────────

        [Fact]
        public void S3Auth_DoesNotSignWithoutCredentials()
        {
            var config = new HttpFSConfig(); // no credentials set
            var request = new HttpRequestMessage(HttpMethod.Get, "https://bucket.s3.us-east-1.amazonaws.com/key");

            S3Auth.SignRequest(request, config);

            Assert.False(request.Headers.Contains("Authorization"));
            Assert.False(request.Headers.Contains("x-amz-date"));
        }

        [Fact]
        public void S3Auth_SignsRequestWithCredentials()
        {
            var config = new HttpFSConfig
            {
                S3AccessKeyId = "AKIAIOSFODNN7EXAMPLE",
                S3SecretAccessKey = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
                S3Region = "us-east-1"
            };
            var request = new HttpRequestMessage(HttpMethod.Get, "https://examplebucket.s3.us-east-1.amazonaws.com/test.txt");

            S3Auth.SignRequest(request, config);

            Assert.True(request.Headers.Contains("Authorization"));
            Assert.True(request.Headers.Contains("x-amz-date"));
            Assert.True(request.Headers.Contains("x-amz-content-sha256"));

            var auth = request.Headers.GetValues("Authorization").First();
            Assert.StartsWith("AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/", auth);
            Assert.Contains("SignedHeaders=", auth);
            Assert.Contains("Signature=", auth);
        }

        [Fact]
        public void S3Auth_IncludesSessionToken()
        {
            var config = new HttpFSConfig
            {
                S3AccessKeyId = "AKID",
                S3SecretAccessKey = "SECRET",
                S3SessionToken = "TOKEN123"
            };
            var request = new HttpRequestMessage(HttpMethod.Get, "https://bucket.s3.us-east-1.amazonaws.com/key");

            S3Auth.SignRequest(request, config);

            Assert.True(request.Headers.Contains("x-amz-security-token"));
            var token = request.Headers.GetValues("x-amz-security-token").First();
            Assert.Equal("TOKEN123", token);
        }

        // ── Config tests ───────────────────────────────────────────────────────

        [Fact]
        public void HttpFSConfig_DefaultValues()
        {
            var config = HttpFSConfig.Default;

            Assert.Equal(TimeSpan.FromSeconds(30), config.Timeout);
            Assert.Equal(3, config.MaxRetries);
            Assert.Null(config.S3AccessKeyId);
            Assert.False(config.HasS3Credentials);
            Assert.False(config.S3UsePathStyle);
        }

        [Fact]
        public void HttpFSConfig_ResolvesExplicitOverEnvVar()
        {
            Environment.SetEnvironmentVariable("AWS_REGION", "us-west-2");
            var config = new HttpFSConfig { S3Region = "eu-west-1" };

            Assert.Equal("eu-west-1", config.ResolvedRegion);

            // Clean up
            Environment.SetEnvironmentVariable("AWS_REGION", null);
        }

        [Fact]
        public void ParseS3Url_PathStyle_FormatsCorrectly()
        {
            var config = new HttpFSConfig
            {
                S3Endpoint = "localhost:9000",
                S3UsePathStyle = true
            };
            var fs = new HttpFileSystem(new HttpClient(), config);

            var url = fs.ParseS3Url("s3://mybucket/mykey/file.csv");

            Assert.Equal("https://localhost:9000/mybucket/mykey/file.csv", url);
        }

        [Fact]
        public void ParseS3Url_NonS3Url_PassesThrough()
        {
            var fs = new HttpFileSystem();
            Assert.Equal("https://example.com/file.csv", fs.ParseS3Url("https://example.com/file.csv"));
        }
    }
}
