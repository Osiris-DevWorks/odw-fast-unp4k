using System;
using System.Collections.Generic;
using System.Xml;
using unforge;

namespace sc.gamedata
{
	internal static class WeaponExtractor
	{
		private const String GunPathPrefix = "libs/foundry/records/entities/scitem/ships/weapons/";
		private const String MissilePathPrefix = "libs/foundry/records/entities/scitem/ships/weapons/missiles/";

		// Subfolders under weapons/ that hold extra installable weapon classes
		// (rocket pods, ship-mounted EMPs). The default filter only walks
		// direct children of weapons/ to skip turret-platform / mount /
		// attachment-part records, but these subdirs hold real weapons that
		// CIG groups separately. New weapon-bearing subfolders need to be
		// added here explicitly so we don't accidentally pick up
		// weaponaimableangles/, weapongimbalmodemodifiers/, parts/, or qig/.
		private static readonly String[] AllowedGunSubfolders = new[] { "rocket_pods/", "emp/" };

		// Returns (guns, missiles). Both lists carry the same WeaponRecord
		// shape; `kind` distinguishes them and missile-only fields stay null
		// on guns (and vice versa).
		public static (List<WeaponRecord> guns, List<WeaponRecord> missiles) Extract(
			DataForge df,
			IDictionary<String, String> loc,
			IReadOnlyDictionary<String, AmmoEntry> ammoMap)
		{
			var guns = new List<WeaponRecord>();
			var missiles = new List<WeaponRecord>();

			foreach (var path in df.PathToRecordMap.Keys)
			{
				var isMissile = path.StartsWith(MissilePathPrefix, StringComparison.OrdinalIgnoreCase);
				var isGun = !isMissile && path.StartsWith(GunPathPrefix, StringComparison.OrdinalIgnoreCase);
				if (!isGun && !isMissile) continue;
				// Subdirectories under weapons/ may contain non-weapon
				// entities (turret platforms, mounts, attachment parts).
				// Direct children of weapons/ are the default; AllowedGunSubfolders
				// (rocket_pods/, emp/) are the explicit additions for installable
				// weapon classes CIG groups in subfolders. Missiles/ has its own
				// prefix and is handled in parallel.
				var rest = path.Substring(isMissile ? MissilePathPrefix.Length : GunPathPrefix.Length);
				if (rest.Contains('/'))
				{
					if (isMissile) continue; // missiles/ subfolders are not real missiles
					var allowed = false;
					foreach (var sub in AllowedGunSubfolders)
					{
						if (rest.StartsWith(sub, StringComparison.OrdinalIgnoreCase) && rest.Substring(sub.Length).IndexOf('/') < 0)
						{
							allowed = true;
							break;
						}
					}
					if (!allowed) continue;
				}

				var root = df.ReadRecordByPathAsXml(path);
				if (root == null) continue;

				if (isMissile)
				{
					var m = ParseMissile(root, loc);
					if (m != null) missiles.Add(m);
				}
				else
				{
					var g = ParseGun(root, loc, ammoMap);
					if (g != null) guns.Add(g);
				}
			}
			return (guns, missiles);
		}

		// SWeaponActionFire*Params/launchParams/SProjectileLauncher@pelletCount
		// holds the projectiles-per-trigger-pull count. Returns null when the
		// element is absent (treated as 1 by consumers) and skips the value
		// 1 explicitly so we only carry meaningful (scattergun) counts.
		private static Int32? ReadPelletCount(XmlElement fireAct)
		{
			var launch = XmlNav.FindFirst(fireAct, "launchParams");
			if (launch == null) return null;
			var sp = XmlNav.FindFirst(launch, "SProjectileLauncher");
			if (sp == null) return null;
			var n = XmlHelpers.AttrInt(sp, "pelletCount", 0);
			return n > 1 ? n : null;
		}

