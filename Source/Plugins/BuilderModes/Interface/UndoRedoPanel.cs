
#region ================== Copyright (c) 2007 Pascal vd Heiden

/*
 * Copyright (c) 2007 Pascal vd Heiden, www.codeimp.com
 * This program is released under GNU General Public License
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 */

#endregion

#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.Windows;

#endregion

namespace CodeImp.DoomBuilder.BuilderModes
{
	public partial class UndoRedoPanel : UserControl
	{
		#region ================== Constants
		
		private const int MAX_DISPLAY_LEVELS = 400;
		
		#endregion
		
		#region ================== Variables
		
		private bool ignoreevents;
		private int currentselection;
		private int addindex;
		private string begindescription;
		private bool previewing; // rows are recoloured for a target other than the current level
		private int pendingindex = -1; // row whose second click within DoubleClickTime applies the jump
		private int pendingtime;
		private UndoManager watchedundo; // raises BranchChanged for the restore button

		#endregion
		
		#region ================== Constructor
		
		// Constructor
		public UndoRedoPanel()
		{
			InitializeComponent();
		}
		
		#endregion
		
		#region ================== Methods
		
		// This sets the description for the first item
		public void SetBeginDescription(string description)
		{
			begindescription = description;
		}

		// Follows the open map's discarded undo branch; called on map new/open, and with null on close
		public void WatchUndoManager(UndoManager undoredo)
		{
			if(watchedundo != null) watchedundo.BranchChanged -= undoredo_BranchChanged;
			watchedundo = undoredo;
			if(watchedundo != null) watchedundo.BranchChanged += undoredo_BranchChanged;
		}

		// Shows the restore button only while there is a branch to restore
		private void UpdateRestoreButton()
		{
			bool show = (General.Map != null) && !General.Map.IsDisposed && (General.Map.UndoRedo != null) && General.Map.UndoRedo.HasDiscardedBranch;
			if(show) restorebranch.Text = "Restore Discarded Changes (" + General.Map.UndoRedo.DiscardedBranchCount + ")";
			restorebranch.Visible = show;
		}

		// This refills the list
		public void UpdateList()
		{
			ignoreevents = true;
			currentselection = -1;
			previewing = false;
			pendingindex = -1;
			UpdateRestoreButton();

			// Check we have undo/redo capability
			if((General.Map == null) || General.Map.IsDisposed || (General.Map.UndoRedo == null))
			{
				// No, clear the list
				list.Items.Clear();
				return;
			}
			
			// Make complete list of levels
			List<UndoSnapshot> levels = General.Map.UndoRedo.GetUndoList();
			levels.Reverse();
			int numundos = levels.Count;
			levels.AddRange(General.Map.UndoRedo.GetRedoList());
			//int numredos = levels.Count - numundos;
			
			// Determine the offset to show items at
			int offset = numundos - (MAX_DISPLAY_LEVELS >> 1);
			if((offset + MAX_DISPLAY_LEVELS) > levels.Count) offset = levels.Count - MAX_DISPLAY_LEVELS;
			if(offset < 0) offset = 0;
			
			// Reset the list
			list.SelectedItems.Clear();
			list.BeginUpdate();
			addindex = 0;
			
			// Add beginning
			if(offset > 0)
			{
				// This indicates there is more above, but we don't display it
				// because when the list gets too long it becomes slow
				AddItem("...");
			}
			else
			{
				// Real beginning
				ListViewItem firstitem = AddItem(begindescription);
				
				// Are we at the first item?
				if(numundos == 0)
				{
					// Highlight the last undo level
					firstitem.BackColor = SystemColors.Highlight;
					firstitem.ForeColor = SystemColors.HighlightText;
					currentselection = 0;
				}
				else
				{
					// Normal undo level
					firstitem.ForeColor = SystemColors.WindowText;
					firstitem.BackColor = SystemColors.Window;
				}
			}
			
			// Add levels!
			for(int i = offset; i < levels.Count; i++)
			{
				// Add no more than the MAX_DISPLAY_LEVELS
				ListViewItem item;
				if((addindex - 1) == MAX_DISPLAY_LEVELS)
					item = AddItem("...");
				else
					item = AddItem(levels[i].Description);
				
				// Color item
				if(i == (numundos - 1))
				{
					// Highlight the last undo level
					item.BackColor = SystemColors.Highlight;
					item.ForeColor = SystemColors.HighlightText;
					currentselection = addindex - 1;
				}
				else if(i >= numundos)
				{
					// Make gray because this is a redo level
					item.ForeColor = SystemColors.GrayText;
					item.BackColor = SystemColors.Control;
				}
				else
				{
					// Normal undo level
					item.ForeColor = SystemColors.WindowText;
					item.BackColor = SystemColors.Window;
				}
				
				// Leave when list is full
				if((addindex - 1) > MAX_DISPLAY_LEVELS)
					break;
			}
			
			// Remove the excessive items
			for(int i = list.Items.Count - 1; i >= addindex; i--)
				list.Items.RemoveAt(i);
			
			// We must always have the "selected" item in the list
			if(currentselection == -1)
				throw new Exception("Where is the selection?");
				
			// Make sure we can see the highlighted item
			list.Items[currentselection].EnsureVisible();
			
			list.EndUpdate();
			UpdateColumnSizes();
			ignoreevents = false;
		}
		
