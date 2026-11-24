using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.IO;
using CodeImp.DoomBuilder.Map;

namespace CodeImp.DoomBuilder.Portals
{
	// The only part of portal analysis that reads the live map and configuration
	public static class PortalMapSource
	{
		public static PortalCapabilities Capabilities(GameConfiguration config, IMapSetIO io)
		{
			if(config == null || io == null) return PortalCapabilities.None;

			// Formats without linedef tags take line ids from args
			return Capabilities(config, !io.HasLinedefTag, io.HasThingTag);
		}

		public static PortalCapabilities Capabilities(GameConfiguration config, bool hexen, bool thingtids)
		{
			Func<string, int> actionbyid = delegate(string id)
			{
				int best = 0;
				foreach(KeyValuePair<int, LinedefActionInfo> pair in config.LinedefActions)
					if(pair.Key > 0 && string.Equals(pair.Value.Id, id, StringComparison.OrdinalIgnoreCase) && (best == 0 || pair.Key < best)) best = pair.Key;
				return best;
			};
			Func<string, int> thingbyclass = delegate(string classname)
			{
				int best = 0;
				foreach(KeyValuePair<int, ThingTypeInfo> pair in config.ThingTypes)
					if(pair.Key > 0 && string.Equals(pair.Value.ClassName, classname, StringComparison.OrdinalIgnoreCase) && (best == 0 || pair.Key < best)) best = pair.Key;
				return best;
			};

			return PortalCapabilities.Create(actionbyid, thingbyclass, hexen, thingtids);
		}

		public static PortalMapData FromMap(MapSet map, PortalCapabilities caps)
		{
			PortalMapData data = new PortalMapData();
			if(map == null) return data;

			foreach(Sector s in map.Sectors)
			{
				PortalSector ps = data.AddSector(NonZero(s.Tags));
				ps.FloorSloped = IsSloped(s.FloorSlope);
				ps.CeilingSloped = IsSloped(s.CeilSlope);
			}

			foreach(Linedef l in map.Linedefs)
			{
				int front = (l.Front != null && l.Front.Sector != null) ? l.Front.Sector.Index : -1;
				int back = (l.Back != null && l.Back.Sector != null) ? l.Back.Sector.Index : -1;
				data.AddLine(l.Action, l.Args, caps.Hexen ? null : NonZero(l.Tags), l.Start.Position, l.End.Position, front, back);
			}
			if(caps.Hexen) data.AssignHexenIds(caps);

			// Only portal things need their sector, which costs a sector search each
			foreach(Thing t in map.Things)
			{
				int sector = -1;
				if(IsPortalThing(caps, t.Type))
				{
					Sector s = t.Sector ?? map.GetSectorByCoordinates(new Vector2D(t.Position.x, t.Position.y));
					if(s != null) sector = s.Index;
				}
				data.AddThing(t.Type, t.Tag, t.Args, new Vector2D(t.Position.x, t.Position.y), sector);
			}
			return data;
		}

		private static bool IsPortalThing(PortalCapabilities caps, int type)
		{
			return type != 0 && (caps.IsViewpoint(type) || type == caps.SkyPicker);
		}

		private static bool IsSloped(Vector3D normal)
		{
			return normal.GetLengthSq() > 0 && (normal.x != 0 || normal.y != 0);
		}

		private static int[] NonZero(List<int> tags)
		{
			if(tags == null) return new int[0];
			List<int> result = new List<int>(tags.Count);
			foreach(int t in tags) if(t != 0 && !result.Contains(t)) result.Add(t);
			return result.ToArray();
		}
	}
}