		// SAttachableComponentParams/AttachDef carries Size + Localization.Name
		// on every attachable item — it's the first place to look for the
		// in-world display name plus the canonical hardpoint size.
		private static (Int32 size, String? locKey) AttachSizeAndLoc(XmlElement root)
		{
			var attach = XmlNav.FindFirst(root, "AttachDef");
			var size = XmlHelpers.AttrInt(attach, "Size", 0);
			XmlElement? locElem = null;
			if (attach != null)
			{
				foreach (XmlNode c in attach.ChildNodes)
				{
					if (c is XmlElement e && e.LocalName == "Localization") { locElem = e; break; }
				}
			}
			var locKey = locElem != null ? XmlHelpers.Attr(locElem, "Name") : null;
			return (size, locKey);
		}

		private static WeaponRecord? ParseGun(
			XmlElement root,
			IDictionary<String, String> loc,
			IReadOnlyDictionary<String, AmmoEntry> ammoMap)
		{
			var entityId = XmlHelpers.EntityIdFromRoot(root);
			var weaponGuid = XmlHelpers.Attr(root, "__ref");

			// SAmmoContainerComponentParams.ammoParamsRecord holds the GUID
			// pointing at the ammo profile we built in AmmoExtractor.
			var ammoContainer = XmlNav.FindFirst(root, "SAmmoContainerComponentParams");
			if (ammoContainer == null) return null;
			var ammoGuid = XmlHelpers.Attr(ammoContainer, "ammoParamsRecord");
			if (String.IsNullOrEmpty(ammoGuid) || !ammoMap.TryGetValue(ammoGuid, out var ammo)) return null;

			// Skip weapons whose ammo entry has zero damage on every axis —
			// those are placeholder/dummy entries, same logic as the seed
			// Python extractor.
			if (ammo.damage.phys == 0 && ammo.damage.energy == 0 && ammo.damage.dist == 0
				&& ammo.damage.therm == 0 && ammo.damage.bio == 0 && ammo.damage.stun == 0)
				return null;

			var (size, locKey) = AttachSizeAndLoc(root);
			var fallback = NameResolver.PrettifyEntityId(entityId, "");
			var name = NameResolver.Resolve(locKey, loc, fallback, entityId);

			// Fire rate: SWeaponActionFireSingleParams (or SWeaponActionFireBurstParams)
			// carries fireRate in RPM and heatPerShot. We sum bursts and grab
			// the dominant firing action when there are multiple.
			//
			// Scatterguns expose pelletCount on the nested SProjectileLauncher;
			// most weapons fire 1 pellet per shot (default), scatterguns fire 8.
			// CIG's engine applies the deflection threshold against the volley
			// total, so battlestations needs the count to score penetration.
			Double? fireRate = null;
			Double? heatPerShot = null;
			Int32? capacity = null;
			Int32? pelletCount = null;
			foreach (var fireAct in XmlNav.FindAll(root, "SWeaponActionFireSingleParams"))
			{
				fireRate ??= XmlHelpers.AttrDoubleNullable(fireAct, "fireRate");
				heatPerShot ??= XmlHelpers.AttrDoubleNullable(fireAct, "heatPerShot");
				pelletCount ??= ReadPelletCount(fireAct);
			}
			foreach (var fireAct in XmlNav.FindAll(root, "SWeaponActionFireBurstParams"))
			{
				fireRate ??= XmlHelpers.AttrDoubleNullable(fireAct, "fireRate");
				heatPerShot ??= XmlHelpers.AttrDoubleNullable(fireAct, "heatPerShot");
				pelletCount ??= ReadPelletCount(fireAct);
			}

			// Magazine capacity: SAmmoContainerComponentParams.maxAmmoCount
			// is the in-world reload size. Zero means unlimited / not used.
			var capInt = XmlHelpers.AttrInt(ammoContainer, "maxAmmoCount");
			if (capInt > 0) capacity = capInt;

			// Heat behavior: SWeaponSimplifiedHeatParams is CIG's firing-heat
			// model (separate from <temperature>, which governs thermal
			// signature / IR detection — same enableOverheat="0" pattern shows
			// up there for every weapon). Presence of this element is what
			// signals an overheat-capable gun; weapons without it
			// (e.g. AMRS_LaserCannon_S1) intentionally never overheat.
			Double? heatCapacity = null;
			Double? coolingDelay = null;
			Double? coolingPerSecond = null;
			Double? overheatFixTime = null;
			Boolean overheatEnabled = false;
			var simplifiedHeat = XmlNav.FindFirst(root, "SWeaponSimplifiedHeatParams");
			if (simplifiedHeat != null)
			{
				overheatEnabled = true;
				heatCapacity = XmlHelpers.AttrDoubleNullable(simplifiedHeat, "overheatTemperature");
				coolingPerSecond = XmlHelpers.AttrDoubleNullable(simplifiedHeat, "coolingPerSecond");
				coolingDelay = XmlHelpers.AttrDoubleNullable(simplifiedHeat, "timeTillCoolingStarts");
				overheatFixTime = XmlHelpers.AttrDoubleNullable(simplifiedHeat, "overheatFixTime");
			}

			// Derive shots/time-to-overheat the same way scunpacked does so
			// existing battlestations sim code keeps working unchanged.
			Int32? shotsToOverheat = null;
			Double? timeToOverheat = null;
			if (overheatEnabled && heatCapacity is > 0 && heatPerShot is > 0)
			{
				shotsToOverheat = (Int32)Math.Floor(heatCapacity.Value / heatPerShot.Value);
				if (fireRate is > 0)
					timeToOverheat = shotsToOverheat.Value / (fireRate.Value / 60.0);
			}

			// alpha_damage and dps_burst are reported as PER-PELLET values to
			// stay backwards-compatible with legacy consumers; battlestations
			// scales them by pellet_count where the volley total is needed.
			// Damage profile itself (`damage`) is also per-pellet — scattergun
			// behavior diverges from non-scatter only via pellet_count > 1.
			Double? alpha = null;
			if (ammo.damage is { } d)
				alpha = d.phys + d.energy + d.dist + d.therm + d.bio + d.stun;
			Double? dpsBurst = null;
			if (alpha is not null && fireRate is > 0) dpsBurst = alpha.Value * (fireRate.Value / 60.0);

			Double? range = null;
			if (ammo.speed is > 0 && ammo.lifetime is > 0) range = ammo.speed.Value * ammo.lifetime.Value;

			return new WeaponRecord
			{
				id = entityId,
				name = name,
				size = size,
				kind = "gun",
				damage = ammo.damage,
				// Emit only when >1 to keep the JSON tidy — consumers default
				// to 1 when the field is absent, so non-scatter weapons stay
				// untouched and existing #pen= URLs continue to round-trip.
				pellet_count = (pelletCount is > 1) ? pelletCount : null,
				rate_of_fire = XmlHelpers.Round(fireRate, 1),
				projectile_velocity = XmlHelpers.Round(ammo.speed, 1),
				projectile_lifetime = XmlHelpers.Round(ammo.lifetime, 3),
				range = XmlHelpers.Round(range, 0),
				alpha_damage = XmlHelpers.Round(alpha, 2),
				alpha_phys = XmlHelpers.Round(ammo.damage.phys, 2),
				alpha_energy = XmlHelpers.Round(ammo.damage.energy, 2),
				alpha_dist = XmlHelpers.Round(ammo.damage.dist, 2),
				dps_burst = XmlHelpers.Round(dpsBurst, 1),
				magazine_capacity = capacity,
				base_penetration_distance = XmlHelpers.Round(ammo.base_penetration_distance, 2),
				heat_capacity = overheatEnabled ? XmlHelpers.Round(heatCapacity, 2) : null,
				heat_per_shot = overheatEnabled ? XmlHelpers.Round(heatPerShot, 3) : null,
				cooling_delay = overheatEnabled ? XmlHelpers.Round(coolingDelay, 3) : null,
				cooling_per_second = overheatEnabled ? XmlHelpers.Round(coolingPerSecond, 2) : null,
				overheat_fix_time = overheatEnabled ? XmlHelpers.Round(overheatFixTime, 2) : null,
				shots_to_overheat = shotsToOverheat,
				time_to_overheat = overheatEnabled ? XmlHelpers.Round(timeToOverheat, 3) : null,
				_guid = weaponGuid,
			};
		}

