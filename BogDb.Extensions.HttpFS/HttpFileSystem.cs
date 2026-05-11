using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using BogDb.Core.Common.FileSystem;

namespace BogDb.Extensions.HttpFS
{
    public class HttpFileSystem : VirtualFileSystem
    {
        internal readonly HttpClient _httpClient;
        private readonly BogDb.Core.Main.BogDatabase? _database;

        /// <summary>Configuration for HTTP/S3 connections.</summary>
        internal HttpFSConfig Config { get; }

        /// <summary>
        /// In-memory response cache for HTTP range reads. Keyed by (url, offset, length).
        /// Avoids redundant HTTP round-trips for repeated reads of the same content.
        /// C++ parity: httpfs has a CachedFile abstraction; this is the .NET equivalent.
        /// </summary>
        internal readonly HttpResponseCache Cache = new();

        public HttpFileSystem(BogDb.Core.Main.BogDatabase? database = null)
            : this(null, null, database)
        {
        }

        public HttpFileSystem(HttpClient? httpClient, BogDb.Core.Main.BogDatabase? database = null)
            : this(httpClient, null, database)
        {
        }

        public HttpFileSystem(HttpClient? httpClient, HttpFSConfig? config, BogDb.Core.Main.BogDatabase? database = null)
        {
            Config = config ?? new HttpFSConfig();
            _database = database;

            // Load config from database extension options if available
            if (_database != null)
                Config.LoadFromDatabase(_database);

            if (httpClient != null)
            {
                _httpClient = httpClient;
            }
            else
            {
                var handler = new HttpClientHandler();
                if (!string.IsNullOrEmpty(Config.ProxyUrl))
                    handler.Proxy = new WebProxy(Config.ProxyUrl);

                _httpClient = new HttpClient(handler)
                {
                    Timeout = Config.Timeout
                };
            }
        }

        public override BogDb.Core.Common.FileSystem.FileInfo OpenFile(string path, FileFlags flags)
        {
            if (flags.HasFlag(FileFlags.Write) || flags.HasFlag(FileFlags.Create))
            {
                throw new NotSupportedException("HttpFileSystem only supports Read-Only access natively.");
            }

            return new HttpFileInfo(path, _httpClient, this);
        }

        public override void CreateDirectory(string path) => throw new NotSupportedException();
        public override void RemoveFileIfExists(string path) => throw new NotSupportedException();

        public override bool FileExists(string path)
        {
            var parsedPath = ParseS3Url(path);
            var request = new HttpRequestMessage(HttpMethod.Head, parsedPath);
            SignIfS3(request);
            var response = SendWithRetry(request);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Send an HTTP request with retry and exponential backoff.
        /// C++ parity: <c>HTTPFileSystem::runRequestWithRetry()</c>
        /// Retries on transient errors: 5xx, 429, timeouts, network errors.
        /// </summary>
        public HttpResponseMessage SendWithRetry(HttpRequestMessage request, HttpClient? client = null)
        {
            var httpClient = client ?? _httpClient;
            int maxRetries = Config.MaxRetries;
            var baseDelay = Config.RetryBaseDelay;
            Exception? lastException = null;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Clone the request for retry (HttpRequestMessage can only be sent once)
                    HttpRequestMessage req;
                    if (attempt == 0)
                    {
                        req = request;
                    }
                    else
                    {
                        req = CloneRequest(request);
                        // Re-sign on retry (SigV4 timestamp changes)
                        SignIfS3(req);
                    }

                    var response = httpClient.Send(req);

                    // Retry on transient server errors or throttling
                    if (IsRetryable(response.StatusCode) && attempt < maxRetries)
                    {
                        var delay = TimeSpan.FromMilliseconds(
                            baseDelay.TotalMilliseconds * Math.Pow(2, attempt));
                        Thread.Sleep(delay);
                        continue;
                    }

                    return response;
                }
                catch (HttpRequestException ex) when (attempt < maxRetries)
                {
                    lastException = ex;
                    var delay = TimeSpan.FromMilliseconds(
                        baseDelay.TotalMilliseconds * Math.Pow(2, attempt));
                    Thread.Sleep(delay);
                }
                catch (TaskCanceledException ex) when (attempt < maxRetries)
                {
                    // Timeout
                    lastException = ex;
                    var delay = TimeSpan.FromMilliseconds(
                        baseDelay.TotalMilliseconds * Math.Pow(2, attempt));
                    Thread.Sleep(delay);
                }
            }

