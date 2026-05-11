using System;
using System.IO;
using System.Linq;
using Xunit;
using BogDb.Extensions.Json;

namespace BogDb.Tests.Extension
{
    public class JsonExtensionTests : IDisposable
    {
        private readonly string _testFilePath;

        public JsonExtensionTests()
        {
            _testFilePath = Path.GetTempFileName() + ".json";
            
            // Build synthetic JSON Array representing typical Graph Database Node Ingestions
            string jsonContent = @"[
                { ""id"": 1, ""name"": ""Alice"", ""age"": 30, ""isActive"": true },
                { ""id"": 2, ""name"": ""Bob"", ""age"": 20, ""isActive"": false },
                { ""id"": 3, ""name"": ""Charlie"", ""age"": 25, ""isActive"": true },
                { ""id"": 4, ""name"": ""David"", ""age"": 40, ""isActive"": true }
            ]";
            
            File.WriteAllText(_testFilePath, jsonContent);
        }

        [Fact]
        public void BogDbJsonQueryable_ScanJsonArray_AppliesLinqFiltersCorrectly()
        {
            // Act
            var results = BogDbJsonQueryable.ScanJsonArray(_testFilePath)
                .Where(node => (int)node["age"] > 22 && (bool)node["isActive"])
                .Select(node => node["name"].ToString())
                .ToList();

            // Assert
            Assert.Equal(3, results.Count);
            Assert.Contains("Alice", results);
            Assert.Contains("Charlie", results);
            Assert.Contains("David", results);
            Assert.DoesNotContain("Bob", results);
        }
        
        [Fact]
        public void BogDbJsonQueryable_ScanJsonArray_ValidatesCompleteExtraction()
        {
            var results = BogDbJsonQueryable.ScanJsonArray(_testFilePath).ToList();
            
            Assert.Equal(4, results.Count);
            Assert.Equal("Alice", results[0]["name"].ToString());
            Assert.Equal(20, (int)results[1]["age"]);
        }

        [Fact]
        public void BogDbJsonQueryable_ScanAs_StronglyTypedDeserializationWorks()
        {
            var results = BogDbJsonQueryable.ScanAs<TestUser>(_testFilePath).ToList();
            
            Assert.Equal(4, results.Count);
            Assert.Equal("Bob", results[1].Name);
            Assert.False(results[1].IsActive);
        }
        
        public void Dispose()
        {
            if (File.Exists(_testFilePath))
            {
                File.Delete(_testFilePath);
            }
        }
    }

    public class TestUser
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int Age { get; set; }
        public bool IsActive { get; set; }
    }
}