		private static WeaponRecord? ParseMissile(XmlElement root, IDictionary<String, String> loc)
		{
			var entityId = XmlHelpers.EntityIdFromRoot(root);
			var missileGuid = XmlHelpers.Attr(root, "__ref");

			// Bombs share the missiles/ folder but have a different schema —
			// they're skipped by absence of SCItemMissileParams (matches the
			// Python parse_missile guard).
			var missileParams = XmlNav.FindFirst(root, "SCItemMissileParams");
			if (missileParams == null) return null;

			XmlElement? damageElem = null;
			var explosion = XmlNav.FindFirst(missileParams, "explosionParams");
			if (explosion != null)
			{
				var damageWrap = XmlNav.FindFirst(explosion, "damage");
				if (damageWrap != null) damageElem = XmlNav.FindFirst(damageWrap, "DamageInfo");
			}
			if (damageElem == null) return null;
			var damage = XmlHelpers.DamageFrom(damageElem);
			if (damage.phys == 0 && damage.energy == 0 && damage.dist == 0
				&& damage.therm == 0 && damage.bio == 0 && damage.stun == 0)
				return null;

			var (size, locKey) = AttachSizeAndLoc(root);
			var fallback = NameResolver.PrettifyEntityId(entityId, "");
			var name = NameResolver.Resolve(locKey, loc, fallback, entityId);

			// Lock + engagement-range params live under SCItemMissileParams/
			// targetingParams (lockTime, lockRangeMin, lockRangeMax). Cruise speed
			// is GCSParams/linearSpeed; the bay has no explicit range attr, so max
			// travel ≈ cruise speed × maxLifetime (same speed×lifetime model as guns).
			Double? lockTime = null, lockRangeMin = null, lockRangeMax = null;
			var targeting = XmlNav.FindFirst(missileParams, "targetingParams");
			if (targeting != null)
			{
				lockTime = XmlHelpers.AttrDoubleNullable(targeting, "lockTime");
				lockRangeMin = XmlHelpers.AttrDoubleNullable(targeting, "lockRangeMin");
				lockRangeMax = XmlHelpers.AttrDoubleNullable(targeting, "lockRangeMax");
			}
			Double? linearSpeed = null;
			var gcs = XmlNav.FindFirst(missileParams, "GCSParams");
			if (gcs != null) linearSpeed = XmlHelpers.AttrDoubleNullable(gcs, "linearSpeed");
			var armTime = XmlHelpers.AttrDoubleNullable(missileParams, "armTime");
			var maxLifetime = XmlHelpers.AttrDoubleNullable(missileParams, "maxLifetime");
			Double? distance = (linearSpeed is > 0 && maxLifetime is > 0)
				? linearSpeed!.Value * maxLifetime!.Value : (Double?)null;

			Double? hp = null;
			var durability = XmlNav.FindFirst(root, "Durability");
			if (durability != null) hp = XmlHelpers.AttrDoubleNullable(durability, "Health");
			else
			{
				var health = XmlNav.FindFirst(root, "SHealthComponentParams");
				if (health != null) hp = XmlHelpers.AttrDoubleNullable(health, "Health");
			}

			// scunpacked exposes the missile sub-classification via subType;
			// in the raw record we don't have a direct subType attr, but the
			// SAttachableComponentParams/AttachDef.SubType reliably reads
			// "Rocket" / "Missile" / "Torpedo" — same enum scunpacked feeds.
			String? subType = null;
			var attach = XmlNav.FindFirst(root, "AttachDef");
			if (attach != null) subType = XmlHelpers.Attr(attach, "SubType");

			return new WeaponRecord
			{
				id = entityId,
				name = name,
				size = size,
				kind = "missile",
				damage = damage,
				projectile_velocity = XmlHelpers.Round(linearSpeed, 0),
				range = XmlHelpers.Round(distance, 0),
				lock_time = XmlHelpers.Round(lockTime, 2),
				lock_range_min = XmlHelpers.Round(lockRangeMin, 0),
				lock_range_max = XmlHelpers.Round(lockRangeMax, 0),
				arm_time = XmlHelpers.Round(armTime, 2),
				health = XmlHelpers.Round(hp, 0),
				missile_subtype = subType,
				_guid = missileGuid,
			};
		}
	}
}
