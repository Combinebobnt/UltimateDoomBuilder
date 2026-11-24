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
	// Picks the settings groups to export, or the groups and units to import from a file
	internal sealed class SettingsTransferForm : DelayedForm
	{
		#region ================== Variables

		private readonly TreeView tree = new TreeView();
		private readonly bool importing;
		private bool checking;

		#endregion

		#region ================== Properties

		// Export: the checked groups
		public List<SettingsGroup> SelectedGroups
		{
			get
			{
				List<SettingsGroup> groups = new List<SettingsGroup>();
				foreach(TreeNode n in tree.Nodes)
					if(n.Checked || AnyChildChecked(n)) groups.Add((SettingsGroup)n.Tag);
				return groups;
			}
		}

		// Import: the checked units' paths
		public List<string> SelectedUnits
		{
			get
			{
				List<string> units = new List<string>();
				foreach(TreeNode n in tree.Nodes)
				{
					if(n.Nodes.Count > 0)
					{
						foreach(TreeNode c in n.Nodes)
							if(c.Checked) units.AddRange((List<string>)c.Tag);
					}
					else if(n.Checked)
					{
						units.AddRange(GroupUnits(n));
					}
				}
				return units;
			}
		}

		#endregion

		#region ================== Constructor

		// Export mode
		public SettingsTransferForm(Configuration live) : this(live, null, false) { }

		// Import mode
		public SettingsTransferForm(Configuration source, SettingsFileInfo info) : this(source, info, true) { }

		private SettingsTransferForm(Configuration cfg, SettingsFileInfo info, bool importing)
		{
			this.importing = importing;
			Text = importing ? "Import Settings" : "Export Settings";
			FormBorderStyle = FormBorderStyle.Sizable;
			MaximizeBox = false;
			MinimizeBox = false;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.CenterParent;
			AutoScaleMode = AutoScaleMode.Font;
			ClientSize = new Size(460, importing ? 480 : 330);
			MinimumSize = new Size(380, 300);

			TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), ColumnCount = 1, RowCount = 3 };
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			Label intro = new Label { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
			intro.MaximumSize = new Size(ClientSize.Width - 24, 0);
			intro.Text = importing ? ImportNote(cfg, info) : "Choose the settings to export. Window layout is screen-specific and off by default.";
			layout.Controls.Add(intro, 0, 0);

			tree.CheckBoxes = true;
			tree.Dock = DockStyle.Fill;
			tree.HideSelection = true;
			tree.AfterCheck += tree_AfterCheck;
			layout.Controls.Add(tree, 0, 1);
			BuildTree(cfg);

			FlowLayoutPanel buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
			Button cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
			Button ok = new Button { Text = importing ? "Import..." : "Export...", Width = 90 };
			ok.Click += ok_Click;
			buttons.Controls.Add(cancel);
			buttons.Controls.Add(ok);
			layout.Controls.Add(buttons, 0, 2);

			Controls.Add(layout);
			AcceptButton = ok;
			CancelButton = cancel;
		}

		#endregion

		#region ================== Methods

		private static string ImportNote(Configuration cfg, SettingsFileInfo info)
		{
			List<string> lines = new List<string>();
			if(info.IsExport)
			{
				string from = string.IsNullOrEmpty(info.Program) ? "an unknown version" : info.Program;
				lines.Add("Exported from Ultimate Doom Builder " + from + (string.IsNullOrEmpty(info.Platform) ? "" : " on " + info.Platform) + ".");
			}
			else
			{
				lines.Add("This is a settings file without an export header; every group it holds is offered.");
			}

			if(SettingsTransfer.IsCrossPlatform(info, SettingsTransfer.LocalPlatform))
				lines.Add("It comes from another platform, so the platform-specific settings (" + string.Join(", ", SettingsTransfer.PLATFORM_KEYS) + ") are skipped.");
			if(cfg.SettingExists("plugins.udbscript") && !SettingsTransfer.KeepsUDBScriptPaths(info, General.AppPath))
				lines.Add("UDBScript script slots and per-script options are skipped: they refer to scripts by their path in another install.");
			lines.Add("Imported settings replace the matching ones here and are applied the next time Ultimate Doom Builder starts.");
			return string.Join("\n", lines.ToArray());
		}

		private void BuildTree(Configuration cfg)
		{
			Dictionary<SettingsGroup, TreeNode> nodes = new Dictionary<SettingsGroup, TreeNode>();
			List<SettingsUnit> units = SettingsTransfer.EnumerateUnits(cfg);

			foreach(SettingsGroup g in Enum.GetValues(typeof(SettingsGroup)))
			{
				List<string> paths = new List<string>();
				foreach(SettingsUnit u in units) if(u.Group == g) paths.Add(u.Path);
				if(importing && paths.Count == 0) continue;

				TreeNode node = new TreeNode(SettingsTransfer.GroupTitle(g)) { Tag = g, Checked = SettingsTransfer.DefaultSelected(g) };
				nodes[g] = node;
				groupunits[node] = paths;
				tree.Nodes.Add(node);

				// One entry per game configuration or plugin, so a single one can be taken
				if(importing && (g == SettingsGroup.GameConfigurations || g == SettingsGroup.Plugins))
				{
					foreach(SettingsUnit u in units)
					{
						if(u.Group != g) continue;
						TreeNode child = new TreeNode(u.Name) { Tag = new List<string> { u.Path }, Checked = node.Checked };
						node.Nodes.Add(child);
					}
				}
			}
		}

		private readonly Dictionary<TreeNode, List<string>> groupunits = new Dictionary<TreeNode, List<string>>();

		private List<string> GroupUnits(TreeNode node)
		{
			List<string> paths;
			return groupunits.TryGetValue(node, out paths) ? paths : new List<string>();
		}

		private static bool AnyChildChecked(TreeNode node)
		{
			foreach(TreeNode c in node.Nodes) if(c.Checked) return true;
			return false;
		}

		#endregion

		#region ================== Events

		private void tree_AfterCheck(object sender, TreeViewEventArgs e)
		{
			if(checking) return;
			checking = true;
			if(e.Node.Parent == null)
			{
				foreach(TreeNode c in e.Node.Nodes) c.Checked = e.Node.Checked;
			}
			else
			{
				e.Node.Parent.Checked = AnyChildChecked(e.Node.Parent);
			}
			checking = false;
		}

		private void ok_Click(object sender, EventArgs e)
		{
			bool any = importing ? SelectedUnits.Count > 0 : SelectedGroups.Count > 0;
			if(!any)
			{
				General.ShowWarningMessage("Nothing is selected.", MessageBoxButtons.OK);
				return;
			}
			DialogResult = DialogResult.OK;
			Close();
		}

		#endregion
	}
}
