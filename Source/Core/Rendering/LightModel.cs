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

		// Packs the vertex light attribute: level, its fake contrast + 128, black-fog level, and 255 for "set"
		public static int PackLight(int level, int shadedlevel, int foglevel)
		{
			int light = General.Clamp(level, 0, 255);
			int rel = General.Clamp(General.Clamp(shadedlevel, 0, 255) - light + 128, 0, 255);
			return light | (rel << 8) | (General.Clamp(foglevel, 0, 255) << 16) | (255 << 24);
		}

		// A packed light with one level and no fake contrast
		public static int PackLight(int level)
		{
			return PackLight(level, level, level);
		}

		// The light level a packed light shows: level plus fake contrast, as the shader reads it
		public static int UnpackLevel(int packed)
		{
			return General.Clamp((packed & 0xFF) + ((packed >> 8) & 0xFF) - 128, 0, 255);
		}

		// The packed light as the shader's lightoverride uniform
		public static Vector4f LightUniform(int packed)
		{
			return new Vector4f((packed & 0xFF) / 255f, ((packed >> 8) & 0xFF) / 255f, ((packed >> 16) & 0xFF) / 255f, ((packed >> 24) & 0xFF) / 255f);
		}

		// The Editor model's brightness for a level (doomlightlevels' x1.5 curve below 192)
		public static int EditorBrightness(int level, bool doomcurve)
		{
			if(level < 192 && doomcurve) level = (int)(192.0f - (192 - level) * 1.5f);
			return General.Clamp(level, 0, 255);
		}

		private static bool IsHardwareMode(int mode)
		{
			return mode >= (int)MapInfo.GZDoomLightMode.STANDARD && mode <= (int)MapInfo.GZDoomLightMode.LEGACY;
		}
	}
}
