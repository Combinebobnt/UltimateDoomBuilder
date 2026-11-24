#region ================== Namespaces

using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Compilers;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	// Runs a ZDRay bake of the open map with progress and cancel, and holds the baker settings
	internal sealed class LightmapBakeForm : DelayedForm
	{
		#region ================== Variables

		private readonly TextBox bakerpath = new TextBox();
		private readonly Button browse = new Button();
		private readonly ComboBox texturesize = new ComboBox();
		private readonly NumericUpDown threads = new NumericUpDown();
		private readonly CheckBox cpuonly = new CheckBox();
		private readonly Label status = new Label();
		private readonly ProgressBar progress = new ProgressBar();
		private readonly TextBox output = new TextBox();
		private readonly Button bake = new Button();
		private readonly Button close = new Button();
		private readonly Timer timer = new Timer();
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

		#endregion

		#region ================== Constructor

		public LightmapBakeForm() : this(false) { }

		// With autostart, Bake is pressed as the form shows, when nothing stops a bake
		public LightmapBakeForm(bool autostart)
		{
			this.autostart = autostart;
			Text = "Bake Lightmaps";
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = false;
			MinimizeBox = false;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.CenterParent;
			AutoScaleMode = AutoScaleMode.Font;
			ClientSize = new Size(560, 400);

			TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), ColumnCount = 3, RowCount = 8 };
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

			bakerpath.Dock = DockStyle.Fill;
			browse.Text = "Browse...";
			browse.AutoSize = true;
			browse.Click += BrowseClick;
			AddRow(layout, 0, "ZDRay executable:", bakerpath, browse);

			texturesize.DropDownStyle = ComboBoxStyle.DropDownList;
			foreach(int size in LightmapBakeSettings.TEXTURE_SIZES) texturesize.Items.Add(size);
			AddRow(layout, 1, "Lightmap texture size:", texturesize, null);

			threads.Minimum = 0;
			threads.Maximum = 256;
			AddRow(layout, 2, "Threads (0 = automatic):", threads, null);

			cpuonly.Text = "CPU ray tracing only (skip the Vulkan GPU tracer)";
			cpuonly.AutoSize = true;
			layout.Controls.Add(cpuonly, 1, 3);
			layout.SetColumnSpan(cpuonly, 2);

			status.AutoSize = false;
			status.Dock = DockStyle.Fill;
			status.Height = 48;
			layout.Controls.Add(status, 0, 4);
			layout.SetColumnSpan(status, 3);

			progress.Dock = DockStyle.Fill;
			progress.Maximum = 1000;
			layout.Controls.Add(progress, 0, 5);
			layout.SetColumnSpan(progress, 3);

			output.Multiline = true;
			output.ReadOnly = true;
			output.ScrollBars = ScrollBars.Vertical;
			output.Dock = DockStyle.Fill;
			output.Font = new Font(FontFamily.GenericMonospace, Font.Size);
			layout.Controls.Add(output, 0, 6);
			layout.SetColumnSpan(output, 3);
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			FlowLayoutPanel buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
			close.Text = "Close";
			close.Width = 90;
			close.Click += CloseClick;
			bake.Text = "Bake";
			bake.Width = 90;
			bake.Click += BakeClick;
			buttons.Controls.Add(close);
			buttons.Controls.Add(bake);
			layout.Controls.Add(buttons, 0, 7);
			layout.SetColumnSpan(buttons, 3);

			Controls.Add(layout);
			AcceptButton = bake;
			CancelButton = close; // Escape cancels a running bake, else closes
			close.DialogResult = DialogResult.None; // The CancelButton setter makes it close the form, even mid-bake

			timer.Interval = 200;
			timer.Tick += TimerTick;

			LightmapBakeSettings settings = General.Settings.LightmapBake;
			bakerpath.Text = settings.BakerPath;
			texturesize.SelectedItem = settings.TextureSize;
			if(texturesize.SelectedIndex < 0) texturesize.SelectedItem = LightmapBakeSettings.DEFAULT_TEXTURE_SIZE;
			threads.Value = Math.Min(threads.Maximum, settings.Threads);
			cpuonly.Checked = settings.CpuOnly;

			// Only now: UpdateState writes the controls back into the settings
			bakerpath.TextChanged += delegate { UpdateState(); };
			UpdateState();
		}

		private static void AddRow(TableLayoutPanel layout, int row, string label, Control control, Control extra)
		{
			layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) }, 0, row);
			layout.Controls.Add(control, 1, row);
			if(extra != null) layout.Controls.Add(extra, 2, row);
			else layout.SetColumnSpan(control, 2);
		}

		#endregion

		#region ================== Methods

		// Waiting for the baker's feature probe is only needed to start a bake
		private LightmapBakeSettings ReadControls(bool waitforfeatures)
		{
			LightmapBakeSettings settings = General.Settings.LightmapBake;
			settings.BakerPath = bakerpath.Text.Trim();
			settings.TextureSize = (texturesize.SelectedItem is int ? (int)texturesize.SelectedItem : LightmapBakeSettings.DEFAULT_TEXTURE_SIZE);
			settings.Threads = (int)threads.Value;
			settings.CpuOnly = cpuonly.Checked;
			settings.Features = Features(settings.BakerPath, waitforfeatures);
			return settings;
		}

		private void UpdateState()
		{
			bool running = (job != null);
			bakerpath.Enabled = browse.Enabled = texturesize.Enabled = threads.Enabled = cpuonly.Enabled = !running;
			close.Text = (running ? "Cancel" : "Close");
			if(running) return;

			string problem = General.Map.LightmapBakeUnavailable() ?? ReadControls(false).BakerProblem();
			bake.Enabled = (problem == null);
			if(problem != null) status.Text = problem;
			else if(!showingresult)
				status.Text = "Bakes the open map with ZDRay. Save and Test then write the bake for as long as the map's geometry and lights stay as baked.";
		}

		// The baker's features, probed off the UI thread once per path; waits for the probe only when asked
		private LightmapBakerFeatures Features(string path, bool wait)
		{
			if(probe == null || probepath != path)
			{
				probepath = path;
				probe = Task.Run(() => LightmapBakerFeatures.Probe(path));
			}
			if(!wait && !probe.IsCompleted) return LightmapBakerFeatures.None;
			return probe.Result;
		}

		private void SetResult(string text)
		{
			status.Text = text;
			showingresult = true;
		}

		#endregion

		#region ================== Events

		private void BrowseClick(object sender, EventArgs e)
		{
			using(OpenFileDialog dialog = new OpenFileDialog())
			{
				dialog.Title = "Select the ZDRay executable";
				dialog.Filter = (Environment.OSVersion.Platform == PlatformID.Win32NT ? "Programs (*.exe)|*.exe|All files|*.*" : "All files|*");
				if(File.Exists(bakerpath.Text)) dialog.InitialDirectory = Path.GetDirectoryName(bakerpath.Text);
				if(NativeFileDialogs.Show(dialog) == DialogResult.OK) bakerpath.Text = dialog.FileName;
			}
		}

		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);
			if(autostart && bake.Enabled) BakeClick(this, EventArgs.Empty);
		}

		private void BakeClick(object sender, EventArgs e)
		{
			if(job != null) return;
			showingresult = false;
			bakesucceeded = false;
			string error;
			job = General.Map.StartLightmapBake(ReadControls(true), out error);
			if(job == null)
			{
				SetResult("Bake failed: " + error);
				UpdateState();
				return;
			}

			output.Text = string.Empty;
			status.Text = "Starting the baker...";
			progress.Style = ProgressBarStyle.Marquee;
			bake.Enabled = false;
			UpdateState();
			timer.Start();
		}

		private void TimerTick(object sender, EventArgs e)
		{
			if(job == null) return;

			string stage;
			double fraction;
			string[] lines;
			job.Baker.Snapshot(out stage, out fraction, out lines);
			status.Text = stage + "... (" + job.Baker.Elapsed.TotalSeconds.ToString("0") + " s)";
			if(fraction < 0) progress.Style = ProgressBarStyle.Marquee;
			else
			{
				progress.Style = ProgressBarStyle.Continuous;
				progress.Value = (int)Math.Round(fraction * progress.Maximum);
			}
			string text = string.Join(Environment.NewLine, lines);
			if(output.Text != text) output.Text = text;

			int exitcode;
			if(job.Baker.Poll(out exitcode)) Finish(exitcode);
		}

		private void Finish(int exitcode)
		{
			timer.Stop();
			string message;
			bool ok = General.Map.FinishLightmapBake(job, exitcode, out message);
			bakesucceeded = ok;
			job = null;
			progress.Style = ProgressBarStyle.Continuous;
			progress.Value = (ok ? progress.Maximum : 0);
			SetResult(message);
			UpdateState();
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
			base.OnFormClosing(e);
		}

		protected override void Dispose(bool disposing)
		{
			if(disposing) timer.Dispose();
			base.Dispose(disposing);
		}

		#endregion
	}
}
