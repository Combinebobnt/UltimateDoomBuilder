using System;
using System.Collections.Generic;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Geometry;

namespace CodeImp.DoomBuilder.BuilderModes
{
	// Portal align mode's docker: the offset as numbers, what the move changes, and the mode's buttons
	public class PortalAlignPanel : UserControl
	{
		private readonly Label title;
		private readonly NumericUpDown dx;
		private readonly NumericUpDown dy;
		private readonly Label offset;
		private readonly ListBox results;
		private readonly Button accept;
		private readonly Button cancel;
		private readonly Button makeexact;
		private readonly Button reset;
		private readonly ToolTip tooltip;
		private bool updating;

		public event EventHandler OffsetChanged;
		public event EventHandler AcceptClicked;
		public event EventHandler CancelClicked;
		public event EventHandler MakeExactClicked;
		public event EventHandler ResetClicked;

		public Vector2D Delta { get { return new Vector2D((double)dx.Value, (double)dy.Value); } }

		public PortalAlignPanel()
		{
			tooltip = new ToolTip();
			title = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 64, Padding = new Padding(3) };

			TableLayoutPanel numbers = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, RowCount = 1 };
			dx = MakeNumber();
			dy = MakeNumber();
			numbers.Controls.Add(new Label { Text = "Move X:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
			numbers.Controls.Add(dx, 1, 0);
			numbers.Controls.Add(new Label { Text = "Y:", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 0);
			numbers.Controls.Add(dy, 3, 0);
			tooltip.SetToolTip(dx, "How far the handle moves, in map units");
			tooltip.SetToolTip(dy, "How far the handle moves, in map units");

			offset = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 22, Padding = new Padding(3) };

			FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
			accept = MakeButton(buttons, "Accept", "Apply the move as one undo step (Enter)", delegate { Raise(AcceptClicked); });
			cancel = MakeButton(buttons, "Cancel", "Leave without changing the map (Escape)", delegate { Raise(CancelClicked); });
			reset = MakeButton(buttons, "Reset", "Put the handle back where it started", delegate { Raise(ResetClicked); });
			makeexact = MakeButton(buttons, "Make Exact", "Move the exit line's vertices so the linked pair is exactly reversed and equally long", delegate { Raise(MakeExactClicked); });

			results = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true, IntegralHeight = false, SelectionMode = SelectionMode.None };

			Controls.Add(results);
			Controls.Add(buttons);
			Controls.Add(offset);
			Controls.Add(numbers);
			Controls.Add(title);
			Name = "PortalAlignPanel";
			Size = new System.Drawing.Size(300, 480);
		}

		public void Setup(string text, bool candrag, bool canexact, string exactreason)
		{
			title.Text = text;
			dx.Enabled = dy.Enabled = candrag;
			reset.Enabled = candrag;
			makeexact.Enabled = canexact;
			if(!canexact && !string.IsNullOrEmpty(exactreason)) tooltip.SetToolTip(makeexact, exactreason);
		}

		public void SetDelta(Vector2D delta)
		{
			updating = true;
			dx.Value = Clamp(dx, delta.x);
			dy.Value = Clamp(dy, delta.y);
			updating = false;
		}

		public void ShowResults(string offsettext, List<string> lines, bool canaccept)
		{
			offset.Text = offsettext;
			accept.Enabled = canaccept;
			results.BeginUpdate();
			results.Items.Clear();
			foreach(string line in lines) results.Items.Add(line);
			results.EndUpdate();
		}

		private NumericUpDown MakeNumber()
		{
			NumericUpDown n = new NumericUpDown { Width = 80, Minimum = -65536, Maximum = 65536, DecimalPlaces = 0, Anchor = AnchorStyles.Left };
			n.ValueChanged += delegate { if(!updating) Raise(OffsetChanged); };
			return n;
		}

		private Button MakeButton(FlowLayoutPanel parent, string text, string tip, EventHandler click)
		{
			Button b = new Button { Text = text, AutoSize = true, UseVisualStyleBackColor = true };
			b.Click += click;
			tooltip.SetToolTip(b, tip);
			parent.Controls.Add(b);
			return b;
		}

		private static decimal Clamp(NumericUpDown n, double value)
		{
			decimal v = (decimal)Math.Round(value);
			return Math.Max(n.Minimum, Math.Min(n.Maximum, v));
		}

		private void Raise(EventHandler handler)
		{
			if(handler != null) handler(this, EventArgs.Empty);
		}

		protected override void Dispose(bool disposing)
		{
			if(disposing) tooltip.Dispose();
			base.Dispose(disposing);
		}
	}
}
