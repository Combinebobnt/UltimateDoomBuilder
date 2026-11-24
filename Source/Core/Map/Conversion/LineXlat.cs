#region ================== Namespaces

using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Config;

#endregion

namespace CodeImp.DoomBuilder.Map.Conversion
{
	// Engine activation bits (doomdata.h SPAC_*), the hub every format converts through
	[Flags]
	public enum LineActivation
	{
		None = 0,
		Cross = 1 << 0,
		Use = 1 << 1,
		MCross = 1 << 2,
		Impact = 1 << 3,
		Push = 1 << 4,
		PCross = 1 << 5,
		UseThrough = 1 << 6,
		AnyCross = 1 << 7,
		MUse = 1 << 8,
		MPush = 1 << 9,
		UseBack = 1 << 10,
		Damage = 1 << 11,
		Death = 1 << 12,
		Walking = 1 << 13,
	}

	// A line's special as the engine holds it after loading, whatever format it came from
	public sealed class CanonicalLine
	{
		public int Special;
		public int[] Args = new int[5];
		public LineActivation Activation;
		public bool Repeat;
		public bool Monsters;
		public bool FirstSideOnly;
		public int LineId;
		public List<string> IdFlags = new List<string>(); // Line_SetIdentification flags other than firstsideonly, by UDMF name

		// Engine line flags from the xlat lineflag rules (Doom sources only), and their special cases
		public int EngineFlags;
		public int EngineFlags2;
		public bool Passthrough;
		public double Alpha = 1.0;

		public bool SameSpecial(CanonicalLine o)
		{
			if(Special != o.Special || Activation != o.Activation || Repeat != o.Repeat || Monsters != o.Monsters || FirstSideOnly != o.FirstSideOnly) return false;
			for(int i = 0; i < Args.Length; i++) if(Args[i] != o.Args[i]) return false;
			return true;
		}

		public override string ToString()
		{
			return Special + "(" + string.Join(", ", Args) + ") " + Activation + (Repeat ? " rep" : "") + (Monsters ? " mon" : "") + (FirstSideOnly ? " first" : "") + " id " + LineId;
		}
	}

	// A Doom-format line found for a canonical special
	public struct DoomLine
	{
		public int Type;
		public int Tag;
		public int Flags; // Doom flag bits the match needs (the table's passthrough bit)
		public bool LostPassthrough; // UseThrough matched as Use: the table has no passthrough bit
	}

	// Doom-format line types to and from the engine's specials, per p_xlat.cpp's TranslateLineDef
	public sealed class LineXlat
	{
		#region ================== Constants

		private const int ML_SECRET = 0x20;
		private const int ML_REPEAT_SPECIAL = 0x200;
		private const int ML_SPAC_MASK = 0x1c00;
		private const int ML_SPAC_SHIFT = 10;
		private const int ML_MONSTERSCANACTIVATE = 0x2000;
		private const int ML_FIRSTSIDEONLY = 0x800000;

		private const int TAG_SENTINEL_A = 0x5A17;
		private const int TAG_SENTINEL_B = 0x2C6B;

		#endregion

		#region ================== Variables

		private readonly XlatTable table;
		private readonly int passthroughflag;
		private readonly int genericdoor;

		// Reverse indexes, built on first use
		private Dictionary<int, List<Pattern>> simplebyspecial;
		private Dictionary<string, int> boombykey;

		#endregion

		#region ================== Properties

		public XlatTable Table { get { return table; } }

		// The Doom flag bit the lineflag rules map to ML_PASSTHROUGH, or 0 (Strife's bit 9 is ML_RAILING)
		public int PassthroughFlag { get { return passthroughflag; } }

		#endregion

		#region ================== Constructor

		public LineXlat(XlatTable table)
		{
			this.table = table;
			for(int i = 0; i < 16 && passthroughflag == 0; i++)
				if(table.LineFlags[i].Defined && !table.LineFlags[i].IsMask && table.LineFlags[i].NewValue == -1) passthroughflag = 1 << i;
			if(!table.SpecialNames.TryGetValue("Generic_Door", out genericdoor)) genericdoor = -1;
		}

		#endregion

		#region ================== Forward

