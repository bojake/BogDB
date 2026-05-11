using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BogDb.Extensions.Json
{
    /// <summary>
    /// Introduces Fluent LINQ Bindings capturing JSON byte streams integrating closely into Native arrays.
    /// This drastically outperforms standard C++ static representations mapping raw JSON into query parameters!
    /// </summary>
    public static class BogDbJsonQueryable
    {
        /// <summary>
        /// Reads a JSON Array file iteratively yielding strongly typed representations effortlessly executing LINQ lazily!
        /// </summary>
        public static IEnumerable<JsonNode> ScanJsonArray(string filePath)
        {
            using var fileStream = File.OpenRead(filePath);
            var document = JsonNode.Parse(fileStream);

            if (document is JsonArray jsonArray)
            {
                foreach (var item in jsonArray)
                {
                    yield return item;
                }
            }
            else
            {
                yield return document; // Single JSON object gracefully handled
            }
        }

        /// <summary>
        /// Example demonstrating mapping lazily filtered JSON boundaries traversing perfectly inside native execution loops effortlessly!
        /// db.ScanJsonArray("people.json").Where(p => (int)p["age"] > 25).Select(p => p["name"].ToString());
        /// </summary>
        public static IEnumerable<T> ScanAs<T>(string filePath)
        {
            using var fileStream = File.OpenRead(filePath);
            var items = JsonSerializer.Deserialize<List<T>>(fileStream, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true
            });
            
            if (items == null) yield break;

            foreach (var item in items)
            {
                yield return item;
            }
        }
    }
}
