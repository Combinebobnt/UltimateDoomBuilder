using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.IO;
using Xunit;

namespace Builder.Tests
{
	public class TextureFavoritesTests
	{
		[Fact]
		public void TogglePreservesPinOrderAndSeparatesNamespaces()
		{
			var favorites = new TextureFavorites(new Configuration(), "doom");
			int changed = 0;
			favorites.Changed += (s, e) => changed++;
			Assert.True(favorites.Add(TextureHistoryKind.Texture, "STONE1"));
			Assert.True(favorites.Add(TextureHistoryKind.Texture, "WOOD1"));
			Assert.False(favorites.Add(TextureHistoryKind.Texture, "stone1"));
			Assert.True(favorites.Add(TextureHistoryKind.Flat, "STONE1"));
			Assert.True(favorites.Contains(TextureHistoryKind.Texture, "sToNe1"));
			Assert.True(favorites.Toggle(TextureHistoryKind.Texture, "stone1"));
			Assert.False(favorites.Contains(TextureHistoryKind.Texture, "STONE1"));
			Assert.True(favorites.Toggle(TextureHistoryKind.Texture, "stone1"));
			Assert.Equal(new[] { "WOOD1", "stone1" }, favorites.Get(TextureHistoryKind.Texture).ToArray());
			Assert.Equal(new[] { "STONE1" }, favorites.Get(TextureHistoryKind.Flat).ToArray());
			Assert.Equal(5, changed);
		}

		[Fact]
		public void EmptyAndMissingTextureSentinelsDoNotChangeSettings()
		{
			var favorites = new TextureFavorites(new Configuration(), "doom");
			int changed = 0;
			favorites.Changed += (s, e) => changed++;
			foreach(string name in new[] { null, "", " ", "-" })
			{
				Assert.False(favorites.Add(TextureHistoryKind.Texture, name));
				Assert.False(favorites.Toggle(TextureHistoryKind.Texture, name));
			}
			Assert.False(favorites.Remove(TextureHistoryKind.Flat, "MISSING"));
			Assert.Equal(0, changed);
		}

		[Fact]
		public void TextConfigurationRoundTripKeepsMoreThanTenPinsAndUnresolvedNames()
		{
			var configuration = new Configuration();
			var favorites = new TextureFavorites(configuration, "doom");
			var names = Enumerable.Range(0, 14).Select(i => "unresolved/texture" + i).ToArray();
			foreach(string name in names) favorites.Add(TextureHistoryKind.Texture, name);
			favorites.Add(TextureHistoryKind.Flat, "UNAVAILABLE_FLAT");
			var restored = new Configuration();
			restored.InputConfiguration(configuration.OutputConfiguration(), true);
			var result = new TextureFavorites(restored, "doom");
			Assert.Equal(names, result.Get(TextureHistoryKind.Texture).ToArray());
			Assert.Equal(new[] { "UNAVAILABLE_FLAT" }, result.Get(TextureHistoryKind.Flat).ToArray());
			Assert.True(result.Remove(TextureHistoryKind.Texture, names[4]));
			Assert.Equal(13, ((IDictionary)restored.ReadSettingObject("configurations.doom.favoritetextures", null)).Count);
		}

		[Fact]
		public void RekeyKeepsSubscribersAndWritesOnlyToTheCurrentConfiguration()
		{
			var configuration = new Configuration();
			var favorites = new TextureFavorites(configuration, "doom");
			favorites.Add(TextureHistoryKind.Texture, "DOOMWALL");
			int changed = 0;
			favorites.Changed += (s, e) => changed++;
			favorites.Rekey("heretic");
			Assert.Empty(favorites.Get(TextureHistoryKind.Texture));
			favorites.Add(TextureHistoryKind.Texture, "HERETICWALL");
			favorites.Rekey("doom");
			Assert.Equal(new[] { "DOOMWALL" }, favorites.Get(TextureHistoryKind.Texture).ToArray());
			favorites.Rekey("heretic");
			Assert.Equal(new[] { "HERETICWALL" }, favorites.Get(TextureHistoryKind.Texture).ToArray());
			Assert.Equal(4, changed);
		}

		[Fact]
		public void ScalarListsAndNonStringEntriesAreIgnoredWithWarnings()
		{
			var configuration = new Configuration();
			configuration.InputConfiguration("configurations { doom { favoritetextures = \"X\"; favoriteflats { 0 = 42; 1 = \"FLOOR1\"; 2 = \"floor1\"; } } }", true);
			var warnings = new List<string>();
			var favorites = new TextureFavorites(configuration, "doom", warnings.Add);
			Assert.Empty(favorites.Get(TextureHistoryKind.Texture));
			Assert.Equal(new[] { "FLOOR1" }, favorites.Get(TextureHistoryKind.Flat).ToArray());
			Assert.Equal(2, warnings.Count);
			Assert.True(favorites.Add(TextureHistoryKind.Texture, "STONE1"));
			Assert.True(configuration.ReadSettingObject("configurations.doom.favoritetextures", null) is ListDictionary);
		}

		[Fact]
		public void FailedWritesDoNotPublishAnUnpersistedFavorite()
		{
			var configuration = new Configuration();
			configuration.WriteSetting("configurations.doom", "not a dictionary");
			var warnings = new List<string>();
			var favorites = new TextureFavorites(configuration, "doom", warnings.Add);
			int changed = 0;
			favorites.Changed += (s, e) => changed++;
			Assert.False(favorites.Add(TextureHistoryKind.Texture, "STONE1"));
			Assert.False(favorites.Contains(TextureHistoryKind.Texture, "STONE1"));
			Assert.Equal(0, changed);
			Assert.Contains("unable to write", warnings.Last());
		}
	}
}
