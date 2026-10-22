using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace unforge.cli
{
	class Program
	{
		static void Main(params String[] args)
		{
			var ci = System.Globalization.CultureInfo.InvariantCulture;
			System.Threading.Thread.CurrentThread.CurrentCulture = ci;
			System.Threading.Thread.CurrentThread.CurrentUICulture = ci;


			if (args.Length == 0)
			{
				args = new String[] { "game.dcb" };
				// args = new String[] { "wrld.xml" };
				// args = new String[] { "Data" };
				// args = new String[] { @"S:\Mods\BuildXPLOR\archive-3.0\661655\Data\game.dcb" };
			}

			// After the infile: any number of "--include <record path prefix>"
			// pairs. Anything else there prints the usage, as any second
			// argument always has.
			var includes = new List<String>();
			var showUsage = false;
			for (var i = 1; i < args.Length; i += 2)
			{
				if (args[i] == "--include" && i + 1 < args.Length && !String.IsNullOrWhiteSpace(args[i + 1]))
				{
					includes.Add(args[i + 1]);
				}
				else
				{
					showUsage = true;
					break;
				}
			}

			if (showUsage)
			{
				Console.WriteLine("Usage: unforge.exe [infile] [--include <record path prefix>]...");
				Console.WriteLine();
				Console.WriteLine("Converts any Star Citizen binary file into an actual XML file.");
				Console.WriteLine("CryXml files are saved as text XML in the original location, under");
				Console.WriteLine("the same name with an .xml extension, so a binary .xml file is");
				Console.WriteLine("overwritten in place. Text XML files are left as they are.");
				Console.WriteLine("DataForge files (.dcb) are written as one XML file per record, in");
				Console.WriteLine("folders beside the .dcb.");
				Console.WriteLine();
				Console.WriteLine("Can also convert every file in a directory and its sub-directories");
				Console.WriteLine("the same way. A file that is not CryXml, text XML or DataForge is");
				Console.WriteLine("left as it is, with an error line.");
				Console.WriteLine("With no arguments at all, it reads game.dcb in the current directory.");
				Console.WriteLine();
				Console.WriteLine("--include limits a DataForge file to the records whose path is the");
				Console.WriteLine("prefix or lies under it, e.g. libs/foundry/records/entities/scitem.");
				Console.WriteLine("Repeat it for more prefixes. Without it every record is saved.");
				return;
			}

			if ((args.Length > 0) && Directory.Exists(args[0]))
			{
				foreach (var file in Directory.GetFiles(args[0], "*.*", SearchOption.AllDirectories))
				{
					if (new String[] { "ini", "txt" }.Contains(Path.GetExtension(file), StringComparer.InvariantCultureIgnoreCase)) continue;

					try
					{
						Console.WriteLine("Converting {0}", file.Replace(args[0], ""));

						Smelter.Instance.Smelt(file, includes: includes);
					}
					catch (Exception) { }
				}
			}
			else
			{
				Smelter.Instance.Smelt(args[0], includes: includes);
			}
		}
	}

	public class Smelter
	{
		public static Smelter Instance { get; } = new Smelter { };

		private Boolean _overwrite;

		public void Smelt(String path, Boolean overwrite = true, IReadOnlyCollection<String> includes = null)
		{
			this._overwrite = overwrite;

			try
			{
				if (File.Exists(path))
				{
					if (Path.GetExtension(path) == ".dcb")
					{
						using var fileStream = File.OpenRead(path);
						var ctorSw = System.Diagnostics.Stopwatch.StartNew();
						var df = new DataForge(fileStream);
						ctorSw.Stop();
						Console.WriteLine($"DataForge ctor (header + tables): {ctorSw.Elapsed.TotalSeconds:F2}s");
						df.Save(Path.ChangeExtension(path, "xml"), includes);
					}
					else
					{
						if (!_overwrite)
						{
							if (!File.Exists(Path.ChangeExtension(path, "raw")))
							{
								File.Move(path, Path.ChangeExtension(path, "raw"));
								path = Path.ChangeExtension(path, "raw");
							}
						}

						var xml = CryXmlSerializer.ReadFile(path);

						if (xml != null)
						{
							xml.Save(Path.ChangeExtension(path, "xml"));
						}
						else
						{
							Console.WriteLine("{0} already in XML format", path);
						}
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("Error converting {0}: {1}", path, ex.Message);
			}
		}
	}
}
