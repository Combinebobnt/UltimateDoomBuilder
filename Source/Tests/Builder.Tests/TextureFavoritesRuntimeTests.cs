using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Controls;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.IO;
using CodeImp.DoomBuilder.Windows;

namespace Builder.Tests
{
	[Collection("GeneralStatics")]
	public class TextureFavoritesRuntimeTests
	{
		private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
		private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;

		private sealed class TestImage : ImageData
		{
			internal TestImage(string value, TextureNamespace kind, int w = 64)
			{
				name = shortname = value;
				longname = Lump.MakeLongName(value);
				texturenamespace = kind;
				width = height = w;
				PreviewState = ImageLoadState.Loading;
			}
			protected override LocalLoadResult LocalLoadImage() { throw new InvalidOperationException("Unexpected decode"); }
		}

		private static T Empty<T>()
		{
			T value = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
			GC.SuppressFinalize(value);
			return value;
		}

		private static void Set(object value, string name, object field) { value.GetType().GetField(name, Instance).SetValue(value, field); }

		[Fact]
		public void MapFavoritesKeepsIdentityAcrossConfigurationKeysAndReload()
		{
			object previous = typeof(General).GetField("settings", Static).GetValue(null);
			var settings = new ProgramConfiguration();
			var configuration = new Configuration();
			Set(settings, "cfg", configuration);
			typeof(General).GetField("settings", Static).SetValue(null, settings);
			try
			{
				var map = Empty<MapManager>();
				var info = Empty<ConfigurationInfo>();
				Set(info, "settingskey", "same-engine-one");
				Set(map, "configinfo", info);
				map.UpdateFavorites();
				TextureFavorites favorites = map.Favorites;
				favorites.Add(TextureHistoryKind.Texture, "FIRST");
				int changes = 0;
				favorites.Changed += (s, e) => changes++;
				Set(info, "settingskey", "same-engine-two");
				map.UpdateFavorites();
				Assert.Same(favorites, map.Favorites);
				Assert.Empty(favorites.Get(TextureHistoryKind.Texture));
				favorites.Add(TextureHistoryKind.Texture, "SECOND");
				map.UpdateFavorites();
				Assert.Same(favorites, map.Favorites);
				Assert.Equal(new[] { "SECOND" }, favorites.Get(TextureHistoryKind.Texture).ToArray());
				Set(info, "settingskey", "same-engine-one");
				map.UpdateFavorites();
				Assert.Equal(new[] { "FIRST" }, favorites.Get(TextureHistoryKind.Texture).ToArray());
				Assert.Equal(4, changes);
			}
			finally { typeof(General).GetField("settings", Static).SetValue(null, previous); }
		}

		[Theory]
		[InlineData(TextureNamespace.TEXTURE, TextureHistoryKind.Texture)]
		[InlineData(TextureNamespace.WALLTEXTURE, TextureHistoryKind.Texture)]
		[InlineData(TextureNamespace.FLAT, TextureHistoryKind.Flat)]
		[InlineData(TextureNamespace.SPRITE, TextureHistoryKind.Texture)]
		[InlineData(TextureNamespace.PATCH, TextureHistoryKind.Texture)]
		public void ImageFavoritesUseActualNamespaceAndToggleSuccessIsNotPinState(TextureNamespace ns, TextureHistoryKind kind)
		{
			var favorites = new TextureFavorites(new Configuration(), "test");
			using(var image = new TestImage("SHARED", ns))
			{
				Assert.True(favorites.Toggle(image));
				Assert.True(favorites.Contains(kind, "SHARED"));
				Assert.False(favorites.Contains(kind == TextureHistoryKind.Flat ? TextureHistoryKind.Texture : TextureHistoryKind.Flat, "SHARED"));
				Assert.True(favorites.Toggle(image));
				Assert.False(favorites.Contains(image));
			}
		}

