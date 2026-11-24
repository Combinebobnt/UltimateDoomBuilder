#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.IO;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	// Lets the user point imported paths that don't exist here at this machine's folders
	internal sealed class SettingsPathRemapForm : DelayedForm
	{
		#region ================== Row

		private sealed class Row
		{
			public string OldFolder;
			public List<SettingsPathEntry> Entries = new List<SettingsPathEntry>();
			public TextBox NewFolder;
			public Label Found;
		}

		#endregion

		#region ================== Variables

		private readonly List<Row> rows = new List<Row>();
		private readonly CheckBox screenshotsdefault;
		private readonly SettingsPathEntry screenshots;

		#endregion

		#region ================== Constructor

		public SettingsPathRemapForm(List<SettingsPathEntry> missing, string sourcehome)
		{
			Text = "Import Settings: Missing Paths";
			FormBorderStyle = FormBorderStyle.Sizable;
			MaximizeBox = false;
			MinimizeBox = false;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.CenterParent;
			AutoScaleMode = AutoScaleMode.Font;
			ClientSize = new Size(760, 420);
			MinimumSize = new Size(560, 300);

			string localhome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			foreach(SettingsPathEntry e in missing)
			{
				if(e.Key == SettingsTransfer.SCREENSHOTS_KEY) { screenshots = e; continue; }
				Row row = rows.Find(r => r.OldFolder == e.Folder);
				if(row == null)
				{
					row = new Row { OldFolder = e.Folder };
					rows.Add(row);
				}
				row.Entries.Add(e);
			}

			TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), ColumnCount = 1, RowCount = 3 };
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			Label intro = new Label { AutoSize = true, Margin = new Padding(0, 0, 0, 6), MaximumSize = new Size(ClientSize.Width - 24, 0) };
			intro.Text = "These folders from the imported settings don't exist on this computer. Choose where their files are now, "
				+ "or leave a row blank to keep the old paths. Engine parameters typed by hand are not changed.";
			layout.Controls.Add(intro, 0, 0);

			TableLayoutPanel grid = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 4 };
			grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
			grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
			grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			grid.Controls.Add(new Label { Text = "Old folder", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
			grid.Controls.Add(new Label { Text = "New folder", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 1, 0);

			int r0 = 1;
			foreach(Row row in rows)
			{
				Label old = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = row.OldFolder + " (" + row.Entries.Count + (row.Entries.Count == 1 ? " path)" : " paths)") };
				row.NewFolder = new TextBox { Dock = DockStyle.Fill, Text = SettingsTransfer.SuggestFolder(row.OldFolder, sourcehome, localhome) ?? "" };
				Button browse = new Button { Text = "Browse...", AutoSize = true };
				row.Found = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
				Row captured = row;
				row.NewFolder.TextChanged += delegate { UpdateFound(captured); };
				browse.Click += delegate { Browse(captured); };
				grid.Controls.Add(old, 0, r0);
				grid.Controls.Add(row.NewFolder, 1, r0);
				grid.Controls.Add(browse, 2, r0);
				grid.Controls.Add(row.Found, 3, r0);
				UpdateFound(row);
				r0++;
			}

			if(screenshots != null)
			{
				screenshotsdefault = new CheckBox { AutoSize = true, Checked = true, Text = "Screenshots folder " + screenshots.Value + " is missing: use this install's default" };
				grid.Controls.Add(screenshotsdefault, 0, r0);
				grid.SetColumnSpan(screenshotsdefault, 4);
			}
			layout.Controls.Add(grid, 0, 1);

			FlowLayoutPanel buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
			Button cancel = new Button { Text = "Cancel import", AutoSize = true, DialogResult = DialogResult.Cancel };
			Button ok = new Button { Text = "OK", Width = 90, DialogResult = DialogResult.OK };
			buttons.Controls.Add(cancel);
			buttons.Controls.Add(ok);
			layout.Controls.Add(buttons, 0, 2);

			Controls.Add(layout);
			AcceptButton = ok;
			CancelButton = cancel;
		}

		#endregion

		#region ================== Methods

		// Writes the chosen folders into the pending import
		public void Apply(Configuration pending)
		{
			foreach(Row row in rows)
			{
				string folder = row.NewFolder.Text.Trim();
				if(folder.Length == 0) continue;
				foreach(SettingsPathEntry e in row.Entries) pending.WriteSetting(e.Key, SettingsTransfer.RemapPath(e, folder));
			}

			if(screenshots != null && screenshotsdefault.Checked && pending.SettingExists(screenshots.Key))
				pending.DeleteSetting(screenshots.Key);
		}

		private void UpdateFound(Row row)
		{
			string folder = row.NewFolder.Text.Trim();
			if(folder.Length == 0)
			{
				row.Found.Text = "kept as is";
				return;
			}
			int found = 0;
			foreach(SettingsPathEntry e in row.Entries)
				if(SettingsTransfer.PathExists(SettingsTransfer.RemapPath(e, folder))) found++;
			row.Found.Text = "found " + found + " of " + row.Entries.Count;
		}

		private void Browse(Row row)
		{
			using(FolderBrowserDialog dialog = new FolderBrowserDialog())
			{
				dialog.Description = "Folder that replaces " + row.OldFolder;
				if(row.NewFolder.Text.Trim().Length > 0) dialog.SelectedPath = row.NewFolder.Text.Trim();
				if(NativeFileDialogs.Show(dialog, this) == DialogResult.OK) row.NewFolder.Text = dialog.SelectedPath;
			}
		}

		#endregion
	}
}
