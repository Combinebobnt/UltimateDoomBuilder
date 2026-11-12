using System;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.Rendering;
using Xunit;

namespace Builder.Tests
{
	public class ImageColorStatsTests
	{
		[Fact]
		public void AlphaZeroPixelsDoNotAffectMeanDominantHslOrLab()
		{
			ImageColorStats actual = ImageColorStats.Compute(new[]
			{
				new PixelColor(0, 0, 0, 255), new PixelColor(1, 240, 8, 12),
				new PixelColor(0, 0, 0, 255), new PixelColor(0, 255, 255, 255)
			});
			ImageColorStats expected = ImageColorStats.Compute(new[] { new PixelColor(255, 240, 8, 12) });
			Assert.True(actual.HasColor);
			Assert.Equal(expected.Mean, actual.Mean);
			Assert.Equal(expected.Dominant, actual.Dominant);
			Assert.Equal(expected.Hue, actual.Hue);
			Assert.Equal(expected.Saturation, actual.Saturation);
			Assert.Equal(expected.Lightness, actual.Lightness);
			Assert.Equal(expected.LabL, actual.LabL);
			Assert.Equal(expected.LabA, actual.LabA);
			Assert.Equal(expected.LabB, actual.LabB);
		}

		[Fact]
		public void PartialAlphaCountsEquallyRatherThanWeightingOrPremultiplying()
		{
			ImageColorStats stats = ImageColorStats.Compute(new[] { new PixelColor(1, 255, 0, 0), new PixelColor(255, 0, 0, 255) });
			Assert.Equal(new PixelColor(255, 128, 0, 128), stats.Mean);
			Near(300, stats.Hue, 0.0001);
			Near(0.25, stats.Lightness, 0.0001);
		}

		[Fact]
		public void TransparentAndEmptyInputsAreCompletedWithoutColor()
		{
			foreach(PixelColor[] pixels in new[] { new PixelColor[0], new[] { PixelColor.Transparent, new PixelColor(0, 255, 128, 12) } })
			{
				ImageColorStats stats = ImageColorStats.Compute(pixels);
				Assert.NotNull(stats);
				Assert.False(stats.HasColor);
				Assert.Equal(PixelColor.Transparent, stats.Mean);
				Assert.Equal(PixelColor.Transparent, stats.Dominant);
				Assert.True(double.IsPositiveInfinity(stats.DistanceTo(new PixelColor(255, 0, 0, 0))));
			}
		}

		[Fact]
		public void TwoColorsHaveExpectedMeanAndDominantBucket()
		{
			ImageColorStats stats = ImageColorStats.Compute(new[]
			{
				new PixelColor(255, 240, 8, 12), new PixelColor(255, 248, 12, 6), new PixelColor(255, 0, 0, 255)
			});
			Assert.Equal(new PixelColor(255, 163, 7, 91), stats.Mean);
			Assert.Equal(new PixelColor(255, 244, 10, 9), stats.Dominant);
		}

		[Fact]
		public void BucketsUseFourBitsPerChannelAndReturnTheirActualMean()
		{
			ImageColorStats stats = ImageColorStats.Compute(new[]
			{
				new PixelColor(255, 16, 32, 48), new PixelColor(255, 31, 47, 63),
				new PixelColor(255, 15, 32, 48), new PixelColor(255, 16, 31, 48), new PixelColor(255, 16, 32, 47)
			});
			Assert.Equal(new PixelColor(255, 24, 40, 56), stats.Dominant);
		}

		[Fact]
		public void DominantTieIsDeterministicAcrossInputOrder()
		{
			PixelColor red = new PixelColor(255, 255, 0, 0), blue = new PixelColor(255, 0, 0, 255);
			Assert.Equal(blue, ImageColorStats.Compute(new[] { red, blue }).Dominant);
			Assert.Equal(blue, ImageColorStats.Compute(new[] { blue, red }).Dominant);
		}