		[Theory]
		[InlineData(0)]
		[InlineData(24)]
		public void PinnedGroupsPrecedeRecentAndKeepUnresolvedNamesWithoutRecordingThem(int capacity)
		{
			var history = new TextureHistory(capacity, false);
			history.Record("RECENT", TextureHistoryKind.Texture);
			var favorites = new TextureFavorites(new Configuration(), "test");
			favorites.Add(TextureHistoryKind.Texture, "unresolved/wall");
			favorites.Add(TextureHistoryKind.Flat, "unresolved/flat");
			var groups = TextureHistoryView.BuildEntryGroups(history, favorites, true, true);
			Assert.Equal(capacity == 0 ? new[] { "Pinned textures", "Pinned flats" } : new[] { "Pinned textures", "Pinned flats", "Recent textures" }, groups.Select(g => g.Title).ToArray());
			Assert.Equal("unresolved/wall", groups[0].Entries[0].Name);
			Assert.True(groups[0].Entries[0].Pinned);
			Assert.Equal(capacity == 0 ? new string[0] : new[] { "RECENT" }, history.Get(TextureHistoryKind.Texture).ToArray());
			Assert.Empty(history.Get(TextureHistoryKind.Flat));
		}

		[Theory]
		[InlineData(false, true, false, 1)]
		[InlineData(false, false, true, 1)]
		[InlineData(true, true, false, 2)]
		[InlineData(true, false, true, 2)]
		[InlineData(true, false, false, 0)]
		public void MixedTargetFilteringDoesNotMergeSavedFavoriteNamespaces(bool mixed, bool textures, bool flats, int expected)
		{
			var history = new TextureHistory(0, mixed);
			var favorites = new TextureFavorites(new Configuration(), "test");
			favorites.Add(TextureHistoryKind.Texture, "SAME");
			favorites.Add(TextureHistoryKind.Flat, "SAME");
			var groups = TextureHistoryView.BuildEntryGroups(history, favorites, textures, flats);
			Assert.Equal(expected, groups.Count);
			Assert.Equal(1, favorites.Get(TextureHistoryKind.Texture).Count);
			Assert.Equal(1, favorites.Get(TextureHistoryKind.Flat).Count);
			if(expected == 2) Assert.Equal(new[] { TextureHistoryKind.Texture, TextureHistoryKind.Flat }, groups.Select(g => g.Entries.Single().Kind).ToArray());
			if(expected == 1) Assert.Equal(flats ? TextureHistoryKind.Flat : TextureHistoryKind.Texture, groups.Single().Entries.Single().Kind);
		}

		[Theory]
		[InlineData("popup", false)]
		[InlineData("docker", false)]
		[InlineData("popup", true)]
		[InlineData("docker", true)]
		public void AltRightClickTogglesOnlyExplicitPinKindWithoutApplyOrHistoryTouch(string source, bool mixed)
		{
			var history = new TextureHistory(4, mixed);
			history.Record("FIRST", TextureHistoryKind.Texture);
			history.Record("TARGET", TextureHistoryKind.Flat);
			var before = history.Get(TextureHistoryKind.Texture).ToArray();
			var favorites = new TextureFavorites(new Configuration(), "test");
			favorites.Add(TextureHistoryKind.Texture, "TARGET");
			var view = Empty<TextureHistoryView>();
			Set(view, "favorites", favorites);
			Set(view, "history", history);
			view.Source = source;
			view.EntryClicked += (sender, e) => { throw new InvalidOperationException("An apply event escaped the favorite gesture"); };
			var entry = new TextureHistoryView.EntryInfo { Name = "TARGET", Kind = TextureHistoryKind.Flat, Index = 0 };
			view.ActivateEntry(entry, MouseButtons.Right, Keys.Alt);
			Assert.True(favorites.Contains(TextureHistoryKind.Flat, "TARGET"));
			Assert.True(favorites.Contains(TextureHistoryKind.Texture, "TARGET"));
			view.ActivateEntry(entry, MouseButtons.Right, Keys.Alt | Keys.Control);
			Assert.False(favorites.Contains(TextureHistoryKind.Flat, "TARGET"));
			Assert.True(favorites.Contains(TextureHistoryKind.Texture, "TARGET"));
			Assert.Equal(before, history.Get(TextureHistoryKind.Texture).ToArray());
		}

