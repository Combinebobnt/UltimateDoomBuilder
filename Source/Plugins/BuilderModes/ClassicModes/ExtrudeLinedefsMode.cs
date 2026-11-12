#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Actions;
using CodeImp.DoomBuilder.BuilderModes.Interface;
using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Rendering;
using CodeImp.DoomBuilder.Windows;

#endregion

namespace CodeImp.DoomBuilder.BuilderModes
{
	[EditMode(DisplayName = "Extrude Linedefs",
			  AllowCopyPaste = false,
			  Volatile = true)]
	public sealed class ExtrudeLinedefsMode : BaseClassicMode
	{
		#region ================== Constants

		internal const int DEFAULT_DISTANCE = 64;
		private const float LINE_THICKNESS = 0.6f;

		// The selected lines already use the selection colour
		private static readonly PixelColor ERROR_COLOR = new PixelColor(255, 255, 0, 255);

		#endregion

		#region ================== Structures

		private sealed class Run
		{
			public ExtrudeChain Chain;
			public List<Vector2D> Points;
			public List<Linedef> Lines;
			public ExtrudeOffset Offset;
			public ExtrudeProblem Problem;
		}

		#endregion

		#region ================== Variables

		private readonly ICollection<Linedef> selectedlines;
		private readonly ICollection<Linedef> unselectedlines;
		private readonly List<Run> runs;
		private readonly Dictionary<Linedef, Vector2D> walknormals;
		private readonly List<Vector2D> crossings = new List<Vector2D>();

		private HintLabel hintlabel;
		private ExtrudeLinedefsOptionsPanel panel;
		private Linedef dragline;
		private double dragstart;

		#endregion

		#region ================== Properties

		// Just keep the base mode button checked
		public override string EditModeButtonName { get { return General.Editing.PreviousStableMode.Name; } }

		#endregion

		#region ================== Constructor / Disposer

		public ExtrudeLinedefsMode()
		{
			selectedlines = General.Map.Map.GetSelectedLinedefs(true);
			unselectedlines = General.Map.Map.GetSelectedLinedefs(false);

			string error;
			runs = BuildRuns(selectedlines, out error) ?? new List<Run>();

			// The walked left normal of every selected line, for dragging
			walknormals = new Dictionary<Linedef, Vector2D>();
			foreach(Run run in runs)
			{
				for(int i = 0; i < run.Lines.Count; i++)
				{
					Vector2D a = run.Points[i];
					Vector2D b = run.Points[(i + 1) % run.Points.Count];
					walknormals[run.Lines[i]] = (b - a).GetNormal().GetPerpendicular();
				}
			}

			hintlabel = new HintLabel(General.Colors.InfoLine);
		}

		public override void Dispose()
		{
			if(!isdisposed)
			{
				if(hintlabel != null) hintlabel.Dispose();
				base.Dispose();
			}
		}

		#endregion

		#region ================== Methods

		// Null with a reason when the lines can't be extruded as a set
		internal static string CheckSelection(ICollection<Linedef> lines)
		{
			string error;
			BuildRuns(lines, out error);
			return error;
		}

		private static List<Run> BuildRuns(ICollection<Linedef> lines, out string error)
		{
			Dictionary<Vertex, int> indices = new Dictionary<Vertex, int>();
			List<Vertex> vertices = new List<Vertex>();
			List<Linedef> linelist = new List<Linedef>(lines);
			int[] starts = new int[linelist.Count];
			int[] ends = new int[linelist.Count];
			int[] emptyside = new int[linelist.Count];
			for(int i = 0; i < linelist.Count; i++)
			{
				starts[i] = IndexOf(linelist[i].Start, indices, vertices);
				ends[i] = IndexOf(linelist[i].End, indices, vertices);

				// Doom's front is the right side, so a front-only line is empty on its left
				if(linelist[i].Front != null && linelist[i].Back == null) emptyside[i] = 1;
				else if(linelist[i].Front == null && linelist[i].Back != null) emptyside[i] = -1;
			}

			int branch;
			List<ExtrudeChain> chains = ExtrudePath.BuildChains(starts, ends, emptyside, out branch);
			if(chains == null)
			{
				error = "Can't extrude: vertex " + vertices[branch].Index + " joins more than two selected linedefs.";
				return null;
			}

			List<Run> runs = new List<Run>(chains.Count);
			foreach(ExtrudeChain chain in chains)
			{
				Run run = new Run { Chain = chain, Points = new List<Vector2D>(), Lines = new List<Linedef>() };
				foreach(int v in chain.Vertices) run.Points.Add(vertices[v].Position);
				foreach(int l in chain.Lines) run.Lines.Add(linelist[l]);
				runs.Add(run);
			}

			error = null;
			return runs;
		}

