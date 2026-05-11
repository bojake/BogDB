using System;
using BogDb.Core.Extension;
using BogDb.Core.Main;
using BogDb.Core.Processor.Operator;

namespace BogDb.Extensions.Demo;

/// <summary>
/// A completely separated mock extension validating that the BogDb engine
/// can reach out over Application Boundaries (ALC) and securely pull in custom logical/physical
/// nodes mapped by third-parties!
/// </summary>
public class DummyHttpScan : IExtension
{
    public string Name => "httpfs";

    public void Load(BogDatabase database)
    {
        // Inside a real extension, we would append custom catalog entries here.
        // E.g., Database.Catalog.AddTableFunction("scan_http", new HttpScanSourceRule());
        
        Console.WriteLine("[Extension Load Trace] External httpfs Demo Extension successfully hot-loaded into Engine!");
    }
}
