using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Controls;

namespace Builder.Tests
{
	public class ParentDrawnBorderTests
	{
		private static readonly Color Background = Color.FromArgb(255, 0, 255);

		private static Panel Bordered(BorderStyle style, Rectangle bounds, Control parent)
		{
			Panel p = new Panel { BorderStyle = style, Bounds = bounds };
			parent.Controls.Add(p);
			return p;
		}

		private static Bitmap PaintParent(Control parent)
		{
			Bitmap bmp = new Bitmap(parent.Width, parent.Height);
			using(Graphics g = Graphics.FromImage(bmp))
			{
				g.Clear(Background);
				MethodInfo onpaint = typeof(Control).GetMethod("OnPaint", BindingFlags.NonPublic | BindingFlags.Instance);
				onpaint.Invoke(parent, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, parent.Width, parent.Height)) });
			}
			return bmp;
		}

		[Fact]
		public void Fixed3DShrinksToItsClientRectangleAndKeepsChildren()
		{
			Panel parent = new Panel { Size = new Size(100, 100) };
			Panel box = Bordered(BorderStyle.Fixed3D, new Rectangle(3, 0, 64, 64), parent);
			Label child = new Label { Location = new Point(1, 1), AutoSize = false, Size = new Size(20, 10) };
			box.Controls.Add(child);

			ParentDrawnBorder.Apply(box);

			Assert.Equal(BorderStyle.None, box.BorderStyle);
			Assert.Equal(new Rectangle(5, 2, 60, 60), box.Bounds);
			Assert.Equal(new Rectangle(3, 0, 64, 64), ParentDrawnBorder.OuterBounds(box));
			Assert.Equal(new Point(1, 1), child.Location);
		}

		[Fact]
		public void FixedSingleIsOnePixelWide()
		{
			Panel parent = new Panel { Size = new Size(100, 100) };
			Panel swatch = Bordered(BorderStyle.FixedSingle, new Rectangle(10, 20, 20, 12), parent);

			ParentDrawnBorder.Apply(swatch);

			Assert.Equal(new Rectangle(11, 21, 18, 10), swatch.Bounds);
			Assert.Equal(new Rectangle(10, 20, 20, 12), ParentDrawnBorder.OuterBounds(swatch));
		}

		[Fact]
		public void DockedBorderlessAndParentlessControlsAreLeftAlone()
		{
			Panel parent = new Panel { Size = new Size(100, 100) };
			Panel docked = Bordered(BorderStyle.Fixed3D, new Rectangle(0, 0, 64, 64), parent);
			docked.Dock = DockStyle.Fill;
			Panel plain = Bordered(BorderStyle.None, new Rectangle(4, 4, 30, 30), parent);
			Panel orphan = new Panel { BorderStyle = BorderStyle.Fixed3D, Bounds = new Rectangle(0, 0, 64, 64) };
			Rectangle dockedbounds = docked.Bounds;

			ParentDrawnBorder.Apply(docked, plain, orphan);

			Assert.Equal(BorderStyle.Fixed3D, docked.BorderStyle);
			Assert.Equal(dockedbounds, docked.Bounds);
			Assert.Equal(docked.Bounds, ParentDrawnBorder.OuterBounds(docked));
			Assert.Equal(new Rectangle(4, 4, 30, 30), plain.Bounds);
			Assert.Equal(plain.Bounds, ParentDrawnBorder.OuterBounds(plain));
			Assert.Equal(BorderStyle.Fixed3D, orphan.BorderStyle);
			Assert.False(ParentDrawnBorder.IsConverted(orphan));
		}

		[Fact]
		public void SecondApplyChangesNothing()
		{
			Panel parent = new Panel { Size = new Size(100, 100) };
			Panel box = Bordered(BorderStyle.Fixed3D, new Rectangle(3, 0, 64, 64), parent);

			ParentDrawnBorder.Apply(box);
			ParentDrawnBorder.Apply(box);

			Assert.Equal(new Rectangle(5, 2, 60, 60), box.Bounds);
			Assert.Equal(new Rectangle(3, 0, 64, 64), ParentDrawnBorder.OuterBounds(box));
		}

		[Fact]
		public void ParentPaintsTheRingOnlyAroundVisibleChildren()
		{
			Panel parent = new Panel { Size = new Size(100, 100) };
			Panel shown = Bordered(BorderStyle.Fixed3D, new Rectangle(3, 0, 64, 64), parent);
			Panel hidden = Bordered(BorderStyle.FixedSingle, new Rectangle(70, 70, 20, 12), parent);
			ParentDrawnBorder.Apply(shown, hidden);
			hidden.Visible = false;

			using(Bitmap bmp = PaintParent(parent))
			{
				Assert.NotEqual(Background.ToArgb(), bmp.GetPixel(3, 0).ToArgb());
				Assert.NotEqual(Background.ToArgb(), bmp.GetPixel(4, 1).ToArgb());
				Assert.NotEqual(Background.ToArgb(), bmp.GetPixel(66, 63).ToArgb());
				Assert.Equal(Background.ToArgb(), bmp.GetPixel(2, 0).ToArgb());
				Assert.Equal(Background.ToArgb(), bmp.GetPixel(67, 30).ToArgb());
				Assert.Equal(Background.ToArgb(), bmp.GetPixel(70, 70).ToArgb());
			}
		}
	}
}
