using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using BogDb.Core.Common.FileSystem;

namespace BogDb.Extensions.HttpFS
{
    public class HttpFileInfo : BogDb.Core.Common.FileSystem.FileInfo
    {
        private readonly HttpClient _httpClient;
        private readonly HttpFileSystem _fileSystem;
        
        public HttpFileInfo(string path, HttpClient httpClient, HttpFileSystem fileSystem) : base(path)
        {
            _httpClient = httpClient;
            _fileSystem = fileSystem;
            
            var parsedUrl = _fileSystem.ParseS3Url(path);

            // HTTP HEAD request to determine FileSize (with retry + S3 auth)
            var request = new HttpRequestMessage(HttpMethod.Head, parsedUrl);
            _fileSystem.SignIfS3(request);
            var response = _fileSystem.SendWithRetry(request, _httpClient);
            response.EnsureSuccessStatusCode();
            
            if (response.Content.Headers.ContentLength.HasValue)
            {
                FileSize = response.Content.Headers.ContentLength.Value;
            }
            else
            {
                throw new IOException($"HTTP server did not return Content-Length for {path}");
            }
        }

        public override void Read(Span<byte> buffer, long offset)
        {
            var parsedUrl = _fileSystem.ParseS3Url(Path);

            // Check cache first
            if (_fileSystem.Cache.TryGet(parsedUrl, offset, buffer.Length, out var cached) && cached != null)
            {
                cached.AsSpan(0, Math.Min(cached.Length, buffer.Length)).CopyTo(buffer);
                return;
            }

            // Perform HTTP GET with Range request (with retry + S3 auth)
            var request = new HttpRequestMessage(HttpMethod.Get, parsedUrl);
            request.Headers.Range = new RangeHeaderValue(offset, offset + buffer.Length - 1);
            _fileSystem.SignIfS3(request);
            
            var response = _fileSystem.SendWithRetry(request, _httpClient);
            response.EnsureSuccessStatusCode();
            
            using var stream = response.Content.ReadAsStream();
            int bytesRead = 0;
            int totalRead = 0;
            while (totalRead < buffer.Length && (bytesRead = stream.Read(buffer.Slice(totalRead))) > 0)
            {
                totalRead += bytesRead;
            }

            // Store in cache for future reads
            _fileSystem.Cache.Put(parsedUrl, offset, buffer.Length, buffer.Slice(0, totalRead).ToArray());
        }

        public override void Write(ReadOnlySpan<byte> buffer, long offset)
        {
            throw new NotSupportedException("HttpFileSystem is entirely Read-Only natively.");
        }

        public override void Sync() { }

        public override void Truncate(long size)
        {
            throw new NotSupportedException("Truncate is not supported on HttpFileSystem natively.");
        }

        public override long GetFileSize() => FileSize;

        protected override void Dispose(bool disposing) { }
    }
}