		public CanonicalLine Translate(int type, int doomflags, int tag)
		{
			CanonicalLine ld = new CanonicalLine();
			tag = (short)tag; // maplinedef_t's tag is int16_t
			ld.LineId = tag;

			// Line flags: masks first, then values
			int flags1 = doomflags;
			for(int i = 0; i < 16; i++)
				if((doomflags & (1 << i)) != 0 && table.LineFlags[i].Defined && table.LineFlags[i].IsMask)
					flags1 &= table.LineFlags[i].NewValue;

			int newflags = 0, newflags2 = 0;
			for(int i = 0; i < 16; i++)
			{
				if((flags1 & (1 << i)) == 0 || !table.LineFlags[i].Defined || table.LineFlags[i].IsMask) continue;
				int val = table.LineFlags[i].NewValue;
				switch(val)
				{
					case -1: ld.Passthrough = true; break;
					case -2: ld.Alpha = 0.75; break;
					case -3: ld.Alpha = 0.25; break;
					default:
						if((val & unchecked((int)0x80000000)) != 0 && val != unchecked((int)0x80000000)) newflags2 |= val;
						else newflags |= val;
						break;
				}
			}
			int flags = newflags;
			ld.EngineFlags2 = newflags2;

			// 0 specials are never translated
			if(type == 0)
			{
				ld.EngineFlags = flags;
				ld.Args[0] = tag;
				return ld;
			}

			XlatLineEntry lt;
			if(table.SimpleLines.TryGetValue(type, out lt) && lt.Special != 0)
			{
				ld.Special = lt.Special;
				flags |= (lt.Flags & 0x1f) << 9;
				if((lt.Flags & XlatLineEntry.FIRSTSIDE) != 0) flags |= ML_FIRSTSIDEONLY;

				LineActivation act = (LineActivation)(1 << ((flags & ML_SPAC_MASK) >> ML_SPAC_SHIFT));
				if(act == LineActivation.AnyCross || act == LineActivation.Impact) act = LineActivation.Impact | LineActivation.PCross;
				flags &= ~ML_SPAC_MASK;
				if(ld.Passthrough && act == LineActivation.Use) act = LineActivation.UseThrough;

				for(int t = 0; t < 5; t++) ld.Args[t] = lt.Args[t].Evaluate(tag);

				if((flags & ML_SECRET) != 0 && (act & (LineActivation.Use | LineActivation.UseThrough)) != 0)
					flags &= ~ML_MONSTERSCANACTIVATE;

				ld.Activation = act;
				SetFlags(ld, flags);
				return ld;
			}

			foreach(XlatBoomRange b in table.BoomRanges)
			{
				if(type < b.FirstType || type > b.LastType) continue;
				ld.Special = b.Special;

				LineActivation act;
				switch(type & 7)
				{
					case 1: flags |= ML_REPEAT_SPECIAL; act = LineActivation.Cross; break;
					case 0: act = LineActivation.Cross; break;
					case 3: case 7: flags |= ML_REPEAT_SPECIAL; act = ld.Passthrough ? LineActivation.UseThrough : LineActivation.Use; break;
					case 2: case 6: act = ld.Passthrough ? LineActivation.UseThrough : LineActivation.Use; break;
					case 5: flags |= ML_REPEAT_SPECIAL; act = LineActivation.Impact; break;
					default: act = LineActivation.Impact; break;
				}

				ld.Args[0] = tag;
				foreach(XlatBoomArg arg in b.Args)
				{
					int flagtemp = (flags >> 9) & 0x3f;
					int dest = arg.ArgNum < 4 ? ld.Args[arg.ArgNum + 1] : flagtemp;
					int val = 0;
					bool found;
					if(arg.Filters == null)
					{
						val = arg.ConstantValue;
						found = true;
					}
					else
					{
						// The engine keeps the last match
						found = false;
						for(int k = 0; k < arg.Filters.Length; k++)
						{
							if((type & arg.AndValue) == arg.Filters[k])
							{
								val = arg.Values[k];
								found = true;
							}
						}
					}
					if(!found) continue;

					dest = arg.OrExisting ? dest | val : val;
					if(arg.ArgNum < 4) ld.Args[arg.ArgNum + 1] = dest;
					else flags = (flags & ~0x7e00) | (dest << 9);
				}

				// Push triggers are switch triggers with zero tags
				if((type & 7) == 6 || (type & 7) == 7)
				{
					if(ld.Special == genericdoor) ld.Args[2] |= 128;
					else ld.Args[0] = 0;
				}

				if((flags & ML_MONSTERSCANACTIVATE) != 0 && act == LineActivation.Cross) act = LineActivation.AnyCross;
				ld.Activation = act;
				SetFlags(ld, flags);
				return ld;
			}

			// Unknown types become 0
			ld.EngineFlags = flags;
			return ld;
		}

		private static void SetFlags(CanonicalLine ld, int flags)
		{
			ld.Repeat = (flags & ML_REPEAT_SPECIAL) != 0;
			ld.Monsters = (flags & ML_MONSTERSCANACTIVATE) != 0;
			ld.FirstSideOnly = (flags & ML_FIRSTSIDEONLY) != 0;
			ld.EngineFlags = flags & ~(ML_REPEAT_SPECIAL | ML_MONSTERSCANACTIVATE | ML_FIRSTSIDEONLY | ML_SPAC_MASK);
		}

		#endregion

		#region ================== Reverse

		// One translated line type with its args as constants or tag markers
		private sealed class Pattern
		{
			public int Type;
			public CanonicalLine Line;
			public bool[] IsTag = new bool[5];
		}

