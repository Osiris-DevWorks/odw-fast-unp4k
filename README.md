# unp4k [![Release](https://github.com/dolkensp/unp4k/actions/workflows/release.yaml/badge.svg)](https://github.com/dolkensp/unp4k/actions/workflows/release.yaml)
These tools allow users to open, decrypt, and extract data from Star Citizen `.p4k` files.

# Installation:
1. Download `unp4k-suite-v3.3.x.zip`
2. Right click the zip and select *Properties*
3. Under the *General* tab, check the *Unblock* checkbox at the bottom if it exists, and click ok
4. Extract the selected zip to your desired installation directory

NOTE: The Windows release is self-contained and needs no .NET install. The Linux builds need the .NET 10 runtime.

# Quickstart:

1. Drag `Data.p4k` from the `Starcitizen\LIVE` folder, directly onto `unp4k.exe`

# Advanced Command Line Usage:

1. Launch a command line, and navigate to the unp4k directory
2. Execute `unp4k.exe c:\path\to\data.p4k [filter]` where filter is a keyword used to filter the results (the default filter is `*.*`)

NOTE: The filter does not fully support wildcards. To extract files of a certain type, you may use `*.ext` as the filter, but no further wildcard functionality exists.

# unforge

`unforge.cli.exe [infile [--exclude <record path> | --exclude-file <file>]...]` converts Star Citizen binary files into XML. Tools that bundle it may call it `unforge.exe`. `unforge.cli.exe --help` prints the usage. A CryXml file is saved as text XML under the same name with an `.xml` extension, replacing any file of that name, so a binary `.xml` file is converted in place. A file that starts with `<` is taken as text XML and left as it is. A DataForge file (`.dcb`) is written as one XML file per record that has any content, in folders beside it. The infile can also be a folder. Every file in it and its subfolders is tried, and a file that is not CryXml, text XML or a DataForge file is left as it is, with an error line printed for it. With no arguments at all, it reads `game.dcb` in the current folder. The flags need an infile before them, and can come in any order.

`--exclude` leaves out of a DataForge file the record at that path and every record under it. A path matches whole segments, ignoring case and surrounding spaces, with `/` and `\` alike, so `a/b` leaves out `a/b/c.xml` but not `a/bc.xml`. `--exclude-file` reads such paths from a UTF-8 text file (or one with a byte order mark), one a line, skipping blank lines and lines that start with `#`, for lists too long for a command line. Both can be repeated. Every other record is saved, including any a game update adds. A path that matches no record is reported on a line ending `no record at or under <path>`, with the path as unforge normalised it (forward slashes, no slashes or spaces around it). That is most likely a folder a game update renamed or removed, and the run carries on.

unforge exits with 1 when the arguments are wrong, the infile does not exist, a list file cannot be read or a single infile cannot be converted, and with 0 otherwise. A file that cannot be converted prints a line starting `Error converting`. In folder mode the run carries on past such files and exits 0.

```
unforge.cli.exe Game2.dcb
unforge.cli.exe Game2.dcb --exclude libs/foundry/records/actor --exclude libs/foundry/records/audio
unforge.cli.exe Game2.dcb --exclude-file unused-records.txt
```

# unp4k.fs - Virtual Filesystem

`unp4k.fs` mounts a `.p4k` or `.dcb` file as a read-only virtual drive using [Dokan](https://github.com/dokan-dev/dokany), letting you browse and read Star Citizen game assets directly in Windows Explorer without extracting anything to disk.

**Prerequisites:** [Dokan](https://github.com/dokan-dev/dokany/releases) must be installed.

## Usage:

```
unp4k.fs.exe [path-to-file] [mount-point]
```

- `path-to-file` — path to a `.p4k` or `.dcb` file. Defaults to `Data.p4k` in the current directory.
- `mount-point` — drive letter or empty directory to mount to (e.g. `S:` or `C:\sc-data`). Defaults to `<filename>.unp4k` next to the input file.

**Examples:**

```
unp4k.fs.exe
unp4k.fs.exe "D:\Roberts Space Industries\StarCitizen\LIVE\Data.p4k"
unp4k.fs.exe "D:\Roberts Space Industries\StarCitizen\LIVE\Data.p4k" S:
unp4k.fs.exe game.dcb X:\virtual
```

Once mounted, CryXML files are served as standard XML and DataForge records are extracted as XML on demand. Press `Q` or `Esc` to unmount and exit.

## Interactive options (while mounted):

| Key | Option | Default | Range |
|-----|--------|---------|-------|
| `1` | Max reference depth — how deeply nested DataForge references are followed | 1 | 1–1000 |
| `2` | Max pointer depth — recursion safety limit for DataForge structures | library default | 10–1000 |
| `3` | Max nodes — node count safety limit per DataForge record | library default | 1000–1000000 |

# File Format Overview:

The p4k files used by Star Citizen are Zip archives.

Star Citizen supports data in multiple modes inside the archive, including STORE, DEFLATE, and custom support for ZSTD.

Star Citizen also implements bespoke encryption over *some* of the data inside the archive - this can all be decrypted with the same public key that is utilized by CryEngine based games for various encryption routines within the engine.

Inside the p4k file, XML files are often stored as CryXML rather than raw XML.

CryXML is a basic serialized XML format which `unforge.cli.exe` is able to deserialize.

Inside the p4k file, there is also a `game.dcb` file, which is a bespoke database format, with similarities to CryXML.

This is the product of what is known internally as "DataForge", and is also able to be converted/extracted using the `unforge.cli.exe` tool.