            throw new HttpRequestException(
                $"Request failed after {maxRetries + 1} attempts: {request.RequestUri}",
                lastException);
        }

        /// <summary>Sign the request with SigV4 if credentials are available and URL is S3.</summary>
        internal void SignIfS3(HttpRequestMessage request)
        {
            if (Config.HasS3Credentials)
                S3Auth.SignRequest(request, Config);
        }

        public string ParseS3Url(string path)
        {
            if (path.StartsWith("s3://", StringComparison.OrdinalIgnoreCase))
            {
                var region = Config.ResolvedRegion;
                var endpoint = Config.ResolvedEndpoint;

                var withoutPrefix = path.Substring(5);
                var slashIndex = withoutPrefix.IndexOf('/');
                
                if (slashIndex == -1) throw new ArgumentException("Invalid S3 URL format. Expected s3://bucket/key");
                
                var bucket = withoutPrefix.Substring(0, slashIndex);
                var key = withoutPrefix.Substring(slashIndex + 1);

                if (Config.S3UsePathStyle)
                {
                    // Path-style: https://endpoint/bucket/key (MinIO, LocalStack)
                    return $"https://{endpoint}/{bucket}/{key}";
                }
                
                if (endpoint.Contains(region) || endpoint.Equals("s3.amazonaws.com", StringComparison.OrdinalIgnoreCase))
                {
                    return $"https://{bucket}.s3.{region}.amazonaws.com/{key}";
                }
                
                return $"https://{bucket}.{endpoint}/{key}";
            }
            return path;
        }

        public override IReadOnlyList<string> GetPathsInDirectory(string path)
        {
            throw new NotSupportedException("Directory listing over HTTP is purely virtual mapping direct files natively.");
        }

        public override string JoinPath(string baseDir, string name)
        {
            if (baseDir.EndsWith('/')) return baseDir + name;
            return baseDir + "/" + name;
        }

        // ── Private helpers ─────────────────────────────────────────────────────

        private static bool IsRetryable(HttpStatusCode status) =>
            status == HttpStatusCode.TooManyRequests ||        // 429
            status == HttpStatusCode.InternalServerError ||    // 500
            status == HttpStatusCode.BadGateway ||             // 502
            status == HttpStatusCode.ServiceUnavailable ||     // 503
            status == HttpStatusCode.GatewayTimeout;           // 504

        private static HttpRequestMessage CloneRequest(HttpRequestMessage original)
        {
            var clone = new HttpRequestMessage(original.Method, original.RequestUri);
            if (original.Content != null)
            {
                var body = original.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                clone.Content = new ByteArrayContent(body);
            }
            // Copy Range header if present
            if (original.Headers.Range != null)
                clone.Headers.Range = original.Headers.Range;
            return clone;
        }
    }

    /// <summary>
    /// Thread-safe in-memory cache for HTTP range-read responses.
    /// Uses a bounded LRU eviction strategy to prevent unbounded memory growth.
    /// C++ parity: httpfs cached_file_buffer.cpp
    /// </summary>
    internal sealed class HttpResponseCache
    {
        private readonly Dictionary<(string url, long offset, int length), byte[]> _cache = new();
        private readonly LinkedList<(string url, long offset, int length)> _lru = new();
        private readonly Lock _lock = new();
        private readonly int _maxEntries;
        private long _hits;
        private long _misses;

        public HttpResponseCache(int maxEntries = 256)
        {
            _maxEntries = maxEntries;
        }

        public long Hits => _hits;
        public long Misses => _misses;

        public bool TryGet(string url, long offset, int length, out byte[]? data)
        {
            lock (_lock)
            {
                var key = (url, offset, length);
                if (_cache.TryGetValue(key, out data))
                {
                    // Move to front of LRU
                    _lru.Remove(key);
                    _lru.AddFirst(key);
                    Interlocked.Increment(ref _hits);
                    return true;
                }

                Interlocked.Increment(ref _misses);
                return false;
            }
        }

        public void Put(string url, long offset, int length, byte[] data)
        {
            lock (_lock)
            {
                var key = (url, offset, length);
                if (_cache.ContainsKey(key))
                {
                    _lru.Remove(key);
                }
                else
                {
                    // Evict oldest if at capacity
                    while (_cache.Count >= _maxEntries && _lru.Count > 0)
                    {
                        var oldest = _lru.Last!.Value;
                        _lru.RemoveLast();
                        _cache.Remove(oldest);
                    }
                }

                _cache[key] = data;
                _lru.AddFirst(key);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _cache.Clear();
                _lru.Clear();
            }
        }
    }
}
