using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections.Generic;
using System.IO;
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
				Console.WriteLine($"Found {matching.Count} files to extract.");

				// Phase 2: parallel extraction through the archive opened above.
				// ZipFile takes a lock for each read of the file, and each worker
				// decrypts and decompresses its own entry. A ZipFile per worker
				// parsed the whole central directory again for each one (about
				// 2 s and 0.75 GB on a current Data.p4k), which took 12 GB with
				// 16 workers.
				Parallel.ForEach(
					matching,
					new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
					info => Extract(pak, info)
				);
			}
		}

		static void Extract(ZipFile pak, (long Index, string Name, string Compression, string Crypto) info)
		{
			var created = false;
			try
			{
				var dir = Path.GetDirectoryName(info.Name);
				if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

				Console.WriteLine($"{info.Compression} | {info.Crypto} | {info.Name}");

				using (var s = pak.GetInputStream(info.Index))
				using (var fs = File.Create(info.Name))
				{
					created = true;
					StreamUtils.Copy(s, fs, new byte[81920]);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Exception while extracting {info.Name}: {ex.Message}");

				// A cut-short file would read as already extracted, and every
				// later run would skip it, so it goes.
				if (created)
				{
					try { File.Delete(info.Name); }
					catch (Exception deleteEx)
					{
						Console.WriteLine($"Could not delete the partial file {info.Name}: {deleteEx.Message}");
					}
				}
			}
		}
	}
}