		// This updates/adds an item in the list
		private ListViewItem AddItem(string text)
		{
			ListViewItem item;
			if(addindex < list.Items.Count)
			{
				item = list.Items[addindex];
				item.Text = text;
			}
			else
			{
				item = list.Items.Add(text);
			}
			addindex++;
			return item;
		}
		
		// This updates the list column size
		private void UpdateColumnSizes()
		{
			// Check if a vertical scrollbar exists and adjust the column in the listbox accordingly
			if((BuilderPlug.GetWindowLong(list.Handle, BuilderPlug.GWL_STYLE) & BuilderPlug.WS_VSCROLL) != 0)
				coldescription.Width = list.ClientRectangle.Width - 2;
			else
				coldescription.Width = list.ClientRectangle.Width - SystemInformation.VerticalScrollBarWidth - 2;
		}
		
		#endregion
		
		#region ================== Events
		
		// When layout changes
		protected override void OnLayout(LayoutEventArgs e)
		{
			base.OnLayout(e);
			UpdateColumnSizes();
		}
		
		// Control resizes
		private void list_Resize(object sender, EventArgs e)
		{
			UpdateColumnSizes();
		}
		
		// Item selected
		private void list_SelectedIndexChanged(object sender, EventArgs e)
		{
			if(ignoreevents) return;
			
			ignoreevents = true;
			
			// We must have something selected
			if(list.SelectedIndices.Count > 0)
			{
				// Not the same as last selected?
				int selectedindex = list.SelectedIndices[0];
				if(selectedindex != currentselection)
				{
					// Recolor the elements in the list to match with the selection
					list.BeginUpdate();
					foreach(ListViewItem item in list.Items)
					{
						if(item.Index < selectedindex)
						{
							// Normal undo level
							item.ForeColor = SystemColors.WindowText;
							item.BackColor = SystemColors.Window;
						}
						else if(item.Index == selectedindex)
						{
							// Target level
							item.BackColor = SystemColors.Highlight;
							item.ForeColor = SystemColors.HighlightText;
						}
						else
						{
							// Make gray because this will become a redo level
							item.ForeColor = SystemColors.GrayText;
							item.BackColor = SystemColors.Control;
						}
					}
					list.EndUpdate();
					previewing = true;
				}
			}

			General.Interface.FocusDisplay();

			ignoreevents = false;
		}

		// Mouse released. A single click only previews; a second click on the same row applies the jump.
		private void list_MouseUp(object sender, MouseEventArgs e)
		{
			ignoreevents = true;

			ListViewItem hit = list.GetItemAt(e.X, e.Y);
			bool selectedhit = (hit != null) && (list.SelectedIndices.Count > 0) && (list.SelectedIndices[0] == hit.Index);

			if(!selectedhit)
			{
				pendingindex = -1;
			}
			else if(hit.Index == currentselection)
			{
				// Clicking the current level cancels any preview
				list.SelectedIndices.Clear();
				if(previewing) UpdateList();
				pendingindex = -1;
			}
			else if((hit.Index == pendingindex) && (unchecked(Environment.TickCount - pendingtime) <= SystemInformation.DoubleClickTime))
			{
				pendingindex = -1;
				ApplyJump(hit.Index);
			}
			else
			{
				pendingindex = hit.Index;
				pendingtime = Environment.TickCount;
			}

			General.Interface.FocusDisplay();

			ignoreevents = false;
		}

		// Performs the undo/redos to reach the given row; the list is updated automatically
		private void ApplyJump(int targetindex)
		{
			string origin = list.Items[currentselection].Text;
			int delta = currentselection - targetindex;
			int redosbefore = General.Map.UndoRedo.GetRedoList().Count;

			if(delta < 0)
				General.Map.UndoRedo.PerformRedo(-delta);
			else
				General.Map.UndoRedo.PerformUndo(delta);

			// Measured, since a plugin or mode can refuse the undo
			int moved = Math.Abs(General.Map.UndoRedo.GetRedoList().Count - redosbefore);
			if(moved > 1)
			{
				bool undone = (delta > 0);
				string backaction = undone ? "builder_redo" : "builder_undo";
				// Kept short, key hint last: the status label clips early in a narrow (~1000 px) window
				string text = (undone ? "Undone " : "Redone ") + moved + " changes. Back: double-click \"" + origin + "\"";
				if(General.Actions.Exists(backaction) && (General.Actions.GetActionByName(backaction).ShortcutKey != 0))
					text += " or " + Actions.Action.GetShortcutKeyDesc(backaction) + " x" + moved;
				General.Interface.DisplayStatus(StatusType.Action, text + ".");
			}
		}

		// Moving off the list drops a preview that was never applied
		private void list_MouseLeave(object sender, EventArgs e)
		{
			pendingindex = -1;
			if(previewing) UpdateList();
		}

		// Key released
		private void list_KeyUp(object sender, KeyEventArgs e)
		{
			ignoreevents = true;

			list.SelectedIndices.Clear();
			pendingindex = -1;
			General.Interface.FocusDisplay();

			ignoreevents = false;
		}

		// A swap changes which levels are the grey redo rows, not only the button
		private void undoredo_BranchChanged(object sender, EventArgs e)
		{
			UpdateList();
		}

		private void restorebranch_Click(object sender, EventArgs e)
		{
			General.Actions.InvokeAction("builder_undobranchrestore");
			General.Interface.FocusDisplay();
		}

		#endregion
	}
}
