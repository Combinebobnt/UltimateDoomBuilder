#region ================== Namespaces

using System;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.VisualModes;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	// Menu entries for core actions that otherwise only had a key
	partial class MainForm
	{
		#region ================== Variables

		private ToolStripMenuItem itemacceptmode;
		private ToolStripMenuItem itemcancelmode;
		private ToolStripMenuItem itemdeleteitem;
		private ToolStripMenuItem itemdissolveitem;
		private ToolStripMenuItem itemclearselection;
		private ToolStripMenuItem itemsmartgridtransform;
		private ToolStripMenuItem itemzoomin;
		private ToolStripMenuItem itemzoomout;
		private ToolStripMenuItem itemtogglehighlight;
		private ToolStripMenuItem itemenhancedrendering;
		private ToolStripMenuItem itemcommandpalette;
		private ToolStripMenuItem itemtestmapfromview;
		private ToolStripMenuItem itemlogmark;

		// Enabled only while the current mode binds their action
		private ToolStripMenuItem[] modeboundactionitems;

		#endregion

		#region ================== Setup

		private void SetupActionMenus()
		{
			itemacceptmode = ActionItem("&Accept", "builder_acceptmode");
			itemcancelmode = ActionItem("Ca&ncel", "builder_cancelmode");
			ToolStripItemCollection edit = menuedit.DropDownItems;
			edit.Insert(edit.IndexOf(seperatoreditundo), new ToolStripSeparator());
			edit.Insert(edit.IndexOf(seperatoreditundo), itemacceptmode);
			edit.Insert(edit.IndexOf(seperatoreditundo), itemcancelmode);

			itemdeleteitem = ActionItem("&Delete", "builder_deleteitem");
			itemdissolveitem = ActionItem("D&issolve", "builder_dissolveitem");
			edit.Insert(edit.IndexOf(itempastespecial) + 1, itemdeleteitem);
			edit.Insert(edit.IndexOf(itemdeleteitem) + 1, itemdissolveitem);

			itemsmartgridtransform = ActionItem("Smart Grid Transform", "builder_smartgridtransform");
			edit.Insert(edit.IndexOf(itemresetgrid) + 1, itemsmartgridtransform);

			itemclearselection = ActionItem("Clear Se&lection", "builder_clearselection");
			edit.Insert(edit.IndexOf(addToGroup), itemclearselection);

			itemzoomin = ActionItem("Zoom &In", "builder_zoomin");
			itemzoomout = ActionItem("Zoom &Out", "builder_zoomout");
			menuzoom.DropDownItems.Insert(0, itemzoomin);
			menuzoom.DropDownItems.Insert(1, itemzoomout);
			menuzoom.DropDownItems.Insert(2, new ToolStripSeparator());

			itemtogglehighlight = ActionItem("&Highlight", "builder_togglehighlight");
			itemenhancedrendering = ActionItem("Enhanced Rendering Effects (Visual mode)", "builder_gztoggleenhancedrendering");
			ToolStripItemCollection view = menuview.DropDownItems;
			view.Insert(view.IndexOf(separatorrendering), itemtogglehighlight);
			view.Insert(view.IndexOf(separatorrendering) + 1, itemenhancedrendering);

			itemcommandpalette = ActionItem("Command &Palette...", "builder_opencommandpalette");
			itemtestmapfromview = ActionItem("Test Map from Current &Position", "builder_testmapfromview");
			itemlogmark = ActionItem("Mark Interaction Log Moment", "builder_interactionlogmark");
			ToolStripItemCollection tools = menutools.DropDownItems;
			tools.Insert(0, itemcommandpalette);
			tools.Insert(1, new ToolStripSeparator());
			tools.Insert(tools.IndexOf(itemtestmap) + 1, itemtestmapfromview);
			tools.Insert(tools.IndexOf(itemtestoptions) + 1, itemlogmark);

			modeboundactionitems = new[] { itemdeleteitem, itemdissolveitem, itemclearselection, itemsmartgridtransform,
				itemzoomin, itemzoomout, itemtogglehighlight };

			menuedit.DropDownOpening += actionmenus_DropDownOpening;
			menuview.DropDownOpening += actionmenus_DropDownOpening;
			menutools.DropDownOpening += actionmenus_DropDownOpening;
		}

		private ToolStripMenuItem ActionItem(string text, string action)
		{
			ToolStripMenuItem item = new ToolStripMenuItem(text) { Tag = action };
			item.Click += InvokeTaggedAction;
			return item;
		}

		#endregion

		#region ================== Events

		private void actionmenus_DropDownOpening(object sender, EventArgs e)
		{
			bool maploaded = (General.Map != null);
			EditMode mode = General.Editing.Mode;

			foreach(ToolStripMenuItem item in modeboundactionitems)
			{
				string action = (string)item.Tag;
				item.Enabled = maploaded && General.Actions.Exists(action) && General.Actions[action].BeginBound;
			}

			bool isvolatile = maploaded && (mode != null) && mode.Attributes.Volatile;
			itemacceptmode.Enabled = isvolatile;
			itemcancelmode.Enabled = isvolatile;

			itemtogglehighlight.Checked = General.Settings.UseHighlight;
			itemenhancedrendering.Checked = General.Settings.EnhancedRenderingEffects;
			itemenhancedrendering.Enabled = maploaded;

			itemcommandpalette.Enabled = maploaded;
			itemtestmapfromview.Visible = maploaded;
			itemtestmapfromview.Enabled = (mode is VisualMode);
			itemlogmark.Visible = InteractionLog.Enabled;
		}

		#endregion
	}
}
