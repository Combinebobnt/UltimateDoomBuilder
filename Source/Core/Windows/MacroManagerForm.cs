#region ================== Namespaces

using System;
using System.Drawing;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Macros;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	// Lists the saved macros: run, edit, duplicate, reorder, delete
	internal sealed class MacroManagerForm : DelayedForm
	{
		#region ================== Variables

		private readonly MacroManager manager;
		private readonly ListView list;
		private readonly Button run;
		private readonly Button edit;
		private readonly Button duplicate;
		private readonly Button delete;
		private readonly Button moveup;
		private readonly Button movedown;
		private Macro torun;

		#endregion

		#region ================== Properties

		// Run after the dialog closes, so the map is in front while it runs
		public Macro MacroToRun { get { return torun; } }

		#endregion

		#region ================== Constructor

		public MacroManagerForm(MacroManager manager)
		{
			this.manager = manager;

			Text = "Macros";
			FormBorderStyle = FormBorderStyle.Sizable;
			MaximizeBox = false;
			MinimizeBox = false;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.CenterParent;
			AutoScaleMode = AutoScaleMode.Font;
			ClientSize = new Size(640, 360);
			MinimumSize = new Size(480, 280);

			TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), ColumnCount = 2, RowCount = 3 };
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
			list.Columns.Add("Name", 180);
			list.Columns.Add("Key", 110);
			list.Columns.Add("Steps", 60);
			list.Columns.Add("Mode", 170);
			list.SelectedIndexChanged += delegate { UpdateButtons(); };
			list.DoubleClick += delegate { RunSelected(); };
			layout.Controls.Add(list, 0, 0);

			FlowLayoutPanel side = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
			run = new Button { Text = "Run", Width = 100 };
			edit = new Button { Text = "Edit...", Width = 100 };
			duplicate = new Button { Text = "Duplicate", Width = 100 };
			moveup = new Button { Text = "Move Up", Width = 100 };
			movedown = new Button { Text = "Move Down", Width = 100 };
			delete = new Button { Text = "Delete", Width = 100 };
			run.Click += delegate { RunSelected(); };
			edit.Click += delegate { EditSelected(); };
			duplicate.Click += delegate { DuplicateSelected(); };
			moveup.Click += delegate { MoveSelected(-1); };
			movedown.Click += delegate { MoveSelected(1); };
			delete.Click += delegate { DeleteSelected(); };
			side.Controls.AddRange(new Control[] { run, edit, duplicate, moveup, movedown, delete });
			layout.Controls.Add(side, 1, 0);

			Label hint = new Label { AutoSize = true, Margin = new Padding(3, 6, 3, 0), MaximumSize = new Size(600, 0) };
			hint.Text = "Steps act on the current selection, or on what is under the mouse. Run macros that use the mouse position from their slot key, with the mouse over the map.";
			layout.Controls.Add(hint, 0, 1);
			layout.SetColumnSpan(hint, 2);

			FlowLayoutPanel buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
			Button close = new Button { Text = "Close", Width = 90, DialogResult = DialogResult.Cancel };
			buttons.Controls.Add(close);
			layout.Controls.Add(buttons, 0, 2);
			layout.SetColumnSpan(buttons, 2);

			Controls.Add(layout);
			CancelButton = close;
			FillList(null);
		}

		#endregion

		#region ================== Methods

		private Macro Selected { get { return (list.SelectedItems.Count > 0) ? (Macro)list.SelectedItems[0].Tag : null; } }

		private void FillList(Macro select)
		{
			list.BeginUpdate();
			list.Items.Clear();
			foreach(Macro m in manager.Store.Macros)
			{
				string key = "";
				if(m.Slot > 0)
				{
					string action = "builder_macroslot" + m.Slot;
					int k = General.Actions.Exists(action) ? General.Actions.GetActionByName(action).ShortcutKey : 0;
					key = (k > 0) ? Actions.Action.GetShortcutKeyDesc(k) : "Slot " + m.Slot + " (no key)";
				}
				string mode = m.AnyMode ? "Any" : MacroManager.ModeTitle(m.Mode);
				string why = null;
				if(m.MalformedSteps != null) why = "unreadable steps";
				else if(m.MissingActions(MacroManager.ActionExists).Count > 0) why = "unavailable";
				ListViewItem item = new ListViewItem(new[] { (why != null) ? m.Name + " (" + why + ")" : m.Name, key, m.StepCount.ToString(), mode });
				item.Tag = m;
				if(why != null) item.ForeColor = SystemColors.GrayText;
				list.Items.Add(item);
				if(ReferenceEquals(m, select)) item.Selected = true;
			}
			list.EndUpdate();
			if((list.SelectedItems.Count == 0) && (list.Items.Count > 0)) list.Items[0].Selected = true;
			UpdateButtons();
		}

		private void UpdateButtons()
		{
			Macro m = Selected;
			int index = (m != null) ? manager.Store.Macros.IndexOf(m) : -1;
			run.Enabled = (m != null) && (manager.WhyNotRunnable(m) == null);
			edit.Enabled = duplicate.Enabled = delete.Enabled = (m != null);
			moveup.Enabled = (index > 0);
			movedown.Enabled = (index >= 0) && (index < manager.Store.Macros.Count - 1);
		}

		private void RunSelected()
		{
			Macro m = Selected;
			if(m == null) return;
			string why = manager.WhyNotRunnable(m);
			if(why != null)
			{
				General.ShowWarningMessage("Macro '" + m.Name + "' can't run: " + why + ".", MessageBoxButtons.OK);
				return;
			}
			torun = m;
			DialogResult = DialogResult.OK;
			Close();
		}

		private void EditSelected()
		{
			Macro m = Selected;
			if(m == null) return;
			using(MacroEditForm form = new MacroEditForm(m, false, m))
			{
				if(form.ShowDialog(this) != DialogResult.OK) return;
				manager.Put(form.Result, m);
				FillList(form.Result);
			}
		}

		private void DuplicateSelected()
		{
			Macro m = Selected;
			if(m == null) return;
			Macro copy = m.Clone();
			copy.Name = manager.Store.UniqueName(m.Name);
			copy.Slot = 0;
			manager.Put(copy, null);
			FillList(copy);
		}

		private void MoveSelected(int delta)
		{
			Macro m = Selected;
			if(m == null) return;
			manager.Store.Move(m, delta);
			manager.Save();
			FillList(m);
		}

		private void DeleteSelected()
		{
			Macro m = Selected;
			if(m == null) return;
			if(General.ShowWarningMessage("Delete macro '" + m.Name + "'?", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
			int index = manager.Store.Macros.IndexOf(m);
			manager.Store.Remove(m);
			manager.Save();
			FillList((manager.Store.Macros.Count > 0) ? manager.Store.Macros[Math.Min(index, manager.Store.Macros.Count - 1)] : null);
		}

		#endregion
	}
}