		[Theory]
		[InlineData(TextureHistoryKind.Texture)]
		[InlineData(TextureHistoryKind.Flat)]
		public void OrdinaryRightClickStillRaisesAnUnmodifiedPick(TextureHistoryKind kind)
		{
			var view = Empty<TextureHistoryView>();
			TextureHistoryClickEventArgs picked = null;
			view.EntryClicked += (sender, e) => picked = e;
			view.ActivateEntry(new TextureHistoryView.EntryInfo { Name = "TARGET", Kind = kind, Index = 3, Pinned = true }, MouseButtons.Right, Keys.None);
			Assert.NotNull(picked);
			Assert.Equal(MouseButtons.Right, picked.Button);
			Assert.Equal(kind, picked.Kind);
			Assert.Equal(3, picked.Index);
			Assert.True(picked.IsPinned);
		}

		[Fact]
		public void RemoveRecentUsesNameAndKindNotThePinnedGroupsIndexAndNeverUnpins()
		{
			var history = new TextureHistory(4, false);
			history.Record("SAME", TextureHistoryKind.Texture);
			history.Record("OTHER", TextureHistoryKind.Flat);
			history.Record("SAME", TextureHistoryKind.Flat);
			var favorites = new TextureFavorites(new Configuration(), "test");
			favorites.Add(TextureHistoryKind.Flat, "SAME");
			var view = Empty<TextureHistoryView>();
			Set(view, "history", history);
			Set(view, "favorites", favorites);
			view.PruneRecent("SAME", TextureHistoryKind.Flat);
			Assert.Equal(new[] { "SAME" }, history.Get(TextureHistoryKind.Texture).ToArray());
			Assert.Equal(new[] { "OTHER" }, history.Get(TextureHistoryKind.Flat).ToArray());
			Assert.True(favorites.Contains(TextureHistoryKind.Flat, "SAME"));
			history.Clear();
			Assert.True(favorites.Contains(TextureHistoryKind.Flat, "SAME"));
		}

		[Fact]
		public void FavoritesOnlyIsAndedWithNameAndExactSizeFilters()
		{
			var favorites = new TextureFavorites(new Configuration(), "test");
			favorites.Add(TextureHistoryKind.Texture, "ROCK");
			favorites.Add(TextureHistoryKind.Texture, "WOOD");
			var images = new[] { new TestImage("ROCK", TextureNamespace.TEXTURE), new TestImage("ROCK2", TextureNamespace.TEXTURE),
				new TestImage("WOOD", TextureNamespace.TEXTURE), new TestImage("ROCK", TextureNamespace.TEXTURE, 32), new TestImage("ROCK", TextureNamespace.FLAT) };
			var matcher = new TextureNameMatcher(TextureNameMatchMode.Name, "ROCK");
			var matches = images.Where(image =>
			{
				int score;
				return matcher.Match(image.Name, out score) && ImageBrowserControl.ValidateItemSize(new ImageBrowserItem(image, "", false), 64, 64)
					&& ImageBrowserControl.ValidateFavoriteOnly(image, favorites, true);
			}).ToArray();
			Assert.Same(images[0], matches.Single());
			Assert.True(ImageBrowserControl.ValidateFavoriteOnly(images[1], null, false));
			Assert.False(ImageBrowserControl.ValidateFavoriteOnly(images[1], null, true));
		}

