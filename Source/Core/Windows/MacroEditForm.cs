#region ================== Namespaces

using System;
using System.Drawing;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Macros;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	// Names a recorded macro, or edits a saved one: slot, mode guard and steps
	internal sealed class MacroEditForm : DelayedForm
	{
		#region ================== Variables

		private readonly Macro macro;
		private readonly Macro existing;
		private readonly TextBox name;
		private readonly ComboBox slot;
		private readonly CheckBox anymode;
		private readonly ListBox steps;
		private readonly NumericUpDown count;
		private readonly Button remove;
		private readonly Button moveup;
		private readonly Button movedown;
		private readonly Button ok;
		private bool updating;

		#endregion

		#region ================== Properties

		public Macro Result { get { return macro; } }

		#endregion

		#region ================== Constructor

		// fromrecording: Save / Discard after Stop Recording; otherwise OK / Cancel on a copy of "existing"
		public MacroEditForm(Macro source, bool fromrecording, Macro existing = null)
		{
			this.macro = source.Clone();
			this.existing = existing;

			Text = fromrecording ? "Save Recorded Macro" : "Edit Macro";
			FormBorderStyle = FormBorderStyle.Sizable;
			MaximizeBox = false;
			MinimizeBox = false;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.CenterParent;
			AutoScaleMode = AutoScaleMode.Font;
			ClientSize = new Size(460, 420);
			MinimumSize = new Size(400, 340);

			TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), ColumnCount = 2, RowCount = 7 };
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

			layout.Controls.Add(new Label { Text = "Name:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
			name = new TextBox { Dock = DockStyle.Fill, Text = macro.Name };
			layout.Controls.Add(name, 1, 0);

			layout.Controls.Add(new Label { Text = "Key slot:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
			slot = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, Anchor = AnchorStyles.Left };
			slot.Items.Add("None");
			for(int i = 1; i <= Macro.MAX_SLOTS; i++) slot.Items.Add(SlotText(i));
			slot.SelectedIndex = macro.Slot;
			layout.Controls.Add(slot, 1, 1);

			layout.Controls.Add(new Label { Text = "Recorded in:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
			layout.Controls.Add(new Label { Text = MacroManager.ModeTitle(macro.Mode) + " mode", AutoSize = true, Anchor = AnchorStyles.Left }, 1, 2);

			anymode = new CheckBox { Text = "Run in any mode", AutoSize = true, Checked = macro.AnyMode };
			layout.Controls.Add(anymode, 1, 3);

			Label stepslabel = new Label { Text = "Steps:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
			layout.Controls.Add(stepslabel, 0, 4);
			layout.SetColumnSpan(stepslabel, 2);

			TableLayoutPanel stepspanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
			stepspanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			stepspanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			steps = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
			steps.SelectedIndexChanged += delegate { UpdateButtons(); };
			stepspanel.Controls.Add(steps, 0, 0);

			FlowLayoutPanel stepbuttons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
			moveup = new Button { Text = "Move Up", Width = 90 };
			movedown = new Button { Text = "Move Down", Width = 90 };
			remove = new Button { Text = "Remove", Width = 90 };
			Label countlabel = new Label { Text = "Repeat:", AutoSize = true, Margin = new Padding(3, 10, 3, 0) };
			count = new NumericUpDown { Minimum = 1, Maximum = MacroStore.MAX_STEP_COUNT, Width = 90 };
			moveup.Click += delegate { MoveStep(-1); };
			movedown.Click += delegate { MoveStep(1); };
			remove.Click += delegate { RemoveStep(); };
			count.ValueChanged += delegate { ChangeCount(); };
			stepbuttons.Controls.AddRange(new Control[] { moveup, movedown, remove, countlabel, count });
			stepspanel.Controls.Add(stepbuttons, 1, 0);
			layout.Controls.Add(stepspanel, 0, 5);
			layout.SetColumnSpan(stepspanel, 2);

			FlowLayoutPanel buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
			Button cancel = new Button { Text = fromrecording ? "Discard" : "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
			ok = new Button { Text = fromrecording ? "Save" : "OK", Width = 90 };
			ok.Click += delegate { Accept(); };
			buttons.Controls.Add(cancel);
			buttons.Controls.Add(ok);
			layout.Controls.Add(buttons, 0, 6);
			layout.SetColumnSpan(buttons, 2);

			for(int i = 0; i < 5; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			Controls.Add(layout);

			AcceptButton = ok;
			CancelButton = cancel;
			FillSteps(macro.Steps.Count > 0 ? 0 : -1);
		}

		#endregion

		#region ================== Methods

		private static string SlotText(int i)
		{
			string action = "builder_macroslot" + i;
			int key = General.Actions.Exists(action) ? General.Actions.GetActionByName(action).ShortcutKey : 0;
			return "Slot " + i + ((key > 0) ? " (" + Actions.Action.GetShortcutKeyDesc(key) + ")" : " (no key bound)");
		}

		private void FillSteps(int select)
		{
			updating = true;
			steps.BeginUpdate();
			steps.Items.Clear();
			foreach(MacroStep s in macro.Steps)
			{
				string title = MacroManager.ActionTitle(s.Action);
				if(!General.Actions.Exists(s.Action)) title += " (unavailable)";
				steps.Items.Add((s.Count > 1) ? title + "  x" + s.Count : title);
			}
			steps.EndUpdate();
			if(select >= steps.Items.Count) select = steps.Items.Count - 1;
			steps.SelectedIndex = select;
			updating = false;
			UpdateButtons();
		}

		private void UpdateButtons()
		{
			int i = steps.SelectedIndex;
			moveup.Enabled = (i > 0);
			movedown.Enabled = (i >= 0) && (i < macro.Steps.Count - 1);
			remove.Enabled = (i >= 0);
			count.Enabled = (i >= 0);
			updating = true;
			if(i >= 0) count.Value = macro.Steps[i].Count;
			updating = false;
			ok.Enabled = (macro.Steps.Count > 0) || (macro.MalformedSteps != null);
		}

		private void MoveStep(int delta)
		{
			int i = steps.SelectedIndex;
			int j = i + delta;
			if(i < 0 || j < 0 || j >= macro.Steps.Count) return;
			MacroStep s = macro.Steps[i];
			macro.Steps.RemoveAt(i);
			macro.Steps.Insert(j, s);
			FillSteps(j);
		}

		private void RemoveStep()
		{
			int i = steps.SelectedIndex;
			if(i < 0) return;
			macro.Steps.RemoveAt(i);
			FillSteps(i);
		}

		private void ChangeCount()
		{
			int i = steps.SelectedIndex;
			if(updating || i < 0) return;
			macro.Steps[i].Count = (int)count.Value;
			FillSteps(i);
		}

		private void Accept()
		{
			string newname = name.Text.Trim();
			if(newname.Length == 0)
			{
				General.ShowWarningMessage("Enter a name for the macro.", MessageBoxButtons.OK);
				return;
			}
			if((General.Macros != null) && General.Macros.Store.NameTaken(newname, existing))
			{
				General.ShowWarningMessage("A macro named '" + newname + "' already exists.", MessageBoxButtons.OK);
				return;
			}

			macro.Name = newname;
			macro.Slot = slot.SelectedIndex;
			macro.AnyMode = anymode.Checked;
			DialogResult = DialogResult.OK;
			Close();
		}

		#endregion
	}
}
