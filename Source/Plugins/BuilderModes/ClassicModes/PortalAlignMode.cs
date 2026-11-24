using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Controls;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Portals;
using CodeImp.DoomBuilder.Rendering;
using CodeImp.DoomBuilder.Types;
using CodeImp.DoomBuilder.Windows;

namespace CodeImp.DoomBuilder.BuilderModes
{
	// Moves one portal link's handle: the exit side's portal group, the other stack thing, or an anchored portal's
	// reference line. The map changes only on accept, as one undo step.
	[EditMode(DisplayName = "Portal Align", AllowCopyPaste = false, Volatile = true)]
	public sealed class PortalAlignMode : BaseClassicMode
	{
		private const float LINE_THICKNESS = 1.0f;

		private readonly PortalMapData data;
		private readonly PortalCapabilities caps;
		private readonly PortalReport report;
		private readonly PortalAlignTarget target;
		private readonly List<Linedef> originlines = new List<Linedef>();
		private readonly List<Linedef> islandlines = new List<Linedef>();
		private List<Linedef> movedlines = new List<Linedef>();

		private PortalAlignEdit edit;
		private PortalAlignCheck check;
		private bool exact;
		private bool dragging;
		private Vector2D dragstartmappos;
		private Vector2D dragstartdelta;
		private Linedef highlighted;
		private string repickrefusal;

		private PortalAlignPanel panel;
		private Docker docker;

		public override string EditModeButtonName { get { return General.Editing.PreviousStableMode.Name; } }

		public PortalAlignMode(PortalMapData data, PortalCapabilities caps, PortalReport report, PortalAlignTarget target)
		{
			this.data = data;
			this.caps = caps;
			this.report = report;
			this.target = target;
			edit = new PortalAlignEdit();
		}

		#region ================== Edit state

		private Vector2D Delta { get { return edit.Delta; } }

		private void SetMove(Vector2D delta)
		{
			exact = false;
			check = null;
			edit = PortalAlign.Move(data, target, delta);
			movedlines = target.Handle == PortalAlignHandle.Island && (delta.x != 0 || delta.y != 0) ? islandlines : new List<Linedef>();
		}

		private void SetExact()
		{
			PortalAlignEdit e = PortalAlign.MakeExact(data, target);
			if(e == null) return;
			exact = true;
			edit = e;
			movedlines = new List<Linedef>();
			foreach(int v in e.Vertices.Keys)
				foreach(Linedef l in General.Map.Map.GetVertexByIndex(v).Linedefs)
					if(!movedlines.Contains(l)) movedlines.Add(l);
			if(panel != null) panel.SetDelta(new Vector2D());
		}

		private void SetRepick(Linedef line)
		{
			PortalAlignEdit e = PortalAlign.Repick(data, target, line.Index, out repickrefusal);
			if(e != null)
			{
				exact = false;
				edit = e;
			}
		}

		// Analyses a copy of the map with the edit applied and shows what changes
		private void Recheck()
		{
			check = edit.IsEmpty ? null : PortalAlign.Check(data, caps, report, target, edit);
			List<string> lines = new List<string>();
			string offsettext;
			if(edit.IsEmpty)
			{
				offsettext = "Offset " + Num(target.Link.Displacement) + " (unchanged)";
			}
			else if(check.Link == null)
			{
				offsettext = "The ends no longer pair";
			}
			else
			{
				offsettext = "Offset " + Num(target.Link.Displacement) + " becomes " + (check.Link.HasDisplacement ? Num(check.Link.Displacement) : "none");
			}

			if(repickrefusal != null) lines.Add(repickrefusal);
			if(check != null)
			{
				foreach(PortalIssue i in check.NewErrors) lines.Add("New error: " + i.Message);
				foreach(PortalIssue i in check.NewWarnings) lines.Add("New warning: " + i.Message);
				foreach(PortalAlignChange c in check.Changed)
					lines.Add("Also changes " + PortalsPanel.LinkText(c.Before) + (c.After == null ? ": no longer pairs" : c.After.HasDisplacement ? " to " + Num(c.After.Displacement) : ": loses its offset"));
				if(check.Overlaps > 0)
					lines.Add("The moved group's bounds now meet " + check.Overlaps + (check.Overlaps == 1 ? " other line" : " other lines") + "; the move does not merge geometry, so check for overlaps");
				if(lines.Count == 0) lines.Add("No other portal changes and no new problems.");
			}
			else if(lines.Count == 0)
			{
				lines.Add(Instructions());
			}
			if(panel != null) panel.ShowResults(offsettext, lines, !edit.IsEmpty);
		}

