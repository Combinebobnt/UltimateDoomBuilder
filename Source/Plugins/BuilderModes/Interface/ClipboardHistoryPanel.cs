using System;
using System.ComponentModel;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Editing;

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
					ListViewItem item = list.Items.Add(string.Format("{0:HH:mm:ss}  {1} sectors, {2} lines, {3} things",
						entry.CopiedUtc.ToLocalTime(), entry.Sectors, entry.Linedefs, entry.Things));
					item.Tag = entry;
					item.ToolTipText = string.Format("{0}\n{1} vertices, {2} sectors, {3} lines, {4} things\n{5:0.##} x {6:0.##} map units",
						entry.SourceMap, entry.Vertices, entry.Sectors, entry.Linedefs, entry.Things, entry.Width, entry.Height);
					item.Selected = ReferenceEquals(entry, selected);
				}
				clear.Enabled = history.Entries.Count > 0;
			}
			finally
			{
				list.EndUpdate();
				updating = false;
			}
			UpdateColumnSize();
		}

		private void UpdateColumnSize()
		{
			if(list == null || coldescription == null || !list.IsHandleCreated) return;
			bool scrollbar = (BuilderPlug.GetWindowLong(list.Handle, BuilderPlug.GWL_STYLE) & BuilderPlug.WS_VSCROLL) != 0;
			coldescription.Width = Math.Max(0, list.ClientRectangle.Width - 2 - (scrollbar ? 0 : SystemInformation.VerticalScrollBarWidth));
		}

		protected override void OnLayout(LayoutEventArgs e)
		{
			base.OnLayout(e);
			UpdateColumnSize();
		}

		private void history_Changed(object sender, EventArgs e) { UpdateList(); }
		private void list_Resize(object sender, EventArgs e) { UpdateColumnSize(); }

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
				PasteSelected();
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
			if(e.KeyCode == Keys.Enter) PasteSelected();
			else if(e.KeyCode == Keys.Delete) history.Remove(SelectedEntry);
			General.Interface.FocusDisplay();
		}

		private void PasteSelected()
		{
			ClipboardHistoryEntry entry = SelectedEntry;
			if(entry != null) CopyPasteManager.DoPasteEntry(entry);
			General.Interface.FocusDisplay();
		}

		private void menu_Opening(object sender, CancelEventArgs e)
		{
			bool selected = SelectedEntry != null;
			paste.Enabled = selected && General.Map != null && !General.Map.IsDisposed;
			copy.Enabled = selected;
			remove.Enabled = selected;
			clearall.Enabled = history.Entries.Count > 0;
		}

		private void paste_Click(object sender, EventArgs e) { PasteSelected(); }
		private void copy_Click(object sender, EventArgs e)
		{
			CopyPasteManager.CopyEntryToClipboard(SelectedEntry);
			General.Interface.FocusDisplay();
		}
		private void remove_Click(object sender, EventArgs e)
		{
			history.Remove(SelectedEntry);
			General.Interface.FocusDisplay();
		}
		private void clear_Click(object sender, EventArgs e)
		{
			history.Clear();
			General.Interface.FocusDisplay();
		}
	}
}
