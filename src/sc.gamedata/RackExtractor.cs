using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;
using unforge;

namespace sc.gamedata
{
	// Builds the top-level racks[] array battlestations uses to populate the
	// rack-swap dropdown on missile slots. Rack records live under
	// `entities/scitem/ships/missile_racks/{manufacturer}/...` so this pass
	// walks any depth below `missile_racks/` rather than direct children.
	internal static class RackExtractor
	{
		private const String PathPrefix = "libs/foundry/records/entities/scitem/ships/missile_racks/";
		private static readonly Regex ManufacturerPrefixRe = new(
			@"^[A-Z]+_(?:S\d+_)?([A-Z]+)_",
			RegexOptions.Compiled);

		public static List<RackRecord> Extract(DataForge df, IDictionary<String, String> loc)
		{
			var result = new List<RackRecord>();
			foreach (var path in df.PathToRecordMap.Keys)
			{
				if (!path.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase)) continue;
				var root = df.ReadRecordByPathAsXml(path);
				if (root == null) continue;
				var entityId = XmlHelpers.EntityIdFromRoot(root);
				var rackGuid = XmlHelpers.Attr(root, "__ref");

				// Bail unless AttachDef declares this is in fact a missile-rack
				// item — the folder also contains some peripheral entities.
				var attach = XmlNav.FindFirst(root, "AttachDef");
				if (attach == null) continue;
				var type = XmlHelpers.Attr(attach, "Type");
				var subType = XmlHelpers.Attr(attach, "SubType");
				if (type != "MissileLauncher" || subType != "MissileRack") continue;

				var size = XmlHelpers.AttrInt(attach, "Size");

				// Walk the rack's Ports list — each missile-typed SItemPortDef
				// represents one stowed missile slot. MinSize/MaxSize on the
				// port carry the missile size constraints (rack-only racks
				// hardcode these to the same value).
				Int32 missileCount = 0;
				Int32? missileSize = null;
				var ports = XmlNav.FindFirst(root, "Ports");
				if (ports != null)
				{
					foreach (XmlNode portNode in ports.ChildNodes)
					{
						if (portNode is not XmlElement portDef || portDef.LocalName != "SItemPortDef") continue;
						var typesElem = FindChild(portDef, "Types");
						if (typesElem == null) continue;
						var hasMissileType = false;
						foreach (XmlNode typeChild in typesElem.ChildNodes)
						{
							if (typeChild is XmlElement t
								&& t.LocalName == "SItemPortDefTypes"
								&& XmlHelpers.Attr(t, "Type") == "Missile") { hasMissileType = true; break; }
						}
						if (!hasMissileType) continue;
						missileCount++;
						missileSize ??= XmlHelpers.AttrIntNullable(portDef, "MinSize");
					}
				}
				if (missileCount == 0) continue;  // skip bomb-only / non-missile carriers

				// Localization for the rack display name. Same Localization-
				// nested-in-AttachDef pattern as weapons/armor.
				String? locKey = null;
				foreach (XmlNode c in attach.ChildNodes)
					if (c is XmlElement e && e.LocalName == "Localization") { locKey = XmlHelpers.Attr(e, "Name"); break; }
				var fallback = NameResolver.PrettifyEntityId(entityId, "");
				var name = NameResolver.Resolve(locKey, loc, fallback, entityId);

				// Manufacturer code: 3-letter code that scunpacked publishes
				// (BEH, AEGS, ANVL, etc.). The AttachDef references a manufacturer
				// GUID but resolving GUID → code requires a separate manufacturer
				// table walk we don't yet do. The entity id always carries a
				// reliable 4-letter manufacturer token, so we pull from there
				// — same pattern the seed Python extractor uses.
				String? manufacturer = null;
				var m = ManufacturerPrefixRe.Match(entityId);
				if (m.Success) manufacturer = m.Groups[1].Value;

				result.Add(new RackRecord
				{
					id = entityId,
					_guid = rackGuid,
					name = name,
					size = size,
					missile_size = missileSize ?? 0,
					missile_count = missileCount,
					manufacturer = manufacturer,
				});
			}
			result.Sort((a, b) => String.Compare(a.id, b.id, StringComparison.Ordinal));
			return result;
		}

		private static XmlElement? FindChild(XmlElement parent, String localName)
		{
			foreach (XmlNode c in parent.ChildNodes)
				if (c is XmlElement e && e.LocalName == localName) return e;
			return null;
		}
	}

	internal sealed class RackRecord
	{
		public String id { get; set; } = "";
		// DataForge record GUID (__ref). Used to resolve GUID-referenced racks in
		// vehicle loadouts; stripped from JSON output via [JsonIgnore].
		[JsonIgnore]
		public String? _guid { get; set; }
		public String name { get; set; } = "";
		public Int32 size { get; set; }
		public Int32 missile_size { get; set; }
		public Int32 missile_count { get; set; }
		public String? manufacturer { get; set; }
	}
}
