using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.Portals;
using CodeImp.DoomBuilder.Windows;

namespace CodeImp.DoomBuilder.BuilderModes.Interface
{
	public partial class PortalCreateForm : DelayedForm
	{
		private sealed class Choice
		{
			public readonly string Text;
			public readonly int Value;
			public Choice(string text, int value) { Text = text; Value = value; }
			public override string ToString() { return Text; }
		}

		private readonly PortalMapData data;
		private readonly PortalCapabilities caps;
		private readonly PortalCreateContext ctx;
		private readonly bool lines;
		private readonly int stackopacity;
		private int a, b;
		private Vector2D posa, posb;
		private string problema, problemb;
		private bool updating;

		public PortalCreatePlan Plan { get; private set; }
		public PortalCreateKind Kind { get { return (PortalCreateKind)((Choice)kind.SelectedItem).Value; } }

		// Line kinds take two line indices; the stacked kind takes two sectors with each one's thing position, or why there is none
		public PortalCreateForm(PortalMapData data, PortalCapabilities caps, PortalCreateContext ctx, bool lines, int a, int b,
			Vector2D posa, Vector2D posb, string problema, string problemb, int stackopacity)
		{
			InitializeComponent();
			this.data = data;
			this.caps = caps;
			this.ctx = ctx;
			this.lines = lines;
			this.a = a;
			this.b = b;
			this.posa = posa;
			this.posb = posb;
			this.problema = problema;
			this.problemb = problemb;
			this.stackopacity = stackopacity;

			updating = true;
			foreach(PortalCreateKind k in PortalCreation.Kinds(caps, ctx, lines)) kind.Items.Add(new Choice(KindTitle(k), (int)k));
			if(kind.Items.Count > 0) kind.SelectedIndex = 0;
			updating = false;
			FillOptions();
		}

		private static string KindTitle(PortalCreateKind k)
		{
			switch(k)
			{
				case PortalCreateKind.LinePortal: return "Line portal (Line_SetPortal)";
				case PortalCreateKind.QuickPortal: return "Quick portal (Line_QuickPortal)";
				case PortalCreateKind.AnchoredSector: return "Anchored sector portal (Sector_SetPortal)";
				default: return "Stacked sectors (Upper and Lower Sector things)";
			}
		}

		#region ================== Options

		// Refills type and plane for the chosen kind, then replans
		private void FillOptions()
		{
			if(kind.SelectedItem == null) { Replan(); return; }
			updating = true;
			type.Items.Clear();
			plane.Items.Clear();
			PortalCreateKind k = Kind;
			bool twosided = lines && data.Lines[a].Back >= 0 && data.Lines[b].Back >= 0;
			switch(k)
			{
				case PortalCreateKind.LinePortal:
					type.Items.Add(new Choice("Visual", 0));
					type.Items.Add(new Choice("Teleport", 1));
					type.Items.Add(new Choice("Interactive", 2));
					type.Items.Add(new Choice("Linked", 3));
					type.SelectedIndex = twosided ? 3 : 0;
					planelabel.Text = "Plane align:";
					plane.Items.Add(new Choice("None", 0));
					plane.Items.Add(new Choice("Floor", 1));
					plane.Items.Add(new Choice("Ceiling", 2));
					plane.SelectedIndex = 0;
					break;
				case PortalCreateKind.QuickPortal:
					type.Items.Add(new Choice("Linked", 0));
					type.Items.Add(new Choice("Visual", 1));
					type.SelectedIndex = twosided ? 0 : 1;
					planelabel.Text = "Plane:";
					break;
				case PortalCreateKind.AnchoredSector:
					type.Items.Add(new Choice("Visual", PortalCreation.SECTOR_VISUAL));
					if(caps.ModernSectorPortals) type.Items.Add(new Choice("Linked", PortalCreation.SECTOR_LINKED));
					type.SelectedIndex = 0;
					planelabel.Text = "Plane:";
					FillPlanes();
					opacity.Value = 0;
					break;
				default:
					planelabel.Text = "Plane:";
					opacity.Value = Math.Max(opacity.Minimum, Math.Min(opacity.Maximum, stackopacity));
					break;
			}
			type.Enabled = type.Items.Count > 1;
			plane.Enabled = plane.Items.Count > 0;
			opacity.Enabled = k == PortalCreateKind.AnchoredSector || k == PortalCreateKind.StackedSectors;
			swap.Enabled = k == PortalCreateKind.AnchoredSector || k == PortalCreateKind.StackedSectors;
			updating = false;
			Replan();
		}

