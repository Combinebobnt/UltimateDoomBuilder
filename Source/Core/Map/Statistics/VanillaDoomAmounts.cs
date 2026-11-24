#region ================== Namespaces

using System.Collections.Generic;

#endregion

namespace CodeImp.DoomBuilder.Map.Statistics
{
	// Vanilla Doom count flags, spawn health and pickup amounts by doomednum (info.c, p_inter.c)
	public static class VanillaDoomAmounts
	{
		private struct Entry
		{
			public bool Kill;
			public bool Item;
			public int Health;
			public int HealthAmount;
			public int ArmorAmount;
			public string[] AmmoNames;
			public int[] AmmoAmounts;
		}

		private static readonly Dictionary<int, Entry> table = Build();

		private static Entry M(int health) { return new Entry { Kill = true, Health = health }; }
		private static Entry P(bool item, int health = 0, int armor = 0) { return new Entry { Item = item, HealthAmount = health, ArmorAmount = armor }; }
		private static Entry A(string ammo, int amount) { return new Entry { AmmoNames = new[] { ammo }, AmmoAmounts = new[] { amount } }; }

		private static Dictionary<int, Entry> Build()
		{
			Dictionary<int, Entry> t = new Dictionary<int, Entry>
			{
				// Monsters (Lost Soul, boss brain and shooter don't count as kills)
				{ 3004, M(20) }, { 9, M(30) }, { 64, M(700) }, { 66, M(300) }, { 67, M(600) }, { 65, M(70) },
				{ 3001, M(60) }, { 3002, M(150) }, { 58, M(150) }, { 3005, M(400) }, { 3003, M(1000) }, { 69, M(500) },
				{ 7, M(3000) }, { 68, M(500) }, { 16, M(4000) }, { 71, M(400) }, { 84, M(50) }, { 72, M(100) },
				{ 888, M(500) },
				{ 3006, new Entry { Health = 100 } }, { 88, new Entry { Health = 250 } }, { 89, new Entry() }, { 87, new Entry() },

				// Health and armor (megasphere: 200 health and blue armor)
				{ 2011, P(false, 10) }, { 2012, P(false, 25) }, { 2014, P(true, 1) }, { 2013, P(true, 100) },
				{ 83, P(true, 200, 200) }, { 2018, P(false, 0, 100) }, { 2019, P(false, 0, 200) }, { 2015, P(true, 0, 1) },

				// Powerups and keys count only as items where flagged
				{ 2022, P(true) }, { 2023, P(true, 100) }, { 2024, P(true) }, { 2025, P(false) }, { 2026, P(true) }, { 2045, P(true) },
				{ 5, P(false) }, { 6, P(false) }, { 13, P(false) }, { 38, P(false) }, { 39, P(false) }, { 40, P(false) },

				// Ammo: clipammo is 10/4/20/1 per clip, boxes give five clips
				{ 2007, A("Clip", 10) }, { 2048, A("Clip", 50) }, { 2008, A("Shell", 4) }, { 2049, A("Shell", 20) },
				{ 2010, A("RocketAmmo", 1) }, { 2046, A("RocketAmmo", 5) }, { 2047, A("Cell", 20) }, { 17, A("Cell", 100) },
				{ 8, new Entry { AmmoNames = new[] { "Clip", "Shell", "Cell", "RocketAmmo" }, AmmoAmounts = new[] { 10, 4, 20, 1 } } },

				// Weapons placed in single player give two clips
				{ 2001, A("Shell", 8) }, { 82, A("Shell", 8) }, { 2002, A("Clip", 20) }, { 2003, A("RocketAmmo", 2) },
				{ 2004, A("Cell", 40) }, { 2006, A("Cell", 40) }, { 2005, new Entry() },

				{ 2035, new Entry { Health = 20 } },
			};

			// Decorations, teleport destination and pushers: nothing counted or given
			foreach(int n in new[] { 14, 2028, 10, 12, 15, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37,
				41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 59, 60, 61, 62, 63, 70, 73, 74, 75, 76, 77, 78, 79, 80, 81, 85, 86, 5001, 5002 })
				t[n] = new Entry();

			return t;
		}

		public static ThingTallyInfo Get(int doomednum)
		{
			if(!table.TryGetValue(doomednum, out Entry e)) return null;

			ThingTallyInfo info = new ThingTallyInfo
			{
				CountKill = e.Kill,
				CountItem = e.Item,
				CountSecret = false,
				Friendly = false,
				SpawnHealth = (e.Health > 0) ? e.Health : (int?)null
			};
			info.SetNoAmounts();
			info.HealthAmount = e.HealthAmount;
			info.ArmorAmount = e.ArmorAmount;
			if(e.AmmoNames != null)
				for(int i = 0; i < e.AmmoNames.Length; i++) info.AddAmmo(e.AmmoNames[i], e.AmmoAmounts[i]);

			return info;
		}
	}
}
