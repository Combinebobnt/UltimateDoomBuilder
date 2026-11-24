#region ================== Namespaces

using CodeImp.DoomBuilder.GZBuilder.Data;

#endregion

namespace CodeImp.DoomBuilder.Rendering
{
	// How Visual mode turns a sector light level into brightness.
	public enum LightModelKind
	{
		// The editor's own curve (doomlightlevels), black distance fog only through the fog programs
		Editor = 0,

		// UZDoom's ZDoomSoftware: per-pixel colormap falloff, no black fog
		ZDoomSoftware = 1,

		// UZDoom's hardware light modes 0-4: CalcLightLevel, then black distance fog
		Hardware = 2,
	}

	// The resolved light model for one map
	public struct LightModelSettings
	{
		public LightModelKind Kind;

		// UZDoom light mode 0-4 for Hardware, -1 otherwise
		public int HardwareMode;

		// R_GetGlobVis(r_visibility 8) / 32 as the shader's uGlobVis expects it
		public float GlobVis;

		// Black fog density for light mode 4 (Legacy)
		public int LegacyDensity;

		// The Editor curve's "Doom light levels" flag (the x1.5 curve)
		public bool DoomCurve;

		// Distance effects (the fog toggle): falloff, black fog and coloured fog
		public bool Distance;
	}

	public static class LightModel
	{
		// R_GetGlobVis gives 1280 at r_visibility 8 for 4:3 and wider views; the shader takes it / 32
		public const float SOFTWARE_GLOBVIS = 1280f / 32f;

		// GetFogDensity's Legacy default when the level sets no fog density
		public const int LEGACY_DEFAULT_DENSITY = 18;

		// UZDoom's ELightMode::ZDoomSoftware; other defaultlightmode values besides 0-4 resolve to Editor
		public const int ZDOOM_SOFTWARE = 8;

		// configdefault: the configuration's defaultlightmode, -1 when absent; levelfogdensity: MAPINFO's 0-512
		public static LightModelSettings Resolve(MapInfo.GZDoomLightMode mapmode, int configdefault, bool doomlightlevels, int levelfogdensity, bool distance)
		{
			LightModelSettings s = new LightModelSettings();
			s.HardwareMode = -1;
			s.Distance = distance;
			s.DoomCurve = doomlightlevels;

			// Today's limited MAPINFO support for the Editor curve
			switch(mapmode)
			{
				case MapInfo.GZDoomLightMode.DOOM: s.DoomCurve = true; break;
				case MapInfo.GZDoomLightMode.STANDARD: s.DoomCurve = false; break;
			}

			// G_InitLevelLocals: MAPINFO fogdensity is stored halved; Legacy falls back to 18
			int leveldensity = General.Clamp(levelfogdensity, 0, 512) >> 1;
			s.LegacyDensity = leveldensity != 0 ? leveldensity : LEGACY_DEFAULT_DENSITY;

			// A configuration without the key keeps the editor's look for every map
			if(configdefault < 0) return s;

			int mode = IsHardwareMode((int)mapmode) ? (int)mapmode : configdefault;
			if(IsHardwareMode(mode))
			{
				s.Kind = LightModelKind.Hardware;
				s.HardwareMode = mode;
			}
			else if(mode == ZDOOM_SOFTWARE)
			{
				s.Kind = LightModelKind.ZDoomSoftware;
				s.GlobVis = SOFTWARE_GLOBVIS;
			}

			return s;
		}

		private static bool IsHardwareMode(int mode)
		{
			return mode >= (int)MapInfo.GZDoomLightMode.STANDARD && mode <= (int)MapInfo.GZDoomLightMode.LEGACY;
		}
	}
}
