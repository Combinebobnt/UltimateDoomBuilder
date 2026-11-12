using CodeImp.DoomBuilder.GZBuilder.Data;
using CodeImp.DoomBuilder.Rendering;

namespace Builder.Tests
{
	public class UserShaderBuilderTests
	{
		[Fact]
		public void TimerUpdatesMatchEachDialectAndStayPerProgram()
		{
			var program = new UserShaderProgram();
			Assert.Equal(6f, program.GetTimer(UserShaderDialect.Zandronum, 2f, 3));
			Assert.Equal(6f, program.GetTimer(UserShaderDialect.Zandronum, 0f, 20));
			Assert.Equal(6f, program.GetTimer(UserShaderDialect.Zandronum, -1f, 20));
			Assert.Equal(0f, new UserShaderProgram().GetTimer(UserShaderDialect.Zandronum, -1f, 20));
			Assert.Equal(-20f, program.GetTimer(UserShaderDialect.UZDoom, -1f, 20));
			Assert.Equal(0f, program.GetTimer(UserShaderDialect.UZDoom, 0f, 20));
		}

		[Theory]
		[InlineData("Zandronum_DoomUDMF.cfg", "zandronum", 1)]
		[InlineData("GZDoom_DoomUDMF.cfg", "gzdoom", 2)]
		[InlineData("UZDoom_DoomUDMF.cfg", "uzdoom", 2)]
		[InlineData("ZDoom_DoomUDMF.cfg", "zdoom", 0)]
		public void UdmfNamespaceDoesNotEraseMaterialDialect(string filename, string engine, int expected)
		{
			string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(CodeImp.DoomBuilder.General).Assembly.Location)!, ".."));
			var config = new CodeImp.DoomBuilder.IO.Configuration(Path.Combine(root, "Assets", "Common", "Configurations", filename), true);
			Assert.False(config.ErrorResult, config.ErrorDescription);
			Assert.Equal("zdoom", config.ReadSetting("engine", ""));
			string materialengine = config.ReadSetting("materialengine", config.ReadSetting("engine", ""));
			Assert.Equal(engine, materialengine);
			Assert.Equal(expected, (int)UserShaderBuilder.Dialect(materialengine));
		}

		[Theory]
		[InlineData("zandronum", 1)]
		[InlineData("ZANDRONUM", 1)]
		[InlineData("gzdoom", 2)]
		[InlineData("uzdoom", 2)]
		[InlineData("zdoom", 0)]
		[InlineData("doom2", 0)]
		public void EngineSelectsDialect(string engine, int expected)
		{
			Assert.Equal(expected, (int)UserShaderBuilder.Dialect(engine));
		}

		[Theory]
		[InlineData("zandronum", 1)]
		[InlineData("uzdoom", 2)]
		[InlineData("gzdoom", 2)]
		[InlineData("zdoom", 2)]
		[InlineData("hexen", 2)]
		public void WarpUsesModernFallbackForNonZandronumEngines(string engine, int expected)
		{
			Assert.Equal(expected, (int)UserShaderBuilder.WarpDialect(engine));
		}

		[Theory]
		[InlineData(1, 1)]
		[InlineData(2, 1)]
		[InlineData(1, 2)]
		[InlineData(2, 2)]
		public void EmbeddedWarpsUseTheMaterialCompiler(int style, int dialect)
		{
			var program = UserShaderBuilder.BuildWarp(style, (UserShaderDialect)dialect);
			Assert.True(program.IsWarp);
			Assert.Equal(UserShaderBuilder.WarpKey(style, (UserShaderDialect)dialect), program.Path);
			Assert.Contains("timer *", program.FragmentSource);
			Assert.Contains("#if defined(ALPHA_TEST)", program.FragmentSource);
			Assert.Contains(dialect == 1 ? "vec4 Process(vec4 color)" : "SetMaterialProps(material, GetTexCoord());", program.FragmentSource);
			Assert.NotEqual(UserShaderBuilder.Key(new GldefsMaterial { Shader = program.Path }, (UserShaderDialect)dialect), program.Path);
		}

		private static UserShaderProgram Build(string source, UserShaderDialect dialect = UserShaderDialect.UZDoom)
		{
			return UserShaderBuilder.Build(new GldefsMaterial { Shader = "shaders/test.fp" }, dialect, source);
		}

		[Fact]
		public void LegacyCleanupRetainsUserLineNumbers()
		{
			string source = "uniform\n float timer ;\nuniform sampler2D tex;\nvec4 Process(vec4 c) { return texture2D(tex, gl_TexCoord[0].st) * c; }";
			string clean = UserShaderBuilder.CleanSource(source);
			Assert.Equal(source.Count(c => c == '\n'), clean.Count(c => c == '\n'));
			Assert.DoesNotContain("uniform", clean);
			Assert.Contains("vTexCoord.st", clean);
			Assert.Contains("#line 1 1\n", Build(source).FragmentSource);
		}

		[Theory]
		[InlineData("vec4 Process(vec4 c) { return c; }", "material.Base = Process(vec4(1.0));")]
		[InlineData("vec4 ProcessTexel() { return vec4(1.0); }", "material.Base = ProcessTexel();")]
		[InlineData("vec2 GetTexCoord() { return vec2(0.0); }", "SetMaterialProps(material, GetTexCoord());")]
		public void OldShaderCompatibilityLadder(string source, string expected)
		{
			string fragment = Build(source).FragmentSource;
			Assert.Contains(expected, fragment);
			Assert.DoesNotContain("#define LEGACY_USER_SHADER", fragment);
			if(source.Contains("GetTexCoord")) Assert.DoesNotContain("Process(vec4(1.0))", fragment);
		}

		[Fact]
		public void LegacyMaterialOnlyAndModernSetupAreDifferent()
		{
			Assert.Contains("#define LEGACY_USER_SHADER", Build("Material ProcessMaterial() { Material m; return m; }").FragmentSource);
			Assert.DoesNotContain("#define LEGACY_USER_SHADER", Build("void SetupMaterial(inout Material m) {} Material ProcessMaterial() { Material m; return m; }").FragmentSource);
			Assert.Contains("return ProcessLight(color);", Build("vec4 ProcessLight(vec4 c) { return c; } vec4 ProcessTexel() { return vec4(1.0); }").FragmentSource);
		}

		[Fact]
		public void DialectBranchCoordinatesAndAlphaTwins()
		{
			var zan = Build("vec4 Process(vec4 c) { return c; }", UserShaderDialect.Zandronum);
			Assert.Contains("#define DOOMLIGHTFACTOR 232.0", zan.FragmentSource);
			Assert.DoesNotContain("#define DOOMLIGHTFACTOR", Build("vec4 Process(vec4 c) { return c; }").FragmentSource);
			Assert.Contains("pos.xzy", zan.VertexSource);
			Assert.Contains("-viewpos.z / viewpos.w", zan.VertexSource);
			Assert.Contains("campos.xzy", zan.FragmentSource);
			Assert.Contains("#if defined(ALPHA_TEST)", zan.FragmentSource);
		}

		[Fact]
		public void ProgramKeySharesCodeButNotDefinesOrSamplerLayout()
		{
			var a = new GldefsMaterial { Shader = "shaders/test.fp", Speed = 1 };
			var b = new GldefsMaterial { Shader = "SHADERS/TEST.FP", Speed = 3 };
			Assert.Equal(UserShaderBuilder.Key(a, UserShaderDialect.UZDoom), UserShaderBuilder.Key(b, UserShaderDialect.UZDoom));
			b.Defines.Add("VALUE 2");
			Assert.NotEqual(UserShaderBuilder.Key(a, UserShaderDialect.UZDoom), UserShaderBuilder.Key(b, UserShaderDialect.UZDoom));
			a.Defines.Add("VALUE 2");
			a.Textures.Add(new KeyValuePair<string, string>("detail", "first.png"));
			b.Textures.Add(new KeyValuePair<string, string>("detail", "second.png"));
			Assert.Equal(UserShaderBuilder.Key(a, UserShaderDialect.UZDoom), UserShaderBuilder.Key(b, UserShaderDialect.UZDoom));
			Assert.Contains("#define detail texture4", UserShaderBuilder.Build(a, UserShaderDialect.UZDoom, "vec4 Process(vec4 c) { return c; }").FragmentSource);
		}
	}
}
