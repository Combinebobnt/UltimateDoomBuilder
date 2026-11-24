#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Editing;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	// Compilers tab, Advanced tab and the clipboard history row on the Pasting tab
	internal partial class PreferencesForm
	{
		#region ================== Compilers tab

		private sealed class CompilerRow
		{
			public CompilerInfo Info;
			public string Original;
			public string Override;
			public ListViewItem Item;
		}

		private TabPage tabcompilers;
		private ListView compilerlist;
		private TextBox compilerdefault;
		private TextBox compileroverride;
		private Button compilerbrowse;
		private Button compilerreset;
		private Label compilerusedby;
		private readonly List<CompilerRow> compilerrows = new List<CompilerRow>();
		private bool compilerupdating;

		private void BuildCompilersTab()
		{
			tabcompilers = new TabPage("Compilers");
			tabcompilers.UseVisualStyleBackColor = true;
			tabcompilers.Padding = new Padding(3);

			compilerlist = new ListView();
			compilerlist.View = View.Details;
			compilerlist.FullRowSelect = true;
			compilerlist.HideSelection = false;
			compilerlist.MultiSelect = false;
			compilerlist.Location = new Point(8, 8);
			compilerlist.Size = new Size(664, 330);
			compilerlist.Columns.Add("Name", 95);
			compilerlist.Columns.Add("Kind", 80);
			compilerlist.Columns.Add("Program", 280);
			compilerlist.Columns.Add("Status", 55);
			compilerlist.Columns.Add("Defined in", 150);
			compilerlist.SelectedIndexChanged += compilerlist_SelectedIndexChanged;

			Label defaultlabel = new Label { Text = "Default program:", AutoSize = true, Location = new Point(8, 351) };
			compilerdefault = new TextBox { ReadOnly = true, Location = new Point(110, 348), Size = new Size(562, 20) };
			Label overridelabel = new Label { Text = "Override:", AutoSize = true, Location = new Point(8, 379) };
			compileroverride = new TextBox { Location = new Point(110, 376), Size = new Size(400, 20) };
			compileroverride.TextChanged += compileroverride_TextChanged;
			compilerbrowse = new Button { Text = "Browse...", Location = new Point(516, 374), Size = new Size(75, 24), UseVisualStyleBackColor = true };
			compilerbrowse.Click += compilerbrowse_Click;
			compilerreset = new Button { Text = "Reset", Location = new Point(597, 374), Size = new Size(75, 24), UseVisualStyleBackColor = true };
			compilerreset.Click += (s, e) => compileroverride.Text = "";
			Label usedbylabel = new Label { Text = "Used by:", AutoSize = true, Location = new Point(8, 409) };
			compilerusedby = new Label { AutoEllipsis = true, Location = new Point(110, 409), Size = new Size(562, 40) };
			Label note = new Label
			{
				Text = "An override is the absolute path of the program to run instead of the one the compiler configuration names.\n"
					+ "Leave it empty to use the default. Changes apply to the next compile.",
				ForeColor = SystemColors.GrayText,
				Location = new Point(8, 455),
				Size = new Size(664, 40)
			};
			toolTip1.SetToolTip(compileroverride, "Absolute path to an existing program; empty = the configuration's default");

			tabcompilers.Controls.AddRange(new Control[] { compilerlist, defaultlabel, compilerdefault, overridelabel, compileroverride,
				compilerbrowse, compilerreset, usedbylabel, compilerusedby, note });
			tabs.TabPages.Insert(tabs.TabPages.IndexOf(tabscripteditor) + 1, tabcompilers);

			compilerlist.BeginUpdate();
			foreach(CompilerInfo info in General.Compilers)
			{
				string current;
				General.Settings.CompilerPrograms.TryGetValue(info.Name, out current);
				CompilerRow row = new CompilerRow { Info = info, Original = current ?? "", Override = current ?? "" };
				row.Item = new ListViewItem(new[] { info.Name, IsNodebuilder(info) ? "Nodebuilder" : "Script", "", "", DefinedIn(info) });
				row.Item.Tag = row;
				compilerrows.Add(row);
				compilerlist.Items.Add(row.Item);
				UpdateCompilerRow(row);
			}
			compilerlist.EndUpdate();
			UpdateCompilerEditor();
		}

		private static bool IsNodebuilder(CompilerInfo info)
		{
			return string.Equals(info.ProgramInterface, "NodesCompiler", StringComparison.Ordinal);
		}

		private static string DefinedIn(CompilerInfo info)
		{
			string full = Path.Combine(info.Path, info.FileName);
			string root = General.CompilersPath;
			if(!string.IsNullOrEmpty(root) && full.StartsWith(root, StringComparison.Ordinal))
				return full.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return full;
		}

		private static string EffectivePath(CompilerRow row)
		{
			Dictionary<string, string> overrides = new Dictionary<string, string>(StringComparer.Ordinal);
			overrides[row.Info.Name] = row.Override;
			try { return Path.Combine(row.Info.Path, CompilerInfo.ResolveProgram(row.Info.Name, row.Info.DefaultProgramFile, overrides)); }
			catch(ArgumentException) { return row.Info.DefaultProgramFile; }
		}

		private static void UpdateCompilerRow(CompilerRow row)
		{
			string path = EffectivePath(row);
			row.Item.SubItems[2].Text = path;
			row.Item.SubItems[3].Text = File.Exists(path) ? "found" : "missing";
		}

		private CompilerRow SelectedCompilerRow()
		{
			return compilerlist.SelectedItems.Count > 0 ? compilerlist.SelectedItems[0].Tag as CompilerRow : null;
		}

		private void UpdateCompilerEditor()
		{
			CompilerRow row = SelectedCompilerRow();
			compilerupdating = true;
			compilerdefault.Text = row != null ? row.Info.DefaultProgramFile : "";
			compileroverride.Text = row != null ? row.Override : "";
			compilerupdating = false;
			compileroverride.Enabled = compilerbrowse.Enabled = compilerreset.Enabled = (row != null);
			compilerusedby.Text = row != null ? UsedBy(row.Info) : "";
		}

		private static string UsedBy(CompilerInfo info)
		{
			List<string> users = new List<string>();
			HashSet<ScriptConfiguration> seen = new HashSet<ScriptConfiguration>();
			foreach(Dictionary<string, ScriptConfiguration> configs in new[] { General.ScriptConfigs, General.CompiledScriptConfigs })
			{
				foreach(ScriptConfiguration sc in configs.Values)
					if(sc.Compiler == info && seen.Add(sc)) users.Add(sc.Description);
			}
			foreach(NodebuilderInfo nb in General.Nodebuilders)
				if(nb.Compiler == info) users.Add(nb.Title);
			users.Sort(StringComparer.OrdinalIgnoreCase);
			return users.Count > 0 ? string.Join(", ", users.ToArray()) : "(nothing)";
		}

		private void compilerlist_SelectedIndexChanged(object sender, EventArgs e)
		{
			UpdateCompilerEditor();
		}

		private void compileroverride_TextChanged(object sender, EventArgs e)
		{
			if(compilerupdating) return;
			CompilerRow row = SelectedCompilerRow();
			if(row == null) return;
			row.Override = compileroverride.Text.Trim();
			UpdateCompilerRow(row);
		}

		private void compilerbrowse_Click(object sender, EventArgs e)
		{
			CompilerRow row = SelectedCompilerRow();
			if(row == null) return;
			using(OpenFileDialog dialog = new OpenFileDialog())
			{
				dialog.Title = "Program for " + row.Info.Name;
				dialog.Filter = "All files|*.*";
				string start = EffectivePath(row);
				try
				{
					string dir = Path.GetDirectoryName(start);
					if(!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dialog.InitialDirectory = dir;
				}
				catch(Exception) { } // a typed path .NET can't parse just opens the default folder
				if(NativeFileDialogs.Show(dialog, this) == DialogResult.OK) compileroverride.Text = dialog.FileName;
			}
		}

		// Only overrides edited in this session are checked, so a vanished file never blocks OK
		private bool ValidateCompilerOverrides()
		{
			foreach(CompilerRow row in compilerrows)
			{
				if(row.Override == row.Original || row.Override.Length == 0) continue;
				bool valid = row.Override.IndexOfAny(Path.GetInvalidPathChars()) < 0
					&& Path.IsPathRooted(row.Override) && File.Exists(row.Override);
				if(!valid)
				{
					tabs.SelectedTab = tabcompilers;
					row.Item.Selected = true;
					General.ShowErrorMessage("The program override for compiler \"" + row.Info.Name + "\" must be the absolute path of an existing file:\n" + row.Override, MessageBoxButtons.OK);
					return false;
				}
			}
			return true;
		}

		private void ApplyCompilerOverrides()
		{
			foreach(CompilerRow row in compilerrows)
			{
				if(row.Override == row.Original) continue;
				if(row.Override.Length == 0) General.Settings.CompilerPrograms.Remove(row.Info.Name);
				else General.Settings.CompilerPrograms[row.Info.Name] = row.Override;
			}
		}

		#endregion

		#region ================== Advanced tab

		private TabPage tabadvanced;
		private NumericUpDown texturehistorysize;
		private CheckBox hintsmodifieroverlay;
		private CheckBox nativefiledialogs;
		private NumericUpDown visualmousesensx;
		private NumericUpDown visualmousesensy;
		private CheckBox visualframechain;
		private NumericUpDown visualminframetime;
		private NumericUpDown clipboardhistorysize;
		private decimal loadedsensx;
		private decimal loadedsensy;

		private static bool IsUnix { get { return Environment.OSVersion.Platform == PlatformID.Unix; } }

		private static NumericUpDown MakeNumber(int x, int y, int min, int max, int value)
		{
			NumericUpDown n = new NumericUpDown { Location = new Point(x, y), Size = new Size(60, 20), Minimum = min, Maximum = max };
			n.Value = General.Clamp(value, min, max);
			return n;
		}

		private static Label MakeLabel(string text, int x, int y, bool note)
		{
			Label l = new Label { Text = text, AutoSize = true, Location = new Point(x, y) };
			if(note) l.ForeColor = SystemColors.GrayText;
			return l;
		}

		private void BuildAdvancedTab()
		{
			tabadvanced = new TabPage("Advanced");
			tabadvanced.UseVisualStyleBackColor = true;

			GroupBox options = new GroupBox { Text = " Options ", Location = new Point(8, 8), Size = new Size(331, 110) };
			texturehistorysize = MakeNumber(150, 20, 0, TextureHistory.MAX_CAPACITY, General.Settings.TextureHistorySize);
			hintsmodifieroverlay = new CheckBox { Text = "Show modifier key hints while a modifier is held", AutoSize = true, Location = new Point(16, 50), Checked = General.Settings.HintsModifierOverlay };
			nativefiledialogs = new CheckBox { Text = "Use the desktop's file chooser (zenity)", AutoSize = true, Location = new Point(16, 75), Checked = General.Settings.NativeFileDialogs };
			options.Controls.AddRange(new Control[] { MakeLabel("Recent textures kept:", 16, 22, false), texturehistorysize,
				MakeLabel("(0 = off)", 216, 22, true), hintsmodifieroverlay, nativefiledialogs });

			GroupBox visual = new GroupBox { Text = " Visual Modes ", Location = new Point(345, 8), Size = new Size(331, 140) };
			visualmousesensx = MakeNumber(150, 20, 1, 200, (int)Math.Round(General.Settings.VisualMouseSensX));
			visualmousesensy = MakeNumber(150, 46, 1, 200, (int)Math.Round(General.Settings.VisualMouseSensY));
			loadedsensx = visualmousesensx.Value;
			loadedsensy = visualmousesensy.Value;
			visualframechain = new CheckBox { Text = "Frame pacing", AutoSize = true, Location = new Point(16, 78), Checked = General.Settings.VisualFrameChain };
			visualminframetime = MakeNumber(150, 104, 1, FrameChain.MAX_MIN_FRAME_MS, General.Settings.VisualMinFrameTime);
			visual.Controls.AddRange(new Control[] {
				MakeLabel("Horizontal look speed:", 16, 22, false), visualmousesensx, MakeLabel("x Mouse speed", 216, 22, true),
				MakeLabel("Vertical look speed:", 16, 48, false), visualmousesensy, MakeLabel("x Mouse speed", 216, 48, true),
				visualframechain, MakeLabel("(after restart)", 216, 80, true),
				MakeLabel("Minimum frame time:", 16, 106, false), visualminframetime, MakeLabel("ms", 216, 106, true) });
			toolTip1.SetToolTip(visualmousesensx, "Multiplies the Mouse speed setting for horizontal mouse look (default 40)");
			toolTip1.SetToolTip(visualmousesensy, "Multiplies the Mouse speed setting for vertical mouse look (default 40)");
			toolTip1.SetToolTip(visualminframetime, "Shortest time between Visual Mode frames when frame pacing is on");

			if(!IsUnix)
			{
				nativefiledialogs.Enabled = visualframechain.Enabled = visualminframetime.Enabled = false;
				const string tip = "Only used by the Linux build.";
				toolTip1.SetToolTip(nativefiledialogs, tip);
				toolTip1.SetToolTip(visualframechain, tip);
			}

			tabadvanced.Controls.AddRange(new Control[] { options, visual });
			tabs.TabPages.Insert(tabs.TabPages.IndexOf(tabinterface) + 1, tabadvanced);

			// Pasting tab: below the paste options
			clipboardhistorysize = MakeNumber(160, 494, 0, 100, General.Settings.ClipboardHistorySize);
			tabpasting.Controls.AddRange(new Control[] { MakeLabel("Clipboard history entries:", 11, 496, false), clipboardhistorysize,
				MakeLabel("(0 = off)", 226, 496, true) });
		}

		private void ApplyAdvancedSettings()
		{
			General.Settings.TextureHistorySize = (int)texturehistorysize.Value;
			General.Settings.ClipboardHistorySize = (int)clipboardhistorysize.Value;
			General.Settings.HintsModifierOverlay = hintsmodifieroverlay.Checked;
			// Untouched controls keep a hand-edited value the control had to round or clamp
			if(visualmousesensx.Value != loadedsensx) General.Settings.VisualMouseSensX = (float)visualmousesensx.Value;
			if(visualmousesensy.Value != loadedsensy) General.Settings.VisualMouseSensY = (float)visualmousesensy.Value;
			if(IsUnix)
			{
				General.Settings.NativeFileDialogs = nativefiledialogs.Checked;
				General.Settings.VisualFrameChain = visualframechain.Checked;
				General.Settings.VisualMinFrameTime = (int)visualminframetime.Value;
				if(General.MainWindow != null) General.MainWindow.SetMinFrameTime(General.Settings.VisualMinFrameTime);
			}
		}

		#endregion
	}
}
