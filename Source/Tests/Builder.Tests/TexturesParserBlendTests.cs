using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.ZDoom;
using System.Text;

namespace Builder.Tests
{
	public class TexturesParserBlendTests
	{
		// A sprite block, because the texture/flat branches of TexturesParser.Parse read General.Map
		private static TexturesParser Parse(string blend, bool zandronum, out bool result)
		{
			string content = "Sprite \"TESTA0\", 16, 16\n{\n\tPatch \"PATCH\", 0, 0\n\t{\n\t\tBlend " + blend + "\n\t}\n}\n";
			var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
			var data = new TextResourceData(stream, new DataLocation(DataLocation.RESOURCE_DIRECTORY, String.Empty, false, false, false, new List<string>()), "TEXTURES");
			var parser = new TexturesParser { ZandronumTextureBlend = zandronum };
			result = parser.Parse(data, true);
			return parser;
		}

		[Theory]
		// Zandronum eats the comma after b, so "1.0" is skipped and the patch is multiplied
		[InlineData("255, 255, 255, 1.0", true, TexturePathBlendStyle.BLEND, 255)]
		[InlineData("255, 255, 255, 1.0", false, TexturePathBlendStyle.TINT, 254)]
		[InlineData("10, 20, 30,", true, TexturePathBlendStyle.BLEND, 255)]
		// A second comma is the only way to give Zandronum an alpha
		[InlineData("10, 20, 30, , 0.5", true, TexturePathBlendStyle.TINT, 127)]
		[InlineData("10, 20, 30", false, TexturePathBlendStyle.BLEND, 255)]
		// String colors have no mandatory comma in either engine
		[InlineData("\"#ff0000\", 0.5", true, TexturePathBlendStyle.TINT, 127)]
		[InlineData("\"#ff0000\", 0.5", false, TexturePathBlendStyle.TINT, 127)]
		public void Blend_ParsesPerEngine(string blend, bool zandronum, TexturePathBlendStyle style, int alpha)
		{
			TexturesParser parser = Parse(blend, zandronum, out bool result);

			Assert.True(result, parser.ErrorDescription);
			PatchStructure patch = parser.Sprites.Single().Patches.Single();
			Assert.Equal(style, patch.BlendStyle);
			Assert.Equal(alpha, (int)patch.BlendColor.a);
		}

		[Fact]
		public void Blend_ZandronumStrayAlpha_DoesNotEatNextProperty()
		{
			TexturesParser parser = Parse("10, 20, 30, 0.5\n\t\tFlipX", true, out bool result);

			Assert.True(result, parser.ErrorDescription);
			PatchStructure patch = parser.Sprites.Single().Patches.Single();
			Assert.Equal(TexturePathBlendStyle.BLEND, patch.BlendStyle);
			Assert.True(patch.FlipX);
		}

		[Fact]
		public void Blend_ZandronumWithoutTrailingComma_IsError()
		{
			TexturesParser parser = Parse("10, 20, 30", true, out bool result);

			Assert.False(result);
			Assert.Contains("comma", parser.ErrorDescription);
		}
	}
}
