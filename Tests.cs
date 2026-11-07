using System;
using System.IO;
using System.IO.Compression;
using FovApp.Services;

namespace FovApp;

// Headless logic test: no window, verifies the value model and the builder.
public static class Tests
{
    public static int Run(string[] args)
    {
        var parameters = FovModel.Load("cameras_original.ymt");
        Console.WriteLine($"params loaded: {parameters.Count}");
        if (parameters.Count < 60) { Console.WriteLine("FAIL: too few params"); return 1; }

        // value model: default + personal + global
        var fpAim = parameters.First(p => p.Type == "camFirstPersonPedAimCameraMetadata" && p.Field == "BaseFov");
        var def0 = fpAim.Values.First();
        Console.WriteLine($"FP aim default[0]: {def0} (expect 45)");
        if (Math.Abs(def0 - 45) > 0.01f) return 2;

        // global shift +10
        fpAim.SetGlobal(10f);
        if (Math.Abs(fpAim.Values.First() - 55) > 0.01f) { Console.WriteLine("FAIL global"); return 3; }

        // personal +5 on top: 45 + 5 + 10 = 60
        fpAim.SetPersonal(5f);
        if (Math.Abs(fpAim.Values.First() - 60) > 0.01f) { Console.WriteLine("FAIL personal"); return 4; }

        // reset
        fpAim.Reset();
        if (Math.Abs(fpAim.Values.First() - 45) > 0.01f) { Console.WriteLine("FAIL reset"); return 5; }

        // the 20-FOV camera keeps its own default (item deltas)
        var v20 = fpAim.Values.ToList();
        Console.WriteLine($"FP aim all: [{string.Join(",", v20.Select(v => v.ToString("0")))}]");
        if (!v20.Contains(20)) { Console.WriteLine("FAIL: 20-fov cam missing"); return 6; }

        // builder end-to-end
        var fp = parameters.First(p => p.Group == "fp" && p.Field == "BaseFov" && p.Type == "camFirstPersonPedAimCameraMetadata");
        fp.SetPersonal(fp.BaseDefault); // x2
        var path = ModpackBuilder.Build(parameters, "cameras_original.ymt", out var firstFp);
        Console.WriteLine($"built: {path}, firstFp={firstFp}");

        // verify the produced ymt inside the oiv
        using var zip = System.IO.Compression.ZipFile.OpenRead(path);
        var entry = zip.GetEntry("content/fov/cameras.ymt");
        using var ms = new MemoryStream();
        entry!.Open().CopyTo(ms);
        var data = ms.ToArray();

        var patched = FovModel.ReadBeF32(data, 0x668F0 + 0x24);
        Console.WriteLine($"FP aim[0] in oiv: {patched} (expect 90)");
        if (Math.Abs(patched - 90) > 0.01f) return 7;

        // The original build must package the untouched reference file, have
        // the fixed package name, and deliberately omit a description.
        var originalPath = ModpackBuilder.BuildOriginal("cameras_original.ymt");
        if (Path.GetFileName(originalPath) != "Original FOV.oiv")
        { Console.WriteLine("FAIL: original filename"); return 12; }
        using (var originalZip = ZipFile.OpenRead(originalPath))
        {
            var originalEntry = originalZip.GetEntry("content/fov/cameras.ymt");
            using var originalStream = new MemoryStream();
            originalEntry!.Open().CopyTo(originalStream);
            if (!originalStream.ToArray().SequenceEqual(File.ReadAllBytes("cameras_original.ymt")))
            { Console.WriteLine("FAIL: original cameras.ymt was changed"); return 13; }

            var assemblyEntry = originalZip.GetEntry("assembly.xml");
            using var reader = new StreamReader(assemblyEntry!.Open());
            var assemblyXml = reader.ReadToEnd();
            if (!assemblyXml.Contains("<name>Original FOV</name>") || assemblyXml.Contains("<description>"))
            { Console.WriteLine("FAIL: original package metadata"); return 14; }
        }

        // checksum verify: recompute and compare
        var copy = (byte[])data.Clone();
        var stored = PsoChecksum.Update(copy);
        var orig = (byte[])data.Clone();
        var ok = orig.SequenceEqual(copy);
        Console.WriteLine($"checksum stable (idempotent): {ok}");
        if (!ok) { Console.WriteLine("FAIL: Update not idempotent — wrong algorithm"); return 8; }

        // Import sources: raw YMT and a modpack archive with exactly one YMT.
        var importDir = Path.Combine(Path.GetTempPath(), "FovAppImportTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(importDir);
            var sourceYmt = File.ReadAllBytes("cameras_original.ymt");
            var rawPath = Path.Combine(importDir, "cameras.ymt");
            File.WriteAllBytes(rawPath, sourceYmt);
            if (!CameraFileImporter.ReadCamerasYmt(rawPath).SequenceEqual(sourceYmt))
            { Console.WriteLine("FAIL: raw YMT import"); return 9; }

            var archivePath = Path.Combine(importDir, "single.oiv");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                var importEntry = archive.CreateEntry("content/fov/cameras.ymt");
                using var output = importEntry.Open();
                output.Write(sourceYmt);
            }
            if (!CameraFileImporter.ReadCamerasYmt(archivePath).SequenceEqual(sourceYmt))
            { Console.WriteLine("FAIL: OIV import"); return 10; }

            var ambiguousPath = Path.Combine(importDir, "ambiguous.zip");
            using (var archive = ZipFile.Open(ambiguousPath, ZipArchiveMode.Create))
            {
                foreach (var name in new[] { "content/a/cameras.ymt", "content/b/cameras.ymt" })
                {
                    var duplicateEntry = archive.CreateEntry(name);
                    using var output = duplicateEntry.Open();
                    output.Write(sourceYmt);
                }
            }
            try { CameraFileImporter.ReadCamerasYmt(ambiguousPath); }
            catch (InvalidDataException) { goto AmbiguousArchiveRejected; }
            Console.WriteLine("FAIL: ambiguous archive was accepted"); return 11;
        AmbiguousArchiveRejected:;
        }
        finally { if (Directory.Exists(importDir)) Directory.Delete(importDir, recursive: true); }

        Console.WriteLine("ALL LOGIC TESTS PASSED");
        return 0;
    }
}
