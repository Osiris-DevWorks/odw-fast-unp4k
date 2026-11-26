# Test plan

Amr runs this on the build artifacts the release PR's **Release** workflow uploads, before the release PR merges. Each feature pushed to the active `release/X.Y.Z` adds its own section of concrete pass/fail items under the release's heading; the sections are cleared after the release ships.

Every release also gets the standing checks:

- [ ] `unp4k` extracts a filtered set (for example `*.xml`) from a current LIVE `Data.p4k`, and a second run finds nothing new to extract.
- [ ] `unforge` converts `Game2.dcb` to XML, with and without an `--exclude` path.
- [ ] `unp4k.fs` mounts `Data.p4k` as a drive (Windows, Dokan installed) and opens a CryXML file as XML.
- [ ] The `linux-x64` build of `unp4k` (`dotnet unp4k.dll <Data.p4k> <filter>`) extracts one file.

## Next release

<!-- Add one section per feature, for example:
### #123: short name
- [ ] concrete step and what should happen
-->
