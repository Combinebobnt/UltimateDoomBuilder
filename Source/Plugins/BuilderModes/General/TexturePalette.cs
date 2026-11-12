using System.Collections.Generic;
using System.Linq;
using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Windows;

namespace CodeImp.DoomBuilder.BuilderModes
{
	internal static class TexturePalette
	{
		internal static bool Mixed { get { return General.Map.TextureHistory.Mixed; } }

		internal static bool Accepts(IVisualEventReceiver target, TextureHistoryKind kind, bool mixed)
		{
			return (target is BaseVisualGeometrySidedef && (mixed || kind == TextureHistoryKind.Texture))
				|| (target is BaseVisualGeometrySector && (mixed || kind == TextureHistoryKind.Flat));
		}

		internal static void SetBrush(string name, TextureHistoryKind kind)
		{
			if(Mixed || kind == TextureHistoryKind.Texture) BuilderPlug.Me.CopiedTexture = name;
			if(Mixed || kind == TextureHistoryKind.Flat) BuilderPlug.Me.CopiedFlat = name;
			General.Interface.DisplayStatus(StatusType.Action, "Brush set to \"" + name + "\".");
		}

		internal static int ApplyToLinedefs(string name, IEnumerable<Linedef> lines)
		{
			return ApplyToWalls(name, lines.Where(line => line != null && !line.IsDisposed)
				.SelectMany(line => new[] { line.Front, line.Back }), false);
		}

		internal static int ApplyToSectorWalls(string name, IEnumerable<Sector> sectors)
		{
			return ApplyToWalls(name, sectors.Where(sector => sector != null && !sector.IsDisposed).SelectMany(sector => sector.Sidedefs), true);
		}

		private static int ApplyToWalls(string name, IEnumerable<Sidedef> sides, bool sectorcount)
		{
			List<Sidedef> targets = sides.Where(side => side != null && !side.IsDisposed).Distinct().ToList();
			bool undo = false;
			HashSet<Linedef> lines = new HashSet<Linedef>();
			HashSet<Sector> sectors = new HashSet<Sector>();
			foreach(Sidedef side in targets)
			{
				bool high = (side.HighRequired() || HasTexture(side.HighTexture)) && side.HighTexture != name;
				bool low = (side.LowRequired() || HasTexture(side.LowTexture)) && side.LowTexture != name;
				bool middle = side.MiddleRequired() && side.MiddleTexture != name;
				if(!high && !low && !middle) continue;
				if(!undo)
				{
					General.Map.UndoRedo.CreateUndo("Apply texture \"" + name + "\"");
					undo = true;
				}
				if(high) side.SetTextureHigh(name);
				if(low) side.SetTextureLow(name);
				if(middle) side.SetTextureMid(name);
				lines.Add(side.Line);
				sectors.Add(side.Sector);
			}
			int count = sectorcount ? sectors.Count : lines.Count;
			if(count > 0) Finish(name, TextureHistoryKind.Texture, count, sectorcount ? "sector walls" : "linedefs");
			return count;
		}

		private static bool HasTexture(string name) { return !string.IsNullOrEmpty(name) && name != "-"; }

		internal static int ApplyToSectors(string name, IEnumerable<Sector> sectors, bool floor, bool ceiling)
		{
			List<Sector> targets = sectors.Where(sector => sector != null && !sector.IsDisposed
				&& ((floor && sector.FloorTexture != name) || (ceiling && sector.CeilTexture != name))).Distinct().ToList();
			if(targets.Count == 0) return 0;
			General.Map.UndoRedo.CreateUndo("Apply " + (floor ? "floor" : "ceiling") + " \"" + name + "\"");
			foreach(Sector sector in targets)
			{
				if(floor) sector.SetFloorTexture(name);
				if(ceiling) sector.SetCeilTexture(name);
			}
			Finish(name, TextureHistoryKind.Flat, targets.Count, floor ? "sector floors" : "sector ceilings");
			return targets.Count;
		}

		private static void Finish(string name, TextureHistoryKind kind, int count, string target)
		{
			General.Map.Data.UpdateUsedTextures();
			General.Map.IsChanged = true;
			General.Map.Map.Update();
			General.Map.TextureHistory.Touch(name, kind);
			General.Interface.DisplayStatus(StatusType.Action, "Applied \"" + name + "\" to " + count + " " + target + ". Brush set to \"" + name + "\".");
		}
	}
}