		private void BuildIndex()
		{
			if(simplebyspecial != null) return;
			simplebyspecial = new Dictionary<int, List<Pattern>>();
			boombykey = new Dictionary<string, int>();

			List<int> types = new List<int>(table.SimpleLines.Keys);
			types.Sort();
			foreach(int type in types)
			{
				Pattern p = MakePattern(type);
				if(p == null) continue;
				List<Pattern> list;
				if(!simplebyspecial.TryGetValue(p.Line.Special, out list)) simplebyspecial[p.Line.Special] = list = new List<Pattern>();
				list.Add(p);
			}

			foreach(XlatBoomRange b in table.BoomRanges)
			{
				for(int type = b.FirstType; type <= b.LastType; type++)
				{
					if(table.SimpleLines.ContainsKey(type) && table.SimpleLines[type].Special != 0) continue;
					CanonicalLine a = Translate(type, 0, TAG_SENTINEL_A);
					if(a.Special == 0) continue;
					string key = BoomKey(a, a.Args[0] == TAG_SENTINEL_A);
					if(!boombykey.ContainsKey(key)) boombykey[key] = type;
				}
			}
		}

		private Pattern MakePattern(int type)
		{
			CanonicalLine a = Translate(type, 0, TAG_SENTINEL_A);
			CanonicalLine b = Translate(type, 0, TAG_SENTINEL_B);
			if(a.Special == 0) return null;
			Pattern p = new Pattern { Type = type, Line = a };
			for(int i = 0; i < 5; i++)
			{
				if(a.Args[i] == TAG_SENTINEL_A && b.Args[i] == TAG_SENTINEL_B) p.IsTag[i] = true;
				else if(a.Args[i] != b.Args[i]) return null; // the tag inside an expression is forward-only
			}
			return p;
		}

		private static string BoomKey(CanonicalLine l, bool arg0istag)
		{
			return l.Special + ":" + (arg0istag ? "t" : l.Args[0].ToString()) + ":" + l.Args[1] + ":" + l.Args[2] + ":" + l.Args[3] + ":" + l.Args[4] + ":" + (int)l.Activation + ":" + l.Repeat + ":" + l.Monsters + ":" + l.FirstSideOnly;
		}

		public DoomLine? Find(CanonicalLine c, bool secret)
		{
			BuildIndex();
			if(c.Special == 0) return new DoomLine { Type = 0, Tag = c.LineId };

			// UseThrough comes from Use plus the passthrough flag
			bool passthrough = c.Activation == LineActivation.UseThrough;
			LineActivation act = passthrough ? LineActivation.Use : c.Activation;
			int ptflags = passthrough ? passthroughflag : 0;
			bool lostpt = passthrough && passthroughflag == 0;

			// An ID-only arg is 0 in canonical lines; the Doom tag fills it, so it matches anything
			bool keeparg;
			int idarg = HexenLineCodec.IdArg(c.Special, c.Args, out keeparg);
			if(keeparg) idarg = -1;

			List<DoomLine> found = new List<DoomLine>();
			List<Pattern> list;
			if(simplebyspecial.TryGetValue(c.Special, out list))
			{
				foreach(Pattern p in list)
				{
					CanonicalLine l = p.Line;
					if(l.Activation != act || l.Repeat != c.Repeat || l.FirstSideOnly != c.FirstSideOnly) continue;
					bool monsters = l.Monsters && !(secret && (act & LineActivation.Use) != 0); // the Doom secret flag drops it from use lines
					if(monsters != c.Monsters) continue;

					int tag = int.MinValue;
					bool ok = true;
					for(int i = 0; i < 5 && ok; i++)
					{
						if(i == idarg) continue;
						if(!p.IsTag[i]) ok = l.Args[i] == c.Args[i];
						else if(tag == int.MinValue) tag = c.Args[i];
						else ok = tag == c.Args[i];
					}
					if(ok) found.Add(new DoomLine { Type = p.Type, Tag = tag == int.MinValue ? c.LineId : tag, Flags = ptflags, LostPassthrough = lostpt });
				}
			}

			// Boom ranges: arg0 is the tag, or 0 for push triggers
			CanonicalLine n = c;
			if(passthrough)
			{
				n = new CanonicalLine { Special = c.Special, Args = c.Args, Activation = act, Repeat = c.Repeat, Monsters = c.Monsters, FirstSideOnly = c.FirstSideOnly, LineId = c.LineId };
			}
			int boomtype;
			if(boombykey.TryGetValue(BoomKey(n, true), out boomtype))
				found.Add(new DoomLine { Type = boomtype, Tag = c.Args[0], Flags = ptflags, LostPassthrough = lostpt });
			if(c.Args[0] == 0 && boombykey.TryGetValue(BoomKey(n, false), out boomtype))
				found.Add(new DoomLine { Type = boomtype, Tag = c.LineId, Flags = ptflags, LostPassthrough = lostpt });

			// Lowest type, preferring one whose tag keeps the line ID
			DoomLine? best = null;
			foreach(DoomLine d in found)
			{
				if(best == null) { best = d; continue; }
				bool keeps = d.Tag == c.LineId, bestkeeps = best.Value.Tag == c.LineId;
				if((keeps && !bestkeeps) || (keeps == bestkeeps && d.Type < best.Value.Type)) best = d;
			}
			return best;
		}

		#endregion
	}
}
