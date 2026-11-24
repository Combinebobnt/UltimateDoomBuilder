#region ================== Namespaces

using System.Collections.Generic;

#endregion

namespace CodeImp.DoomBuilder.Map.Conversion
{
	// Special numbers the line ID rules use (actionspecials.h)
	internal static class Specials
	{
		public const int Polyobj_StartLine = 1;
		public const int Polyobj_ExplicitLine = 5;
		public const int Line_SetIdentification = 121;
		public const int Line_SetPortal = 156;
		public const int Sector_Set3DFloor = 160;
		public const int Plane_Align = 181;
		public const int Static_Init = 190;
		public const int TranslucentLine = 208;
		public const int Teleport_Line = 215;
		public const int Scroll_Texture_Model = 222;

		public const int Init_SectorLink = 3;
	}

	// A line's special as the Hexen format stores it
	public sealed class HexenLine
	{
		public int Action;
		public int[] Args = new int[5];
		public int Spac;
		public bool Repeat;
		public bool Monsters;

		public int Activate { get { return Spac << 10; } } // UDB's Hexen activation value
	}

	// Hexen-format specials to and from the canonical line, per maploader.cpp's Hexen line loading and SetLineID
	public static class HexenLineCodec
	{
		public static LineActivation FromSpac(int spac)
		{
			LineActivation a = (LineActivation)(1 << (spac & 7));
			// The engine reads SPAC 7 (PTouch) and Impact both as impact plus projectile cross
			if(a == LineActivation.AnyCross || a == LineActivation.Impact) return LineActivation.Impact | LineActivation.PCross;
			return a;
		}

		// Null when Hexen has no SPAC for this combination
		public static int? ToSpac(LineActivation a)
		{
			switch(a)
			{
				case LineActivation.None: return 0;
				case LineActivation.Cross: return 0;
				case LineActivation.Use: return 1;
				case LineActivation.MCross: return 2;
				case LineActivation.Impact | LineActivation.PCross: return 3;
				case LineActivation.Push: return 4;
				case LineActivation.PCross: return 5;
				case LineActivation.UseThrough: return 6;
				default: return null;
			}
		}

		// The arg holding this line's own ID, and whether the canonical line keeps it in the arg too
		internal static int IdArg(int special, int[] args, out bool keeparg)
		{
			keeparg = true;
			switch(special)
			{
				case Specials.TranslucentLine:
				case Specials.Scroll_Texture_Model:
					return 0;
				case Specials.Teleport_Line: keeparg = false; return 0;
				case Specials.Polyobj_StartLine: keeparg = false; return 3;
				case Specials.Polyobj_ExplicitLine: keeparg = false; return 4;
				case Specials.Plane_Align: keeparg = false; return 2;
				case Specials.Line_SetPortal: return 1;
				case Specials.Static_Init: return args[1] == Specials.Init_SectorLink ? 0 : -1;
				default: return -1;
			}
		}

		// Canonical lines keep ID-only args at 0, as UDMF does: clears one holding the line's ID
		public static void ClearIdArg(CanonicalLine c)
		{
			bool keeparg;
			int idarg = IdArg(c.Special, c.Args, out keeparg);
			if(idarg >= 0 && !keeparg && c.Args[idarg] == c.LineId) c.Args[idarg] = 0;
		}

		// The reverse, for formats whose single tag fills the ID arg (Doom)
		public static void FillIdArg(CanonicalLine c)
		{
			bool keeparg;
			int idarg = IdArg(c.Special, c.Args, out keeparg);
			if(idarg >= 0 && !keeparg && c.Args[idarg] == 0) c.Args[idarg] = c.LineId;
		}