		private static int IndexOf(Vertex v, Dictionary<Vertex, int> indices, List<Vertex> vertices)
		{
			int index;
			if(indices.TryGetValue(v, out index)) return index;
			indices.Add(v, vertices.Count);
			vertices.Add(v);
			return vertices.Count - 1;
		}

		private void UpdateOffsets()
		{
			int decimals = General.Map.FormatInterface.VertexDecimals;
			double distance = panel.Distance;
			ExtrudeProblem first = null;
			foreach(Run run in runs)
			{
				run.Offset = ExtrudePath.Offset(run.Points, run.Chain.Closed, distance, decimals);
				run.Problem = ExtrudePath.Validate(run.Points, run.Chain.Closed, run.Offset, distance);
				if(first == null) first = run.Problem;
			}

			UpdateCrossings();

			hintlabel.Text = "Distance: " + panel.Distance + (first != null ? " (" + first.Reason + ")" : string.Empty);
		}

		// Where the preview crosses unselected lines, which Draw Lines will split
		private void UpdateCrossings()
		{
			crossings.Clear();
			foreach(Run run in runs)
			{
				List<Vector2D> outline = Outline(run);
				for(int i = 0; i < outline.Count - 1; i++)
				{
					Line2D segment = new Line2D(outline[i], outline[i + 1]);
					RectangleF box = RectangleF.FromLTRB((float)Math.Min(segment.v1.x, segment.v2.x), (float)Math.Min(segment.v1.y, segment.v2.y),
														 (float)Math.Max(segment.v1.x, segment.v2.x), (float)Math.Max(segment.v1.y, segment.v2.y));
					foreach(Linedef ld in unselectedlines)
					{
						if(ld.Rect.Right < box.Left || ld.Rect.Left > box.Right || ld.Rect.Bottom < box.Top || ld.Rect.Top > box.Bottom) continue;
						double u;
						if(ld.Line.GetIntersection(segment, out u) && u > 0.001 && u < 0.999)
							crossings.Add(segment.GetCoordinatesAt(u));
					}
				}
			}
		}

		// The new lines one run draws: side walls and offset polyline, closed for a loop
		private static List<Vector2D> Outline(Run run)
		{
			List<Vector2D> outline = new List<Vector2D>();
			if(!run.Chain.Closed) outline.Add(run.Points[0]);
			outline.AddRange(run.Offset.Points);
			if(run.Chain.Closed) outline.Add(run.Offset.Points[0]);
			else outline.Add(run.Points[run.Points.Count - 1]);
			return outline;
		}

		private ExtrudeProblem FirstProblem()
		{
			foreach(Run run in runs) if(run.Problem != null) return run.Problem;
			return null;
		}

		#endregion

		#region ================== Settings panel

		private void AddInterface()
		{
			panel = new ExtrudeLinedefsOptionsPanel();
			panel.SetValues(General.Settings.ReadPluginSetting("extrudelinedefsmode.distance", DEFAULT_DISTANCE),
							General.Settings.ReadPluginSetting("extrudelinedefsmode.floordelta", 0),
							General.Settings.ReadPluginSetting("extrudelinedefsmode.ceildelta", 0));
			panel.Register();
			panel.OnValueChanged += OnValuesChanged;
		}

		private void RemoveInterface()
		{
			panel.OnValueChanged -= OnValuesChanged;
			General.Settings.WritePluginSetting("extrudelinedefsmode.distance", panel.Distance);
			General.Settings.WritePluginSetting("extrudelinedefsmode.floordelta", panel.FloorDelta);
			General.Settings.WritePluginSetting("extrudelinedefsmode.ceildelta", panel.CeilingDelta);
			panel.Unregister();
		}

		private void OnValuesChanged(object sender, EventArgs e)
		{
			UpdateOffsets();
			General.Interface.RedrawDisplay();
		}

		#endregion