		private string Instructions()
		{
			switch(target.Handle)
			{
				case PortalAlignHandle.Thing: return "Drag the square handle, or type a move, to move the other stack thing.";
				case PortalAlignHandle.Island: return "Drag the square handle, or type a move, to move the exit side's portal group (" + target.IslandSectors.Count + " sectors).";
				case PortalAlignHandle.Repick: return "Click a line without an action to move the reference (arg3 = 1) special to it.";
				default: return target.Refusal + (target.CanMakeExact ? " Make Exact is still available." : "");
			}
		}

		private static string Num(Vector2D v)
		{
			return v.x.ToString("0.###", CultureInfo.InvariantCulture) + ", " + v.y.ToString("0.###", CultureInfo.InvariantCulture);
		}

		#endregion

		#region ================== Apply

		private bool ApplyEdit()
		{
			MapSet map = General.Map.Map;
			float min = General.Map.FormatInterface.MinCoordinate, max = General.Map.FormatInterface.MaxCoordinate;
			foreach(Vector2D p in edit.Vertices.Values)
			{
				if(p.x < min || p.x > max || p.y < min || p.y > max)
				{
					General.ShowWarningMessage("The move puts a vertex outside the map format's coordinate range (" + min + " to " + max + ").", MessageBoxButtons.OK);
					return false;
				}
			}
			if(check != null && check.NewErrors.Count > 0
				&& General.ShowWarningMessage("The move adds " + check.NewErrors.Count + (check.NewErrors.Count == 1 ? " portal error" : " portal errors") + ", listed in the Portal Align panel. Apply it anyway?", MessageBoxButtons.YesNo) != DialogResult.Yes)
				return false;

			General.Map.UndoRedo.CreateUndo(exact ? "Make portal exact" : edit.RepickLine >= 0 ? "Re-pick portal reference line" : "Align portal");
			if(target.Handle == PortalAlignHandle.Island && !exact && edit.RepickLine < 0)
			{
				MoveIslandSectors(Delta);
				foreach(Thing t in map.Things)
				{
					Sector s = t.Sector ?? map.GetSectorByCoordinates(new Vector2D(t.Position.x, t.Position.y));
					if(s != null && target.IslandSectors.Contains(s.Index)) t.Move(new Vector2D(t.Position.x, t.Position.y) + Delta);
				}
			}
			else
			{
				foreach(KeyValuePair<int, Vector2D> pair in edit.Things) map.GetThingByIndex(pair.Key).Move(pair.Value);
			}
			foreach(KeyValuePair<int, Vector2D> pair in edit.Vertices) map.GetVertexByIndex(pair.Key).Move(pair.Value);
			foreach(PortalLineEdit e in edit.LineEdits)
			{
				Linedef l = map.GetLinedefByIndex(e.Index);
				l.Action = e.Action;
				for(int i = 0; i < Linedef.NUM_ARGS; i++) l.Args[i] = e.Args[i];
			}

			map.SnapAllToAccuracy();
			map.Update();
			General.Map.ThingsFilter.Update();
			General.Map.IsChanged = true;
			return true;
		}

		// Sloped planes keep their heights over the moved floor, and flats keep their look when the drag lock is on
		private static readonly string[] PLANES = { "floor", "ceiling" };