		[Theory]
		[InlineData(255, 0, 0, 0, 1, 0.5)]
		[InlineData(255, 255, 0, 60, 1, 0.5)]
		[InlineData(0, 255, 0, 120, 1, 0.5)]
		[InlineData(0, 255, 255, 180, 1, 0.5)]
		[InlineData(0, 0, 255, 240, 1, 0.5)]
		[InlineData(255, 0, 255, 300, 1, 0.5)]
		[InlineData(0, 0, 0, 0, 0, 0)]
		[InlineData(255, 255, 255, 0, 0, 1)]
		public void HslMatchesKnownColors(int r, int g, int b, double hue, double saturation, double lightness)
		{
			ImageColorStats stats = Solid(r, g, b);
			Near(hue, stats.Hue, 0.0001);
			Near(saturation, stats.Saturation, 0.0001);
			Near(lightness, stats.Lightness, 0.0001);
		}

		[Theory]
		[InlineData(0, 0, 0, 0, 0, 0)]
		[InlineData(255, 255, 255, 100, 0, 0)]
		[InlineData(255, 0, 0, 53.2408, 80.0925, 67.2032)]
		[InlineData(0, 255, 0, 87.7347, -86.1827, 83.1793)]
		[InlineData(0, 0, 255, 32.2970, 79.1875, -107.8602)]
		[InlineData(128, 128, 128, 53.5850, 0, 0)]
		[InlineData(10, 10, 10, 2.74175, 0, 0)]
		public void LabUsesSrgbD65IncludingLinearLowIntensityBranch(int r, int g, int b, double l, double a, double labb)
		{
			ImageColorStats stats = Solid(r, g, b);
			Near(l, stats.LabL, 0.002);
			Near(a, stats.LabA, 0.002);
			Near(labb, stats.LabB, 0.002);
			Near(stats.LabL, stats.DominantLabL, 0.0001);
			Near(stats.LabA, stats.DominantLabA, 0.0001);
			Near(stats.LabB, stats.DominantLabB, 0.0001);
		}

		[Fact]
		public void DeltaE76MatchesKnownPairsAndIsSymmetric()
		{
			PixelColor black = new PixelColor(255, 0, 0, 0), white = new PixelColor(255, 255, 255, 255);
			PixelColor red = new PixelColor(255, 255, 0, 0), green = new PixelColor(255, 0, 255, 0);
			Near(100, ImageColorStats.ColorDistance(black, white), 0.001);
			Near(170.565, ImageColorStats.ColorDistance(red, green), 0.002);
			Assert.Equal(ImageColorStats.ColorDistance(red, green), ImageColorStats.ColorDistance(green, red));
			Assert.Equal(0.0, ImageColorStats.ColorDistance(red, red));
			Assert.Equal(0.0, ImageColorStats.ColorDistance(red, new PixelColor(red, 0)));
		}

		[Fact]
		public void DistanceUsesDominantWhenTheMeanIsMuddy()
		{
			PixelColor red = new PixelColor(255, 255, 0, 0), blue = new PixelColor(255, 0, 0, 255);
			ImageColorStats stats = ImageColorStats.Compute(new[] { red, red, blue });
			Near(0, stats.DistanceTo(red), 0.0001);
			Assert.True(ImageColorStats.ColorDistance(stats.Mean, red) > 15);
		}

		[Fact]
		public void DistanceUsesMeanWhenItIsCloserThanDominant()
		{
			PixelColor black = new PixelColor(255, 0, 0, 0), white = new PixelColor(255, 255, 255, 255);
			ImageColorStats stats = ImageColorStats.Compute(new[] { black, white });
			Assert.True(stats.DistanceTo(stats.Mean) < 0.3);
			Assert.True(ImageColorStats.ColorDistance(stats.Dominant, stats.Mean) > 50);
		}

