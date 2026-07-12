using System.Text.Json;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: MapRegionizer.Validation <summary.json>");
    return 1;
}

var summaryPath = Path.GetFullPath(args[0]);
if (!File.Exists(summaryPath))
{
    Console.Error.WriteLine($"Summary file was not found: {summaryPath}");
    return 1;
}

try
{
    using var document = JsonDocument.Parse(File.ReadAllText(summaryPath));
    if (document.RootElement.ValueKind != JsonValueKind.Object)
    {
        Console.Error.WriteLine("Summary JSON must contain an object.");
        return 1;
    }

    Console.WriteLine($"Validated summary: {summaryPath}");
    return 0;
}
catch (JsonException exception)
{
    Console.Error.WriteLine($"Summary JSON is invalid: {exception.Message}");
    return 1;
}