		private static ImageBrowserControl FavoritesOnlyBrowser(out ImageSelectorPanel panel, out ImageBrowserItem red, out ImageBrowserItem blue)
		{
			var favorites = new TextureFavorites(new Configuration(), "test");
			favorites.Add(TextureHistoryKind.Texture, "BLUE");
			red = new ImageBrowserItem(new TestImage("RED", TextureNamespace.TEXTURE), "", false);
			blue = new ImageBrowserItem(new TestImage("BLUE", TextureNamespace.TEXTURE), "", false);
			var backing = new List<ImageBrowserItem> { red, blue };
			panel = Empty<ImageSelectorPanel>();
			Set(panel, "items", backing.Where(item => ImageBrowserControl.ValidateFavoriteOnly(item.Icon, favorites, true)).ToList());
			Set(panel, "selection", new List<ImageBrowserItem> { blue });
			Set(panel, "rectangles", new List<Rectangle> { new Rectangle(1, 1, 64, 64) });
			Set(panel, "allowselection", true);
			Set(panel, "scrollbar", Empty<VScrollBar>());
			var browser = Empty<ImageBrowserControl>();
			Set(browser, "items", backing);
			Set(browser, "list", panel);
			return browser;
		}

		[Fact]
		public void ReopenedFavoritesOnlySelectItemCannotAcceptTheInvisibleInitialTexture()
		{
			ImageSelectorPanel panel;
			ImageBrowserItem red, blue;
			var browser = FavoritesOnlyBrowser(out panel, out red, out blue);
			browser.SelectItem(red.Icon.LongName);
			string accepted = null;
			panel.ItemDoubleClicked += (sender, image) => accepted = browser.SelectedItem.TextureName;
			typeof(ImageSelectorPanel).GetMethod("OnKeyDown", Instance).Invoke(panel, new object[] { new KeyEventArgs(Keys.Enter) });
			Assert.Equal("BLUE", accepted);
			Assert.Same(blue, browser.SelectedItem);
			Assert.True(panel.Items.Contains(browser.SelectedItem));
			browser.SelectItem(blue.Icon.LongName);
			Assert.Same(blue, browser.SelectedItem);
		}

		[Theory]
		[InlineData(SearchDirectionHint.Left)]
		[InlineData(SearchDirectionHint.Right)]
		[InlineData(SearchDirectionHint.Up)]
		[InlineData(SearchDirectionHint.Down)]
		public void ReopenedFavoritesOnlySelectItemKeepsArrowNavigationOnVisibleItems(SearchDirectionHint direction)
		{
			ImageSelectorPanel panel;
			ImageBrowserItem red, blue;
			var browser = FavoritesOnlyBrowser(out panel, out red, out blue);
			browser.SelectItem(red.Icon.LongName);
			panel.SelectNextItem(direction);
			Assert.Same(blue, browser.SelectedItem);
		}

		[Fact]
		public void SetSelectedItemsRejectsNonMembersWhileKeepingVisibleSelections()
		{
			ImageSelectorPanel panel;
			ImageBrowserItem red, blue;
			FavoritesOnlyBrowser(out panel, out red, out blue);
			panel.SetSelectedItems(new List<ImageBrowserItem> { red, blue });
			Assert.Same(blue, panel.SelectedItems.Single());
			panel.SetSelectedItem(red);
			Assert.Empty(panel.SelectedItems);
		}

		[Theory]
		[InlineData(true)]
		[InlineData(false)]
		public void NavigationRecoversFromStaleSelectionMembership(bool hasvisibleitems)
		{
			ImageSelectorPanel panel;
			ImageBrowserItem red, blue;
			FavoritesOnlyBrowser(out panel, out red, out blue);
			panel.SelectedItems.Clear();
			panel.SelectedItems.Add(red);
			if(!hasvisibleitems) panel.Items.Clear();
			panel.SelectNextItem(SearchDirectionHint.Right);
			if(hasvisibleitems) Assert.Same(blue, panel.SelectedItems.Single());
			else Assert.Empty(panel.SelectedItems);
		}

