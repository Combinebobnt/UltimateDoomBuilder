using CodeImp.DoomBuilder.Compilers;
using CodeImp.DoomBuilder.IO;

namespace Builder.Tests
{
	public class LightmapBakeOptionsTests
	{
		private const string FORK_FEATURES = "zdray-features 1\nsunintensity\nsample-scale\nsize4096\nsamples\nbounce\n";
		private static readonly LightmapBakerFeatures Fork = LightmapBakerFeatures.Parse(FORK_FEATURES);

		private static string Args(LightmapBakeSettings settings)
		{
			return LightmapBaker.BuildArguments(settings, 16, "MAP01", "in", "out");
		}

		#region Feature probe output

		[Fact]
		public void FeatureParseKeysOnTheHeaderLine()
		{
			// Stock ZDRay: getopt's complaint on stderr, its usage hint on stdout, exit 0
			Assert.True(LightmapBakerFeatures.Parse("Try `zdray --help' for more information.\n").IsEmpty);
			Assert.True(LightmapBakerFeatures.Parse("").IsEmpty);
			Assert.True(LightmapBakerFeatures.Parse(null).IsEmpty);
			Assert.True(LightmapBakerFeatures.Parse("samples\nsize4096\n").IsEmpty);
			Assert.True(LightmapBakerFeatures.Parse("zdray-features 2\nsamples\n").IsEmpty);

			Assert.True(Fork.Has("samples"));
			Assert.True(Fork.Has("size4096"));
			Assert.False(Fork.Has("skylight"));
			Assert.Equal("bounce sample-scale samples size4096 sunintensity", Fork.ToString());
			Assert.True(LightmapBakerFeatures.Parse("zdray-features 1\r\nao\r\n").Has("ao"));
			Assert.False(LightmapBakerFeatures.Parse("zdray-features 1\nnot a token\n").Has("not a token"));
		}

		#endregion

		#region Arguments

		[Fact]
		public void StockAndFinalBakeWithTodaysArguments()
		{
			const string TODAY = "-c -X -m MAP01 -j 4 -S 1024 -o \"out\" \"in\"";
			var settings = new LightmapBakeSettings();
			Assert.Equal(TODAY, Args(settings));

			// A stock baker runs every preset at Final
			settings.Quality = LightmapBakeQuality.FromPreset(LightmapQualityPreset.Draft, 1024);
			Assert.Equal(TODAY, Args(settings));

			settings.Features = Fork;
			settings.Quality = LightmapBakeQuality.FromPreset(LightmapQualityPreset.Final, 1024);
			Assert.Equal(TODAY, Args(settings));
		}

		[Fact]
		public void EachFlagNeedsItsFeature()
		{
			var settings = new LightmapBakeSettings { Features = Fork };
			settings.Quality.SampleScale = 2;
			Assert.Contains(" --sample-scale 2 ", Args(settings));
			Assert.DoesNotContain("--samples", Args(settings));

			settings.Quality.Samples = 16;
			settings.Quality.BounceSamples = 128;
			Assert.Contains(" --samples 16 --bounce-samples 128 ", Args(settings));

			settings.Quality.SampleScale = 0.5f;
			Assert.Contains(" --sample-scale 0.5 ", Args(settings));

			settings.Features = LightmapBakerFeatures.Parse("zdray-features 1\nsamples\n");
			Assert.DoesNotContain("--sample-scale", Args(settings));
			Assert.Contains("--samples 16", Args(settings));

			settings.Features = LightmapBakerFeatures.Parse("zdray-features 1\nsample-scale\n");
			Assert.DoesNotContain("--samples", Args(settings));
			Assert.DoesNotContain("--bounce-samples", Args(settings));
		}

		[Fact]
		public void TextureSizesPast1024NeedSize4096()
		{
			var settings = new LightmapBakeSettings { TextureSize = 4096 };
			Assert.Contains("-S 1024 ", Args(settings));
			Assert.Equal(1024, settings.EffectiveQuality().TextureSize);
			settings.Features = Fork;
			Assert.Contains("-S 4096 ", Args(settings));
			settings.TextureSize = 512;
			Assert.Contains("-S 512 ", Args(settings));
		}

		[Fact]
		public void QuickBakeLeavesTheSettingsAlone()
		{
			var settings = new LightmapBakeSettings { Features = Fork, TextureSize = 2048 };
			LightmapBakeSettings quick = settings.With(LightmapBakeQuality.Quick(settings.TextureSize));
			Assert.Equal(LightmapQualityPreset.Draft, quick.Quality.Preset);
			Assert.Equal(512, quick.TextureSize);
			Assert.Equal(LightmapQualityPreset.Final, settings.Quality.Preset);
			Assert.Equal(2048, settings.TextureSize);
			Assert.Equal(128, LightmapBakeQuality.Quick(128).TextureSize);
		}

		#endregion

		#region Quality

