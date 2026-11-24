using System.Drawing;
using System.Drawing.Imaging;
using CodeImp.DoomBuilder.Controls;
using Xunit;

namespace Builder.Tests
{
	public class MixedCheckGlyphTests
	{
		// A 19px tall control puts the 13x13 box at rows 3..15, so its center is (6, 9)
		private const int HEIGHT = 19;
		private static readonly Color sentinel = Color.FromArgb(255, 255, 0, 255);

		private static Bitmap Render(bool enabled)
		{
			Bitmap bmp = new Bitmap(13, HEIGHT, PixelFormat.Format32bppArgb);
			using(Graphics g = Graphics.FromImage(bmp))
			{
				g.Clear(sentinel);
				MixedCheckGlyph.Draw(g, HEIGHT, enabled);
			}
			return bmp;
		}

		private static void Check(bool enabled, Color mark, Color interior)
		{
			Assert.NotEqual(mark.ToArgb(), interior.ToArgb());
			using(Bitmap bmp = Render(enabled))
			{
				// Mark fills the center 5x5, the interior ring around it erases Mono's check
				Assert.Equal(mark.ToArgb(), bmp.GetPixel(6, 9).ToArgb());
				Assert.Equal(mark.ToArgb(), bmp.GetPixel(4, 7).ToArgb());
				Assert.Equal(mark.ToArgb(), bmp.GetPixel(8, 11).ToArgb());
				Assert.Equal(interior.ToArgb(), bmp.GetPixel(2, 5).ToArgb());
				Assert.Equal(interior.ToArgb(), bmp.GetPixel(10, 13).ToArgb());
				Assert.Equal(interior.ToArgb(), bmp.GetPixel(3, 9).ToArgb());

				// The 3D border and everything outside the box keep Mono's own drawing
				Assert.Equal(sentinel.ToArgb(), bmp.GetPixel(1, 9).ToArgb());
				Assert.Equal(sentinel.ToArgb(), bmp.GetPixel(11, 9).ToArgb());
				Assert.Equal(sentinel.ToArgb(), bmp.GetPixel(6, 4).ToArgb());
				Assert.Equal(sentinel.ToArgb(), bmp.GetPixel(6, 14).ToArgb());
				Assert.Equal(sentinel.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
			}
		}

		[Fact]
		public void EnabledDrawsControlTextSquareOnWindowInterior()
		{
			Check(true, SystemColors.ControlText, SystemColors.Window);
		}

		[Fact]
		public void DisabledDrawsGrayTextSquareOnControlInterior()
		{
			Check(false, SystemColors.GrayText, SystemColors.Control);
		}
	}
}
