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
			string dir = Path.Combine(Path.GetDirectoryName(typeof(CodeImp.DoomBuilder.General).Assembly.Location)!, "Configurations");
			var config = new CodeImp.DoomBuilder.IO.Configuration(Path.Combine(dir, filename), true);
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

		[Theory]
		[InlineData(1)]
		[InlineData(2)]
		public void WallGlowIsAddedToTheLightBeforeMaterialLighting(int dialect)
		{
			string fragment = Build("vec4 Process(vec4 c) { return c; }", (UserShaderDialect)dialect).FragmentSource;
			Assert.True(System.Text.RegularExpressions.Regex.IsMatch(fragment, @"vec3 wallGlow\s*\(vec3 PosW\)\s*\{"));
			int glow = fragment.IndexOf("baseLight = addWallGlow(baseLight, PosW);");
			Assert.True(glow > fragment.IndexOf("vec4 baseLight ="));
			Assert.True(glow < fragment.IndexOf("getDynLightContribution(", glow));
			Assert.True(glow < fragment.IndexOf(dialect == 1 ? "editorBright(vTexCoord.st).rgb" : "desaturate(material.Bright)", glow));
		}

		// A material surface that skipped the sector light would draw fullbright
		[Theory]
		[InlineData(1)]
		[InlineData(2)]
		public void SectorLightReachesBothLightSourcesBeforeTheGlow(int dialect)
		{
			UserShaderProgram program = Build("vec4 Process(vec4 c) { return c; }", (UserShaderDialect)dialect);
			Assert.Contains("vSectorLight = AttrLight;", program.VertexSource);
			Assert.True(System.Text.RegularExpressions.Regex.IsMatch(program.FragmentSource, @"in vec4 [^;]*\bvSectorLight\b"));
			int baselight = program.FragmentSource.IndexOf("vec4 baseLight =");
			string line = program.FragmentSource.Substring(baselight, program.FragmentSource.IndexOf('\n', baselight) - baselight);
			Assert.Contains("sectorLight(vertexColor, lightoverride, PosW)", line);
			Assert.Contains("sectorLight(Color, vSectorLight, PosW)", line);
			Assert.True(baselight < program.FragmentSource.IndexOf("addWallGlow(baseLight, PosW)"));
			Assert.True(program.FragmentSource.IndexOf("udbVColor = baseLight;") > baselight);
		}

		// The lines a GLSL preprocessor keeps, for #if/#ifdef on defined() of names #defined earlier; any other
		// condition keeps both branches, so a check on what is absent stays conservative
		private static string ActiveSource(string source)
		{
			var defines = new HashSet<string>();
			var stack = new Stack<(bool parent, bool active, bool taken)>();
			bool active = true;
			var output = new System.Text.StringBuilder();
			foreach(string raw in source.Split('\n'))
			{
				string line = raw.Trim();
				var cond = System.Text.RegularExpressions.Regex.Match(line, @"^#\s*(if|elif|ifdef|ifndef)\s+(?:defined\s*\(\s*(\w+)\s*\)|(\w+))\s*$");
				if(line.StartsWith("#if") || line.StartsWith("#elif"))
				{
					bool known = cond.Success && (cond.Groups[1].Value != "if" && cond.Groups[1].Value != "elif" || cond.Groups[2].Success);
					string name = cond.Success ? (cond.Groups[2].Success ? cond.Groups[2].Value : cond.Groups[3].Value) : null;
					bool value = !known || (cond.Groups[1].Value == "ifndef" ? !defines.Contains(name) : defines.Contains(name));
					if(line.StartsWith("#elif"))
					{
						var top = stack.Pop();
						bool now = top.parent && !top.taken && value;
						stack.Push((top.parent, now, top.taken || (known && now)));
						active = top.parent && (!known || now);
					}
					else
					{
						bool now = active && value;
						stack.Push((active, now, known && now));
						active = now;
					}
					continue;
				}
				if(line.StartsWith("#else"))
				{
					var top = stack.Pop();
					stack.Push((top.parent, top.parent && !top.taken, true));
					active = top.parent && !top.taken;
					continue;
				}
				if(line.StartsWith("#endif"))
				{
					active = stack.Pop().parent;
					continue;
				}
				if(!active) continue;
				var define = System.Text.RegularExpressions.Regex.Match(line, @"^#\s*define\s+(\w+)");
				if(define.Success) defines.Add(define.Groups[1].Value);
				output.Append(raw).Append('\n');
			}
			return output.ToString();
		}

		private static UserShaderProgram BuildModel(MaterialModel model, UserShaderDialect dialect = UserShaderDialect.UZDoom)
		{
			return UserShaderBuilder.Build(new GldefsMaterial { Shader = "shaders/test.fp" }, dialect, "void SetupMaterial(inout Material m) { SetMaterialProps(m, vTexCoord.st); }", model);
		}

		[Theory]
		[InlineData(0, 2)]
		[InlineData(1, 1)]
		[InlineData(2, 1)]
		public void DefaultModelProgramsGainNoLayerCode(int model, int dialect)
		{
			string active = ActiveSource(BuildModel((MaterialModel)model, (UserShaderDialect)dialect).FragmentSource);
			foreach(string name in new[] { "SPECULAR", "PBR", "NORMALMAP" })
				Assert.DoesNotContain("#define " + name + "\n", active);
			foreach(string name in new[] { "texture11", "texture12", "texture13", "texture14", "specularMaterial", "ProcessMaterialLight", "dFdx" })
				Assert.DoesNotContain(name, active);
			Assert.Contains("getDynLightContribution(", active);
		}

		[Theory]
		[InlineData(1, "#define speculartexture texture12")]
		[InlineData(2, "#define metallictexture texture12\n#define roughnesstexture texture13\n#define aotexture texture14")]
		public void LayeredModelsDefineTheirModelAndSamplers(int model, string samplers)
		{
			string active = ActiveSource(BuildModel((MaterialModel)model).FragmentSource);
			Assert.Contains(model == 1 ? "#define SPECULAR\n" : "#define PBR\n", active);
			Assert.DoesNotContain(model == 1 ? "#define PBR\n" : "#define SPECULAR\n", active);
			Assert.Contains("#define NORMALMAP\n", active);
			Assert.Contains("#define normaltexture texture11\n", active);
			Assert.Contains(samplers + "\n", active);
			Assert.Contains("uniform vec2 specularMaterial;", active);
			Assert.Contains("vec4 frag = vec4(ProcessMaterialLight(material, ProcessLight(material, baseLight).rgb)", active);
			Assert.DoesNotContain("getDynLightContribution(material.Base", active);
		}

		[Fact]
		public void PbrDropsAdditiveLightsAndSpecularKeepsThem()
		{
			string pbr = ActiveSource(UserShaderBuilder.BuildLayered(MaterialModel.Pbr).FragmentSource);
			string spec = ActiveSource(UserShaderBuilder.BuildLayered(MaterialModel.Specular).FragmentSource);
			Assert.Contains("if (lightColor[i].a < 0.4) continue;", pbr);
			Assert.DoesNotContain("addlight", pbr.Substring(pbr.IndexOf("vec3 ProcessMaterialLight")));
			Assert.Contains("addlight += lightColor[i].rgb * attenuation;", spec);
		}

		[Fact]
		public void ModelKeysAndBuiltInLayeredPrograms()
		{
			var material = new GldefsMaterial { Shader = "shaders/test.fp" };
			var keys = new[] { MaterialModel.Default, MaterialModel.Specular, MaterialModel.Pbr }.Select(model => UserShaderBuilder.Key(material, UserShaderDialect.UZDoom, model)).ToArray();
			Assert.Equal(3, keys.Distinct().Count());
			Assert.Equal(UserShaderBuilder.Key(material, UserShaderDialect.UZDoom), keys[0]);
			Assert.NotEqual(UserShaderBuilder.LayeredKey(MaterialModel.Specular), UserShaderBuilder.LayeredKey(MaterialModel.Pbr));
			var first = UserShaderBuilder.BuildLayered(MaterialModel.Specular);
			Assert.Equal(UserShaderBuilder.LayeredKey(MaterialModel.Specular), first.Path);
			Assert.Equal(first.FragmentSource, UserShaderBuilder.BuildLayered(MaterialModel.Specular).FragmentSource);
			Assert.Contains("material.Specular = texture(speculartexture", first.FragmentSource);
			Assert.Contains("material.AO = texture(aotexture", UserShaderBuilder.BuildLayered(MaterialModel.Pbr).FragmentSource);
		}

		[Fact]
		public void ShaderModelIsDefaultOutsideUZDoom()
		{
			var typed = new GldefsMaterial { Shader = "s", ShaderModel = MaterialModel.Specular };
			var layers = new GldefsMaterial { Shader = "s" };
			Assert.Equal(MaterialModel.Specular, MaterialModels.DeclaredShaderModel(typed, MaterialModel.Pbr, UserShaderDialect.UZDoom));
			Assert.Equal(MaterialModel.Specular, MaterialModels.DeclaredShaderModel(layers, MaterialModel.Specular, UserShaderDialect.UZDoom));
			Assert.Equal(MaterialModel.Default, MaterialModels.DeclaredShaderModel(typed, MaterialModel.Pbr, UserShaderDialect.Zandronum));
			Assert.Equal(MaterialModel.Default, MaterialModels.DeclaredShaderModel(layers, MaterialModel.Specular, UserShaderDialect.Zandronum));
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