		[Fact]
		public void EnterDoesNotAcceptAStaleSelectionOutsideTheVisibleItems()
		{
			ImageSelectorPanel panel;
			ImageBrowserItem red, blue;
			FavoritesOnlyBrowser(out panel, out red, out blue);
			panel.SelectedItems.Clear();
			panel.SelectedItems.Add(red);
			int accepted = 0;
			panel.ItemDoubleClicked += (sender, image) => accepted++;
			typeof(ImageSelectorPanel).GetMethod("OnKeyDown", Instance).Invoke(panel, new object[] { new KeyEventArgs(Keys.Enter) });
			Assert.Equal(0, accepted);
		}

		[Fact]
		public void DuplicateFavoriteIndicatorsUseTheSameLiveModelAndActualImageKind()
		{
			var favorites = new TextureFavorites(new Configuration(), "test");
			var browser = Empty<ImageBrowserControl>();
			Set(browser, "showfavorites", true);
			Set(browser, "favorites", favorites);
			var image = new TestImage("SAME", TextureNamespace.FLAT);
			var copies = Enumerable.Range(0, 3).Select(i => new ImageBrowserItem(image, "", false)).ToArray();
			favorites.Add(TextureHistoryKind.Texture, "SAME");
			Assert.True(copies.All(item => !browser.IsFavoriteItem(item)));
			favorites.Add(TextureHistoryKind.Flat, "SAME");
			Assert.True(copies.All(browser.IsFavoriteItem));
			favorites.Remove(TextureHistoryKind.Flat, "SAME");
			Assert.True(copies.All(item => !browser.IsFavoriteItem(item)));
		}

		[Fact]
		public void FailedAndInvalidBrowserPinWritesDoNotRaiseChangesOrPublishPins()
		{
			var configuration = new Configuration();
			configuration.WriteSetting("configurations.test", "broken");
			var favorites = new TextureFavorites(configuration, "test");
			var browser = Empty<ImageBrowserControl>();
			Set(browser, "showfavorites", true);
			Set(browser, "favorites", favorites);
			int changes = 0;
			favorites.Changed += (s, e) => changes++;
			string before = configuration.OutputConfiguration();
			foreach(string name in new[] { "", "-", "VALID" })
				typeof(ImageBrowserControl).GetMethod("ToggleFavorite", Instance).Invoke(browser, new object[] { new TestImage(name, TextureNamespace.FLAT) });
			Assert.Equal(0, changes);
			Assert.Empty(favorites.Get(TextureHistoryKind.Flat));
			Assert.Equal(before, configuration.OutputConfiguration());
		}

		[Fact]
		public void BrowserClosePersistsOnlyChangedFavoritesAndRetriesAFailedSave()
		{
			var form = Empty<TextureBrowserForm>();
			int saves = 0;
			form.SaveFavoritesIfChanged(() => saves++);
			Assert.Equal(0, saves);
			Set(form, "favoriteschanged", true);
			Assert.Throws<InvalidOperationException>(() => form.SaveFavoritesIfChanged(() => { throw new InvalidOperationException(); }));
			form.SaveFavoritesIfChanged(() => saves++);
			form.SaveFavoritesIfChanged(() => saves++);
			Assert.Equal(1, saves);
		}

		[Fact]
		public void PaletteHeightCountsLogicalGroupsAndLimitsTotalVisibleRows()
		{
			Assert.Equal(360, TextureHistoryView.PaletteHeight(new[] { 2, 2, 2, 12 }, 6, 3, 100, 20));
			Assert.Equal(320, TextureHistoryView.PaletteHeight(new[] { 30, 2 }, 6, 3, 100, 20));
			Assert.Equal(240, TextureHistoryView.PaletteHeight(new[] { 1, 1 }, 6, 3, 100, 20));
		}

