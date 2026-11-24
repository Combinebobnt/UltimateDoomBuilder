using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Controls;

namespace CodeImp.DoomBuilder.ThreeDFloorMode
{
	public partial class MenusForm : Form
	{
		public ToolStripButton FloorSlope { get { return floorslope; } }
		public ToolStripButton CeilingSlope { get { return ceilingslope; } }
		public ToolStripButton FloorAndCeilingSlope { get { return floorandceilingslope; } }
		public ToolStripButton UpdateSlopes { get { return updateslopes; } }
		public ToolStripActionButton RelocateControlSectors { get { return relocatecontrolsectors; } }
		public ContextMenuStrip AddSectorsContextMenu { get { return addsectorscontextmenu; } }
		public ToolStripMenuItem FloorsMenu { get { return floorsmenu; } }
		public ToolStripMenuItem DrawSlopesMenu { get { return drawslopesmenu; } }
		public ToolStripMenuItem SlopesMenu { get { return slopesmenu; } }

		private ToolStripMenuItem floorsmenu;
		private ToolStripMenuItem drawslopesmenu;
		private ToolStripMenuItem slopesmenu;

		public MenusForm()
		{
			InitializeComponent();

			// Top-level menus mirroring the mode buttons; each mode adds and removes its menu with its buttons
			floorsmenu = new ToolStripMenuItem("&3D Floors");
			floorsmenu.DropDownItems.Add(CreateMenuItem(relocatecontrolsectors, InvokeTaggedAction));
			floorsmenu.DropDownItems.Add(new ToolStripSeparator());
			floorsmenu.DropDownItems.Add(ActionItem("Select 3D Floor &Control Sector", "select3dfloorcontrolsector"));
			floorsmenu.DropDownItems.Add(ActionItem("&Duplicate 3D Floor Geometry", "duplicate3dfloorgeometry"));
			drawslopesmenu = new ToolStripMenuItem("&Slopes");
			drawslopesmenu.DropDownItems.Add(CreateMenuItem(floorslope, floorslope_Click));
			drawslopesmenu.DropDownItems.Add(CreateMenuItem(ceilingslope, ceilingslope_Click));
			drawslopesmenu.DropDownItems.Add(CreateMenuItem(floorandceilingslope, floorandceilingslope_Click));
			drawslopesmenu.DropDownItems.Add(new ToolStripSeparator());
			drawslopesmenu.DropDownItems.Add(ActionItem("&Finish Slope Drawing", "finishslopedraw"));
			slopesmenu = new ToolStripMenuItem("&Slopes");
			slopesmenu.DropDownItems.Add(CreateMenuItem(updateslopes, toolStripButton1_Click));
		}

		// A menu twin with the button's text, image, action Tag and check mark
		private static ToolStripMenuItem CreateMenuItem(ToolStripButton button, EventHandler click)
		{
			ToolStripMenuItem item = new ToolStripMenuItem(button.Text, button.Image) { Tag = button.Tag, Checked = button.Checked };
			button.CheckedChanged += delegate { item.Checked = button.Checked; };
			item.Click += click;
			return item;
		}

		// A menu item that runs an action by its tag; AddMenu prefixes the tag with the plugin name
		private ToolStripMenuItem ActionItem(string text, string action)
		{
			ToolStripMenuItem item = new ToolStripMenuItem(text) { Tag = action };
			item.Click += InvokeTaggedAction;
			return item;
		}

		public void UpdateToolTips()
		{
			relocatecontrolsectors.UpdateToolTip();
		}

		private void InvokeTaggedAction(object sender, EventArgs e)
		{
			General.Interface.InvokeTaggedAction(sender, e);
		}

		private void floorslope_Click(object sender, EventArgs e)
		{
			if (floorslope.Checked)
				return;

			General.Interface.InvokeTaggedAction(sender, e);
		}

		private void ceilingslope_Click(object sender, EventArgs e)
		{
			if (ceilingslope.Checked)
				return;

			General.Interface.InvokeTaggedAction(sender, e);
		}

