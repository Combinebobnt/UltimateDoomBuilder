using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Controls;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.Rendering;

namespace Builder.Tests
{
	[Collection("GeneralStatics")]
	public class ImageBrowserPipelineTests
	{
		private sealed class TestImage : ImageData
		{
			internal TestImage(string text, int w = 0, int h = 0)
			{
				name = shortname = text;
				width = w;
				height = h;
				PreviewState = ImageLoadState.Loading;
			}
			protected override LocalLoadResult LocalLoadImage() { throw new InvalidOperationException("Unexpected synchronous decode"); }
			internal void SetPreview(Bitmap bitmap)
			{
				typeof(ImageData).GetField("previewbitmap", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(this, bitmap);
				PreviewState = ImageLoadState.Ready;
			}
			internal void SetAlpha(bool known, bool masked, bool translucent)
			{
				typeof(ImageData).GetField("transparencyknown", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(this, known);
				ismasked = masked;
				istranslucent = translucent;
			}
		}

		private static ImageBrowserItem Item(string name, int w = 0, int h = 0)
		{
			return new ImageBrowserItem(new TestImage(name, w, h), "", false);
		}

		[Fact]
		public void SizeSortUsesAreaThenWidthThenNameAndKeepsUnknownLast()
		{
			var items = new List<ImageBrowserItem> { Item("UNKNOWN"), Item("Z", 64, 16), Item("B", 32, 32), Item("A", 32, 32), Item("SMALL", 8, 8) };
			items.Sort((a, b) => ImageBrowserControl.CompareItems(a, b, TextureBrowserSortMode.Size, null, null));
			Assert.Equal(new[] { "SMALL", "A", "B", "Z", "UNKNOWN" }, items.Select(i => i.TextureName).ToArray());
		}

		[Fact]
		public void MatchSortUsesDescendingScoreWithExistingNameTieBreak()
		{
			var items = new List<ImageBrowserItem> { Item("B"), Item("A"), Item("TOP") };
			var scores = new Dictionary<ImageBrowserItem, int> { [items[0]] = 1, [items[1]] = 1, [items[2]] = 99 };
			items.Sort((a, b) => ImageBrowserControl.CompareItems(a, b, TextureBrowserSortMode.Match, scores, null));
			Assert.Equal(new[] { "TOP", "A", "B" }, items.Select(i => i.TextureName).ToArray());
		}

		[Fact]
		public void ColorRequestDoesNotDecodeWhilePreviewLoadsAndCachedStatsSurviveStateChanges()
		{
			using(var image = new TestImage("TEST"))
			{
				Assert.False(image.UpdateColorStats());
				Assert.Null(image.ColorStats);
				var bitmap = new Bitmap(2, 1, PixelFormat.Format32bppArgb);
				bitmap.SetPixel(0, 0, Color.Red);
				bitmap.SetPixel(1, 0, Color.Transparent);
				image.SetPreview(bitmap);
				Assert.True(image.UpdateColorStats());
				Assert.Equal((byte)255, image.ColorStats.Mean.r);
				Assert.Equal((byte)0, image.ColorStats.Mean.g);
				ImageColorStats stats = image.ColorStats;
				image.PreviewState = ImageLoadState.Loading;
				Assert.True(image.UpdateColorStats());
				Assert.Same(stats, image.ColorStats);
			}
		}

		[Fact]
		public void FailedPreviewDoesNotAcquirePlaceholderColor()
		{
			using(var image = new TestImage("FAILED"))
			{
				var bitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
				bitmap.SetPixel(0, 0, Color.Red);
				image.SetPreview(bitmap);
				typeof(ImageData).GetField("previewfailed", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(image, true);
				Assert.True(image.UpdateColorStats());
				Assert.False(image.ColorStats.HasColor);
			}
		}

		[Theory]
		[InlineData(0, 0, 64, 64, true)]
		[InlineData(64, 32, 64, 32, true)]
		[InlineData(64, 32, 64, -1, true)]
		[InlineData(64, 32, -1, 32, true)]
		[InlineData(64, 32, 32, 32, false)]
		[InlineData(64, 32, 64, 64, false)]
		public void ExactSizeFilteringUsesOnlyKnownMetadata(int width, int height, int wantedwidth, int wantedheight, bool passes)
		{
			ImageBrowserItem item = Item("SIZE", width, height);
			item.Icon.PreviewState = ImageLoadState.None;
			// No map exists for QueueLoadPreview: any preview access would fail the test.
			Assert.Equal(passes, ImageBrowserControl.ValidateItemSize(item, wantedwidth, wantedheight));
			Assert.Equal(ImageLoadState.None, item.Icon.PreviewState);
		}

		[Fact]
		public void ColorAndTransparencyReadinessDoNotFollowTransientPreviewState()
		{
			using(var image = new TestImage("READY", 64, 64))
			{
				Assert.False(ImageBrowserControl.IsPending(image, false, false, false));
				Assert.False(ImageBrowserControl.IsPending(image, false, false, true));
				Assert.True(ImageBrowserControl.IsPending(image, true, false, false));
				image.SetAlpha(false, true, false);
				Assert.True(ImageBrowserControl.IsPending(image, false, true, false));
				image.SetAlpha(true, true, false);
				Assert.False(ImageBrowserControl.IsPending(image, false, true, false));
				image.SetPreview(new Bitmap(1, 1, PixelFormat.Format32bppArgb));
				image.UpdateColorStats();
				image.PreviewState = ImageLoadState.Loading;
				Assert.False(ImageBrowserControl.IsPending(image, true, true, true));
			}
		}

		[Fact]
		public void BudgetRejectionIsAnErrorNotAnEmptyMatchOrFallback()
		{
			Stopwatch elapsed = Stopwatch.StartNew();
			Thread.Sleep(260);
			int score;
			string error;
			Assert.False(ImageBrowserControl.MatchWithinBudget(new TextureNameMatcher(TextureNameMatchMode.Regex, ".*"), "ANY", true, elapsed, out score, out error));
			Assert.Contains("250 ms", error);
			Assert.True(ImageBrowserControl.MatchWithinBudget(new TextureNameMatcher(TextureNameMatchMode.Name, "ANY"), "ANY", false, elapsed, out score, out error));
			Assert.Null(error);
		}

		[Fact]
		public void InvalidRegexAndPerMatchTimeoutPropagateWithoutFallback()
		{
			int score;
			string error;
			Assert.False(ImageBrowserControl.MatchWithinBudget(new TextureNameMatcher(TextureNameMatchMode.Regex, "["), "[", true, Stopwatch.StartNew(), out score, out error));
			Assert.NotNull(error);
			var slow = new TextureNameMatcher(TextureNameMatchMode.Regex, "(a+)+$");
			Assert.False(ImageBrowserControl.MatchWithinBudget(slow, new string('a', 40) + "b", true, Stopwatch.StartNew(), out score, out error));
			Assert.True(slow.TimedOut);
			Assert.NotNull(error);
		}

		[Theory]
		[InlineData(0, 15)]
		[InlineData(1, 25)]
		[InlineData(2, 40)]
		public void ToleranceComboUsesDeltaEThresholds(int index, int expected)
		{
			Assert.Equal((double)expected, ImageBrowserControl.ColorTolerance(index));
		}

		[Fact]
		public void AttributeFiltersKeepUnknownsVisibleAndUseInclusiveDeltaEBoundary()
		{
			using(var image = new TestImage("ATTRIBUTES"))
			{
				Assert.True(ImageBrowserControl.ValidateAttributes(image, true, true, double.PositiveInfinity, 25));
				image.SetAlpha(false, true, false);
				Assert.True(ImageBrowserControl.ValidateAttributes(image, true, false, 0, 25));
				image.SetAlpha(true, false, false);
				Assert.False(ImageBrowserControl.ValidateAttributes(image, true, false, 0, 25));
				image.SetAlpha(true, false, true);
				Assert.True(ImageBrowserControl.ValidateAttributes(image, true, false, 0, 25));
				image.SetPreview(Solid(Color.Red));
				image.UpdateColorStats();
				Assert.True(ImageBrowserControl.ValidateAttributes(image, true, true, 25, 25));
				Assert.False(ImageBrowserControl.ValidateAttributes(image, true, true, 25.001, 25));
			}
		}

		[Fact]
		public void SourceResetInvalidatesPendingVersionAndClearsDelayedRefill()
		{
			var browser = Empty<ImageBrowserControl>();
			const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
			Type type = typeof(ImageBrowserControl);
			using(var timer = new System.Windows.Forms.Timer())
			{
				type.GetField("statstimer", flags).SetValue(browser, timer);
				type.GetField("pendingstats", flags).SetValue(browser, new Queue<ImageData>(new[] { new TestImage("OLD") }));
				type.GetField("sourceversion", flags).SetValue(browser, 42);
				type.GetField("pendingversion", flags).SetValue(browser, 42);
				type.GetField("statsdirty", flags).SetValue(browser, true);
				type.GetMethod("ResetPendingWork", flags).Invoke(browser, null);
				Assert.Equal(43, (int)type.GetField("sourceversion", flags).GetValue(browser));
				Assert.Equal(43, (int)type.GetField("pendingversion", flags).GetValue(browser));
				Assert.Empty((Queue<ImageData>)type.GetField("pendingstats", flags).GetValue(browser));
				Assert.False((bool)type.GetField("statsdirty", flags).GetValue(browser));
				Assert.False(timer.Enabled);
			}
		}

		[Fact]
		public void PendingColorSortsRemainNameOrderedBehindKnownColors()
		{
			ImageBrowserItem red = Item("RED"), unknownZ = Item("Z"), unknownA = Item("A");
			((TestImage)red.Icon).SetPreview(Solid(Color.Red));
			red.Icon.UpdateColorStats();
			foreach(TextureBrowserSortMode mode in new[] { TextureBrowserSortMode.Hue, TextureBrowserSortMode.Brightness })
			{
				var items = new List<ImageBrowserItem> { unknownZ, red, unknownA };
				items.Sort((a, b) => ImageBrowserControl.CompareItems(a, b, mode, null, null));
				Assert.Equal(new[] { "RED", "A", "Z" }, items.Select(i => i.TextureName).ToArray());
			}
		}

		[Fact]
		public void HueSortUsesHueWrapAndGrayTailAndSimilarityUsesCachedDistance()
		{
			ImageBrowserItem red = Item("RED"), rose = Item("ROSE"), green = Item("GREEN"), gray = Item("GRAY"), unknown = Item("UNKNOWN");
			foreach(var pair in new[] { Tuple.Create(red, Color.Red), Tuple.Create(rose, Color.FromArgb(255, 0, 30)),
				Tuple.Create(green, Color.Lime), Tuple.Create(gray, Color.Gray) })
			{
				((TestImage)pair.Item1.Icon).SetPreview(Solid(pair.Item2));
				pair.Item1.Icon.UpdateColorStats();
			}
			var items = new List<ImageBrowserItem> { unknown, gray, rose, green, red };
			items.Sort((a, b) => ImageBrowserControl.CompareItems(a, b, TextureBrowserSortMode.Hue, null, null));
			Assert.Equal(new[] { "RED", "GREEN", "ROSE", "GRAY", "UNKNOWN" }, items.Select(i => i.TextureName).ToArray());
			var distances = items.ToDictionary(item => item, item => item.Icon.ColorStats == null ? double.PositiveInfinity
				: item.Icon.ColorStats.DistanceTo(PixelColor.FromColor(Color.Red)));
			items.Sort((a, b) => ImageBrowserControl.CompareItems(a, b, TextureBrowserSortMode.SimilarColor, null, distances));
			Assert.Same(red, items.First());
			Assert.Same(unknown, items.Last());
		}

		private static Bitmap Solid(Color color)
		{
			Bitmap bitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
			bitmap.SetPixel(0, 0, color);
			return bitmap;
		}

		private static T Empty<T>()
		{
			T result = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
			GC.SuppressFinalize(result);
			return result;
		}

		private sealed class SilentScrollBar : VScrollBar
		{
			protected override void SetVisibleCore(bool value) { }
		}

		private static void SetPanel(ImageSelectorPanel panel, string field, object value)
		{
			 typeof(ImageSelectorPanel).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(panel, value);
		}

		[Fact]
		public void ScrollAnchorPreservesOccurrenceAndPixelOffsetAfterReorder()
		{
			var panel = Empty<ImageSelectorPanel>();
			var scroll = Empty<VScrollBar>();
			scroll.Maximum = 2000;
			scroll.Value = 180;
			ImageBrowserItem first = Item("FIRST"), repeated = Item("REPEATED");
			SetPanel(panel, "scrollbar", scroll);
			SetPanel(panel, "items", new List<ImageBrowserItem> { repeated, first, repeated });
			SetPanel(panel, "itemgroups", new List<ImageBrowserItemGroup> { new ImageBrowserItemGroup("All textures")
				{ Items = new List<ImageBrowserItem> { repeated, first, repeated } } });
			SetPanel(panel, "rectangles", new List<Rectangle> { new Rectangle(2, 20, 50, 50), new Rectangle(2, 90, 50, 50), new Rectangle(2, 200, 50, 50) });
			var anchor = panel.CaptureScrollAnchor();
			Assert.Same(repeated, anchor.Item);
			Assert.Equal(1, anchor.Occurrence);
			Assert.Equal(20, anchor.Offset);
			SetPanel(panel, "rectangles", new List<Rectangle> { new Rectangle(2, 30, 50, 50), new Rectangle(2, 100, 50, 50), new Rectangle(2, 400, 50, 50) });
			panel.RestoreScrollAnchor(anchor);
			Assert.Equal(380, scroll.Value);
		}

		[Theory]
		[InlineData("All textures", true)]
		[InlineData("Used textures", true)]
		[InlineData("All textures", false)]
		[InlineData("Used textures", false)]
		public void QuietRefillKeepsLogicalGroupAnchorWhenFavoritesOccurrenceIsInsertedOrRemoved(string title, bool insert)
		{
			FieldInfo settings = typeof(General).GetField("settings", BindingFlags.NonPublic | BindingFlags.Static);
			object previous = settings.GetValue(null);
			settings.SetValue(null, new ProgramConfiguration());
			try
			{
				var panel = Empty<ImageSelectorPanel>();
				var scroll = Empty<SilentScrollBar>();
				ImageBrowserItem repeated = Item("A", 64, 64), tail = Item("TAIL", 64, 64);
				SetPanel(panel, "scrollbar", scroll);
				SetPanel(panel, "items", new List<ImageBrowserItem>());
				SetPanel(panel, "selection", new List<ImageBrowserItem>());
				SetPanel(panel, "rectangles", new List<Rectangle>());
				SetPanel(panel, "itemgroups", new List<ImageBrowserItemGroup>());
				SetPanel(panel, "classicview", true);
				SetPanel(panel, "imagesize", 64);
				Func<bool, List<ImageBrowserItemGroup>> groups = pinned =>
				{
					var result = new List<ImageBrowserItemGroup>();
					if(pinned) result.Add(new ImageBrowserItemGroup("Favorites") { Items = new List<ImageBrowserItem> { repeated } });
					result.Add(new ImageBrowserItemGroup("Used textures") { Items = new List<ImageBrowserItem> { repeated } });
					result.Add(new ImageBrowserItemGroup("All textures") { Items = new List<ImageBrowserItem> { repeated, tail } });
					return result;
				};
				panel.SetItems(groups(!insert));
				int before = (!insert ? 1 : 0) + (title == "All textures" ? 1 : 0);
				scroll.Value = panel.GetItemRectangle(before).Top - 7;
				panel.SetItems(groups(insert), true);
				int after = (insert ? 1 : 0) + (title == "All textures" ? 1 : 0);
				Assert.Same(repeated, panel.Items[after]);
				Assert.Equal(7, panel.GetItemRectangle(after).Top);
			}
			finally { settings.SetValue(null, previous); }
		}

		[Theory]
		[InlineData(true)]
		[InlineData(false)]
		public void MissingAnchorGroupOrItemKeepsNumericScrollInsteadOfAnotherGroupOccurrence(bool groupremoved)
		{
			var panel = Empty<ImageSelectorPanel>();
			var scroll = Empty<VScrollBar>();
			scroll.Maximum = 2000;
			scroll.Value = 180;
			ImageBrowserItem repeated = Item("A"), other = Item("B");
			SetPanel(panel, "scrollbar", scroll);
			SetPanel(panel, "items", new List<ImageBrowserItem> { repeated });
			SetPanel(panel, "rectangles", new List<Rectangle> { new Rectangle(2, 200, 50, 50) });
			SetPanel(panel, "itemgroups", new List<ImageBrowserItemGroup> { new ImageBrowserItemGroup("All textures") { Items = new List<ImageBrowserItem> { repeated } } });
			var anchor = panel.CaptureScrollAnchor();
			var groups = new List<ImageBrowserItemGroup> { new ImageBrowserItemGroup("Favorites") { Items = new List<ImageBrowserItem> { repeated } } };
			if(!groupremoved) groups.Add(new ImageBrowserItemGroup("All textures") { Items = new List<ImageBrowserItem> { other } });
			SetPanel(panel, "itemgroups", groups);
			SetPanel(panel, "items", groups.SelectMany(group => group.Items).ToList());
			SetPanel(panel, "rectangles", new List<Rectangle> { new Rectangle(2, 400, 50, 50), new Rectangle(2, 500, 50, 50) }.Take(groupremoved ? 1 : 2).ToList());
			panel.RestoreScrollAnchor(anchor);
			Assert.Equal(180, scroll.Value);
		}

		[Fact]
		public void QuietRefillPreservesSelectionWithoutEventsOrPreviewAccess()
		{
			FieldInfo settings = typeof(General).GetField("settings", BindingFlags.NonPublic | BindingFlags.Static);
			object previous = settings.GetValue(null);
			settings.SetValue(null, new ProgramConfiguration());
			try
			{
				var panel = Empty<ImageSelectorPanel>();
				var scroll = Empty<SilentScrollBar>();
				ImageBrowserItem selected = Item("SELECTED", 64, 64);
				selected.Icon.PreviewState = ImageLoadState.None;
				SetPanel(panel, "scrollbar", scroll);
				SetPanel(panel, "items", new List<ImageBrowserItem> { selected });
				SetPanel(panel, "selection", new List<ImageBrowserItem> { selected });
				SetPanel(panel, "rectangles", new List<Rectangle> { new Rectangle(2, 20, 64, 64) });
				SetPanel(panel, "itemgroups", new List<ImageBrowserItemGroup>());
				int changes = 0;
				panel.SelectionChanged += (sender, selection) => changes++;
				var group = new ImageBrowserItemGroup("All") { Items = new List<ImageBrowserItem> { selected } };
				panel.SetItems(new[] { group }, true);
				Assert.Same(selected, panel.SelectedItems.Single());
				Assert.Equal(0, changes);
				Assert.Equal(ImageLoadState.None, selected.Icon.PreviewState);
				panel.SetItems(new[] { group });
				Assert.Equal(1, changes);
				Assert.Empty(panel.SelectedItems);
			}
			finally { settings.SetValue(null, previous); }
		}
	}
}
