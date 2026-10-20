using CodeWalker.GameFiles;
using CodeWalker.Core.Utils;

RpfManager.IsGen9 = true;
if (args[0] == "convert")
{
    Directory.CreateDirectory(args[2]);
    foreach (var input in Directory.GetFiles(args[1]).Where(p => p.EndsWith(".ydr") || p.EndsWith(".ytd")))
    {
        var bytes = Gen9Converter.TryConvert(File.ReadAllBytes(input), Path.GetExtension(input), Console.WriteLine, Path.GetFileName(input), true, out var converted);
        File.WriteAllBytes(Path.Combine(args[2], Path.GetFileName(input)), bytes);
        Console.WriteLine($"Saved {bytes.Length} bytes, converted={converted}");
    }
    return;
}
GTA5Keys.LoadFromPath(args[1], true);
if (args[0] == "find")
{
    foreach(var path in Directory.EnumerateFiles(args[1], "*.rpf", SearchOption.AllDirectories).Where(p=>!p.Contains("\\mods\\")))
    {
        var archive = new RpfFile(path, Path.GetRelativePath(args[1], path));
        archive.ScanStructure(_=>{}, Console.WriteLine);
        foreach(var f in All(archive)) foreach(var e in f.AllEntries)
            if(e.NameLower.Contains(args[2])) Console.WriteLine($"{path}: {e.Path}");
    }
    return;
}
var rpf = new RpfFile(args[2], Path.GetFileName(args[2]));
rpf.ScanStructure(_ => {}, Console.WriteLine);
IEnumerable<RpfFile> All(RpfFile f) { yield return f; if(f.Children != null) foreach(var child in f.Children) foreach(var x in All(child)) yield return x; }
if (args[0] == "scan")
{
    foreach (var f in All(rpf)) foreach (var entry in f.AllEntries) if (entry.NameLower.Contains(args[3])) Console.WriteLine(entry.Path);
}
else if (args[0] == "replace")
{
    if (!args[2].Contains("\\mods\\", StringComparison.OrdinalIgnoreCase)) throw new Exception("Refusing to edit an original archive");
    foreach(var input in Directory.GetFiles(args[3]).Where(p => p.EndsWith(".ydr") || p.EndsWith(".ytd")))
    {
        var found = All(rpf).SelectMany(f => f.AllEntries).Where(e=>e.NameLower == Path.GetFileName(input).ToLowerInvariant()).ToList();
        foreach(var e in found)
        {
            RpfFile.EnsureValidEncryption(e.File, _ => true);
            RpfFile.CreateFile(e.Parent, e.Name, File.ReadAllBytes(input));
            Console.WriteLine($"Replaced {e.Path}");
        }
        if(found.Count == 0) throw new Exception($"Missing archive entry: {input}");
    }
}
