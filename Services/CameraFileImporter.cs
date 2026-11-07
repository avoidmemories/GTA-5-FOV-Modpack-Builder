using System.IO;
using System.IO.Compression;
using System.Linq;

namespace FovApp.Services;

/// <summary>Loads a single cameras.ymt directly or from an OIV/ZIP modpack.</summary>
public static class CameraFileImporter
{
    public static byte[] ReadCamerasYmt(string path)
    {
        var extension = Path.GetExtension(path);
        byte[] data;
        if (extension.Equals(".ymt", StringComparison.OrdinalIgnoreCase))
        {
            data = File.ReadAllBytes(path);
        }
        else if (extension.Equals(".oiv", StringComparison.OrdinalIgnoreCase)
                 || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ZipFile.OpenRead(path);
            var matches = archive.Entries
                .Where(e => e.Length > 0 && IsCamerasYmtName(e.FullName))
                .ToList();

            if (matches.Count == 0)
                throw new InvalidDataException("The archive does not contain a cameras.ymt file.");
            if (matches.Count > 1)
                throw new InvalidDataException(
                    $"Found {matches.Count} files named cameras.ymt in the archive. " +
                    "Import was cancelled because the correct file cannot be chosen safely.");

            using var stream = matches[0].Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            data = memory.ToArray();
        }
        else
        {
            throw new InvalidDataException("Choose a cameras.ymt, .oiv, or .zip file.");
        }

        if (!LooksLikePso(data))
            throw new InvalidDataException("The selected cameras.ymt is not a valid PSO file.");
        return data;
    }

    private static bool IsCamerasYmtName(string entryName)
    {
        var normalised = entryName.Replace('\\', '/');
        var slash = normalised.LastIndexOf('/');
        var fileName = slash >= 0 ? normalised[(slash + 1)..] : normalised;
        return fileName.Equals("cameras.ymt", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikePso(byte[] data) =>
        data.Length >= 16 && data[0] == (byte)'P' && data[1] == (byte)'S';

}
