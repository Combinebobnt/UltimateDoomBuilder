using System.Drawing;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Controls;

namespace Builder.Tests
{
	public class ControlTreePainterTests
	{
		private readonly List<Control> invalidated = new List<Control>();
		private readonly List<string> invalidatedrects = new List<string>();
		private readonly List<Control> updated = new List<Control>();
		private readonly ControlTreePainter painter;
		private readonly Panel root = new Panel { Size = new Size(200, 200) };
		private readonly Panel group = new Panel { Bounds = new Rectangle(0, 0, 100, 100) };
		private readonly Label inner = new Label { Bounds = new Rectangle(0, 0, 50, 10) };
		private readonly Label outer = new Label { Bounds = new Rectangle(0, 120, 50, 10) };

		public ControlTreePainterTests()
		{
			painter = new ControlTreePainter(c => invalidated.Add(c), (c, r) => invalidatedrects.Add(Name(c) + " " + r), c => updated.Add(c));
			group.Controls.Add(inner);
			root.Controls.Add(group);
			root.Controls.Add(outer);
		}

		private string Name(Control c)
		{
			return c == root ? "root" : c == group ? "group" : "other";
		}

		private void Pass()
		{
			invalidated.Clear();
			invalidatedrects.Clear();
			updated.Clear();
			painter.Paint(root);
		}

		[Fact]
		public void OnlyNewMovedOrReshownControlsAreInvalidatedButAllVisibleAreUpdated()
		{
			Pass();
			Assert.Equal(new Control[] { root, group, inner, outer }, invalidated.ToArray());

			Pass();
			Assert.Empty(invalidated);
			Assert.Empty(invalidatedrects);
			Assert.Equal(new Control[] { root, group, inner, outer }, updated.ToArray());

			// A hidden subtree is skipped, and comes back invalidated with its children
			group.Visible = false;
			Pass();
			Assert.Equal(new Control[] { root, outer }, updated.ToArray());
			Assert.Empty(invalidated);
			group.Visible = true;
			Pass();
			Assert.Equal(new Control[] { group, inner }, invalidated.ToArray());

			// Hidden and shown again with no pass in between still counts as re-shown
			group.Visible = false;
			group.Visible = true;
			Pass();
			Assert.Equal(new Control[] { group, inner }, invalidated.ToArray());

			// A moved control repaints, and so does the parent area it left
			Rectangle before = outer.Bounds;
			outer.Top += 5;
			Pass();
			Assert.Equal(new Control[] { outer }, invalidated.ToArray());
			Assert.Equal(new[] { "root " + before }, invalidatedrects.ToArray());

			painter.Reset();
			Pass();
			Assert.Equal(4, invalidated.Count);
		}
	}
}