		[Fact]
		public void HueSortPlacesChromaticColorsThenGrayLightnessThenNoColor()
		{
			ImageColorStats red = Solid(255, 0, 0), green = Solid(0, 255, 0), blue = Solid(0, 0, 255);
			ImageColorStats black = Solid(0, 0, 0), gray = Solid(128, 128, 128), white = Solid(255, 255, 255);
			ImageColorStats empty = ImageColorStats.Compute(new PixelColor[0]);
			ImageColorStats[] ordered = { white, blue, empty, gray, green, black, red };
			Array.Sort(ordered, ImageColorStats.CompareHue);
			Assert.Equal(new[] { red, green, blue, black, gray, white, empty }, ordered);
			Assert.True(ImageColorStats.CompareHue(white, null) < 0);
			Assert.True(ImageColorStats.CompareHue(null, red) > 0);
			Assert.Equal(0, ImageColorStats.CompareHue(empty, null));
			Assert.Equal(0, ImageColorStats.CompareHue(null, null));
		}

		[Fact]
		public void HueSortNearGrayThresholdAndLightnessTiesAreExplicit()
		{
			ImageColorStats nearGray = Solid(140, 128, 128), chromatic = Solid(150, 120, 120);
			Assert.True(nearGray.Saturation < 0.12);
			Assert.True(chromatic.Saturation >= 0.12);
			Assert.True(ImageColorStats.CompareHue(Solid(0, 0, 255), nearGray) < 0);
			Assert.True(ImageColorStats.CompareHue(chromatic, nearGray) < 0);
			Assert.True(ImageColorStats.CompareHue(Solid(128, 0, 0), Solid(255, 0, 0)) < 0);
			Assert.True(ImageColorStats.CompareHue(Solid(100, 100, 110), nearGray) < 0);
			Assert.Equal(0, ImageColorStats.CompareHue(chromatic, Solid(150, 120, 120)));
		}

		[Fact]
		public unsafe void PointerAndArrayOverloadsAgreeAndHonorCount()
		{
			PixelColor[] pixels = { new PixelColor(255, 255, 0, 0), new PixelColor(0, 0, 255, 0), new PixelColor(255, 0, 0, 255) };
			fixed(PixelColor* data = pixels)
			{
				ImageColorStats pointer = ImageColorStats.Compute(data, pixels.Length), array = ImageColorStats.Compute(pixels);
				Assert.Equal(array.Mean, pointer.Mean);
				Assert.Equal(array.Dominant, pointer.Dominant);
				Assert.Equal(array.LabL, pointer.LabL);
				Assert.Equal(new PixelColor(255, 255, 0, 0), ImageColorStats.Compute(data, 2).Mean);
			}
			Assert.False(ImageColorStats.Compute((PixelColor*)null, 0).HasColor);
			Assert.Throws<ArgumentNullException>(() => ImageColorStats.Compute((PixelColor[])null));
			Assert.Throws<ArgumentNullException>(() => ImageColorStats.Compute((PixelColor*)null, 1));
			Assert.Throws<ArgumentOutOfRangeException>(() => ImageColorStats.Compute((PixelColor*)null, -1));
		}

		[Fact]
		public void LargePreviewSumsDoNotOverflow()
		{
			PixelColor[] pixels = new PixelColor[65536];
			for(int i = 0; i < pixels.Length; i++) pixels[i] = new PixelColor(255, 255, 255, 255);
			ImageColorStats stats = ImageColorStats.Compute(pixels);
			Assert.Equal(new PixelColor(255, 255, 255, 255), stats.Mean);
			Assert.Equal(stats.Mean, stats.Dominant);
			Near(100, stats.LabL, 0.001);
		}

		private static ImageColorStats Solid(int r, int g, int b)
		{
			return ImageColorStats.Compute(new[] { new PixelColor(255, (byte)r, (byte)g, (byte)b) });
		}

		private static void Near(double expected, double actual, double tolerance)
		{
			Assert.True(Math.Abs(expected - actual) <= tolerance, "Expected " + expected + ", actual " + actual);
		}
	}
}