		private void MoveIslandSectors(Vector2D offset)
		{
			if(!General.Map.UDMF) return;
			bool lockoffsets = BuilderPlug.Me.LockSectorTextureOffsetsWhileDragging;
			foreach(int index in target.IslandSectors)
			{
				Sector s = General.Map.Map.GetSectorByIndex(index);
				if(s == null) continue;
				Vector2D center = new Vector2D(s.BBox.X + s.BBox.Width / 2, s.BBox.Y + s.BBox.Height / 2);
				if(lockoffsets)
				{
					s.Fields.BeforeFieldsChange();
					foreach(string plane in PLANES) ShiftFlat(s, plane, offset);
				}
				if(s.FloorSlope.GetLengthSq() > 0 && !double.IsNaN(s.FloorSlopeOffset / s.FloorSlope.z))
				{
					Plane floor = new Plane(s.FloorSlope, s.FloorSlopeOffset);
					s.FloorSlopeOffset = -Vector3D.DotProduct(s.FloorSlope, new Vector3D(center + offset, floor.GetZ(center)));
				}
				if(s.CeilSlope.GetLengthSq() > 0 && !double.IsNaN(s.CeilSlopeOffset / s.CeilSlope.z))
				{
					Plane ceiling = new Plane(s.CeilSlope, s.CeilSlopeOffset);
					s.CeilSlopeOffset = -Vector3D.DotProduct(s.CeilSlope, new Vector3D(center + offset, ceiling.GetZ(center)));
				}
			}
		}

		// The same panning shift as dragging sectors with locked texture offsets
		private static void ShiftFlat(Sector s, string plane, Vector2D offset)
		{
			bool floor = plane == "floor";
			if((floor ? s.LongFloorTexture : s.LongCeilTexture) == MapSet.EmptyLongName) return;
			ImageData texture = General.Map.Data.GetFlatImage(floor ? s.FloorTexture : s.CeilTexture);
			if(texture == null) return;
			double scalex = s.Fields.GetValue("xscale" + plane, 1.0), scaley = s.Fields.GetValue("yscale" + plane, 1.0);
			if(scalex == 0 || scaley == 0) return;

			Vector2D shift = new Vector2D(-offset.x, offset.y).GetRotated(-Angle2D.DegToRad((int)s.Fields.GetValue("rotation" + plane, 0.0)));
			int width = (int)Math.Round(texture.Width / scalex), height = (int)Math.Round(texture.Height / scaley);
			SetFloat(s, "xpanning" + plane, (shift.x + s.Fields.GetValue("xpanning" + plane, 0.0)) % width);
			SetFloat(s, "ypanning" + plane, (shift.y + s.Fields.GetValue("ypanning" + plane, 0.0)) % height);
		}

		private static void SetFloat(Sector s, string key, double value)
		{
			if(!s.Fields.ContainsKey(key)) s.Fields.Add(key, new UniValue(UniversalType.Float, value));
			else s.Fields[key].Value = value;
		}

		#endregion

		#region ================== Events

		public override void OnEngage()
		{
			base.OnEngage();
			renderer.SetPresentation(Presentation.Standard);

			MapSet map = General.Map.Map;
			HashSet<int> origin = new HashSet<int>(target.OriginSectors);
			foreach(Linedef l in map.Linedefs)
			{
				bool front = l.Front != null && l.Front.Sector != null, back = l.Back != null && l.Back.Sector != null;
				if((front && origin.Contains(l.Front.Sector.Index)) || (back && origin.Contains(l.Back.Sector.Index))) originlines.Add(l);
			}
			foreach(int i in target.IslandLines) islandlines.Add(map.GetLinedefByIndex(i));

			panel = new PortalAlignPanel();
			panel.Setup(Describe(), target.Handle == PortalAlignHandle.Thing || target.Handle == PortalAlignHandle.Island, target.CanMakeExact, target.MakeExactRefusal);
			panel.OffsetChanged += panel_OffsetChanged;
			panel.AcceptClicked += delegate { General.Editing.AcceptMode(); };
			panel.CancelClicked += delegate { General.Editing.CancelMode(); };
			panel.ResetClicked += delegate { SetMove(new Vector2D()); panel.SetDelta(Delta); Recheck(); General.Interface.RedrawDisplay(); };
			panel.MakeExactClicked += delegate { SetExact(); Recheck(); General.Interface.RedrawDisplay(); };
			docker = new Docker("portalalign", "Portal Align", panel);
			General.Interface.AddDocker(docker, true);
			General.Interface.SelectDocker(docker);

			Recheck();
			General.Interface.DisplayStatus(StatusType.Info, Instructions());
		}

