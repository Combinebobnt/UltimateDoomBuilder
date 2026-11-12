using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.ZDoom;

namespace Builder.Tests
{
	[Collection("GeneralStatics")]
	public class AnimdefsAnimationParserTests
	{
		private static AnimdefsParser Parse(string text)
		{
			var parser = new AnimdefsParser();
			Assert.True(parser.Parse(CvarInfoParserTests.CreateTextResourceDataFromString(text), true));
			Assert.False(parser.HasError);
			return parser;
		}

		[Fact]
		public void RangeOptionsAndFractionalDurationArePreserved()
		{
			var anim = Parse("TeXtUrE optional FIRST allowdecals notrim oscillate range LAST tics 3.5").Animations[0];
			Assert.Equal("FIRST", anim.Name);
			Assert.True(anim.Optional);
			Assert.True(anim.IsTexture);
			Assert.Equal(AnimationKind.Range, anim.Kind);
			Assert.Equal(AnimationType.OscillateUp, anim.Type);
			Assert.Equal("LAST", anim.Frames[0].Name);
			Assert.Equal(100, anim.Frames[0].MinMs);
			Assert.Equal(100, anim.Frames[0].MaxMs);
		}

		[Fact]
		public void PicNamesNumbersAndRandomTimesArePreserved()
		{
			var anim = Parse("flat FIRST random pic SECOND rand 3.5 7 pic 3 tics 1").Animations[0];
			Assert.False(anim.IsTexture);
			Assert.Equal(AnimationKind.Pic, anim.Kind);
			Assert.Equal(AnimationType.Random, anim.Type);
			Assert.Equal("SECOND", anim.Frames[0].Name);
			Assert.Equal(100, anim.Frames[0].MinMs);
			Assert.Equal(200, anim.Frames[0].MaxMs);
			Assert.Equal(3, anim.Frames[1].Number.Value);
		}

		[Theory]
		[InlineData("texture A pic B tics 1")]
		[InlineData("texture A range B tics 1 pic C tics 1")]
		[InlineData("texture A pic B tics 1 range C tics 1")]
		[InlineData("texture A range B tics 1 range C tics 1")]
		[InlineData("texture A random oscillate range B tics 1")]
		[InlineData("texture A oscillate random range B tics 1")]
		[InlineData("texture \"long/name.png\" range B tics 1")]
		[InlineData("texture A range \"long/name.png\" tics 1")]
		[InlineData("texture A range B bogus")]
		[InlineData("texture A range B tics nope")]
		[InlineData("texture A range B rand 7 3")]
		[InlineData("warp texture \"long/name.png\"")]
		public void BadDefinitionWarnsAndFollowingDefinitionSurvives(string bad)
		{
			using(var map = new SndInfoParserTests.FakeMap("doom"))
			{
				var parser = Parse(bad + "\nflat GOOD range END tics 1");
				Assert.Equal(1, map.Logger.ErrorsCount);
				Assert.Equal(1, parser.Animations.Count);
				Assert.Equal("GOOD", parser.Animations[0].Name);
			}
		}

		[Theory]
		[InlineData("0")]
		[InlineData("0.02")]
		[InlineData("-3")]
		public void NonpositiveOrSubmillisecondDurationIsClamped(string duration)
		{
			using(var map = new SndInfoParserTests.FakeMap("doom"))
			{
				var parser = Parse("texture A range B tics " + duration);
				Assert.Equal(1, parser.Animations[0].Frames[0].MinMs);
				Assert.Equal(1, map.Logger.ErrorsCount);
			}
		}

		[Fact]
		public void UnsupportedDefinitionsConsumeOnlyTheirOwnArguments()
		{
			var parser = Parse("switch doom 1 SW1 on sound click pic SW2 tics 0 off pic SW1 rand 1 2\n"
				+ "animateddoor DOOR opensound open closesound close pic 1 pic 2 allowdecals\n"
				+ "skyoffset SKY 12\ncanvastexture CANVAS 32 64 fit 64 128 worldpanning hdr translucent offset 1 2\n"
				+ "firetexture FIRE tics 4 color 255 0 0 255 palette 12\ntexture REAL range END tics 4");
			Assert.Equal(1, parser.Animations.Count);
			Assert.Equal("REAL", parser.Animations[0].Name);
		}

		[Fact]
		public void InterleavedSwitchesDoNotLeakPicsAndCameraStillLoads()
		{
			var parser = Parse("texture LAVA1 range LAVA3 tics 4\nswitch SW1 on pic SW2 tics 0\n"
				+ "flat SLIME1 range SLIME3 tics 4\nswitch any SW3 on pic SW4 tics 0 off pic SW3 tics 0\n"
				+ "texture FALL1 range FALL3 tics 4\nswitch heretic SW5 on pic SW6 tics 0\n"
				+ "cameratexture CAMERA 64 32 fit 128 64 worldpanning");
			Assert.Equal(3, parser.Animations.Count);
			Assert.Equal(1, parser.CameraTextures.Count);
			Assert.Equal(2.0f, parser.CameraTextures["CAMERA"].ScaleX);
			Assert.True(parser.CameraTextures["CAMERA"].WorldPanning);
		}

		[Fact]
		public void WarpsConsumeOptionalSpeedRegardlessOfTargetExistence()
		{
			var parser = Parse("warp flat NUKAGE1\nwarp2 texture ABSENT -2.5 allowdecals\ntexture A range B tics 4");
			Assert.Equal(2, parser.Warps.Count);
			Assert.Equal(1, parser.Warps[0].Style);
			Assert.Equal(1.0, parser.Warps[0].Speed);
			Assert.False(parser.Warps[0].IsTexture);
			Assert.Equal(2, parser.Warps[1].Style);
			Assert.Equal(-2.5, parser.Warps[1].Speed);
			Assert.Equal(1, parser.Animations.Count);
		}
	}
}