		#region ================== Events

		public override void OnCancel()
		{
			base.OnCancel();
			General.Editing.ChangeMode(General.Editing.PreviousStableMode.Name);
		}

		public override void OnEngage()
		{
			base.OnEngage();
			renderer.SetPresentation(Presentation.Standard);
			AddInterface();
			UpdateOffsets();
			if(runs.Count > 0) hintlabel.Move(runs[0].Points[0], runs[0].Points[0]);
		}

		public override void OnDisengage()
		{
			base.OnDisengage();
			RemoveInterface();
		}

		public override void OnAccept()
		{
			ExtrudeProblem problem = FirstProblem();
			if(runs.Count == 0 || problem != null)
			{
				General.Interface.DisplayStatus(StatusType.Warning, "Can't extrude: " + (problem != null ? problem.Reason : "nothing selected") + ".");
				return;
			}

			string rest = (selectedlines.Count == 1 ? "a linedef" : selectedlines.Count + " linedefs");
			General.Map.UndoRedo.CreateUndo("Extrude " + rest);

			// Points just off each source line on the extrusion side. Draw Lines may replace the
			// source lines themselves when it joins them with the redrawn closing edge.
			double sign = Math.Sign(panel.Distance);
			List<Vector2D> probes = new List<Vector2D>();
			List<Sector> before = new List<Sector>();
			foreach(Run run in runs)
			{
				foreach(Linedef ld in run.Lines)
				{
					Vector2D probe = ld.GetCenterPoint() + walknormals[ld] * sign * 0.5;
					probes.Add(probe);
					before.Add(General.Map.Map.GetSectorByCoordinates(probe));
				}
			}

			List<Linedef> drawnlines = new List<Linedef>();
			List<Linedef> outerlines = new List<Linedef>();
			foreach(Run run in runs)
			{
				// Closed polygons only: an open drawing would make Draw Lines search for its own way back
				List<DrawnVertex> points = new List<DrawnVertex>();
				if(run.Chain.Closed)
				{
					foreach(Vector2D p in run.Offset.Points) points.Add(Drawn(p));
					points.Add(Drawn(run.Offset.Points[0]));
				}
				else
				{
					points.Add(Drawn(run.Points[0]));
					foreach(Vector2D p in run.Offset.Points) points.Add(Drawn(p));
					for(int i = run.Points.Count - 1; i >= 0; i--) points.Add(Drawn(run.Points[i]));
				}

				if(!Tools.DrawLines(points, false, BuilderPlug.Me.AutoAlignTextureOffsetsOnCreate))
				{
					// Withdrawing also cancels this volatile mode
					General.Map.UndoRedo.WithdrawUndo();
					General.Interface.DisplayStatus(StatusType.Warning, "Extrusion failed.");
					return;
				}

				// Marks only last until the next draw
				foreach(Linedef ld in General.Map.Map.GetMarkedLinedefs(true))
				{
					drawnlines.Add(ld);
					if(!TouchesSource(ld)) outerlines.Add(ld);
				}
			}

			General.Map.Map.SnapAllToAccuracy();
			General.Map.Map.Update();
			if(General.Settings.SplitJoinedSectors)
			{
				drawnlines.RemoveAll(ld => ld.IsDisposed);
				Tools.SplitOuterSectors(drawnlines);
			}

			// Height deltas go to whatever new sector now sits on each source line's extrusion side
			if(panel.FloorDelta != 0 || panel.CeilingDelta != 0)
			{
				HashSet<Sector> strips = new HashSet<Sector>();
				for(int i = 0; i < probes.Count; i++)
				{
					Sector s = General.Map.Map.GetSectorByCoordinates(probes[i]);
					if(s != null && (before[i] == null || before[i].IsDisposed || s != before[i])) strips.Add(s);
				}
				foreach(Sector s in strips)
				{
					s.FloorHeight += panel.FloorDelta;
					s.CeilHeight = Math.Max(s.FloorHeight, s.CeilHeight + panel.CeilingDelta);
				}
			}

			// The new outer edge stays selected, so the action can be repeated
			General.Map.Map.ClearAllSelected();
			foreach(Linedef ld in outerlines) if(!ld.IsDisposed) ld.Selected = true;

			General.Map.Map.Update();
			General.Map.Data.UpdateUsedTextures();
			General.Map.Renderer2D.UpdateExtraFloorFlag();
			General.Map.IsChanged = true;

			General.Editing.ChangeMode(General.Editing.PreviousStableMode.Name);
		}