		[Fact]
		public void PresetsRoundTripAndEditsMakeCustom()
		{
			foreach(LightmapQualityPreset p in new[] { LightmapQualityPreset.Draft, LightmapQualityPreset.Normal, LightmapQualityPreset.Final })
			{
				LightmapBakeQuality q = LightmapBakeQuality.FromPreset(p, 1024);
				Assert.Equal(p, q.Preset);
				LightmapBakeQuality back = LightmapBakeQuality.Parse(q.Serialize());
				Assert.NotNull(back);
				Assert.Equal(p, back.Preset);
				Assert.Equal(q.Serialize(), back.Serialize());

				q.Samples++;
				Assert.Equal(LightmapQualityPreset.Custom, q.Preset);
			}

			// Final is exactly what the baker does with no flags
			LightmapBakeQuality final = LightmapBakeQuality.FromPreset(LightmapQualityPreset.Final, 1024);
			foreach(LightmapBakeOption o in LightmapBakeOptions.All)
				if(o.Kind == LightmapOptionKind.CliFlag)
					Assert.Equal(Convert.ToDouble(o.Default), final.Value(o.Id));
		}

		[Fact]
		public void BelowComparesSamplingNotSize()
		{
			LightmapBakeQuality draft = LightmapBakeQuality.FromPreset(LightmapQualityPreset.Draft, 512);
			LightmapBakeQuality normal = LightmapBakeQuality.FromPreset(LightmapQualityPreset.Normal, 1024);
			LightmapBakeQuality final = LightmapBakeQuality.FromPreset(LightmapQualityPreset.Final, 128);
			Assert.True(draft.IsBelow(normal));
			Assert.True(normal.IsBelow(final));
			Assert.False(final.IsBelow(normal));
			Assert.False(final.IsBelow(LightmapBakeQuality.FromPreset(LightmapQualityPreset.Final, 4096)));
		}

		[Fact]
		public void QualityParseRejectsGarbage()
		{
			Assert.Null(LightmapBakeQuality.Parse(null));
			Assert.Null(LightmapBakeQuality.Parse(""));
			Assert.Null(LightmapBakeQuality.Parse("draft"));
			Assert.Null(LightmapBakeQuality.Parse("scale=1;samples=256;bounce=2048"));
			Assert.Null(LightmapBakeQuality.Parse("scale=x;samples=256;bounce=2048;size=1024"));
			Assert.Null(LightmapBakeQuality.Parse("scale=0;samples=256;bounce=2048;size=1024"));
			Assert.NotNull(LightmapBakeQuality.Parse("scale=1;samples=256;bounce=2048;size=1024"));
		}

		#endregion

		#region Per map settings

		[Fact]
		public void MapSettingsRoundTripThroughConfiguration()
		{
			var cfg = new Configuration(true);
			LightmapBakeMapSettings empty = LightmapBakeMapSettings.Read(cfg);
			Assert.Null(empty.Quality);
			Assert.Equal(LightmapAutoBake.Off, empty.AutoBake);
			empty.Write(cfg);
			Assert.Null(cfg.ReadSetting("lightmapbake.texturesize", (string)null));

			var s = new LightmapBakeMapSettings
			{
				Quality = LightmapBakeQuality.FromPreset(LightmapQualityPreset.Normal, 2048),
				AutoBake = LightmapAutoBake.OnSaveAndTest,
				LastQuality = LightmapBakeQuality.Quick(1024).Serialize(),
				LastSha = "ab12",
			};
			s.Quality.SampleScale = 0.5f;
			s.Write(cfg);

			// Through the .dbs text, as a save and reopen would
			var reread = new Configuration(true);
			reread.InputConfiguration(cfg.OutputConfiguration());
			LightmapBakeMapSettings back = LightmapBakeMapSettings.Read(reread);
			Assert.Equal(s.Quality.Serialize(), back.Quality.Serialize());
			Assert.Equal(LightmapAutoBake.OnSaveAndTest, back.AutoBake);
			Assert.Equal(s.LastQuality, back.LastQuality);
			Assert.Equal("ab12", back.LastSha);

			// Clearing writes nothing back
			back.Quality = null;
			back.AutoBake = LightmapAutoBake.Off;
			back.LastSha = null;
			back.Write(reread);
			Assert.Null(LightmapBakeMapSettings.Read(reread).Quality);
			Assert.Null(LightmapBakeMapSettings.Read(reread).LastQuality);
		}

		[Fact]
		public void BadMapValuesFallBack()
		{
			var cfg = new Configuration(true);
			cfg.WriteSetting("lightmapbake.texturesize", 3000);
			cfg.WriteSetting("lightmapbake.samples", 99999);
			cfg.WriteSetting("lightmapbake.autobake", "always");
			LightmapBakeMapSettings s = LightmapBakeMapSettings.Read(cfg);
			Assert.Equal(LightmapBakeSettings.DEFAULT_TEXTURE_SIZE, s.Quality.TextureSize);
			Assert.Equal(4096, s.Quality.Samples);
			Assert.Equal(LightmapAutoBake.Off, s.AutoBake);
		}

		[Fact]
		public void LastQualityAppliesOnlyToTheSameLump()
		{
			var s = new LightmapBakeMapSettings { LastQuality = LightmapBakeQuality.Quick(1024).Serialize(), LastSha = "ABCDEF" };
			Assert.Equal(LightmapQualityPreset.Draft, s.QualityOfLoadedLump("abcdef").Preset);
			Assert.Null(s.QualityOfLoadedLump("abcdee"));
			Assert.Null(s.QualityOfLoadedLump(null));
			s.LastSha = null;
			Assert.Null(s.QualityOfLoadedLump("abcdef"));
		}

		#endregion
	}
}
