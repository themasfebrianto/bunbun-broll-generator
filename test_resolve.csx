var absolutePath = @"E:\\VibeCode\\ScriptFlow_workspace\\bunbun-broll-generator\\output\\9f092d36\\whisks_images\\img-1.png";

var normalized = absolutePath.Replace("\\", "/");
var baseDir = Path.Combine(Directory.GetCurrentDirectory(), "output");

string relative = "";
var markerIndex = normalized.IndexOf("output/", StringComparison.OrdinalIgnoreCase);
if (markerIndex >= 0)
{
    relative = normalized.Substring(markerIndex + "output/".Length);
}
else
{
    try { relative = Path.GetRelativePath(baseDir, absolutePath); } catch { relative = absolutePath; }
}

if (relative.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase))
{
    var parts = relative.Split('/');
    if (parts.Length > 2 && parts[1].Length == 8) // Looks like scripts/<sessionId>/...
    {
        relative = string.Join("/", parts.Skip(1)); // Remove 'scripts/'
    }
}

var resolved = Path.Combine(baseDir, relative.Replace("/", Path.DirectorySeparatorChar.ToString()));
Console.WriteLine($"Original: {absolutePath}");
Console.WriteLine($"Normalized: {normalized}");
Console.WriteLine($"Relative: {relative}");
Console.WriteLine($"BaseDir: {baseDir}");
Console.WriteLine($"Resolved: {resolved}");
Console.WriteLine($"Exists: {File.Exists(resolved)}");
