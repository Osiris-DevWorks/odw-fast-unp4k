using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace unp4k
{
	class Program
	{
		static void Main(string[] args)
		{
			var key = new Byte[] { 0x5E, 0x7A, 0x20, 0x02, 0x30, 0x2E, 0xEB, 0x1A, 0x3B, 0xB6, 0x17, 0xC3, 0x0F, 0xDE, 0x1E, 0x47 };

			if (args.Length == 0) args = new[] { @"Data.p4k" };
			if (args.Length == 1) args = new[] { args[0], "*.*" };

			// Normalize filter once, before touching any entries
			var filter = args[1];
			if (filter.StartsWith("*.")) filter = filter.Substring(1);

			var p4kPath = Path.GetFullPath(args[0]);

			// Phase 1: single sequential pass to collect matching entries
			Console.WriteLine($"Scanning {Path.GetFileName(p4kPath)}...");
			var matching = new List<(long Index, string Name, string Compression, string Crypto)>();
			using (var pakFile = File.OpenRead(p4kPath))
			{
				var pak = new ZipFile(pakFile) { Key = key };
				foreach (ZipEntry entry in pak)
				{
					if (filter == ".*" ||
						filter == "*" ||
						entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
						(filter.EndsWith("xml", StringComparison.InvariantCultureIgnoreCase) && entry.Name.EndsWith(".dcb", StringComparison.InvariantCultureIgnoreCase)))
					{
						if (!new FileInfo(entry.Name).Exists)
							matching.Add((
								entry.ZipFileIndex,
								entry.Name,
								$"{entry.CompressionMethod}",
								entry.IsAesCrypted ? "Crypt" : "Plain"));
					}
				}
			}
			Console.WriteLine($"Found {matching.Count} files to extract.");

			// Phase 2: parallel extraction; each thread owns its own ZipFile + FileStream
			var extractCount = 0;
			var total = matching.Count;
			Parallel.ForEach(
				matching,
				new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
				() => new ZipFile(File.OpenRead(p4kPath)) { Key = key },
				(info, _, localPak) =>
				{
					try
					{
						var dir = Path.GetDirectoryName(info.Name);
						if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

						using (var s = localPak.GetInputStream(info.Index))
						using (var fs = File.Create(info.Name))
						{
							StreamUtils.Copy(s, fs, new byte[81920]);
						}

						// Print every 100th file to avoid Console lock contention
						// at high throughput; always print the last one.
						var n = Interlocked.Increment(ref extractCount);
						if (n % 100 == 0 || n == total)
							Console.WriteLine($"[{n}/{total}] {info.Compression} | {info.Crypto} | {info.Name}");
					}
					catch (Exception ex)
					{
						Console.WriteLine($"Exception while extracting {info.Name}: {ex.Message}");
					}
					return localPak;
				},
				localPak => ((IDisposable)localPak).Dispose()
			);
		}
	}
}
