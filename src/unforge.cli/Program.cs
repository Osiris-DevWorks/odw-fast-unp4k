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

			if (args[0] is "-h" or "--help" or "-?" or "/?")
			{
				PrintUsage();
				return;
			}

			// After the infile: any number of "--exclude <record path>" and
			// "--exclude-file <file>" pairs, in any order. Anything else there
			// prints the usage and exits 1, since nothing is converted.
			var exclude = new List<String>();
			var excludeFiles = new List<String>();
			var showUsage = false;
			for (var i = 1; i < args.Length && !showUsage; i += 2)
			{
				var value = i + 1 < args.Length ? args[i + 1] : null;
				if (String.IsNullOrWhiteSpace(value)) showUsage = true;
				else if (args[i] == "--exclude") exclude.Add(value);
				else if (args[i] == "--exclude-file") excludeFiles.Add(value);
				else showUsage = true;
			}

			if (showUsage)
			{
				PrintUsage();
				Environment.ExitCode = 1;
				return;
			}

			foreach (var file in excludeFiles)
			{
				try
				{
					var paths = File.ReadAllLines(file)
						.Select(line => line.Trim())
						.Where(line => line.Length > 0 && !line.StartsWith('#'))
						.ToList();
					if (paths.Count == 0) Console.WriteLine("{0} lists no record paths, so it leaves nothing out.", file);
					exclude.AddRange(paths);
				}
				catch (Exception ex)
				{
					Console.WriteLine("Error reading {0}: {1}", file, ex.Message);
					Environment.ExitCode = 1;
					return;
				}
			}

			if (!File.Exists(args[0]) && !Directory.Exists(args[0]))
			{
				Console.WriteLine("Error converting {0}: no such file or folder", args[0]);
				Environment.ExitCode = 1;
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

						Smelter.Instance.Smelt(file, exclude: exclude);
					}
					catch (Exception) { }
				}
			}
			else if (!Smelter.Instance.Smelt(args[0], exclude: exclude))
			{
				Environment.ExitCode = 1;
			}
		}

		static void PrintUsage()
		{
			Console.WriteLine("Usage: unforge.cli.exe [infile [--exclude <record path> | --exclude-file <file>]...]");
			Console.WriteLine("       unforge.cli.exe --help");
			Console.WriteLine();
			Console.WriteLine("Converts any Star Citizen binary file into an actual XML file.");
			Console.WriteLine("A CryXml file is saved as text XML under the same name with an .xml");
			Console.WriteLine("extension, replacing any file of that name, so a binary .xml file is");
			Console.WriteLine("converted in place. A file that starts with < is taken as text XML");
			Console.WriteLine("and left as it is. DataForge files (.dcb) are written as one XML file");
			Console.WriteLine("per record that has any content, in folders beside the .dcb.");
			Console.WriteLine();
			Console.WriteLine("Can also convert every file in a directory and its sub-directories");
			Console.WriteLine("the same way. A file that is not CryXml, text XML or DataForge is");
			Console.WriteLine("left as it is, with an error line.");
			Console.WriteLine("With no arguments at all, it reads game.dcb in the current directory.");
			Console.WriteLine();
			Console.WriteLine("--exclude leaves out of a DataForge file the record at that path and");
			Console.WriteLine("every record under it, e.g. libs/foundry/records/actor. --exclude-file");
			Console.WriteLine("reads such paths from a UTF-8 text file (or one with a byte order");
			Console.WriteLine("mark), one a line, skipping blank lines and lines that start with #.");
			Console.WriteLine("Both can be repeated, in any order. A path that matches no record is");
			Console.WriteLine("reported.");
			Console.WriteLine();
			Console.WriteLine("Exits 1 for wrong arguments, a missing infile, an unreadable list file");
			Console.WriteLine("or a single infile that cannot be converted, and 0 otherwise. A file");
			Console.WriteLine("that cannot be converted prints a line starting \"Error converting\".");
			Console.WriteLine("In folder mode the run carries on past such files and exits 0.");
		}
	}

	public class Smelter
	{
		public static Smelter Instance { get; } = new Smelter { };

		private Boolean _overwrite;

		// Returns true once the file is converted or found to be text XML
		// already. Returns false when a conversion throws (after printing an
		// "Error converting" line) or the file is missing, which Main checks
		// first and reports itself.
		public Boolean Smelt(String path, Boolean overwrite = true, IReadOnlyCollection<String> exclude = null)
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
						df.Save(Path.ChangeExtension(path, "xml"), exclude);
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

					return true;
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("Error converting {0}: {1}", path, ex.Message);
			}

			return false;
		}
	}
}