		// A linked sector portal takes one plane; a visual one may take both
		private void FillPlanes()
		{
			int previous = plane.SelectedItem != null ? ((Choice)plane.SelectedItem).Value : 0;
			plane.Items.Clear();
			plane.Items.Add(new Choice("Floor", 0));
			plane.Items.Add(new Choice("Ceiling", 1));
			if(Value(type) != PortalCreation.SECTOR_LINKED) plane.Items.Add(new Choice("Floor and ceiling", 2));
			plane.SelectedIndex = Math.Min(previous, plane.Items.Count - 1);
		}

		private static int Value(ComboBox box)
		{
			return box.SelectedItem != null ? ((Choice)box.SelectedItem).Value : 0;
		}

		#endregion

		#region ================== Plan

		private void Replan()
		{
			Plan = null;
			ends.Text = EndsText();
			if(kind.SelectedItem == null)
			{
				result.Text = "This configuration or map format cannot create a portal from this selection.";
				apply.Enabled = false;
				return;
			}

			PortalCreateKind k = Kind;
			string problem = k == PortalCreateKind.StackedSectors ? (problema ?? problemb) : null;
			if(problem != null)
			{
				result.Text = problem;
				apply.Enabled = false;
				return;
			}

			PortalCreateRequest req = new PortalCreateRequest
			{
				Kind = k, A = a, B = b, Type = Value(type), Plane = Value(plane), Opacity = (int)opacity.Value,
				PositionA = posa, PositionB = posb,
			};
			Plan = PortalCreation.Plan(data, caps, ctx, req);
			result.Text = ResultText(Plan);
			apply.Enabled = Plan.CanApply;
		}

		private string EndsText()
		{
			if(!lines)
				return "Upper Sector thing (its floor shows the other sector): sector " + a + "\r\n"
					+ "Lower Sector thing (its ceiling shows the other sector): sector " + b;

			if(kind.SelectedItem != null && Kind == PortalCreateKind.AnchoredSector)
				return "Anchor, where the portal shows (arg3 = 0): " + LineText(a) + "\r\n"
					+ "Reference, where it looks (arg3 = 1): " + LineText(b);
			return "Lines: " + LineText(a) + " and " + LineText(b);
		}

		private string LineText(int index)
		{
			PortalLine l = data.Lines[index];
			return "line " + index + (l.Front >= 0 ? " (front sector " + l.Front + ")" : " (no front sector)");
		}

		private static string ResultText(PortalCreatePlan plan)
		{
			StringBuilder sb = new StringBuilder();
			if(plan.Refusal != null) return "Cannot create: " + plan.Refusal;

			if(plan.Link != null && plan.Link.HasDisplacement)
				sb.Append("Offset: " + plan.Link.Displacement.x.ToString("0.###", CultureInfo.InvariantCulture) + ", "
					+ plan.Link.Displacement.y.ToString("0.###", CultureInfo.InvariantCulture) + "\r\n");
			foreach(string n in plan.Notes) sb.Append(n + "\r\n");
			if(plan.NewErrors.Count > 0)
			{
				sb.Append("\r\nBlocked, the engine would not build this as chosen:\r\n");
				foreach(PortalIssue i in plan.NewErrors) sb.Append("- " + i.Message + "\r\n");
			}
			if(plan.NewWarnings.Count > 0)
			{
				sb.Append("\r\nWarnings:\r\n");
				foreach(PortalIssue i in plan.NewWarnings) sb.Append("- " + i.Message + "\r\n");
			}
			if(plan.Confirms.Count > 0)
			{
				sb.Append("\r\nAsks before creating:\r\n");
				foreach(string c in plan.Confirms) sb.Append("- " + c + "\r\n");
			}
			return sb.ToString().TrimEnd();
		}

		#endregion

		#region ================== Events

		private void kind_SelectedIndexChanged(object sender, EventArgs e)
		{
			if(!updating) FillOptions();
		}

		private void type_SelectedIndexChanged(object sender, EventArgs e)
		{
			if(updating) return;
			if(Kind == PortalCreateKind.AnchoredSector)
			{
				updating = true;
				FillPlanes();
				updating = false;
			}
			Replan();
		}

		private void option_Changed(object sender, EventArgs e)
		{
			if(!updating) Replan();
		}

		private void swap_Click(object sender, EventArgs e)
		{
			int i = a; a = b; b = i;
			Vector2D p = posa; posa = posb; posb = p;
			string s = problema; problema = problemb; problemb = s;
			Replan();
		}

		private void apply_Click(object sender, EventArgs e)
		{
			if(Plan == null || !Plan.CanApply) return;
			if(Plan.Confirms.Count > 0)
			{
				string text = string.Join("\n\n", Plan.Confirms.ToArray()) + "\n\nCreate the portal anyway?";
				if(MessageBox.Show(this, text, "Create Portal", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
			}
			DialogResult = DialogResult.OK;
			Close();
		}

		private void cancel_Click(object sender, EventArgs e)
		{
			DialogResult = DialogResult.Cancel;
			Close();
		}

		#endregion
	}
}
