#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Compilers;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.ZDoom;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	// Runs a ZDRay bake of the open map with progress and cancel, and edits the map's bake lighting,
	// its bake quality and the baker settings. The rows come from LightmapBakeOptions.
	internal sealed class LightmapBakeForm : DelayedForm
	{
		#region ================== Row

		private sealed class Row
		{
			public LightmapBakeOption Option;
			public Control Control;
			public Label Label;
			public Label Note;
			public Button Extra;
		}

		#endregion

		#region ================== Variables

		private readonly TabControl tabs = new TabControl();
		private readonly Dictionary<LightmapOptionTab, TabPage> pages = new Dictionary<LightmapOptionTab, TabPage>();
		private readonly List<Row> rows = new List<Row>();
		private readonly Label lightingnotice = new Label();
		private readonly Button apply = new Button();
		private readonly TextBox output = new TextBox();
		private readonly Label stats = new Label();
		private readonly Button copylog = new Button();
		private readonly Button savelog = new Button();
		private readonly Button exportatlas = new Button();
		private int shownlines; // the running bake's lines the log shows
		private readonly Label status = new Label();
		private readonly ProgressBar progress = new ProgressBar();
		private readonly Button quickbake = new Button();
		private readonly Button bake = new Button();
		private readonly Button close = new Button();
		private readonly ToolTip tips = new ToolTip();
		private readonly Timer timer = new Timer();

		private LightmapLighting original; // the map's lighting when last read
		private LightmapLighting edited;
		private int infocount;
		private readonly LightmapBakeQuality quality;
		private bool loading;

		private LightmapBakeJob job;
		private bool showingresult;
		private readonly bool autostart;
		private bool bakesucceeded;
		private Task<LightmapBakerFeatures> probe; // --features of the baker at probepath
		private string probepath;

		#endregion

		#region ================== Properties

		// The last bake run in this form succeeded
		public bool BakeSucceeded { get { return bakesucceeded; } }

		private LightmapBakeSettings Settings { get { return General.Settings.LightmapBake; } }

		#endregion

		#region ================== Constructor

		public LightmapBakeForm() : this(false) { }

		// With autostart, Bake is pressed as the form shows, when nothing stops a bake
		public LightmapBakeForm(bool autostart)
		{
			this.autostart = autostart;
			Text = "Bake Lightmaps";
			FormBorderStyle = FormBorderStyle.Sizable;
			MaximizeBox = false;
			MinimizeBox = false;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.CenterParent;
			AutoScaleMode = AutoScaleMode.Font;
			ClientSize = new Size(640, 560);
			MinimumSize = new Size(600, 520);

			LightmapBakeQuality mapquality = General.Map.Options.LightmapBake.Quality;
			quality = (mapquality ?? Settings.Quality).Clone();
			original = General.Map.ReadLightmapLighting(out infocount);
			edited = original.Clone();

			TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), ColumnCount = 1, RowCount = 4 };
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			tabs.Dock = DockStyle.Fill;
			foreach(LightmapOptionTab tab in Enum.GetValues(typeof(LightmapOptionTab)))
			{
				TabPage page = new TabPage(tab.ToString()) { Padding = new Padding(6) };
				pages[tab] = page;
				tabs.TabPages.Add(page);
			}
			BuildLightingTab();
			BuildOptionsTab(LightmapOptionTab.Quality, null);
			BuildOptionsTab(LightmapOptionTab.Run, null);
			BuildOutputTab();
			layout.Controls.Add(tabs, 0, 0);

			status.AutoSize = false;
			status.Dock = DockStyle.Fill;
			layout.Controls.Add(status, 0, 1);

			progress.Dock = DockStyle.Fill;
			progress.Maximum = 1000;
			layout.Controls.Add(progress, 0, 2);

			FlowLayoutPanel buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
			close.Text = "Close";
			close.Width = 90;
			close.Click += CloseClick;
			bake.Text = "Bake";
			bake.Width = 90;
			bake.Click += delegate { StartBake(false); };
			quickbake.Text = "Quick bake";
			quickbake.Width = 100;
			quickbake.Click += delegate { StartBake(true); };
			tips.SetToolTip(quickbake, "Bakes at the Draft quality and at most " + LightmapBakeQuality.QUICK_BAKE_MAX_SIZE +
				" texels per page, without changing the map's quality setting");
			buttons.Controls.Add(close);
			buttons.Controls.Add(bake);
			buttons.Controls.Add(quickbake);
			layout.Controls.Add(buttons, 0, 3);

			Controls.Add(layout);
			CancelButton = close; // Escape cancels a running bake, else closes
			close.DialogResult = DialogResult.None; // The CancelButton setter makes it close the form, even mid-bake

			timer.Interval = 200;
			timer.Tick += TimerTick;

			LoadRows();
			UpdateAvailability();
			UpdateState();
			UpdateOutput(true, false);
		}

		private void BuildLightingTab()
		{
			TableLayoutPanel table = BuildOptionsTab(LightmapOptionTab.Lighting, lightingnotice);
			lightingnotice.AutoSize = true;
			lightingnotice.MaximumSize = new Size(560, 0);
			lightingnotice.Margin = new Padding(3, 3, 3, 9);

			apply.Text = "Apply";
			apply.Width = 90;
			apply.Click += delegate { ApplyLighting(); };
			tips.SetToolTip(apply, "Writes these values to the map's ZDRayInfo thing as one undo step. Bake and Quick bake apply them too.");
			int row = table.RowCount++;
			table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			table.Controls.Add(apply, 1, row);
		}

		// One row per registry entry on the tab, under an optional heading
		private TableLayoutPanel BuildOptionsTab(LightmapOptionTab tab, Control heading)
		{
			TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 4, RowCount = 0 };
			table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			pages[tab].Controls.Add(table);

			if(heading != null)
			{
				table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				table.Controls.Add(heading, 0, table.RowCount++);
				table.SetColumnSpan(heading, 4);
			}

			foreach(LightmapBakeOption o in LightmapBakeOptions.OnTab(tab))
			{
				if(o.Id == "autobake" || o.Id == "estimate") continue; // rows the form doesn't offer yet
				Row r = new Row { Option = o };
				r.Note = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = SystemColors.GrayText, Visible = false };
				int row = table.RowCount++;
				table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

				if(o.Id == "emissive")
				{
					r.Label = RowLabel(o.Label);
					table.Controls.Add(r.Label, 0, row);
					table.Controls.Add(r.Note, 3, row);
					r.Control = BuildEmissiveList();
					row = table.RowCount++;
					table.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
					table.Controls.Add(r.Control, 0, row);
					table.SetColumnSpan(r.Control, 4);
					rows.Add(r);
					continue;
				}

				r.Control = CreateControl(r);
				if(o.Type != LightmapOptionType.Bool)
				{
					r.Label = RowLabel(o.Label);
					table.Controls.Add(r.Label, 0, row);
				}
				table.Controls.Add(r.Control, 1, row);
				if(r.Extra != null) table.Controls.Add(r.Extra, 2, row);
				table.Controls.Add(r.Note, 3, row);
				rows.Add(r);
			}
			return table;
		}

		private static Label RowLabel(string text)
		{
			return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) };
		}

		private Control CreateControl(Row r)
		{
			LightmapBakeOption o = r.Option;
			switch(o.Type)
			{
				case LightmapOptionType.Bool:
				{
					CheckBox box = new CheckBox { Text = o.Label, AutoSize = true, Anchor = AnchorStyles.Left };
					box.CheckedChanged += delegate { Changed(o, box.Checked); };
					return box;
				}

				case LightmapOptionType.Int:
				case LightmapOptionType.Float:
				{
					NumericUpDown number = new NumericUpDown { Minimum = (decimal)o.Min, Maximum = (decimal)o.Max, Width = 100, Anchor = AnchorStyles.Left };
					if(o.Type == LightmapOptionType.Float)
					{
						number.DecimalPlaces = 2;
						number.Increment = (o.Max - o.Min > 100 ? 1m : 0.25m);
					}
					number.ValueChanged += delegate
					{
						if(o.Type == LightmapOptionType.Int) Changed(o, (int)number.Value);
						else Changed(o, (double)number.Value);
					};
					return number;
				}

				case LightmapOptionType.Colour:
				{
					TextBox text = new TextBox { Width = 100, Anchor = AnchorStyles.Left, MaxLength = 7 };
					text.TextChanged += delegate
					{
						string colour = LightmapLighting.NormalizeColour(text.Text);
						if(colour != null) Changed(o, colour);
					};
					r.Extra = new Button { Text = "...", Width = 32, Anchor = AnchorStyles.Left };
					r.Extra.Click += delegate { PickColour(text); };
					return text;
				}

				case LightmapOptionType.Choice:
				{
					ComboBox combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, Anchor = AnchorStyles.Left };
					if(o.Id == "preset") foreach(LightmapQualityPreset p in Enum.GetValues(typeof(LightmapQualityPreset))) combo.Items.Add(p);
					else if(o.Id == "texturesize") foreach(int size in LightmapBakeSettings.TEXTURE_SIZES) combo.Items.Add(size);
					combo.SelectedIndexChanged += delegate { if(combo.SelectedItem != null) Changed(o, combo.SelectedItem); };
					return combo;
				}

				default:
				{
					// The baker path
					TextBox text = new TextBox { Dock = DockStyle.Fill };
					text.TextChanged += delegate { Changed(o, text.Text.Trim()); };
					r.Extra = new Button { Text = "Browse...", AutoSize = true };
					r.Extra.Click += delegate { BrowseBaker(text); };
					return text;
				}
			}
		}

		private ListView BuildEmissiveList()
		{
			ListView list = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HeaderStyle = ColumnHeaderStyle.Nonclickable };
			foreach(string column in new[] { "Texture", "Surfaces", "Colour", "Intensity", "Distance", "Defined in", "Used by map" }) list.Columns.Add(column);

			EmissiveTable table = General.Map.Data.EmissiveTable;
			HashSet<EmitdefsEntry> used = General.Map.EmissiveEntriesUsed();
			foreach(EmissiveItem item in table.Items)
			{
				if(item.FromAnimation) continue;
				string colour = item.Color.ToString("x6", CultureInfo.InvariantCulture);
				if(item.Entry.Color == EmitdefsEntry.AUTO_COLOR) colour = "auto (" + colour + ")";
				list.Items.Add(new ListViewItem(new[]
				{
					table.ImageName(item),
					(item.Kind == EmissiveSurface.Walls ? "walls" : "flats"),
					colour,
					item.Intensity.ToString("0.##", CultureInfo.InvariantCulture),
					item.Distance.ToString("0.##", CultureInfo.InvariantCulture),
					Path.GetFileName(item.Entry.Source) + ":" + item.Entry.Line,
					(used.Contains(item.Entry) ? "yes" : ""),
				}));
			}
			if(list.Items.Count == 0) list.Items.Add(new ListViewItem(new[] { "(no EMITDEFS entries are loaded)" }));
			list.AutoResizeColumns(ColumnHeaderAutoResizeStyle.HeaderSize);
			return list;
		}

		private void BuildOutputTab()
		{
			TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
			table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			pages[LightmapOptionTab.Output].Controls.Add(table);

			stats.AutoSize = true;
			stats.MaximumSize = new Size(600, 0);
			stats.Margin = new Padding(3, 3, 3, 6);
			table.Controls.Add(stats, 0, 0);

			FlowLayoutPanel buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
			copylog.Text = "Copy log";
			copylog.AutoSize = true;
			copylog.Click += delegate { if(output.TextLength > 0) Clipboard.SetText(output.Text); };
			savelog.Text = "Save log...";
			savelog.AutoSize = true;
			savelog.Click += delegate { SaveLog(); };
			exportatlas.Text = "Export atlas...";
			exportatlas.AutoSize = true;
			exportatlas.Click += delegate { ExportAtlas(); };
			tips.SetToolTip(exportatlas, "Writes each lightmap texture page of the bake as a PNG, light clamped to full brightness");
			buttons.Controls.Add(copylog);
			buttons.Controls.Add(savelog);
			buttons.Controls.Add(exportatlas);
			table.Controls.Add(buttons, 0, 1);

			output.Multiline = true;
			output.ReadOnly = true;
			output.WordWrap = false;
			output.ScrollBars = ScrollBars.Both;
			output.Dock = DockStyle.Fill;
			output.Font = new Font(FontFamily.GenericMonospace, Font.Size);
			table.Controls.Add(output, 0, 2);
		}

		// The bake's stats, and its log unless a bake's log is already showing. After a failed attempt
		// the stats are still the map's earlier bake, and say so.
		private void UpdateOutput(bool showlog, bool failed)
		{
			LightmapBakeCache bake;
			string error;
			byte[] lump = General.Map.ReadLightmapBakeLump(out bake, out error);
			exportatlas.Enabled = (lump != null);
			if(bake == null)
			{
				stats.Text = "No bake yet. Bake or Quick bake fills this tab.";
				return;
			}
			if(showlog) ShowLog(bake.Log ?? new[] { "(The map's lightmap came with the opened file; it has no baker log.)" });

			List<string> lines = new List<string>();
			if(failed) lines.Add("This attempt failed. The map keeps its earlier bake:");
			lines.Add("Bake: " + (bake.Seconds >= 0 ? bake.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s" : "came with the opened map") +
				", quality " + (bake.Quality != null ? bake.Quality.Describe() : "unknown"));
			LightmapStats s = (lump != null ? LightmapStats.Read(lump, out error) : null);
			if(s == null) lines.Add("The LIGHTMAP can't be read: " + error + ".");
			else
			{
				lines.Add("Texture pages: " + s.TextureCount + " of " + s.TextureSize + "x" + s.TextureSize +
					", used texels " + s.UsedTexels.ToString("N0", CultureInfo.InvariantCulture) + " (" + s.Fill.ToString("0.0%", CultureInfo.InvariantCulture) + " of the pages)");
				lines.Add("Surfaces: " + s.SurfaceCount.ToString("N0", CultureInfo.InvariantCulture) + ", light probes: " + s.LightProbeCount.ToString("N0", CultureInfo.InvariantCulture) +
					", LIGHTMAP lump: " + FormatBytes(s.CompressedSize) + " compressed");
				if(s.PreviewRefusal != null) lines.Add("The Visual-mode lightmap preview is off for this bake: " + s.PreviewRefusal + ".");
			}
			stats.Text = string.Join(Environment.NewLine, lines);
		}

		private static string FormatBytes(long bytes)
		{
			if(bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KiB";
			return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MiB";
		}

		private void ShowLog(string[] lines)
		{
			string text = string.Join(Environment.NewLine, lines);
			if(output.Text == text) return;
			output.Text = text;
			output.SelectionStart = output.TextLength;
			output.ScrollToCaret();
		}

		#endregion

		#region ================== Values

		// Puts the current values into the controls without treating them as edits
		private void LoadRows()
		{
			loading = true;
			try
			{
				foreach(Row r in rows) SetControl(r, ValueOf(r.Option));
			}
			finally { loading = false; }

			if(!original.HasThing)
				lightingnotice.Text = "Baker default (no ZDRayInfo thing). Apply, Bake or Quick bake with a changed value adds one at the map's centre. " +
					"Its first bake differs slightly from a bake without it, since the baker normalises the thing's sun direction but not its default.";
			else if(infocount > 1)
				lightingnotice.Text = infocount + " ZDRayInfo things: the last one is used.";
			else
				lightingnotice.Text = "The map's ZDRayInfo thing. Its thing properties edit the same values.";
		}

		private object ValueOf(LightmapBakeOption o)
		{
			switch(o.Kind)
			{
				case LightmapOptionKind.MapKey:
				case LightmapOptionKind.ThingAngle:
				case LightmapOptionKind.ThingPitch:
					return edited[o.Id];
				case LightmapOptionKind.CliFlag:
					return quality.Value(o.Id);
				case LightmapOptionKind.MapSetting:
					if(o.Id == "preset") return quality.Preset;
					return quality.TextureSize;
				case LightmapOptionKind.ProgramSetting:
					if(o.Id == "bakerpath") return Settings.BakerPath;
					if(o.Id == "threads") return Settings.Threads;
					return Settings.CpuOnly;
				default:
					return null;
			}
		}

		private static void SetControl(Row r, object value)
		{
			if(r.Control is CheckBox) ((CheckBox)r.Control).Checked = (bool)value;
			else if(r.Control is NumericUpDown)
			{
				NumericUpDown number = (NumericUpDown)r.Control;
				decimal v = (decimal)Convert.ToDouble(value, CultureInfo.InvariantCulture);
				number.Value = Math.Max(number.Minimum, Math.Min(number.Maximum, v));
			}
			else if(r.Control is ComboBox) ((ComboBox)r.Control).SelectedItem = value;
			else if(r.Control is TextBox) ((TextBox)r.Control).Text = (value as string) ?? string.Empty;
		}

		// A value the user changed
		private void Changed(LightmapBakeOption o, object value)
		{
			if(loading) return;
			switch(o.Kind)
			{
				case LightmapOptionKind.MapKey:
				case LightmapOptionKind.ThingAngle:
				case LightmapOptionKind.ThingPitch:
					edited[o.Id] = value;
					break;

				case LightmapOptionKind.CliFlag:
					quality.SetValue(o.Id, Convert.ToDouble(value, CultureInfo.InvariantCulture));
					QualityChanged();
					break;

				case LightmapOptionKind.MapSetting:
					if(o.Id == "preset")
					{
						LightmapQualityPreset preset = (LightmapQualityPreset)value;
						if(preset == LightmapQualityPreset.Custom)
						{
							LoadRows(); // Custom is only ever the result of an edit
							return;
						}
						LightmapBakeQuality q = LightmapBakeQuality.FromPreset(preset, quality.TextureSize);
						quality.SampleScale = q.SampleScale;
						quality.Samples = q.Samples;
						quality.BounceSamples = q.BounceSamples;
					}
					else quality.TextureSize = (int)value;
					QualityChanged();
					break;

				case LightmapOptionKind.ProgramSetting:
					if(o.Id == "bakerpath") Settings.BakerPath = (string)value;
					else if(o.Id == "threads") Settings.Threads = (int)value;
					else Settings.CpuOnly = (bool)value;
					if(o.Id == "bakerpath") UpdateAvailability();
					break;
			}
			if(o.Id == "sun") UpdateAvailability();
			UpdateState();
		}

		// The map keeps its own quality from its first change or bake on; the program keeps it as the last used
		private void QualityChanged()
		{
			General.Map.Options.LightmapBake.Quality = quality.Clone();
			Settings.Quality = quality.Clone();
			LoadRows();
		}

		// A number typed into a spin box only reaches ValueChanged when it loses focus, which Mono
		// doesn't do before a button's Click
		private void CommitTypedNumbers()
		{
			foreach(Row r in rows)
			{
				NumericUpDown number = r.Control as NumericUpDown;
				decimal typed;
				if(number == null || !number.Enabled || !decimal.TryParse(number.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out typed)) continue;
				typed = Math.Max(number.Minimum, Math.Min(number.Maximum, Math.Round(typed, number.DecimalPlaces)));
				if(typed != number.Value) number.Value = typed;
			}
		}

		private void ApplyLighting()
		{
			CommitTypedNumbers();
			if(!edited.DiffersFrom(original) || General.Map.LightmapBakeUnavailable() != null) return;
			General.Map.ApplyLightmapLighting(original, edited);
			original = General.Map.ReadLightmapLighting(out infocount);
			edited = original.Clone();
			LoadRows();
			UpdateState();
		}

		#endregion

		#region ================== Availability and state

		// Greys out what the configured baker can't do; the map's own values stay visible
		private void UpdateAvailability()
		{
			LightmapBakerFeatures features = Features(Settings.BakerPath, false);
			bool probing = !probe.IsCompleted;
			foreach(Row r in rows)
			{
				LightmapBakeOption o = r.Option;
				string feature = o.Feature;
				string tip = null, note = null;
				bool available = o.Available(features);

				if(o.Id == "sun")
				{
					feature = LightmapBakeOptions.FEATURE_SUNINTENSITY;
					available = features.Has(feature);
					if(!available && (bool)edited[o.Id]) tip = "Stock ZDRay always bakes a sun. Turning it off needs a ZDRay build with " + feature + " (the fork's README).";
				}
				else if(o.Id == "sunintensity" && available && !(bool)edited["sun"])
				{
					r.Control.Enabled = false;
					r.Note.Visible = false;
					tips.SetToolTip(r.Control, null);
					if(r.Label != null) tips.SetToolTip(r.Label, null);
					continue;
				}
				else if(o.Id == "bouncesamples" && available && !features.Has("bounce") && !features.Has("surfacelights"))
				{
					note = "no effect yet";
					tip = "Bounce samples only matter once the baker bounces light (a ZDRay build with bounce or surfacelights).";
				}

				if(!available)
				{
					note = (probing ? "checking baker..." : "needs ZDRay fork");
					if(tip == null) tip = "Needs a ZDRay build with " + feature + " (the fork's README).";
				}

				r.Control.Enabled = available;
				if(r.Extra != null) r.Extra.Enabled = available;
				r.Note.Text = note ?? string.Empty;
				r.Note.Visible = (note != null);
				tips.SetToolTip(r.Note, tip);
				tips.SetToolTip(r.Control, tip);
				if(r.Label != null) tips.SetToolTip(r.Label, tip);
			}
		}

		private void UpdateState()
		{
			bool running = (job != null);
			foreach(KeyValuePair<LightmapOptionTab, TabPage> p in pages)
				if(p.Key != LightmapOptionTab.Output) p.Value.Enabled = !running;
			close.Text = (running ? "Cancel" : "Close");
			// Not tied to a pending edit: a number still being typed isn't one until Apply commits it
			apply.Enabled = !running && General.Map.LightmapBakeUnavailable() == null;
			if(running) return;

			string problem = General.Map.LightmapBakeUnavailable() ?? Settings.BakerProblem();
			bake.Enabled = quickbake.Enabled = (problem == null);
			if(problem != null) status.Text = problem;
			else if(!showingresult)
			{
				string text = "Bakes the open map with ZDRay. Save and Test then write the bake for as long as the map's geometry and lights stay as baked.";
				LightmapBakerFeatures features = Features(Settings.BakerPath, false);
				if(probe.IsCompleted && quality.TextureSize > 1024 && !features.Has(LightmapBakeOptions.FEATURE_SIZE4096))
					text = "Baking at 1024: this ZDRay has no " + LightmapBakeOptions.FEATURE_SIZE4096 + ". " + text;
				status.Text = text;
			}
		}

		// The baker's features, probed off the UI thread once per path. A bake waits for the probe, and
		// probes again when the last answer was none, since a probe that timed out isn't cached.
		private LightmapBakerFeatures Features(string path, bool wait)
		{
			if(probe == null || probepath != path)
			{
				probepath = path;
				probe = Task.Run(() => LightmapBakerFeatures.Probe(path));
				Task<LightmapBakerFeatures> started = probe;
				started.ContinueWith(t => ProbeDone(started));
			}
			if(!wait) return (probe.IsCompleted ? probe.Result : LightmapBakerFeatures.None);
			if(!probe.Result.IsEmpty) return probe.Result;

			// Stock ZDRay's answer is cached, so this only runs the baker again after a timeout or failure
			probe = Task.FromResult(LightmapBakerFeatures.Probe(path));
			return probe.Result;
		}

		private void ProbeDone(Task<LightmapBakerFeatures> finished)
		{
			try
			{
				if(IsDisposed || !IsHandleCreated) return;
				BeginInvoke((Action)delegate
				{
					if(IsDisposed || finished != probe) return;
					UpdateAvailability();
					UpdateState();
				});
			}
			catch(InvalidOperationException) { } // the form closed meanwhile
		}

		private void SetResult(string text)
		{
			status.Text = text;
			showingresult = true;
		}

		#endregion

		#region ================== Events

		private void BrowseBaker(TextBox path)
		{
			using(OpenFileDialog dialog = new OpenFileDialog())
			{
				dialog.Title = "Select the ZDRay executable";
				dialog.Filter = (Environment.OSVersion.Platform == PlatformID.Win32NT ? "Programs (*.exe)|*.exe|All files|*.*" : "All files|*");
				if(File.Exists(path.Text)) dialog.InitialDirectory = Path.GetDirectoryName(path.Text);
				if(NativeFileDialogs.Show(dialog) == DialogResult.OK) path.Text = dialog.FileName;
			}
		}

		private void PickColour(TextBox text)
		{
			using(ColorDialog dialog = new ColorDialog { FullOpen = true })
			{
				string colour = LightmapLighting.NormalizeColour(text.Text);
				if(colour != null) dialog.Color = Color.FromArgb(int.Parse(colour, NumberStyles.HexNumber, CultureInfo.InvariantCulture) | unchecked((int)0xFF000000));
				if(dialog.ShowDialog(this) == DialogResult.OK)
					text.Text = (dialog.Color.ToArgb() & 0xFFFFFF).ToString("x6", CultureInfo.InvariantCulture);
			}
		}

		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);
			if(probe != null && probe.IsCompleted) ProbeDone(probe);
			if(autostart && bake.Enabled) StartBake(false);
		}

		private void StartBake(bool quick)
		{
			if(job != null) return;
			ApplyLighting();
			showingresult = false;
			bakesucceeded = false;

			LightmapBakeSettings settings = Settings;
			settings.Features = Features(settings.BakerPath, true);
			UpdateAvailability();
			if(!quick)
			{
				General.Map.Options.LightmapBake.Quality = quality.Clone();
				settings.Quality = quality.Clone();
			}
			LightmapBakeSettings run = settings.With(quick ? LightmapBakeQuality.Quick(quality.TextureSize) : quality);

			string error;
			job = General.Map.StartLightmapBake(run, out error);
			if(job == null)
			{
				SetResult("Bake failed: " + error);
				UpdateState();
				return;
			}

			output.Text = string.Empty;
			shownlines = 0;
			tabs.SelectedTab = pages[LightmapOptionTab.Output];
			status.Text = "Starting the baker...";
			progress.Style = ProgressBarStyle.Marquee;
			bake.Enabled = quickbake.Enabled = false;
			UpdateState();
			timer.Start();
		}

		private void TimerTick(object sender, EventArgs e)
		{
			if(job == null) return;

			string stage;
			double fraction;
			int linecount;
			job.Baker.Snapshot(out stage, out fraction, out linecount);
			status.Text = stage + "... (" + job.Baker.Elapsed.TotalSeconds.ToString("0") + " s)";
			if(fraction < 0) progress.Style = ProgressBarStyle.Marquee;
			else
			{
				progress.Style = ProgressBarStyle.Continuous;
				progress.Value = (int)Math.Round(fraction * progress.Maximum);
			}
			if(linecount != shownlines)
			{
				// Only new lines are appended, so a long log isn't rewritten every tick
				string[] added = job.Baker.LinesFrom(shownlines, out linecount);
				if(added == null) ShowLog(job.Baker.Lines);
				else foreach(string line in added) output.AppendText((output.TextLength > 0 ? Environment.NewLine : "") + line);
				shownlines = linecount;
			}

			int exitcode;
			if(job.Baker.Poll(out exitcode)) Finish(exitcode);
		}

		private void Finish(int exitcode)
		{
			timer.Stop();
			string message;
			string[] log = job.Baker.Lines;
			bool ok = General.Map.FinishLightmapBake(job, exitcode, out message);
			bakesucceeded = ok;
			job = null;
			progress.Style = ProgressBarStyle.Continuous;
			progress.Value = (ok ? progress.Maximum : 0);
			SetResult(message);
			UpdateState();
			ShowLog(log);
			UpdateOutput(false, !ok);
		}

		private void SaveLog()
		{
			using(SaveFileDialog dialog = new SaveFileDialog())
			{
				dialog.Title = "Save the baker log";
				dialog.Filter = "Text files (*.txt)|*.txt|All files|*.*";
				dialog.FileName = General.Map.Options.LevelName + "_lightmap_log.txt";
				if(NativeFileDialogs.Show(dialog) != DialogResult.OK) return;
				try { File.WriteAllText(dialog.FileName, output.Text); }
				catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException)
				{
					General.ShowErrorMessage("The log could not be saved: " + ex.Message, MessageBoxButtons.OK);
				}
			}
		}

		private void ExportAtlas()
		{
			LightmapBakeCache bake;
			string error;
			byte[] lump = General.Map.ReadLightmapBakeLump(out bake, out error);
			if(lump == null)
			{
				General.ShowErrorMessage("The atlas can't be exported: " + error + ".", MessageBoxButtons.OK);
				return;
			}
			using(SaveFileDialog dialog = new SaveFileDialog())
			{
				dialog.Title = "Export the lightmap atlas";
				dialog.Filter = "PNG images (*.png)|*.png";
				dialog.FileName = General.Map.Options.LevelName + "_lightmap.png";
				if(NativeFileDialogs.Show(dialog) != DialogResult.OK) return;
				Cursor = Cursors.WaitCursor;
				List<string> files;
				try { files = LightmapAtlasExport.Export(lump, dialog.FileName, out error); }
				finally { Cursor = Cursors.Default; }
				string written = (files.Count == 1 ? Path.GetFileName(files[0]) : files.Count + " pages, " + string.Join(", ", files.ConvertAll(Path.GetFileName)));
				General.WriteLogLine("Lightmap atlas export: " + (files.Count > 0 ? written : "nothing") + (error != null ? ", failed: " + error : ""));
				if(error != null) General.ShowErrorMessage("The atlas export failed: " + error + "." + (files.Count > 0 ? " Written before it: " + written + "." : ""), MessageBoxButtons.OK);
				else SetResult("Exported the lightmap atlas: " + written + ".");
			}
		}

		private void CloseClick(object sender, EventArgs e)
		{
			if(job != null) job.Baker.Cancel();
			else Close();
		}

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			if(job != null)
			{
				job.Baker.Cancel();
				job.Baker.WaitForExit();
				int exitcode;
				job.Baker.Poll(out exitcode);
				Finish(exitcode);
			}
			else
			{
				CommitTypedNumbers();
			}
			if(job == null && edited.DiffersFrom(original) && General.Map.LightmapBakeUnavailable() == null)
			{
				DialogResult answer = MessageBox.Show(this, "Some ZDRayInfo values (the Lighting tab and Sky samples) aren't on the map yet. Apply them?",
					"Bake Lightmaps", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
				if(answer == DialogResult.Cancel)
				{
					e.Cancel = true;
					return;
				}
				if(answer == DialogResult.Yes) ApplyLighting();
			}
			base.OnFormClosing(e);
		}

		protected override void Dispose(bool disposing)
		{
			if(disposing)
			{
				timer.Dispose();
				tips.Dispose();
			}
			base.Dispose(disposing);
		}

		#endregion
	}
}
