using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FovApp.Services;

/// <summary>
/// Builds the installable OIV modpack from the template and the patched ymt.
/// </summary>
public static class ModpackBuilder
{
    public static string Build(List<FovParam> parameters, string ymtFileName, out float firstFpValue,
        string extension = ".oiv", string? simpleDescription = null, string? simpleNameSuffix = null)
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;

        var data = File.ReadAllBytes(Path.Combine(appDir, ymtFileName));

        foreach (var p in parameters)
        {
            var vals = p.Values.ToList();
            for (int i = 0; i < p.Offsets.Count && i < vals.Count; i++)
                FovModel.WriteBeF32(data, p.Offsets[i], vals[i]);
        }

        PsoChecksum.Update(data);

        var fp = parameters.FirstOrDefault(x => x.Group == "fp" && x.Field == "BaseFov" && x.Type == "camFirstPersonPedAimCameraMetadata")
            ?? parameters.FirstOrDefault(x => x.Group == "fp");
        firstFpValue = fp?.Values.FirstOrDefault() ?? 0;

        // SIMPLE names and describes the pack with its three master values.
        // ADVANCED generates one ID and reuses that exact ID in both places.
        string desc;
        string fileName;
        if (!string.IsNullOrWhiteSpace(simpleNameSuffix))
        {
            fileName = $"Custom FOV {simpleNameSuffix}";
            desc = simpleDescription ?? $"Custom FOV {simpleNameSuffix}";
        }
        else
        {
            var presetId = Random.Shared.Next(100000, 999999);
            fileName = $"Custom FOV #{presetId}";
            desc = simpleDescription ?? $"Custom Preset #{presetId}";
        }
        const string modpackTitle = "Custom FOV";
        var outPath = UniquePath(Path.Combine(appDir, fileName + extension));
        using (var src = ZipFile.OpenRead(Path.Combine(appDir, "Assets", "Templates", "oiv_template.zip")))
        using (var outZip = new ZipArchive(File.Create(outPath), ZipArchiveMode.Create))
        {
            foreach (var entry in src.Entries)
            {
                var newEntry = outZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                using var os = newEntry.Open();
                if (entry.FullName == "assembly.xml")
                {
                    using var rs = new StreamReader(entry.Open(), Encoding.UTF8);
                    var xml = rs.ReadToEnd();
                    xml = xml.Replace(">TEST FOV<", $">{modpackTitle}<");
                    // black modpack card
                    xml = xml.Replace("$FF465FAF", "$FF000000");
                    // The card itself is black, so its title must remain white.
                    xml = xml.Replace("useBlackTextColor=\"True\"", "useBlackTextColor=\"False\"");
                    xml = Regex.Replace(xml,
                        "<description><!\\[CDATA\\[.*?\\]\\]></description>",
                        $"<description><![CDATA[{desc}]]></description>",
                        RegexOptions.Singleline);
                    var bytes = Encoding.UTF8.GetBytes(xml);
                    os.Write(bytes, 0, bytes.Length);
                }
                else if (entry.FullName == "content/fov/cameras.ymt")
                {
                    os.Write(data, 0, data.Length);
                }
                else if (entry.FullName == "icon.png")
                {
                    // embedded WPF resource via pack URI - no loose file
                    System.Windows.Resources.StreamResourceInfo? sri = null;
                    try
                    {
                        sri = System.Windows.Application.GetResourceStream(
                            new Uri("pack://application:,,,/Assets/Icons/icon.png"));
                    }
                    catch { /* resource missing: fall back to the template icon */ }
                    if (sri is not null)
                    {
                        using var res = sri.Stream;
                        res.CopyTo(os);
                    }
                    else
                    {
                        using var rs = entry.Open();
                        rs.CopyTo(os);
                    }
                }
                else
                {
                    using var rs = entry.Open();
                    rs.CopyTo(os);
                }
            }
        }

        return outPath;
    }


    /// <summary>Returns a path that does not exist yet, appending " (n)".</summary>
    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int n = 1; ; n++)
        {
            var candidate = Path.Combine(dir, $"{name} ({n}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
