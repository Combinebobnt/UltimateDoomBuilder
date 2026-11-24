#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Map.Conversion;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	// Picks a target map format and game configuration for the open map and shows the conversion's report before it runs
	internal sealed class ConvertMapFormatForm : DelayedForm
	{
		#region ================== Constants

		private static readonly MapFormat[] FORMATS = { MapFormat.Doom, MapFormat.Hexen, MapFormat.Udmf };
		private static readonly string[] FORMATNAMES = { "Doom", "Hexen", "UDMF" };

		#endregion

		#region ================== Variables

		private readonly RadioButton[] formats = new RadioButton[FORMATS.Length];
		private readonly ComboBox configs = new ComboBox();
		private readonly Label note = new Label();
		private readonly Label summary = new Label();
		private readonly ListBox entries = new ListBox();
		private readonly Button copy = new Button();
		private readonly Button convert = new Button();
		private readonly Button cancel = new Button();

		private readonly MapFormat? current;
		private readonly string basegame;
		private readonly string engine;
		private readonly string sourcefile;
		private MapSetConversion conversion; // null while Convert is disabled
		private MapSetConversion shown; // the plan the report shows, blocked or not

		#endregion

		#region ================== Properties

		public ConfigurationInfo Target { get { return configs.SelectedItem as ConfigurationInfo; } }
		public MapSetConversion Conversion { get { return conversion; } }

		#endregion

		#region ================== Constructor

		public ConvertMapFormatForm()
		{
			Text = "Convert Map Format";
			FormBorderStyle = FormBorderStyle.Sizable;
			MaximizeBox = false;
			MinimizeBox = false;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.CenterParent;
			AutoScaleMode = AutoScaleMode.Font;
			ClientSize = new Size(620, 480);
			MinimumSize = new Size(480, 400);

			ConfigurationInfo source = General.Map.ConfigSettings;
			current = FormatProfile.FormatOf(source.Configuration);
			basegame = source.Configuration.ReadSetting("basegame", string.Empty).ToLowerInvariant(); // read as EligibleConfigs reads the others
			engine = source.Configuration.ReadSetting("engine", "");
			sourcefile = source.Filename;
			bool covered = FormatProfile.Covers(source.Configuration);

			TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), ColumnCount = 2, RowCount = 7 };
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			layout.Controls.Add(new Label { Text = "Current format:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 6) }, 0, 0);
			layout.Controls.Add(new Label { Text = (current != null ? FORMATNAMES[Array.IndexOf(FORMATS, current.Value)] : source.FormatInterface) + " (" + source.Name + ")", AutoSize = true, Anchor = AnchorStyles.Left }, 1, 0);

			layout.Controls.Add(new Label { Text = "Convert to:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
			FlowLayoutPanel formatrow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
			for(int i = 0; i < FORMATS.Length; i++)
			{
				MapFormat f = FORMATS[i];
				formats[i] = new RadioButton { Text = FORMATNAMES[i], AutoSize = true, Enabled = covered && f != current && EligibleConfigs(f).Count > 0 };
				formats[i].CheckedChanged += delegate(object sender, EventArgs e) { if(((RadioButton)sender).Checked) FillConfigs(f); };
				formatrow.Controls.Add(formats[i]);
			}
			layout.Controls.Add(formatrow, 1, 1);

			layout.Controls.Add(new Label { Text = "Game configuration:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
			configs.DropDownStyle = ComboBoxStyle.DropDownList;
			configs.Dock = DockStyle.Fill;
			configs.SelectedIndexChanged += delegate { Replan(); };
			layout.Controls.Add(configs, 1, 2);

			note.AutoSize = false;
			note.Dock = DockStyle.Fill;
			layout.Controls.Add(note, 0, 3);
			layout.SetColumnSpan(note, 2);

			TableLayoutPanel reportpanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
			reportpanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			reportpanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			summary.AutoSize = true;
			summary.Font = new Font(Font, FontStyle.Bold);
			summary.Margin = new Padding(3, 3, 3, 6);
			reportpanel.Controls.Add(summary, 0, 0);
			entries.Dock = DockStyle.Fill;
			entries.HorizontalScrollbar = true;
			entries.IntegralHeight = false;
			reportpanel.Controls.Add(entries, 0, 1);
			layout.Controls.Add(reportpanel, 0, 4);
			layout.SetColumnSpan(reportpanel, 2);

			Label footer = new Label
			{
				Text = "Converting clears the undo history. The entries also go to the Errors and Warnings panel (the first 50 of each kind), and the full report to the log file.",
				AutoSize = false,
				Dock = DockStyle.Fill
			};
			layout.Controls.Add(footer, 0, 5);
			layout.SetColumnSpan(footer, 2);

			FlowLayoutPanel buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
			cancel.Text = "Cancel";
			cancel.Width = 90;
			cancel.DialogResult = DialogResult.Cancel;
			convert.Text = "Convert";
			convert.Width = 90;
			convert.Click += delegate { DialogResult = DialogResult.OK; Close(); };
			copy.Text = "Copy report";
			copy.Width = 110;
			copy.Click += delegate { if(shown != null) Clipboard.SetText(shown.FullReport()); };
			buttons.Controls.Add(cancel);
			buttons.Controls.Add(convert);
			buttons.Controls.Add(copy);
			layout.Controls.Add(buttons, 0, 6);
			layout.SetColumnSpan(buttons, 2);

			Controls.Add(layout);
			AcceptButton = convert;
			CancelButton = cancel;

			// UDMF holds everything the others can, so it's the default target
			RadioButton initial = null;
			for(int i = FORMATS.Length - 1; i >= 0 && initial == null; i--)
				if(formats[i].Enabled) initial = formats[i];
			if(initial != null)
			{
				initial.Checked = true;
			}
			else
			{
				configs.Enabled = false;
				SetResult(null, covered
					? "No enabled game configuration for this game uses another map format."
					: "The converter doesn't cover \"" + source.Name + "\": Doom-format configurations need an engine translation table, and UDMF only the ZDoom, GZDoom and Zandronum namespaces. Map Options can still change its configuration with the old partial translation.");
			}
		}

		#endregion

		#region ================== Methods

		private List<ConfigurationInfo> EligibleConfigs(MapFormat format)
		{
			// Enabled configs of the same game in that format that the converter covers
			List<ConfigurationInfo> list = new List<ConfigurationInfo>();
			foreach(ConfigurationInfo info in General.Configs)
			{
				if(!info.Enabled || FormatProfile.FormatOf(info.Configuration) != format || !FormatProfile.Covers(info.Configuration)) continue;
				if(info.Configuration.ReadSetting("basegame", string.Empty).ToLowerInvariant() != basegame) continue;
				list.Add(info);
			}
			return list;
		}

		private static string PortPrefix(string filename)
		{
			string name = System.IO.Path.GetFileName(filename ?? "");
			int cut = name.IndexOf('_');
			string prefix = (cut > 0 ? name.Substring(0, cut) : name).ToLowerInvariant();
			return prefix == "uzdoom" ? "gzdoom" : prefix; // UZDoom ships only UDMF configs; GZDoom's are its other formats
		}

		internal static int PickConfig(IList<string> filenames, IList<string> engines, string sourcefile, string sourceengine)
		{
			// The first candidate with the best score, or -1 for none
			int select = -1, best = -1;
			for(int i = 0; i < filenames.Count; i++)
			{
				int score = (PortPrefix(filenames[i]) == PortPrefix(sourcefile) ? 2 : 0)
				            + (string.Equals(engines[i], sourceengine, StringComparison.OrdinalIgnoreCase) ? 1 : 0);
				if(score > best)
				{
					best = score;
					select = i;
				}
			}
			return select;
		}

		private void FillConfigs(MapFormat format)
		{
			// The same port comes preselected: by file name prefix (GZDoom_, Zandronum_), since every ZDoom-family UDMF
			// config's engine key is its namespace "zdoom"; the engine key only breaks ties between other configs
			configs.BeginUpdate();
			configs.Items.Clear();
			List<string> files = new List<string>(), engines = new List<string>();
			foreach(ConfigurationInfo info in EligibleConfigs(format))
			{
				configs.Items.Add(info);
				files.Add(info.Filename);
				engines.Add(info.Configuration.ReadSetting("engine", ""));
			}
			int select = Math.Max(0, PickConfig(files, engines, sourcefile, engine));
			configs.EndUpdate();
			note.Text = format == MapFormat.Hexen
				? "Hexen has one line ID per line and one activation, so line IDs on lines with another special and combined activations may be lost. UDMF keeps both."
				: format == MapFormat.Doom ? "The Doom format holds only the line and sector types the configuration offers; thing heights, specials and IDs, and scripts are dropped." : "";
			if(configs.Items.Count > 0)
			{
				if(configs.SelectedIndex == select) Replan();
				else configs.SelectedIndex = select;
			}
		}

		private void Replan()
		{
			conversion = null;
			ConfigurationInfo target = Target;
			if(target == null)
			{
				SetResult(null, "Select a game configuration.");
				return;
			}
			if(!target.ValidateMapName(General.Map.Options.CurrentName))
			{
				SetResult(null, "The map name " + General.Map.Options.CurrentName + " isn't valid in \"" + target.Name + "\". Rename it in Map Options first.");
				return;
			}

			MapSetConversion planned;
			string unavailable;
			Cursor.Current = Cursors.WaitCursor;
			try
			{
				planned = General.Map.PlanFormatConversion(target, out unavailable);
			}
			catch(Exception ex)
			{
				General.WriteLogLine("Map conversion failed: " + ex);
				SetResult(null, "The conversion failed: " + ex.Message);
				return;
			}
			finally
			{
				Cursor.Current = Cursors.Default;
			}

			if(planned == null) SetResult(null, "Map conversion is unavailable" + (unavailable != null ? " for " + unavailable.TrimEnd('.') : "") + ".");
			else SetResult(planned, planned.Report.Summary() + (planned.Report.Blocked ? "" : "."));
		}

		private void SetResult(MapSetConversion planned, string text)
		{
			// Convert needs a plan the target format can hold
			summary.Text = text;
			entries.BeginUpdate();
			entries.Items.Clear();
			if(planned != null)
			{
				foreach(ConversionIssue i in planned.Report.Issues) entries.Items.Add(i.Severity + ": " + i);
				foreach(DroppedProperty p in planned.Report.Dropped.Values) entries.Items.Add("Dropped: " + p);
			}
			entries.EndUpdate();
			shown = planned;
			conversion = planned != null && !planned.Report.Blocked ? planned : null;
			convert.Enabled = conversion != null;
			copy.Enabled = planned != null;
		}

		#endregion
	}
}