		public static CanonicalLine Read(HexenLine h)
		{
			CanonicalLine c = new CanonicalLine();
			c.Special = h.Action;
			for(int i = 0; i < 5; i++) c.Args[i] = h.Args[i];
			c.Activation = h.Action == 0 ? LineActivation.None : FromSpac(h.Spac);
			c.Repeat = h.Repeat;
			c.Monsters = h.Monsters;

			switch(h.Action)
			{
				case Specials.Line_SetIdentification:
					c.LineId = h.Args[0] + 256 * h.Args[4];
					AddIdFlags(c, h.Args[1]);
					c.Special = 0;
					c.Args = new int[5];
					c.Activation = LineActivation.None;
					c.Repeat = c.Monsters = false;
					return c;

				case Specials.TranslucentLine:
					AddIdFlags(c, h.Args[3]);
					c.Args[3] = 0;
					break;

				case Specials.Sector_Set3DFloor:
					// Arg4 is a line ID with flag 8, else the sector tag's high byte
					if((c.Args[1] & 8) != 0)
					{
						c.LineId = c.Args[4];
						c.Args[1] &= ~8;
					}
					else
					{
						c.Args[0] += c.Args[4] * 256;
					}
					c.Args[4] = 0;
					return c;
			}

			bool keeparg;
			int idarg = IdArg(h.Action, h.Args, out keeparg);
			if(idarg >= 0)
			{
				c.LineId = h.Args[idarg];
				if(!keeparg) c.Args[idarg] = 0;
			}
			return c;
		}

		private static void AddIdFlags(CanonicalLine c, int bits)
		{
			for(int i = 0; i < Linedef.IdentificationArgFlags.Length; i++)
			{
				if((bits & (1 << i)) == 0) continue;
				if(Linedef.IdentificationArgFlags[i] == "firstsideonly") c.FirstSideOnly = true;
				else c.IdFlags.Add(Linedef.IdentificationArgFlags[i]);
			}
		}

		// needid: some special references this line's ID, so it must survive
		public static HexenLine Write(CanonicalLine c, bool needid, List<string> issues)
		{
			HexenLine h = new HexenLine();
			h.Action = c.Special;
			for(int i = 0; i < 5; i++) h.Args[i] = c.Args[i];
			h.Repeat = c.Repeat;
			h.Monsters = c.Monsters;

			if(c.Special != 0)
			{
				int? spac = ToSpac(c.Activation);
				if(spac == null)
				{
					spac = FirstSpac(c.Activation);
					issues.Add("activation " + c.Activation + " has no Hexen equivalent, kept " + FromSpac(spac.Value));
				}
				h.Spac = spac.Value;
			}

			bool wantid = needid && c.LineId != 0;
			Dictionary<string, bool> idflagset = ToDictionary(c.IdFlags);
			if(c.FirstSideOnly) idflagset["firstsideonly"] = true;
			int idflags = Linedef.FlagsToArgBits(idflagset);

			if(c.Special == 0)
			{
				if(wantid || idflags != 0)
				{
					if(c.LineId > 255 * 256 + 255) issues.Add("line ID " + c.LineId + " is over 65535");
					h.Action = Specials.Line_SetIdentification;
					h.Args = new[] { c.LineId & 255, idflags, 0, 0, (c.LineId >> 8) & 255 };
				}
				return h;
			}

			if(c.Special == Specials.TranslucentLine) h.Args[3] = idflags;
			else if(idflags != 0) issues.Add("line flags " + string.Join(", ", idflagset.Keys) + " need Line_SetIdentification but the line has another special");

			if(c.Special == Specials.Sector_Set3DFloor)
			{
				if(h.Args[0] > 255)
				{
					h.Args[4] = h.Args[0] / 256;
					h.Args[0] %= 256;
					if(wantid) issues.Add("line ID " + c.LineId + " lost: arg4 holds the sector tag's high byte");
				}
				else if(wantid)
				{
					if(c.LineId > 255) issues.Add("line ID " + c.LineId + " is over 255");
					else
					{
						h.Args[4] = c.LineId;
						h.Args[1] |= 8;
					}
				}
				return h;
			}

			if(!wantid) return h;

			bool keeparg;
			int idarg = IdArg(c.Special, c.Args, out keeparg);
			if(idarg < 0) issues.Add("line ID " + c.LineId + " lost: the line has a special with no ID argument");
			else if(c.LineId > 255) issues.Add("line ID " + c.LineId + " is over 255");
			else if(h.Args[idarg] != 0 && h.Args[idarg] != c.LineId) issues.Add("line ID " + c.LineId + " lost: arg" + idarg + " holds " + h.Args[idarg]);
			else h.Args[idarg] = c.LineId;
			return h;
		}

