using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.IO;
using System.Reflection;

namespace Builder.Tests
{
	[Collection("GeneralStatics")]
	public class CompilerOverrideTests
	{
		private static Dictionary<string, string> Overrides(params string[] pairs)
		{
			var result = new Dictionary<string, string>(StringComparer.Ordinal);
			for(int i = 0; i < pairs.Length; i += 2) result[pairs[i]] = pairs[i + 1];
			return result;
		}

		[Fact]
		public void ResolverPrefersAUsableOverride()
		{
			Assert.Equal("acc", CompilerInfo.ResolveProgram("zdoom_acc", "acc", null));
			Assert.Equal("acc", CompilerInfo.ResolveProgram("zdoom_acc", "acc", Overrides()));
			Assert.Equal("acc", CompilerInfo.ResolveProgram("zdoom_acc", "acc", Overrides("other", "/x/acc")));
			Assert.Equal("/opt/acc", CompilerInfo.ResolveProgram("zdoom_acc", "acc", Overrides("zdoom_acc", "/opt/acc")));
			Assert.Equal("acc", CompilerInfo.ResolveProgram("zdoom_acc", "acc", Overrides("zdoom_acc", "")));
			Assert.Equal("acc", CompilerInfo.ResolveProgram("zdoom_acc", "acc", Overrides("zdoom_acc", "/opt/a\0cc")));
			Assert.Equal("acc", CompilerInfo.ResolveProgram("zdoom_acc", "acc", Overrides("ZDOOM_ACC", "/opt/acc")));
		}

		private static Configuration Parse(string text)
		{
			Configuration cfg = new Configuration(true);
			Assert.True(cfg.InputConfiguration(text, true));
			return cfg;
		}

		[Fact]
		public void OverridesRoundTripThroughTheSettingsFile()
		{
			string path = Path.Combine(Path.GetTempPath(), "compiler-overrides-" + Guid.NewGuid().ToString("N") + ".cfg");
			try
			{
				Configuration cfg = Parse("currentversion = 1;\n");
				ProgramConfiguration.WriteCompilerOverrides(cfg, Overrides("zt-bcc", "/usr/bin/bcc-stock", "zdbsp", ""));
				Assert.True(cfg.SaveConfiguration(path));
				ProgramConfiguration config = new ProgramConfiguration();
				Assert.True(config.Load(path, path, path));
				Assert.Equal("/usr/bin/bcc-stock", Assert.Single(config.CompilerPrograms).Value);
				Assert.True(config.CompilerPrograms.ContainsKey("zt-bcc"));
			}
			finally { File.Delete(path); }
		}

		[Fact]
		public void NoOverridesLeaveNoKey()
		{
			Configuration cfg = Parse("compileroverrides { zt-bcc { program = \"/a\"; } }\n");
			ProgramConfiguration.WriteCompilerOverrides(cfg, Overrides("zt-bcc", ""));
			Assert.False(cfg.SettingExists("compileroverrides"));
		}

		[Fact]
		public void MalformedEntriesAreSkipped()
		{
			Configuration cfg = Parse("compileroverrides { plain = \"/a\"; nested { program = 3; } good { program = \"/b\"; } empty { program = \"\"; } }\n");
			var result = ProgramConfiguration.ReadCompilerOverrides(cfg);
			Assert.Equal("/b", Assert.Single(result).Value);
			Assert.Empty(ProgramConfiguration.ReadCompilerOverrides(Parse("compileroverrides = \"/a\";\n")));
		}

		private static ConfigurationInfo GameInfo(string gamecfg)
		{
			return (ConfigurationInfo)Activator.CreateInstance(typeof(ConfigurationInfo), BindingFlags.NonPublic | BindingFlags.Instance, null,
				new object[] { Parse(gamecfg), "TestGame.cfg" }, null);
		}

		[Fact]
		public void GameDefaultScriptCompilerCanBeOverridden()
		{
			string path = Path.Combine(Path.GetTempPath(), "script-compiler-" + Guid.NewGuid().ToString("N") + ".cfg");
			FieldInfo settings = typeof(General).GetField("settings", BindingFlags.NonPublic | BindingFlags.Static);
			object previous = settings.GetValue(null);
			try
			{
				File.WriteAllText(path, "currentversion = 1;\nconfigurations { testgame { defaultscriptcompiler = \"zt_bcs.cfg\"; } }\n");
				ProgramConfiguration config = new ProgramConfiguration();
				Assert.True(config.Load(path, path, path));
				settings.SetValue(null, config);
				const string key = "configurations.testgame.defaultscriptcompiler";

				ConfigurationInfo info = GameInfo("game = \"Test\";\ndefaultscriptcompiler = \"zdoom_acs.cfg\";\n");
				Assert.Equal("zdoom_acs.cfg", info.GameDefaultScriptCompiler);
				Assert.Equal("zt_bcs.cfg", info.DefaultScriptCompiler);

				ConfigurationInfo clone = info.Clone();
				Assert.Equal("zt_bcs.cfg", clone.DefaultScriptCompiler);
				clone.ScriptCompilerOverride = "zdoom_acs.cfg";
				Assert.Equal("", clone.ScriptCompilerOverride);
				info.Apply(clone);
				Assert.Equal("zdoom_acs.cfg", info.DefaultScriptCompiler);
				info.Changed = true;
				info.SaveSettings();
				Assert.False(config.Config.SettingExists(key));

				info.ScriptCompilerOverride = "hexen_acs.cfg";
				info.SaveSettings();
				Assert.Equal("hexen_acs.cfg", config.ReadSetting(key, ""));

				FieldInfo compiled = typeof(General).GetField("compiledscriptconfigs", BindingFlags.NonPublic | BindingFlags.Static);
				object installed = compiled.GetValue(null);
				try
				{
					compiled.SetValue(null, new Dictionary<string, ScriptConfiguration> { { "zdoom_acs.cfg", null } });
					Assert.Equal("zdoom_acs.cfg", info.DefaultScriptCompiler);
				}
				finally { compiled.SetValue(null, installed); }
			}
			finally
			{
				settings.SetValue(null, previous);
				File.Delete(path);
			}
		}
	}
}