		private static DrawnVertex Drawn(Vector2D p)
		{
			return new DrawnVertex { pos = p, stitch = true, stitchline = true };
		}

		private bool TouchesSource(Linedef ld)
		{
			foreach(Run run in runs)
			{
				foreach(Vector2D p in run.Points)
				{
					if(Vector2D.DistanceSq(ld.Start.Position, p) < 0.0001 || Vector2D.DistanceSq(ld.End.Position, p) < 0.0001)
						return true;
				}
			}
			return false;
		}

		public override void OnRedrawDisplay()
		{
			renderer.RedrawSurface();

			if(renderer.StartPlotter(true))
			{
				renderer.PlotLinedefSet(unselectedlines);
				renderer.PlotVerticesSet(General.Map.Map.Vertices);
				renderer.Finish();
			}

			if(renderer.StartThings(true))
			{
				renderer.RenderThingSet(General.Map.Map.Things, General.Settings.ActiveThingsAlpha);
				renderer.Finish();
			}

			if(renderer.StartOverlay(true))
			{
				float vsize = (renderer.VertexSize + 1.0f) / renderer.Scale;
				foreach(Run run in runs)
				{
					PixelColor color = (run.Problem != null ? ERROR_COLOR : General.Colors.Highlight);
					foreach(Linedef ld in run.Lines)
						renderer.RenderLine(ld.Start.Position, ld.End.Position, LINE_THICKNESS, General.Colors.Selection, true);

					List<Vector2D> outline = Outline(run);
					for(int i = 0; i < outline.Count - 1; i++)
						renderer.RenderLine(outline[i], outline[i + 1], LINE_THICKNESS, color, true);

					foreach(Vector2D p in run.Offset.Points)
						renderer.RenderRectangleFilled(new RectangleF((float)(p.x - vsize), (float)(p.y - vsize), vsize * 2.0f, vsize * 2.0f), color, true);
				}

				foreach(Vector2D p in crossings)
					renderer.RenderRectangle(new RectangleF((float)(p.x - vsize), (float)(p.y - vsize), vsize * 2.0f, vsize * 2.0f), 1.0f, General.Colors.Indication, true);

				renderer.RenderText(hintlabel);
				renderer.Finish();
			}

			renderer.Present();
		}

		public override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			if(e.Button != MouseButtons.Left) return;

			dragline = MapSet.NearestLinedef(selectedlines, mousedownmappos);
			if(dragline == null || !walknormals.ContainsKey(dragline)) { dragline = null; return; }

			// Mouse position along the normal, less the distance it already stands for
			dragstart = Vector2D.DotProduct(mousedownmappos - dragline.GetCenterPoint(), walknormals[dragline]) - panel.Distance;
		}

		public override void OnMouseUp(MouseEventArgs e)
		{
			base.OnMouseUp(e);
			dragline = null;
		}

		public override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			if(!selectpressed || dragline == null) return;

			Vector2D normal = walknormals[dragline];
			double distance = Vector2D.DotProduct(mousemappos - dragline.GetCenterPoint(), normal) - dragstart;
			if(!General.Interface.ShiftState)
			{
				double grid = General.Map.Grid.GridSize;
				distance = Math.Round(distance / grid) * grid;
			}
			else
			{
				distance = Math.Round(distance);
			}

			if((int)distance != panel.Distance)
			{
				panel.SetValues((int)distance, panel.FloorDelta, panel.CeilingDelta);
				UpdateOffsets();
			}

			Vector2D labelpos = dragline.GetCenterPoint() + normal * (panel.Distance + 16 / renderer.Scale * Math.Sign(panel.Distance == 0 ? 1 : panel.Distance));
			hintlabel.Move(labelpos, labelpos);
			General.Interface.RedrawDisplay();
		}

		#endregion

		#region ================== Actions

		[BeginAction("increasebevel")]
		private void IncreaseDistance() { panel.Distance += General.Map.Grid.GridSize; }

		[BeginAction("decreasebevel")]
		private void DecreaseDistance() { panel.Distance -= General.Map.Grid.GridSize; }

		#endregion
	}
}