		// The first activation bit by SPAC order
		private static int FirstSpac(LineActivation a)
		{
			for(int spac = 0; spac < 7; spac++)
				if((a & (LineActivation)(1 << spac)) != 0) return spac;
			if((a & LineActivation.AnyCross) != 0) return 0;
			return 1;
		}

		private static Dictionary<string, bool> ToDictionary(IEnumerable<string> flags)
		{
			Dictionary<string, bool> d = new Dictionary<string, bool>();
			foreach(string f in flags) d[f] = true;
			return d;
		}
	}

	// UDMF activation fields to and from the canonical line, per udmf.cpp's ParseLinedef
	public static class UdmfLineCodec
	{
		private static readonly KeyValuePair<string, LineActivation>[] fields =
		{
			new KeyValuePair<string, LineActivation>("playercross", LineActivation.Cross),
			new KeyValuePair<string, LineActivation>("playeruse", LineActivation.Use),
			new KeyValuePair<string, LineActivation>("monstercross", LineActivation.MCross),
			new KeyValuePair<string, LineActivation>("impact", LineActivation.Impact),
			new KeyValuePair<string, LineActivation>("playerpush", LineActivation.Push),
			new KeyValuePair<string, LineActivation>("missilecross", LineActivation.PCross),
			new KeyValuePair<string, LineActivation>("anycross", LineActivation.AnyCross),
			new KeyValuePair<string, LineActivation>("monsteruse", LineActivation.MUse),
			new KeyValuePair<string, LineActivation>("monsterpush", LineActivation.MPush),
			new KeyValuePair<string, LineActivation>("playeruseback", LineActivation.UseBack),
		};

		// Every field this codec reads or writes
		public static IEnumerable<string> FieldNames
		{
			get
			{
				foreach(var f in fields) yield return f.Key;
				yield return "passuse";
				yield return "repeatspecial";
				yield return "monsteractivate";
				yield return "firstsideonly";
			}
		}

		public static CanonicalLine Read(int action, int[] args, IDictionary<string, bool> flags, int lineid)
		{
			CanonicalLine c = new CanonicalLine();
			c.Special = action;
			for(int i = 0; i < 5; i++) c.Args[i] = args[i];
			c.LineId = lineid;
			foreach(var f in fields) if(IsSet(flags, f.Key)) c.Activation |= f.Value;
			if(IsSet(flags, "passuse") && (c.Activation & LineActivation.Use) != 0)
				c.Activation = (c.Activation & ~LineActivation.Use) | LineActivation.UseThrough;
			c.Repeat = IsSet(flags, "repeatspecial");
			c.Monsters = IsSet(flags, "monsteractivate");
			c.FirstSideOnly = IsSet(flags, "firstsideonly");
			foreach(string f in Linedef.IdentificationArgFlags) if(f != "firstsideonly" && IsSet(flags, f)) c.IdFlags.Add(f);
			return c;
		}

		// Sets the activation fields on flags (clearing the others); IdFlags are set too
		public static void Write(CanonicalLine c, IDictionary<string, bool> flags)
		{
			foreach(string name in FieldNames) flags.Remove(name);
			LineActivation a = c.Activation;
			if((a & LineActivation.UseThrough) != 0)
			{
				a = (a & ~LineActivation.UseThrough) | LineActivation.Use;
				flags["passuse"] = true;
			}
			foreach(var f in fields) if((a & f.Value) != 0) flags[f.Key] = true;
			if(c.Repeat) flags["repeatspecial"] = true;
			if(c.Monsters) flags["monsteractivate"] = true;
			if(c.FirstSideOnly) flags["firstsideonly"] = true;
			foreach(string f in c.IdFlags) flags[f] = true;
		}

		private static bool IsSet(IDictionary<string, bool> flags, string name)
		{
			bool v;
			return flags.TryGetValue(name, out v) && v;
		}
	}
}
