using System;
using CodeImp.DoomBuilder.Rendering;

namespace CodeImp.DoomBuilder.Data
{
	internal sealed class ImageColorStats
	{
		public bool HasColor { get; }
		public PixelColor Mean { get; }
		public PixelColor Dominant { get; }
		public double Hue { get; }
		public double Saturation { get; }
		public double Lightness { get; }
		public double LabL { get; }
		public double LabA { get; }
		public double LabB { get; }
		public double DominantLabL { get; }
		public double DominantLabA { get; }
		public double DominantLabB { get; }

		private ImageColorStats() { }

		private ImageColorStats(double r, double g, double b, double dr, double dg, double db)
		{
			HasColor = true;
			Mean = ToPixel(r, g, b);
			Dominant = ToPixel(dr, dg, db);
			r /= 255.0;
			g /= 255.0;
			b /= 255.0;
			double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
			double delta = max - min;
			Lightness = (max + min) / 2.0;
			if(delta > 0)
			{
				Saturation = delta / (1.0 - Math.Abs(2.0 * Lightness - 1.0));
				double hue = max == r ? (g - b) / delta : max == g ? (b - r) / delta + 2.0 : (r - g) / delta + 4.0;
				Hue = (hue * 60.0 + 360.0) % 360.0;
			}
			double l, a, labb;
			ToLab(r, g, b, out l, out a, out labb);
			LabL = l;
			LabA = a;
			LabB = labb;
			ToLab(dr / 255.0, dg / 255.0, db / 255.0, out l, out a, out labb);
			DominantLabL = l;
			DominantLabA = a;
			DominantLabB = labb;
		}

		public static unsafe ImageColorStats Compute(PixelColor[] pixels)
		{
			if(pixels == null) throw new ArgumentNullException(nameof(pixels));
			fixed(PixelColor* data = pixels) return Compute(data, pixels.Length);
		}

		public static unsafe ImageColorStats Compute(PixelColor* pixels, int count)
		{
			if(count < 0) throw new ArgumentOutOfRangeException(nameof(count));
			if(pixels == null && count > 0) throw new ArgumentNullException(nameof(pixels));
			int[] counts = new int[4096];
			long[] reds = new long[4096], greens = new long[4096], blues = new long[4096];
			long red = 0, green = 0, blue = 0;
			int visible = 0;
			for(int i = 0; i < count; i++)
			{
				PixelColor pixel = pixels[i];
				if(pixel.a == 0) continue;
				int bucket = ((pixel.r >> 4) << 8) | ((pixel.g >> 4) << 4) | (pixel.b >> 4);
				counts[bucket]++;
				reds[bucket] += pixel.r;
				greens[bucket] += pixel.g;
				blues[bucket] += pixel.b;
				red += pixel.r;
				green += pixel.g;
				blue += pixel.b;
				visible++;
			}
			if(visible == 0) return new ImageColorStats();
			int dominant = 0;
			// Equal populations choose the lowest RGB bucket, independent of pixel order.
			for(int i = 1; i < counts.Length; i++)
				if(counts[i] > counts[dominant]) dominant = i;
			return new ImageColorStats((double)red / visible, (double)green / visible, (double)blue / visible,
				(double)reds[dominant] / counts[dominant], (double)greens[dominant] / counts[dominant],
				(double)blues[dominant] / counts[dominant]);
		}

		public double DistanceTo(PixelColor swatch)
		{
			if(!HasColor) return double.PositiveInfinity;
			double l, a, b;
			ToLab(swatch.r / 255.0, swatch.g / 255.0, swatch.b / 255.0, out l, out a, out b);
			return Math.Min(Distance(LabL, LabA, LabB, l, a, b), Distance(DominantLabL, DominantLabA, DominantLabB, l, a, b));
		}

		public static double ColorDistance(PixelColor left, PixelColor right)
		{
			double ll, la, lb, rl, ra, rb;
			ToLab(left.r / 255.0, left.g / 255.0, left.b / 255.0, out ll, out la, out lb);
			ToLab(right.r / 255.0, right.g / 255.0, right.b / 255.0, out rl, out ra, out rb);
			return Distance(ll, la, lb, rl, ra, rb);
		}

		public static int CompareHue(ImageColorStats left, ImageColorStats right)
		{
			bool leftColor = left != null && left.HasColor, rightColor = right != null && right.HasColor;
			if(leftColor != rightColor) return leftColor ? -1 : 1;
			if(!leftColor) return 0;
			bool leftGray = left.Saturation < 0.12, rightGray = right.Saturation < 0.12;
			if(leftGray != rightGray) return leftGray ? 1 : -1;
			if(!leftGray)
			{
				int hue = left.Hue.CompareTo(right.Hue);
				if(hue != 0) return hue;
			}
			return left.Lightness.CompareTo(right.Lightness);
		}

		private static PixelColor ToPixel(double r, double g, double b)
		{
			return new PixelColor(255, (byte)Math.Round(r, MidpointRounding.AwayFromZero),
				(byte)Math.Round(g, MidpointRounding.AwayFromZero), (byte)Math.Round(b, MidpointRounding.AwayFromZero));
		}

		private static double Distance(double ll, double la, double lb, double rl, double ra, double rb)
		{
			double l = ll - rl, a = la - ra, b = lb - rb;
			return Math.Sqrt(l * l + a * a + b * b);
		}

		private static void ToLab(double r, double g, double b, out double l, out double a, out double labb)
		{
			r = Linear(r);
			g = Linear(g);
			b = Linear(b);
			double x = LabCurve((0.4124564 * r + 0.3575761 * g + 0.1804375 * b) / 0.95047);
			double y = LabCurve(0.2126729 * r + 0.7151522 * g + 0.0721750 * b);
			double z = LabCurve((0.0193339 * r + 0.1191920 * g + 0.9503041 * b) / 1.08883);
			l = 116.0 * y - 16.0;
			a = 500.0 * (x - y);
			labb = 200.0 * (y - z);
		}

		private static double Linear(double channel)
		{
			return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
		}

		private static double LabCurve(double value)
		{
			return value > 216.0 / 24389.0 ? Math.Pow(value, 1.0 / 3.0) : value * (841.0 / 108.0) + 4.0 / 29.0;
		}
	}
}
