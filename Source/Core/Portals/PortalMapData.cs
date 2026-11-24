using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Geometry;

namespace CodeImp.DoomBuilder.Portals
{
	// A plain snapshot of what portal analysis reads. Each list is in map index order and an element's Index is its position.
	public sealed class PortalLine
	{
		public int Index;
		public int Action;
		public int[] Args = new int[5];
		public int[] Ids = new int[0];
		public Vector2D V1;
		public Vector2D V2;
		public int Front = -1;
		public int Back = -1;

		public int FirstId { get { return Ids.Length > 0 ? Ids[0] : 0; } }
		public Vector2D Middle { get { return new Vector2D((V1.x + V2.x) * 0.5, (V1.y + V2.y) * 0.5); } }
	}

	public sealed class PortalSector
	{
		public int Index;
		public int[] Tags = new int[0];
		public bool FloorSloped;
		public bool CeilingSloped;

		// Tag 0 means "every untagged sector", as the engine's sector tag iterator treats it
		public bool MatchesTag(int tag)
		{
			if(tag != 0) return Array.IndexOf(Tags, tag) >= 0;
			foreach(int t in Tags) if(t != 0) return false;
			return true;
		}
	}

	public sealed class PortalThing
	{
		public int Index;
		public int Type;
		public int Tid;
		public int[] Args = new int[5];
		public Vector2D Position;
		public int Sector = -1;
	}

	public sealed class PortalMapData
	{
		public readonly List<PortalLine> Lines = new List<PortalLine>();
		public readonly List<PortalSector> Sectors = new List<PortalSector>();
		public readonly List<PortalThing> Things = new List<PortalThing>();

		public PortalLine AddLine(int action, int[] args, int[] ids, Vector2D v1, Vector2D v2, int front, int back)
		{
			PortalLine l = new PortalLine { Index = Lines.Count, Action = action, Args = PadArgs(args), Ids = ids ?? new int[0], V1 = v1, V2 = v2, Front = front, Back = back };
			Lines.Add(l);
			return l;
		}

		public PortalSector AddSector(params int[] tags)
		{
			PortalSector s = new PortalSector { Index = Sectors.Count, Tags = tags ?? new int[0] };
			Sectors.Add(s);
			return s;
		}

		public PortalThing AddThing(int type, int tid, int[] args, Vector2D pos, int sector)
		{
			PortalThing t = new PortalThing { Index = Things.Count, Type = type, Tid = tid, Args = PadArgs(args), Position = pos, Sector = sector };
			Things.Add(t);
			return t;
		}

		// Replaces every line's ids with the ones the engine derives from args in Hexen format
		public void AssignHexenIds(PortalCapabilities caps)
		{
			foreach(PortalLine l in Lines)
			{
				int id = PortalLineIds.ForHexen(caps, l.Action, l.Args);
				l.Ids = id != 0 ? new[] { id } : new int[0];
			}
		}

		private static int[] PadArgs(int[] args)
		{
			int[] result = new int[5];
			if(args != null) Array.Copy(args, result, Math.Min(args.Length, 5));
			return result;
		}
	}

	// Which portal kinds the active configuration and map format support. Action and thing numbers are 0 when absent.
	public sealed class PortalCapabilities
	{
		public int LineSetPortal;
		public int LineQuickPortal;
		public int SectorSetPortal;
		public int UpperStack;
		public int LowerStack;
		public int SkyViewpoint;
		public int SkyPicker;
		public int SkyCamCompat;
		public bool StackedThings;
		public bool Hexen;

		// Specials that carry a line id in Hexen format
		public int LineSetIdentification;
		public int TranslucentLine;
		public int TeleportLine;
		public int ScrollTextureModel;
		public int PolyobjStartLine;
		public int PolyobjExplicitLine;
		public int PlaneAlign;
		public int StaticInit;

		// Only ZDoom-family configurations define UpperStackLookOnly; Eternity gives 57 and 301 other meanings
		public bool Any { get { return UpperStack != 0; } }
		public bool ModernSectorPortals { get { return LineSetPortal != 0; } }

		public static readonly PortalCapabilities None = new PortalCapabilities();

		public static PortalCapabilities Create(Func<string, int> actionbyid, Func<string, int> thingbyclass, bool hexen, bool thingtids)
		{
			PortalCapabilities c = new PortalCapabilities();
			c.UpperStack = thingbyclass("UpperStackLookOnly");
			if(c.UpperStack == 0) return c;

			c.LineSetPortal = actionbyid("Line_SetPortal");
			c.LineQuickPortal = actionbyid("Line_QuickPortal");
			c.SectorSetPortal = actionbyid("Sector_SetPortal");
			c.LowerStack = thingbyclass("LowerStackLookOnly");
			c.SkyViewpoint = thingbyclass("SkyViewpoint");
			c.SkyPicker = thingbyclass("SkyPicker");
			c.SkyCamCompat = thingbyclass("SkyCamCompat");
			c.StackedThings = thingtids && c.LowerStack != 0;
			c.Hexen = hexen;
			c.LineSetIdentification = actionbyid("Line_SetIdentification");
			c.TranslucentLine = actionbyid("TranslucentLine");
			c.TeleportLine = actionbyid("Teleport_Line");
			c.ScrollTextureModel = actionbyid("Scroll_Texture_Model");
			c.PolyobjStartLine = actionbyid("Polyobj_StartLine");
			c.PolyobjExplicitLine = actionbyid("Polyobj_ExplicitLine");
			c.PlaneAlign = actionbyid("Plane_Align");
			c.StaticInit = actionbyid("Static_Init");
			return c;
		}

		// Thing classes a skybox picker accepts: SkyViewpoint and its subclasses
		public bool IsViewpoint(int type)
		{
			return type != 0 && (type == SkyViewpoint || type == SkyCamCompat || type == UpperStack || type == LowerStack);
		}
	}

	// The line id the engine gives a Hexen-format line (maploader.cpp, SetLineID); 0 when it has none
	public static class PortalLineIds
	{
		private const int INIT_SECTORLINK = 3;

		public static int ForHexen(PortalCapabilities caps, int action, int[] args)
		{
			if(action == 0) return 0;
			if(action == caps.LineSetIdentification) return args[0] + 256 * args[4];
			if(action == caps.TranslucentLine || action == caps.TeleportLine || action == caps.ScrollTextureModel) return args[0];
			if(action == caps.PolyobjStartLine) return args[3];
			if(action == caps.PolyobjExplicitLine) return args[4];
			if(action == caps.PlaneAlign) return args[2];
			if(action == caps.StaticInit) return args[1] == INIT_SECTORLINK ? args[0] : 0;
			if(action == caps.LineSetPortal) return args[1];
			return 0;
		}
	}
}