		public override void OnDisengage()
		{
			base.OnDisengage();
			if(docker != null)
			{
				General.Interface.RemoveDocker(docker);
				docker = null;
			}
			if(panel != null)
			{
				panel.Dispose();
				panel = null;
			}
		}

		public override void OnCancel()
		{
			base.OnCancel();
			General.Editing.ChangeMode(General.Editing.PreviousStableMode.Name);
		}

		public override void OnAccept()
		{
			dragging = false;
			if(!edit.IsEmpty && check == null) Recheck();
			if(!edit.IsEmpty && !ApplyEdit()) return;
			string key = check != null && check.Link != null ? check.Link.Key : target.Link.Key;
			if(!edit.IsEmpty) General.Interface.DisplayStatus(StatusType.Action, exact ? "Made the portal pair exact." : edit.RepickLine >= 0 ? "Moved the reference line to line " + edit.RepickLine + "." : "Aligned the portal.");
			General.Editing.ChangeMode(General.Editing.PreviousStableMode.Name);
			BuilderPlug.Me.ShowPortalLink(key);
		}

		private string Describe()
		{
			string text = PortalsPanel.LinkText(target.Link) + "\n";
			switch(target.Handle)
			{
				case PortalAlignHandle.Thing: return text + "The handle moves thing " + target.Link.Destination.Element.Index + ".";
				case PortalAlignHandle.Island: return text + "The handle moves the exit side's portal group: " + target.IslandSectors.Count + " sectors, " + target.IslandVertices.Count + " vertices and the things in them.";
				case PortalAlignHandle.Repick: return text + "Both anchors are in one portal group, so click another line to take the reference special.";
				default: return text + target.Refusal;
			}
		}

		private void panel_OffsetChanged(object sender, EventArgs e)
		{
			SetMove(panel.Delta);
			Recheck();
			General.Interface.RedrawDisplay();
		}

		public override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			if(e.Button != MouseButtons.Left) return;
			if(target.Handle == PortalAlignHandle.Repick)
			{
				if(highlighted != null)
				{
					SetRepick(highlighted);
					Recheck();
					General.Interface.RedrawDisplay();
				}
				return;
			}
			if(target.Handle != PortalAlignHandle.Thing && target.Handle != PortalAlignHandle.Island) return;
			Vector2D handle = target.HandlePosition + Delta;
			if(Vector2D.Distance(handle, mousemappos) > HandleRange()) return;
			if(exact) SetMove(new Vector2D());
			dragging = true;
			dragstartmappos = mousemappos;
			dragstartdelta = Delta;
		}

