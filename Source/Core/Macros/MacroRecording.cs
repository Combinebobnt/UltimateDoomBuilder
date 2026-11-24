
#region ================== Namespaces

using System.Collections.Generic;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Actions;

#endregion

namespace CodeImp.DoomBuilder.Macros
{
	internal struct MacroClassification
	{
		public readonly bool Record;

		// Why a step was skipped, for the status line; null when it records or the skip is not worth telling
		public readonly string Reason;

		private MacroClassification(bool record, string reason) { Record = record; Reason = reason; }

		public static readonly MacroClassification Recorded = new MacroClassification(true, null);
		public static readonly MacroClassification Silent = new MacroClassification(false, null);
		public static MacroClassification Skip(string reason) { return new MacroClassification(false, reason); }
	}

	// What a macro records. Pure: the recorder passes in everything it decides on.
	internal static class MacroRecording
	{
		// Actions a macro never records, by rule: held input or view navigation; map and session
		// lifecycle; undo history; app configuration and UI shells; the macro actions themselves
		internal static readonly HashSet<string> NotRecordable = new HashSet<string>
		{
			// Held input, mouse position or view navigation
			"builder_classicselect", "builder_classicedit", "builder_visualselect", "builder_visualedit",
			"builder_scrollwest", "builder_scrolleast", "builder_scrollnorth", "builder_scrollsouth",
			"builder_pan_view", "builder_zoomin", "builder_zoomout", "builder_centerinscreen",
			"builder_centeroncoordinates", "builder_moveforward", "builder_movebackward", "builder_moveleft",
			"builder_moveright", "builder_moveup", "builder_movedown", "builder_orbit",
			"builder_movecameratocursor", "buildermodes_classicpaintselect", "buildermodes_visualpaintselect",
			"buildermodes_drawpoint", "buildermodes_removepoint", "buildermodes_removefirstpoint",
			"buildermodes_finishdraw", "buildermodes_lookthroughthing",

			// Map and session lifecycle
			"builder_newmap", "builder_openmap", "builder_openmapincurrentwad", "builder_closemap",
			"builder_savemap", "builder_savemapas", "builder_savemapinto", "builder_mapoptions",
			"builder_convertmapformat", "builder_testmap", "builder_testmapfromview", "builder_reloadresources",
			"builder_gzreloadmodeldef", "builder_gzreloadgldefs",

			// Undo history
			"builder_undo", "builder_redo", "builder_undobranchrestore",

			// App configuration and UI shells
			"builder_preferences", "builder_configuration", "builder_exportsettings",
			"builder_importsettings", "builder_opencommandpalette", "builder_openscripteditor",
			"builder_bakelightmaps", "builder_interactionlogmark", "builder_savescreenshot",
			"builder_saveeditareascreenshot", "builder_viewusedtags", "builder_viewthingtypes",
			"builder_viewmapstatistics", "builder_showerrors", "builder_thingsfilterssetup",
			"builder_linedefcolorssetup", "builder_gridsetup", "builder_toggleinfopanel",
			"buildermodes_exporttoobj", "buildermodes_exporttoimage", "buildermodes_exporttoidstudio",

			// Macros
			"builder_macrorecord", "builder_macrorunlast", "builder_macromanage",
			"builder_macroslot1", "builder_macroslot2", "builder_macroslot3", "builder_macroslot4",
			"builder_macroslot5", "builder_macroslot6", "builder_macroslot7", "builder_macroslot8",
			"builder_macroslot9", "builder_macroslot10",
		};

		internal static bool IsMouseButton(int key)
		{
			Keys code = (Keys)key & Keys.KeyCode;
			return (code == Keys.LButton) || (code == Keys.RButton) || (code == Keys.MButton) ||
				   (code == Keys.XButton1) || (code == Keys.XButton2);
		}

		internal static MacroClassification Classify(string name, int key, int depth, bool volatilebefore, bool volatileafter, bool bound)
		{
			// Nested and macro invocations are not steps the user took
			if(depth > 0) return MacroClassification.Silent;
			if(name.StartsWith("builder_macro", System.StringComparison.Ordinal)) return MacroClassification.Silent;

			if(NotRecordable.Contains(name)) return MacroClassification.Skip("can't be recorded");
			if(IsMouseButton(key)) return MacroClassification.Skip("mouse buttons aren't recorded");
			if(volatilebefore || volatileafter) return MacroClassification.Skip("drawing, pasting and selection editing aren't recorded");
			if(!bound) return MacroClassification.Skip("does nothing here");
			return MacroClassification.Recorded;
		}
	}
}