		[Fact]
		public void SameKindPinnedAndRecentAreTwoVisualGroupsForNumericShortcuts()
		{
			var history = new TextureHistory(4, true);
			history.Record("RECENT", TextureHistoryKind.Texture);
			var favorites = new TextureFavorites(new Configuration(), "test");
			favorites.Add(TextureHistoryKind.Texture, "PINNED");
			var groups = TextureHistoryView.BuildEntryGroups(history, favorites, true, false);
			Assert.Equal(2, groups.Count);
			var view = Empty<TextureHistoryView>();
			Set(view, "groups", groups.Count);
			Assert.False(view.PickEntry(0, MouseButtons.Left));
		}

		[Fact]
		public void ResolvedPinsUseExplicitKindAndAreResolvedAgainFromReplacementData()
		{
			var wall = new TestImage("SAME", TextureNamespace.TEXTURE);
			var flat = new TestImage("SAME", TextureNamespace.FLAT);
			var unknown = Empty<UnknownImage>();
			var data = Empty<DataManager>();
			Set(data, "textures", new Dictionary<long, ImageData> { [wall.LongName] = wall });
			Set(data, "flats", new Dictionary<long, ImageData> { [flat.LongName] = flat });
			Set(data, "texturenamesshorttofull", new Dictionary<long, long>());
			Set(data, "flatnamesshorttofull", new Dictionary<long, long>());
			Set(data, "unknownimage", unknown);
			Assert.Same(wall, TextureHistoryView.ResolveImage(data, TextureHistoryKind.Texture, "SAME"));
			Assert.Same(flat, TextureHistoryView.ResolveImage(data, TextureHistoryKind.Flat, "SAME"));
			Assert.Same(unknown, TextureHistoryView.ResolveImage(data, TextureHistoryKind.Flat, "UNRESOLVED"));
			wall.Dispose();
			var replacement = new TestImage("SAME", TextureNamespace.TEXTURE);
			Set(data, "textures", new Dictionary<long, ImageData> { [replacement.LongName] = replacement });
			Assert.Same(replacement, TextureHistoryView.ResolveImage(data, TextureHistoryKind.Texture, "SAME"));
			Assert.False(replacement.IsDisposed);
		}

		[Fact]
		public void TabCyclesAllThreeOccurrencesAndRightDoubleClickNeverAccepts()
		{
			var panel = Empty<ImageSelectorPanel>();
			var scroll = Empty<VScrollBar>();
			scroll.Maximum = 1000;
			var item = new ImageBrowserItem(new TestImage("SAME", TextureNamespace.TEXTURE), "", false);
			Set(panel, "scrollbar", scroll);
			Set(panel, "items", new List<ImageBrowserItem> { item, item, item });
			Set(panel, "selection", new List<ImageBrowserItem> { item });
			Set(panel, "rectangles", new List<Rectangle> { new Rectangle(1, 1, 64, 64), new Rectangle(1, 200, 64, 64), new Rectangle(1, 400, 64, 64) });
			panel.JumpBetweenItems(item);
			Assert.Equal(200, scroll.Value);
			panel.JumpBetweenItems(item);
			Assert.Equal(400, scroll.Value);
			panel.JumpBetweenItems(item);
			Assert.Equal(1, scroll.Value);
			scroll.Value = 0;
			int accepted = 0;
			panel.ItemDoubleClicked += (sender, image) => accepted++;
			var doubleclick = typeof(ImageSelectorPanel).GetMethod("OnMouseDoubleClick", Instance);
			doubleclick.Invoke(panel, new object[] { new MouseEventArgs(MouseButtons.Right, 2, 10, 10, 0) });
			Assert.Equal(0, accepted);
			FieldInfo window = typeof(General).GetField("mainwindow", Static);
			object previous = window.GetValue(null);
			window.SetValue(null, Empty<MainForm>());
			try { doubleclick.Invoke(panel, new object[] { new MouseEventArgs(MouseButtons.Left, 2, 10, 10, 0) }); }
			finally { window.SetValue(null, previous); }
			Assert.Equal(1, accepted);
		}