		public override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			if(dragging)
			{
				Vector2D raw = dragstartdelta + (mousemappos - dragstartmappos);
				bool snap = General.Interface.ShiftState ^ General.Interface.SnapToGrid;
				Vector2D d = snap ? General.Map.Grid.SnappedToGrid(raw) : new Vector2D(Math.Round(raw.x), Math.Round(raw.y));
				if(d.x != Delta.x || d.y != Delta.y)
				{
					SetMove(d);
					panel.SetDelta(d);
					General.Interface.RedrawDisplay();
				}
				return;
			}
			if(target.Handle == PortalAlignHandle.Repick)
			{
				Linedef l = General.Map.Map.NearestLinedefRange(mousemappos, HandleRange());
				if(l != null && l.Index == target.Link.Origin.Element.Index) l = null;
				if(l != highlighted)
				{
					highlighted = l;
					General.Interface.RedrawDisplay();
				}
			}
		}

		public override void OnMouseUp(MouseEventArgs e)
		{
			base.OnMouseUp(e);
			if(!dragging) return;
			dragging = false;
			Recheck();
			General.Interface.RedrawDisplay();
		}

		private double HandleRange() { return BuilderPlug.Me.HighlightRange / renderer.Scale; }

		#endregion

		#region ================== Rendering

		public override void OnRedrawDisplay()
		{
			renderer.RedrawSurface();
			if(renderer.StartPlotter(true))
			{
				renderer.PlotLinedefSet(General.Map.Map.Linedefs);
				renderer.PlotVerticesSet(General.Map.Map.Vertices);
				renderer.Finish();
			}
			if(renderer.StartThings(true))
			{
				renderer.RenderThingSet(General.Map.ThingsFilter.HiddenThings, General.Settings.HiddenThingsAlpha);
				renderer.RenderThingSet(General.Map.ThingsFilter.VisibleThings, General.Settings.ActiveThingsAlpha);
				renderer.Finish();
			}
			if(renderer.StartOverlay(true))
			{
				RenderOverlay();
				renderer.Finish();
			}
			renderer.Present();
		}

		private void RenderOverlay()
		{
			MapSet map = General.Map.Map;
			PixelColor ghostcolor = General.Colors.Indication.WithAlpha(150);
			PixelColor movecolor = General.Colors.Highlight;
			PixelColor endcolor = General.Colors.Selection;

			// The origin side's outline where the portal shows it: over the destination, at the current offset
			Vector2D offset = check != null && check.Link != null && check.Link.HasDisplacement ? check.Link.Displacement : target.Link.Displacement + Delta;
			foreach(Linedef l in originlines)
				renderer.RenderLine(l.Start.Position + offset, l.End.Position + offset, LINE_THICKNESS, ghostcolor, true);

			// The edit's moved geometry
			foreach(Linedef l in movedlines)
				renderer.RenderLine(MovedVertex(l.Start, Delta), MovedVertex(l.End, Delta), LINE_THICKNESS, movecolor, true);

			RenderEnd(target.Link.Origin.Element, new Vector2D(), endcolor);
			if(edit.RepickLine >= 0)
			{
				Linedef picked = map.GetLinedefByIndex(edit.RepickLine);
				renderer.RenderLine(picked.Start.Position, picked.End.Position, LINE_THICKNESS * 2, endcolor, true);
			}
			else
			{
				RenderEnd(target.Link.Destination.Element, exact ? new Vector2D() : Delta, endcolor);
			}
			if(highlighted != null)
				renderer.RenderLine(highlighted.Start.Position, highlighted.End.Position, LINE_THICKNESS * 2, movecolor, true);

			if(target.Handle == PortalAlignHandle.Thing || target.Handle == PortalAlignHandle.Island)
			{
				float size = (float)((renderer.VertexSize + 3.0f) / renderer.Scale);
				Vector2D h = target.HandlePosition + (exact ? new Vector2D() : Delta);
				RectangleF rect = new RectangleF((float)(h.x - size), (float)(h.y - size), size * 2.0f, size * 2.0f);
				renderer.RenderRectangleFilled(rect, dragging ? movecolor : endcolor, true);
			}
		}

		private Vector2D MovedVertex(Vertex v, Vector2D delta)
		{
			Vector2D p;
			if(exact && edit.Vertices.TryGetValue(v.Index, out p)) return p;
			return exact ? v.Position : v.Position + delta;
		}

		private void RenderEnd(PortalElement e, Vector2D delta, PixelColor color)
		{
			if(e.Type == PortalElementType.Linedef)
			{
				Linedef l = General.Map.Map.GetLinedefByIndex(e.Index);
				Vector2D a = exact ? MovedVertex(l.Start, delta) : l.Start.Position + delta;
				Vector2D b = exact ? MovedVertex(l.End, delta) : l.End.Position + delta;
				renderer.RenderLine(a, b, LINE_THICKNESS * 2, color, true);
			}
			else if(e.Type == PortalElementType.Thing)
			{
				Thing t = General.Map.Map.GetThingByIndex(e.Index);
				float r = Math.Max(8f, t.Size);
				Vector2D p = new Vector2D(t.Position.x, t.Position.y) + delta;
				renderer.RenderRectangle(new RectangleF((float)(p.x - r), (float)(p.y - r), r * 2, r * 2), 2f, color, true);
			}
		}

		#endregion
	}
}