		private void floorandceilingslope_Click(object sender, EventArgs e)
		{
			if (floorandceilingslope.Checked)
				return;

			General.Interface.InvokeTaggedAction(sender, e);
		}

		private void toolStripButton1_Click(object sender, EventArgs e)
		{
			BuilderPlug.Me.UpdateSlopes();
		}

		private void floorToolStripMenuItem_Click(object sender, EventArgs e)
		{
			List<SlopeVertexGroup> svgs = ((SlopeMode)General.Editing.Mode).GetSelectedSlopeVertexGroups();

			// Can only add sectors to one slope vertex group
			if (svgs.Count != 1)
				return;

			foreach (Sector s in (List<Sector>)addsectorscontextmenu.Tag)
			{
				SlopeVertexGroup rsvg = BuilderPlug.Me.GetSlopeVertexGroup(s);

				if (rsvg != null)
					rsvg.RemoveSector(s, PlaneType.Floor);

				svgs[0].AddSector(s, PlaneType.Floor);
				BuilderPlug.Me.UpdateSlopes(s);
			}

			General.Interface.RedrawDisplay();
		}

		private void removeSlopeFromFloorToolStripMenuItem_Click(object sender, EventArgs e)
		{
			foreach (Sector s in (List<Sector>)addsectorscontextmenu.Tag)
			{
				SlopeVertexGroup svg = BuilderPlug.Me.GetSlopeVertexGroup(s);

				if (svg != null)
					svg.RemoveSector(s, PlaneType.Floor);
			}

			General.Interface.RedrawDisplay();
		}

		private void ceilingToolStripMenuItem_Click(object sender, EventArgs e)
		{
			List<SlopeVertexGroup> svgs = ((SlopeMode)General.Editing.Mode).GetSelectedSlopeVertexGroups();

			// Can only add sectors to one slope vertex group
			if (svgs.Count != 1)
				return;

			foreach (Sector s in (List<Sector>)addsectorscontextmenu.Tag)
			{
				SlopeVertexGroup rsvg = BuilderPlug.Me.GetSlopeVertexGroup(s);

				if (rsvg != null)
					rsvg.RemoveSector(s, PlaneType.Ceiling);

				svgs[0].AddSector(s, PlaneType.Ceiling);
				BuilderPlug.Me.UpdateSlopes(s);
			}

			General.Interface.RedrawDisplay();
		}

		private void removeSlopeFromCeilingToolStripMenuItem_Click(object sender, EventArgs e)
		{
			foreach (Sector s in (List<Sector>)addsectorscontextmenu.Tag)
			{
				SlopeVertexGroup svg = BuilderPlug.Me.GetSlopeVertexGroup(s);

				if(svg != null)
					svg.RemoveSector(s, PlaneType.Ceiling);
			}

			General.Interface.RedrawDisplay();
		}

		private void addsectorscontextmenu_Opening(object sender, CancelEventArgs e)
		{
			// Disable adding if more than one slope vertex group is selected,
			// otherwise enable adding
			List<SlopeVertexGroup> svgs = ((SlopeMode)General.Editing.Mode).GetSelectedSlopeVertexGroups();

			addslopefloor.Enabled = svgs.Count == 1;
			addslopeceiling.Enabled = svgs.Count == 1;
		}

		private void addsectorscontextmenu_Closing(object sender, ToolStripDropDownClosingEventArgs e)
		{
			if(	e.CloseReason != ToolStripDropDownCloseReason.ItemClicked &&
				e.CloseReason != ToolStripDropDownCloseReason.Keyboard &&
				e.CloseReason != ToolStripDropDownCloseReason.AppFocusChange)
				((SlopeMode)General.Editing.Mode).ContextMenuClosing = true;
		}

		private void relocatecontrolsectors_Click(object sender, EventArgs e)
		{
			General.Interface.InvokeTaggedAction(sender, e);
		}
	}
}