		[Fact]
		public void StarIsAnOverlayAndDoesNotMutateThePreviewBitmap()
		{
			using(var preview = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
			using(var canvas = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
			using(Graphics graphics = Graphics.FromImage(canvas))
			{
				using(Graphics background = Graphics.FromImage(preview)) background.Clear(Color.Navy);
				graphics.DrawImageUnscaled(preview, 0, 0);
				ImageBrowserItem.DrawFavorite(graphics, 10, 3);
				Assert.Equal(Color.Navy.ToArgb(), preview.GetPixel(17, 10).ToArgb());
				Assert.Equal(Color.Gold.ToArgb(), canvas.GetPixel(17, 10).ToArgb());
			}
		}

		private static IEnumerable<MethodBase> Calls(MethodInfo method)
		{
			var ops = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.FieldType == typeof(OpCode))
				.Select(field => (OpCode)field.GetValue(null)).ToDictionary(op => op.Value);
			byte[] code = method.GetMethodBody().GetILAsByteArray();
			for(int i = 0; i < code.Length;)
			{
				short value = code[i++];
				if(value == 0xFE) value = (short)(0xFE00 | code[i++]);
				OpCode op = ops[value];
				if(op.OperandType == OperandType.InlineMethod) yield return method.Module.ResolveMethod(BitConverter.ToInt32(code, i));
				switch(op.OperandType)
				{
					case OperandType.InlineNone: break;
					case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: i++; break;
					case OperandType.InlineVar: i += 2; break;
					case OperandType.InlineI8: case OperandType.InlineR: i += 8; break;
					case OperandType.InlineSwitch: i += 4 + 4 * BitConverter.ToInt32(code, i); break;
					default: i += 4; break;
				}
			}
		}

		[Fact]
		public void ViewSetEntriesAndDisposeBothRemoveFavoritesSubscriptions()
		{
			Assert.Equal(2, Calls(typeof(TextureHistoryView).GetMethod("SetEntries")).Count(method => method.Name == "remove_Changed"));
			Assert.Equal(2, Calls(typeof(TextureHistoryView).GetMethod("SetEntries")).Count(method => method.Name == "add_Changed"));
			Assert.Equal(2, Calls(typeof(TextureHistoryView).GetMethod("Dispose", Instance)).Count(method => method.Name == "remove_Changed"));
			Assert.True(Calls(typeof(TextureBrowserForm).GetMethod("TextureBrowserForm_FormClosing", Instance)).Any(method => method.Name == "SaveFavoritesIfChanged"));
		}

		[Fact]
		public void MapInitializationCreatesFavoritesBeforeDataAndReloadRekeysOnlyAfterDataLoad()
		{
			foreach(string name in new[] { "InitializeNewMapCore", "InitializeOpenMapCore" })
			{
				var calls = Calls(typeof(MapManager).GetMethod(name, Instance)).ToList();
				int favorites = calls.FindIndex(method => method.Name == "UpdateFavorites");
				int data = calls.FindIndex(method => method is ConstructorInfo && method.DeclaringType == typeof(DataManager));
				Assert.True(favorites >= 0 && favorites < data);
			}
			var reload = Calls(typeof(MapManager).GetMethods(Instance).Single(method => method.Name == "ReloadResources" && method.GetParameters().Length == 3)).ToList();
			int rekey = reload.FindIndex(method => method.Name == "UpdateFavorites");
			Assert.True(rekey > reload.FindLastIndex(method => method.DeclaringType == typeof(DataManager) && method.Name == "Load"));
			Assert.True(rekey < reload.FindIndex(method => method.Name == "ReloadResources"));
			Assert.True(Calls(typeof(MapManager).GetMethod("UpdateConfiguration", Instance)).Any(method => method.Name == "UpdateFavorites"));
			Assert.True(Calls(typeof(MapManager).GetMethod("ShowMapOptions", Instance)).Any(method => method.Name == "ReloadResources"));
		}
	}
}
