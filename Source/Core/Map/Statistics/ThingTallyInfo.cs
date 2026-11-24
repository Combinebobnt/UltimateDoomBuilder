#region ================== Namespaces

using System;
using System.Collections.Generic;

#endregion

namespace CodeImp.DoomBuilder.Map.Statistics
{
	// What the game counts and hands out for one thing type; null means no route resolved that field
	public sealed class ThingTallyInfo
	{
		public bool? CountKill;
		public bool? CountItem;
		public bool? CountSecret;
		public bool? Friendly;
		public int? SpawnHealth;
		public int? HealthAmount;
		public int? ArmorAmount;
		public Dictionary<string, int> Ammo;
		public string ResolvedBy = string.Empty;

		public bool IsUnresolved { get { return !CountKill.HasValue || !CountItem.HasValue; } }

		// Marks every pickup field as resolved to nothing
		public void SetNoAmounts()
		{
			HealthAmount = 0;
			ArmorAmount = 0;
			Ammo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		}

		public void AddAmmo(string name, int amount)
		{
			if(Ammo == null) Ammo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			Ammo.TryGetValue(name, out int current);
			Ammo[name] = current + amount;
		}

		// Fills the fields a higher-precedence route left empty
		public void FillFrom(ThingTallyInfo other, string route)
		{
			if(other == null) return;
			bool used = false;

			if(!CountKill.HasValue && other.CountKill.HasValue) { CountKill = other.CountKill; used = true; }
			if(!CountItem.HasValue && other.CountItem.HasValue) { CountItem = other.CountItem; used = true; }
			if(!CountSecret.HasValue && other.CountSecret.HasValue) { CountSecret = other.CountSecret; used = true; }
			if(!Friendly.HasValue && other.Friendly.HasValue) { Friendly = other.Friendly; used = true; }
			if(!SpawnHealth.HasValue && other.SpawnHealth.HasValue) { SpawnHealth = other.SpawnHealth; used = true; }
			if(!HealthAmount.HasValue && other.HealthAmount.HasValue) { HealthAmount = other.HealthAmount; used = true; }
			if(!ArmorAmount.HasValue && other.ArmorAmount.HasValue) { ArmorAmount = other.ArmorAmount; used = true; }
			if(Ammo == null && other.Ammo != null) { Ammo = new Dictionary<string, int>(other.Ammo, StringComparer.OrdinalIgnoreCase); used = true; }

			if(used) ResolvedBy = string.IsNullOrEmpty(ResolvedBy) ? route : ResolvedBy + ", " + route;
		}
	}
}
