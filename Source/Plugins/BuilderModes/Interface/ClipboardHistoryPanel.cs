using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.Windows;

namespace CodeImp.DoomBuilder.BuilderModes
{
	public partial class ClipboardHistoryPanel : UserControl
	{
		private readonly ClipboardHistory history;
		private ClipboardHistoryEntry pendingentry;
		private int pendingtime;
		private bool updating;

		public ClipboardHistoryPanel()
		{
			InitializeComponent();
			history = General.ClipboardHistory;
			history.Changed += history_Changed;
			UpdateList();
		}

		private ClipboardHistoryEntry SelectedEntry
		{
			get { return list.SelectedItems.Count == 0 ? null : (ClipboardHistoryEntry)list.SelectedItems[0].Tag; }
		}

		private void UpdateList()
		{
			ClipboardHistoryEntry selected = SelectedEntry;
			updating = true;
			pendingentry = null;
			list.BeginUpdate();
			try
			{
				list.Items.Clear();
				foreach(ClipboardHistoryEntry entry in history.Entries)
				{
					List<string> counts = new List<string>();
					if(entry.Sectors > 0) counts.Add(entry.Sectors + (entry.Sectors == 1 ? " sector" : " sectors"));
					if(entry.Linedefs > 0) counts.Add(entry.Linedefs + (entry.Linedefs == 1 ? " linedef" : " linedefs"));
					if(entry.Things > 0) counts.Add(entry.Things + (entry.Things == 1 ? " thing" : " things"));
					if(counts.Count == 0 && entry.Vertices > 0) counts.Add(entry.Vertices + (entry.Vertices == 1 ? " vertex" : " vertices"));
					ListViewItem item = list.Items.Add(string.Join(", ", counts));
					item.SubItems.Add(string.Format("{0:0.##} x {1:0.##}", entry.Width, entry.Height));
					item.SubItems.Add(entry.SourceMap);
					item.SubItems.Add(entry.CopiedUtc.ToLocalTime().ToString("HH:mm"));
					item.Tag = entry;
					item.ToolTipText = string.Format("{0}\n{1:G}\n{2} vertices, {3} sectors, {4} linedefs, {5} things",
						entry.SourceMap, entry.CopiedUtc.ToLocalTime(), entry.Vertices, entry.Sectors, entry.Linedefs, entry.Things);
					item.Selected = ReferenceEquals(entry, selected);
				}
				clear.Enabled = history.Entries.Count > 0;
			}
			finally
			{
				list.EndUpdate();
				updating = false;
			}
			UpdateColumnSizes();
		}

		private void UpdateColumnSizes()
		{
			if(list == null || colcopied == null || !list.IsHandleCreated) return;
			colsize.Width = (int)Math.Round(64 * MainForm.DPIScaler.Width);
			colsource.Width = (int)Math.Round(90 * MainForm.DPIScaler.Width);
			colcopied.Width = (int)Math.Round(45 * MainForm.DPIScaler.Width);
			coldescription.Width = Math.Max(0, BuilderPlug.ListViewColumnSpace(list) - 2 - colsize.Width - colsource.Width - colcopied.Width);
		}

		protected override void OnLayout(LayoutEventArgs e)
		{
			base.OnLayout(e);
			UpdateColumnSizes();
		}

		private void history_Changed(object sender, EventArgs e) { UpdateList(); }
		private void list_Resize(object sender, EventArgs e) { UpdateColumnSizes(); }

		private void list_SelectedIndexChanged(object sender, EventArgs e)
		{
			if(!updating) General.Interface.FocusDisplay();
		}

		private void list_MouseDown(object sender, MouseEventArgs e)
		{
			if(e.Button != MouseButtons.Right) return;
			list.SelectedIndices.Clear();
			ListViewItem hit = list.GetItemAt(e.X, e.Y);
			if(hit != null) hit.Selected = true;
			pendingentry = null;
		}

		private void list_MouseUp(object sender, MouseEventArgs e)
		{
			if(e.Button != MouseButtons.Left) return;
			ListViewItem hit = list.GetItemAt(e.X, e.Y);
			ClipboardHistoryEntry entry = hit == null ? null : (ClipboardHistoryEntry)hit.Tag;
			if(entry == null || !hit.Selected) pendingentry = null;
			else if(ReferenceEquals(pendingentry, entry) && unchecked(Environment.TickCount - pendingtime) <= SystemInformation.DoubleClickTime)
			{
				pendingentry = null;
				PasteSelected(false);
			}
			else
			{
				pendingentry = entry;
				pendingtime = Environment.TickCount;
			}
			General.Interface.FocusDisplay();
		}

		private void list_KeyUp(object sender, KeyEventArgs e)
		{
			pendingentry = null;
			General.Interface.FocusDisplay();
		}

		private void PasteSelected(bool askoptions)
		{
			pendingentry = null;
			ClipboardHistoryEntry entry = SelectedEntry;
			if(entry != null) CopyPasteManager.PasteHistoryEntry(entry, askoptions);
			General.Interface.FocusDisplay();
		}

		private void menu_Opening(object sender, CancelEventArgs e)
		{
			bool selected = SelectedEntry != null;
			paste.Enabled = selected && General.Map != null && !General.Map.IsDisposed;
			pastespecial.Enabled = paste.Enabled;
			copy.Enabled = selected;
			remove.Enabled = selected;
			clearall.Enabled = history.Entries.Count > 0;
		}

		private void paste_Click(object sender, EventArgs e) { PasteSelected(false); }
		private void pastespecial_Click(object sender, EventArgs e) { PasteSelected(true); }
		private void copy_Click(object sender, EventArgs e)
		{
			CopyPasteManager.PromoteHistoryEntry(SelectedEntry);
			General.Interface.FocusDisplay();
		}
		private void remove_Click(object sender, EventArgs e)
		{
			history.Remove(SelectedEntry);
			General.Interface.FocusDisplay();
		}
		private void clear_Click(object sender, EventArgs e)
		{
			pendingentry = null;
			if(history.Entries.Count > 0 && General.ShowWarningMessage("Clear all clipboard history entries?",
				MessageBoxButtons.OKCancel, MessageBoxDefaultButton.Button2) == DialogResult.OK) history.Clear();
			General.Interface.FocusDisplay();
		}
	}
}
