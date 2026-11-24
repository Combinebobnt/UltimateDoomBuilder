#region ================== Namespaces

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	public enum UIThemeKind
	{
		Light,
		Dark,
	}

	// What UITheme needs from the toolkit, so tests can apply a theme without touching process-global state
	internal interface IThemeBackend
	{
		// Remaps every given system color. All-or-nothing: throws before any write when it can't apply them all.
		// Returns the previous values of the remapped colors.
		IDictionary<KnownColor, Color> ApplyPalette(IReadOnlyDictionary<KnownColor, Color> palette);
		// Built before the palette is written, so nothing that can fail runs after it
		ToolStripRenderer CreateToolStripRenderer();
		void InstallToolStripRenderer(ToolStripRenderer renderer);
	}

	public static class UITheme
	{
		#region ================== Variables

		private static UIThemeKind current = UIThemeKind.Light;
		private static IDictionary<KnownColor, Color> lightcolors;

		private static readonly IReadOnlyDictionary<KnownColor, Color> darkpalette = CreateDarkPalette();

		// Warning and error text (DarkRed, Red in the light theme) on dark controls and windows
		public static readonly Color DarkWarningText = Color.FromArgb(0xF4, 0x87, 0x71);

		// Accent fill under HighlightText (HotTrack is too light for that in the dark palette)
		public static readonly Color DarkAccent = Color.FromArgb(0x00, 0x7A, 0xCC);

		#endregion

		#region ================== Properties

		public static UIThemeKind Current { get { return current; } }
		public static bool IsDark { get { return current == UIThemeKind.Dark; } }

		// The system-color remap only reaches native controls on Mono WinForms
		public static bool IsSupported
		{
			get
			{
#if MONO_WINFORMS
				return true;
#else
				return false;
#endif
			}
		}

		public static IReadOnlyDictionary<KnownColor, Color> DarkPalette { get { return darkpalette; } }

		#endregion

		#region ================== Methods

		private static IReadOnlyDictionary<KnownColor, Color> CreateDarkPalette()
		{
			Dictionary<KnownColor, Color> p = new Dictionary<KnownColor, Color>();
			Color control = Color.FromArgb(0x2D, 0x2D, 0x30);
			Color controltext = Color.FromArgb(0xF1, 0xF1, 0xF1);
			Color border = Color.FromArgb(0x3F, 0x3F, 0x46);

			p[KnownColor.Control] = control;
			p[KnownColor.ButtonFace] = control;
			p[KnownColor.Menu] = control;
			p[KnownColor.MenuBar] = control;
			p[KnownColor.ControlText] = controltext;
			p[KnownColor.MenuText] = controltext;
			p[KnownColor.Window] = Color.FromArgb(0x1E, 0x1E, 0x1E);
			p[KnownColor.WindowText] = Color.FromArgb(0xDC, 0xDC, 0xDC);
			p[KnownColor.AppWorkspace] = Color.FromArgb(0x25, 0x25, 0x26);
			p[KnownColor.GrayText] = Color.FromArgb(0x8C, 0x8C, 0x8C);
			p[KnownColor.ControlLight] = border;
			p[KnownColor.ControlLightLight] = Color.FromArgb(0x50, 0x50, 0x58);
			p[KnownColor.ButtonHighlight] = Color.FromArgb(0x50, 0x50, 0x58);
			// Lighter than Control: Mono draws disabled text in the midpoint of these two
			p[KnownColor.ControlDark] = Color.FromArgb(0x82, 0x82, 0x82);
			p[KnownColor.ButtonShadow] = Color.FromArgb(0x82, 0x82, 0x82);
			p[KnownColor.ControlDarkDark] = Color.FromArgb(0x6E, 0x6E, 0x6E);
			p[KnownColor.Highlight] = Color.FromArgb(0x26, 0x4F, 0x78);
			p[KnownColor.MenuHighlight] = Color.FromArgb(0x26, 0x4F, 0x78);
			p[KnownColor.HighlightText] = Color.FromArgb(0xFF, 0xFF, 0xFF);
			p[KnownColor.HotTrack] = Color.FromArgb(0x4E, 0xA6, 0xEA);
			p[KnownColor.Info] = Color.FromArgb(0x3C, 0x3C, 0x3C);
			p[KnownColor.InfoText] = controltext;
			p[KnownColor.ScrollBar] = Color.FromArgb(0x25, 0x25, 0x26);
			p[KnownColor.ActiveBorder] = border;
			p[KnownColor.InactiveBorder] = border;
			p[KnownColor.WindowFrame] = border;

			// Only Mono's own MDI and tool-window captions use these
			p[KnownColor.ActiveCaption] = control;
			p[KnownColor.InactiveCaption] = control;
			p[KnownColor.ActiveCaptionText] = controltext;
			p[KnownColor.InactiveCaptionText] = controltext;

			return p;
		}

		// Called once at startup, before the main window exists. Never throws.
		public static void Apply(UIThemeKind kind)
		{
			if(kind == UIThemeKind.Light || !IsSupported) return;
			Apply(kind, new MonoThemeBackend());
		}

		internal static void Apply(UIThemeKind kind, IThemeBackend backend)
		{
			if(kind == UIThemeKind.Light) return;

			try
			{
				ToolStripRenderer renderer = backend.CreateToolStripRenderer();
				lightcolors = backend.ApplyPalette(darkpalette);
				current = kind;
				backend.InstallToolStripRenderer(renderer);
			}
			catch(Exception e)
			{
				string message = "Unable to apply the dark interface theme: " + e.GetType().Name + ": " + e.Message;
				if(General.ErrorLogger != null) General.ErrorLogger.Add(ErrorType.Warning, message);
				else General.WriteLogLine(message);
			}
		}

		// Test hook: back to the startup state
		internal static void Reset()
		{
			current = UIThemeKind.Light;
			lightcolors = null;
		}

		// For colors set in code: picks the variant for the current theme
		public static Color Pick(Color light, Color dark)
		{
			return IsDark ? dark : light;
		}

		// A system color's light-theme value, even after the dark palette has replaced it
		public static Color LightSystemColor(KnownColor kc)
		{
			Color c;
			if(lightcolors != null && lightcolors.TryGetValue(kc, out c)) return c;
			return Color.FromKnownColor(kc);
		}

		#endregion
	}

	// Mono WinForms: SystemColors and SystemBrushes resolve through System.Drawing.KnownColorTable.s_colorTable
	internal sealed class MonoThemeBackend : IThemeBackend
	{
		public IDictionary<KnownColor, Color> ApplyPalette(IReadOnlyDictionary<KnownColor, Color> palette)
		{
			// Mono's X11 driver copies the desktop's GTK colors into the table when it starts, so it must start first
			Assembly winforms = typeof(Form).Assembly;
			Type xplatui = Resolve(winforms, "System.Windows.Forms.XplatUI");
			RuntimeHelpers.RunClassConstructor(xplatui.TypeHandle);

			Type themeengine = Resolve(winforms, "System.Windows.Forms.ThemeEngine");
			PropertyInfo currenttheme = themeengine.GetProperty("Current", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
			object theme = currenttheme != null ? currenttheme.GetValue(null, null) : null;

			Type table = Resolve(typeof(Color).Assembly, "System.Drawing.KnownColorTable");
			MethodInfo ensure = table.GetMethod("EnsureColorTable", BindingFlags.NonPublic | BindingFlags.Static);
			FieldInfo field = table.GetField("s_colorTable", BindingFlags.NonPublic | BindingFlags.Static);
			if(ensure == null || field == null || field.FieldType != typeof(int[]))
				throw new MissingMemberException("System.Drawing.KnownColorTable has no usable color table");

			ensure.Invoke(null, null);
			int[] colors = (int[])field.GetValue(null);
			foreach(KnownColor kc in palette.Keys)
			{
				if(colors == null || (int)kc < 0 || (int)kc >= colors.Length)
					throw new IndexOutOfRangeException("System.Drawing.KnownColorTable has no entry for " + kc);
			}

			Dictionary<KnownColor, Color> previous = new Dictionary<KnownColor, Color>();
			foreach(KeyValuePair<KnownColor, Color> entry in palette)
			{
				previous[entry.Key] = Color.FromArgb(colors[(int)entry.Key]);
				colors[(int)entry.Key] = entry.Value.ToArgb();
			}

			General.WriteLogLine("Interface theme: dark (" + (theme != null ? theme.GetType().Name : "no theme engine") + ")");

			try { SolidScrollBarTrack(theme); }
			catch(Exception e) { General.WriteLogLine("Interface theme: scrollbar track left dithered (" + e.GetType().Name + ": " + e.Message + ")"); }

			return previous;
		}

		// Mono's classic scrollbar track is a 50% dither of ScrollBar and hardcoded white, fetched from the
		// theme's brush cache by a key built from its arguments. Seeding that key gives a solid dark track.
		private static void SolidScrollBarTrack(object theme)
		{
			if(theme == null) throw new MissingMemberException("no theme engine");
			FieldInfo respoolfield = theme.GetType().GetField("ResPool", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			object respool = respoolfield != null ? respoolfield.GetValue(theme) : null;
			FieldInfo cachefield = respool != null ? respool.GetType().GetField("hatchbrushes", BindingFlags.NonPublic | BindingFlags.Instance) : null;
			Hashtable cache = cachefield != null ? cachefield.GetValue(respool) as Hashtable : null;
			if(cache == null) throw new MissingMemberException("no hatch brush cache");

			string key = ((int)HatchStyle.Percent50).ToString() + SystemColors.ScrollBar.ToString() + Color.White.ToString();
			lock(cache) cache[key] = new HatchBrush(HatchStyle.Percent50, SystemColors.ScrollBar, SystemColors.ScrollBar);
		}

		public ToolStripRenderer CreateToolStripRenderer()
		{
			return new DarkToolStripRenderer();
		}

		public void InstallToolStripRenderer(ToolStripRenderer renderer)
		{
			ToolStripManager.Renderer = renderer;
		}

		private static Type Resolve(Assembly assembly, string name)
		{
			Type t = assembly.GetType(name, false);
			if(t == null) throw new TypeLoadException(name + " not found in " + assembly.GetName().Name);
			return t;
		}
	}
}
