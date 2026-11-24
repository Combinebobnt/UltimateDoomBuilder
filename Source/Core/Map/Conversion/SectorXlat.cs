#region ================== Namespaces

using System.Collections.Generic;
using CodeImp.DoomBuilder.Config;

#endregion

namespace CodeImp.DoomBuilder.Map.Conversion
{
	// Doom-format sector types to and from the engine's sector specials, per p_xlat.cpp's TranslateSectorSpecial
	public sealed class SectorXlat
	{
		private readonly XlatTable table;
		private readonly int tablesize;
		private readonly int allmasks;

		// Lowest Doom type per engine special, without and with bitmask (generalized) bits
		private Dictionary<int, int> plain;
		private Dictionary<int, int> generalized;

		public SectorXlat(XlatTable table)
		{
			this.table = table;
			foreach(int key in table.Sectors.Keys) if(key + 1 > tablesize) tablesize = key + 1;
			foreach(XlatSectorMask m in table.SectorMasks) allmasks |= m.Mask;
		}

		// Bits a bitmask rule consumes, which a target without generalized effects can't hold
		public int MaskBits { get { return allmasks; } }

		public int Translate(int special)
		{
			int mask = 0;
			for(int i = table.SectorMasks.Count - 1; i >= 0; i--)
			{
				XlatSectorMask m = table.SectorMasks[i];
				int newmask = special & m.Mask;
				if(newmask == 0) continue;
				special &= ~newmask;
				if(m.Op == 1) newmask <<= m.Shift;
				else if(m.Op == -1) newmask >>= m.Shift;
				else if(m.Shift == 1) newmask = 0;
				mask |= newmask;
			}

			// The engine's table is an array: unset entries below its size are 0 without bitmask
			if(special >= 0 && special < tablesize)
			{
				XlatSectorEntry e;
				if(!table.Sectors.TryGetValue(special, out e)) e = new XlatSectorEntry();
				special = (!e.BitmaskAllowed && mask != 0) ? 0 : e.NewType;
			}
			return special | mask;
		}

		// The lowest Doom type translating to this special, preferring one without generalized bits
		public int? Find(int special, bool allowgeneralized)
		{
			if(plain == null)
			{
				plain = new Dictionary<int, int>();
				generalized = new Dictionary<int, int>();
				for(int d = 0; d <= 0xffff; d++)
				{
					int s = Translate(d);
					Dictionary<int, int> target = (d & allmasks) == 0 ? plain : generalized;
					if(!target.ContainsKey(s)) target[s] = d;
				}
			}

			int result;
			if(plain.TryGetValue(special, out result)) return result;
			if(allowgeneralized && generalized.TryGetValue(special, out result)) return result;
			return null;
		}
	}
}
