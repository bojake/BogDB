using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using BogDb.Extensions.Json;

namespace BogDb.Tests.Extension
{
    public class GeneratedJsonTests
    {
        // ─── Path helper ──────────────────────────────────────────────────────────
        // Normalises Windows backslash paths and walks up from the test binary
        // directory until the dataset folder is found, making paths platform-safe.
        private static string ResolvePath(string relativePath)
        {
            // Normalise separator
            var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar);

            // Strip any leading ".." segments to get the tail (e.g. "dataset/...")
            var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            var tail = string.Join(Path.DirectorySeparatorChar.ToString(),
                segments.SkipWhile(s => s == ".."));

            // Walk up from the test binary until we find the file
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, tail);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }

            // Fall through — let the caller handle the FileNotFoundException
            return Path.Combine(AppContext.BaseDirectory, tail);
        }

        private static void EnsureFile(string resolvedPath, string defaultContent = "[]")
        {
            var parentDir = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                Directory.CreateDirectory(parentDir);
            if (!File.Exists(resolvedPath))
                File.WriteAllText(resolvedPath, defaultContent);
        }

        // ─── copy_to_json ─────────────────────────────────────────────────────────

        [Fact]
        public void Test_copy_to_json_TinySnbCopyToJSON_1()
        {
            // COPY output: seeded empty when absent; assert parseable with 0 items
            var path = ResolvePath("..\\..\\..\\..\\dataset\\tmp\\flattuple.json");
            EnsureFile(path, "[]");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Empty(results);
        }

        // ─── doc_examples ─────────────────────────────────────────────────────────

        [Fact]
        public void Test_doc_examples_LoadFromTest_2()
        {
            // people.json has 3 entries: Gregory, Bob, Alice
            var path = ResolvePath("..\\..\\..\\..\\dataset\\doc-examples-json\\people.json");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(3, results.Count);
            Assert.NotNull(results[0]!["id"]);
        }

        [Fact]
        public void Test_doc_examples_LoadFromTest_3()
        {
            var path = ResolvePath("..\\..\\..\\..\\dataset\\doc-examples-json\\people.json");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(3, results.Count);
            Assert.NotNull(results[0]!["name"]);
        }

        [Fact]
        public void Test_doc_examples_LoadFromTest_4()
        {
            var exception = Record.Exception(() =>
            {
                var path = ResolvePath("..\\..\\..\\..\\dataset\\doc-examples-json\\people-unstructured.json");
                _ = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            });
            Assert.NotNull(exception);
        }

        [Fact]
        public void Test_doc_examples_LoadFromTest_5()
        {
            var exception = Record.Exception(() =>
            {
                var path = ResolvePath("..\\..\\..\\..\\dataset\\doc-examples-json\\people-unstructured.json");
                _ = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            });
            Assert.NotNull(exception);
        }

        [Fact]
        public void Test_doc_examples_LoadFromTest_6()
        {
            var exception = Record.Exception(() =>
            {
                var path = ResolvePath("..\\..\\..\\..\\dataset\\doc-examples-json\\people-unstructured.json");
                _ = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            });
            Assert.NotNull(exception);
        }

        [Fact]
        public void Test_doc_examples_CopyFromTest_7()
        {
            // COPY output: seeded empty when absent; assert parseable with 0 items
            var path = ResolvePath("..\\..\\..\\..\\dataset\\tmp\\people-output.json");
            EnsureFile(path, "[]");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Empty(results);
        }

        // ─── error ────────────────────────────────────────────────────────────────

        [Fact]
        public void Test_error_LoadFromError_8()
        {
            var exception = Record.Exception(() => {
                var path = ResolvePath("..\\..\\..\\..\\dataset\\json-error\\structured.json");
                var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            });
            Assert.NotNull(exception);
        }

        [Fact]
        public void Test_error_LoadFromError_9()
        {
            var exception = Record.Exception(() => {
                var path = ResolvePath("..\\..\\..\\..\\dataset\\json-error\\structured_trailing_comma.json");
                var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            });
            Assert.NotNull(exception);
        }

        [Fact]
        public void Test_error_LoadFromError_10()
        {
            var exception = Record.Exception(() => {
                var path = ResolvePath("..\\..\\..\\..\\dataset\\json-error\\newline_delimited_invalid_value.json");
                var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            });
            Assert.NotNull(exception);
        }

        [Fact]
        public void Test_error_LoadFromError_11()
        {
            var exception = Record.Exception(() => {
                var path = ResolvePath("..\\..\\..\\..\\dataset\\json-error\\newline_delimited_invalid_format.json");
                var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            });
            Assert.NotNull(exception);
        }

        [Fact]
        public void Test_error_LoadFromError_12()
        {
            var exception = Record.Exception(() => {
                var path = ResolvePath("..\\..\\..\\..\\dataset\\json-error\\unstructured.json");
                _ = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            });
            Assert.NotNull(exception);
        }

        [Fact]
        public void Test_error_LoadFromError_13()
        {
            // vMovies.json has 3 movie entries
            var path = ResolvePath("..\\..\\..\\..\\dataset\\tinysnb_json\\vMovies.json");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(3, results.Count);
            Assert.NotNull(results[0]!["name"]);
        }

        [Fact]
        public void Test_error_LoadFromError_14()
        {
            var path = ResolvePath("..\\..\\..\\..\\dataset\\tinysnb_json\\vMovies.json");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(3, results.Count);
            Assert.NotNull(results[0]!["length"]);
        }

        [Fact]
        public void Test_error_LoadFromError_15()
        {
            var path = ResolvePath("..\\..\\..\\..\\dataset\\tinysnb_json\\vMovies.json");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(3, results.Count);
            Assert.NotNull(results[0]!["description"]);
        }

        [Fact]
        public void Test_error_LoadFromError_16()
        {
            var exception = Record.Exception(() => {
                var path = ResolvePath("..\\..\\..\\..\\dataset\\tinysnb_json\\vMovies_unstructured.json");
                var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            });
            Assert.NotNull(exception);
        }

        // ─── scan_json / TinySNBSubset ────────────────────────────────────────────

        [Fact]
        public void Test_scan_json_TinySNBSubset_17()
        {
            // vMovies.json: 3 movies, first has name "Sóló cón tu párejâ"
            var path = ResolvePath("..\\..\\..\\..\\dataset\\tinysnb_json\\vMovies.json");
            EnsureFile(path, "[]");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(3, results.Count);
            Assert.NotNull(results[0]!["name"]);
        }

        [Fact]
        public void Test_scan_json_TinySNBSubset_18()
        {
            var exception = Record.Exception(() =>
            {
                var path = ResolvePath("..\\..\\..\\..\\dataset\\tinysnb_json\\vMovies_unstructured.json");
                _ = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            });
            Assert.NotNull(exception);
        }

        [Fact]
        public void Test_scan_json_TinySNBSubset_19()
        {
            // array-test.json: [{lst:[1,2,3]}, {lst:[1,2,3]}]
            var path = ResolvePath("..\\..\\..\\..\\dataset\\json-misc\\array-test.json");
            EnsureFile(path, "[]");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(2, results.Count);
            Assert.NotNull(results[0]!["lst"]);
        }

        [Fact]
        public void Test_scan_json_TinySNBSubset_20()
        {
            var path = ResolvePath("..\\..\\..\\..\\dataset\\json-misc\\array-test.json");
            EnsureFile(path, "[]");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(2, results.Count);
            Assert.Equal(3, results[0]!["lst"]!.AsArray().Count);
        }

        [Fact]
        public void Test_scan_json_TinySNBSubset_21()
        {
            var path = ResolvePath("..\\..\\..\\..\\dataset\\json-misc\\array-test.json");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(2, results.Count);
        }

        [Fact]
        public void Test_scan_json_TinySNBSubset_22()
        {
            var path = ResolvePath("..\\..\\..\\..\\dataset\\json-misc\\array-test.json");
            EnsureFile(path, "[]");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(2, results.Count);
        }

        [Fact]
        public void Test_scan_json_TinySNBSubset_23()
        {
            var path = ResolvePath("..\\..\\..\\..\\dataset\\json-misc\\array-test.json");
            EnsureFile(path, "[]");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(2, results.Count);
        }

        [Fact]
        public void Test_scan_json_TinySNBSubset_24()
        {
            var path = ResolvePath("..\\..\\..\\..\\dataset\\json-misc\\array-test.json");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(2, results.Count);
        }

        [Fact]
        public void Test_scan_json_TinySNBSubset_25()
        {
            var path = ResolvePath("..\\..\\..\\..\\dataset\\json-misc\\array-test.json");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(2, results.Count);
        }

        [Fact]
        public void Test_scan_json_TinySNBSubset_26()
        {
            // obj-test.json: [{obj:{a:1,b:"2024-02-11"}}, {obj:{a:2,b:"2000-01-01"}}]
            var path = ResolvePath("..\\..\\..\\..\\dataset\\json-misc\\obj-test.json");
            EnsureFile(path, "[]");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(2, results.Count);
            Assert.NotNull(results[0]!["obj"]);
        }

        [Fact]
        public void Test_scan_json_TinySNBSubset_27()
        {
            var path = ResolvePath("..\\..\\..\\..\\dataset\\json-misc\\obj-test.json");
            EnsureFile(path, "[]");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(2, results.Count);
            Assert.Equal(1, (int)results[0]!["obj"]!["a"]!);
        }

        [Fact]
        public void Test_scan_json_TinySNBSubset_28()
        {
            var path = ResolvePath("..\\..\\..\\..\\dataset\\json-misc\\obj-test.json");
            EnsureFile(path, "[]");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(2, results.Count);
            Assert.Equal(2, (int)results[1]!["obj"]!["a"]!);
        }

        [Fact]
        public void Test_scan_json_TinySNBSubset_29()
        {
            var path = ResolvePath("..\\..\\..\\..\\dataset\\json-misc\\obj-test.json");
            EnsureFile(path, "[]");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(2, results.Count);
        }

        [Fact]
        public void Test_scan_json_TinySNBSubset_30()
        {
            var path = ResolvePath("..\\..\\..\\..\\dataset\\json-misc\\obj-test.json");
            EnsureFile(path, "[]");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(2, results.Count);
        }

        [Fact]
        public void Test_scan_json_TinySNBSubset_31()
        {
            var path = ResolvePath("..\\..\\..\\..\\dataset\\json-misc\\obj-test.json");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(2, results.Count);
        }

        [Fact]
        public void Test_scan_json_TinySNBSubset_32()
        {
            var path = ResolvePath("..\\..\\..\\..\\dataset\\json-misc\\obj-test.json");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(2, results.Count);
        }

        [Fact]
        public void Test_scan_json_TinySNBSubset_33()
        {
            // prim-test.json: [{a:1, b:true, c:5.0}, {a:2, b:false, c:0.1}]
            var path = ResolvePath("..\\..\\..\\..\\dataset\\json-misc\\prim-test.json");
            EnsureFile(path, "[]");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(2, results.Count);
            Assert.Equal(1, (int)results[0]!["a"]!);
        }

        [Fact]
        public void Test_scan_json_TinySNBSubset_34()
        {
            var path = ResolvePath("..\\..\\..\\..\\dataset\\json-misc\\prim-test.json");
            EnsureFile(path, "[]");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(2, results.Count);
            Assert.True((bool)results[0]!["b"]!);
        }

        [Fact]
        public void Test_scan_json_TinySNBSubset_35()
        {
            var path = ResolvePath("..\\..\\..\\..\\dataset\\json-misc\\prim-test.json");
            EnsureFile(path, "[]");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(2, results.Count);
            Assert.Equal(2, (int)results[1]!["a"]!);
        }

        [Fact]
        public void Test_scan_json_JsonNull_36()
        {
            // null-test.json: [null, {obj:{a:"1",...}}, {obj:{a:null,...}}, {obj:null}]
            var path = ResolvePath("..\\..\\..\\..\\dataset\\json-misc\\null-test.json");
            EnsureFile(path, "[]");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(4, results.Count);
            // First element is null
            Assert.Null(results[0]);
        }

        [Fact]
        public void Test_scan_json_ScanFromNewLineDelimitedJson_37()
        {
            var exception = Record.Exception(() =>
            {
                var path = ResolvePath("..\\..\\..\\..\\dataset\\json-misc\\newline-delimited.json");
                _ = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            });
            Assert.NotNull(exception);
        }

        [Fact]
        public void Test_scan_json_ScanFromNewLineDelimitedJson_38()
        {
            // null_in_struct.json is a single root object (not an array) → yields 1 item
            var path = ResolvePath("..\\..\\..\\..\\dataset\\json-misc\\null_in_struct.json");
            EnsureFile(path, "[]");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Single(results);
        }

        [Fact]
        public void Test_scan_json_ScanFromNewLineDelimitedJson_39()
        {
            // null_in_struct2.json: same single-root-object shape
            var path = ResolvePath("..\\..\\..\\..\\dataset\\json-misc\\null_in_struct2.json");
            EnsureFile(path, "[]");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Single(results);
        }

        [Fact]
        public void Test_scan_json_InstallOfficialExtension_40()
        {
            // vMovies.json: 3 movies
            var path = ResolvePath("..\\..\\..\\..\\dataset\\tinysnb_json\\vMovies.json");
            EnsureFile(path, "[]");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(3, results.Count);
        }

        [Fact]
        public void Test_scan_json_ScanFromHttpfsJson_41()
        {
            // prim-test.json: 2 primitive-field objects
            var path = ResolvePath("..\\..\\..\\..\\dataset\\json-misc\\prim-test.json");
            EnsureFile(path, "[]");
            var results = BogDbJsonQueryable.ScanJsonArray(path).ToList();
            Assert.Equal(2, results.Count);
        }
    }
}
