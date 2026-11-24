#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CodeImp.DoomBuilder.IO;

#endregion

namespace CodeImp.DoomBuilder.Compilers
{
	internal enum LightmapOptionTab { Lighting, Quality, Run, Output }

	// Where an option's value lives
	internal enum LightmapOptionKind
	{
		MapKey,         // a UDMF key on the ZDRayInfo thing (9890)
		ThingAngle,     // the ZDRayInfo thing's angle
		ThingPitch,     // the ZDRayInfo thing's pitch
		CliFlag,        // a baker command line flag
		MapSetting,     // the map's .dbs, under lightmapbake.
		ProgramSetting, // the program configuration
		ReadOnly,       // shown, never written
	}

	internal enum LightmapOptionType { Bool, Int, Float, Colour, Choice, Text }

	// One entry of the Bake Lightmaps dialog. The dialog builds its rows from the registry, and the
	// baker's arguments come from its CliFlag entries.
	internal sealed class LightmapBakeOption
	{
		public readonly string Id;
		public readonly LightmapOptionTab Tab;
		public readonly string Label;
		public readonly LightmapOptionKind Kind;
		public readonly LightmapOptionType Type;
		public readonly object Default;
		public readonly double Min;
		public readonly double Max;
		public readonly string Feature; // the --features token it needs, null when stock ZDRay has it
		public readonly string Key;     // map key, flag, or setting name

		public LightmapBakeOption(string id, LightmapOptionTab tab, string label, LightmapOptionKind kind, LightmapOptionType type,
			object def, double min, double max, string feature, string key)
		{
			Id = id;
			Tab = tab;
			Label = label;
			Kind = kind;
			Type = type;
			Default = def;
			Min = min;
			Max = max;
			Feature = feature;
			Key = key;
		}

		public bool Available(LightmapBakerFeatures features)
		{
			return Feature == null || features.Has(Feature);
		}
	}

	internal static class LightmapBakeOptions
	{
		public const string FEATURE_SUNINTENSITY = "sunintensity";
		public const string FEATURE_SAMPLESCALE = "sample-scale";
		public const string FEATURE_SIZE4096 = "size4096";
		public const string FEATURE_SAMPLES = "samples";

		private static LightmapBakeOption O(string id, LightmapOptionTab tab, string label, LightmapOptionKind kind, LightmapOptionType type,
			object def, double min, double max, string feature, string key)
		{
			return new LightmapBakeOption(id, tab, label, kind, type, def, min, max, feature, key);
		}

		private const LightmapOptionTab L = LightmapOptionTab.Lighting, Q = LightmapOptionTab.Quality, R = LightmapOptionTab.Run;

		public static readonly LightmapBakeOption[] All =
		{
			O("sun", L, "Sun", LightmapOptionKind.MapKey, LightmapOptionType.Bool, true, 0, 1, null, "lm_sunintensity"),
			O("suncolor", L, "Sun colour:", LightmapOptionKind.MapKey, LightmapOptionType.Colour, "ffffff", 0, 0, null, "lm_suncolor"),
			O("sunintensity", L, "Sun intensity:", LightmapOptionKind.MapKey, LightmapOptionType.Float, 1.0, 0, 16, FEATURE_SUNINTENSITY, "lm_sunintensity"),
			O("sunangle", L, "Sun angle:", LightmapOptionKind.ThingAngle, LightmapOptionType.Int, 214, 0, 359, null, "angle"),
			O("sunelevation", L, "Sun elevation:", LightmapOptionKind.ThingPitch, LightmapOptionType.Int, 59, -90, 90, null, "pitch"),
			O("sampledistance", L, "Default sample distance:", LightmapOptionKind.MapKey, LightmapOptionType.Int, 8, 8, 128, null, "lm_sampledistance"),
			O("gridsize", L, "Light probe grid size:", LightmapOptionKind.MapKey, LightmapOptionType.Float, 32.0, 1, 1024, null, "lm_gridsize"),
			O("bounce", L, "Light bounces:", LightmapOptionKind.MapKey, LightmapOptionType.Int, 0, 0, 8, "bounce", "lm_bounce"),
			O("ao", L, "Ambient occlusion", LightmapOptionKind.MapKey, LightmapOptionType.Bool, true, 0, 1, "ao", "lm_ao"),
			O("skycolor", L, "Sky light colour:", LightmapOptionKind.MapKey, LightmapOptionType.Colour, "ffffff", 0, 0, "skylight", "lm_skycolor"),
			O("skyintensity", L, "Sky light intensity:", LightmapOptionKind.MapKey, LightmapOptionType.Float, 0.0, 0, 16, "skylight", "lm_skyintensity"),
			O("emissive", L, "Emissive textures:", LightmapOptionKind.ReadOnly, LightmapOptionType.Text, null, 0, 0, "emissive-table", null),

			O("preset", Q, "Quality preset:", LightmapOptionKind.MapSetting, LightmapOptionType.Choice, "final", 0, 0, FEATURE_SAMPLES, "preset"),
			O("samplescale", Q, "Sample distance scale:", LightmapOptionKind.CliFlag, LightmapOptionType.Float, 1.0, 0.25, 16, FEATURE_SAMPLESCALE, "--sample-scale"),
			O("samples", Q, "Shadow samples:", LightmapOptionKind.CliFlag, LightmapOptionType.Int, 256, 1, 4096, FEATURE_SAMPLES, "--samples"),
			O("bouncesamples", Q, "Bounce samples:", LightmapOptionKind.CliFlag, LightmapOptionType.Int, 2048, 1, 65536, FEATURE_SAMPLES, "--bounce-samples"),
			O("skysamples", Q, "Sky samples:", LightmapOptionKind.MapKey, LightmapOptionType.Int, 256, 16, 1024, "skylight", "lm_skysamples"),
			O("texturesize", Q, "Lightmap texture size:", LightmapOptionKind.MapSetting, LightmapOptionType.Choice, LightmapBakeSettings.DEFAULT_TEXTURE_SIZE, 128, 4096, null, "texturesize"),

			O("bakerpath", R, "ZDRay executable:", LightmapOptionKind.ProgramSetting, LightmapOptionType.Text, "", 0, 0, null, "lightmapbaker.path"),
			O("threads", R, "Threads (0 = automatic):", LightmapOptionKind.ProgramSetting, LightmapOptionType.Int, 0, 0, 256, null, "lightmapbaker.threads"),
			O("cpuonly", R, "CPU ray tracing only (skip the Vulkan GPU tracer)", LightmapOptionKind.ProgramSetting, LightmapOptionType.Bool, false, 0, 1, null, "lightmapbaker.cpuonly"),
			O("autobake", R, "Rebake automatically when stale:", LightmapOptionKind.MapSetting, LightmapOptionType.Choice, "off", 0, 0, null, "autobake"),
			O("estimate", R, "Estimate:", LightmapOptionKind.ReadOnly, LightmapOptionType.Text, null, 0, 0, null, null),
		};

		public static LightmapBakeOption Get(string id)
		{
			return All.First(o => o.Id == id);
		}

		public static IEnumerable<LightmapBakeOption> OnTab(LightmapOptionTab tab)
		{
			return All.Where(o => o.Tab == tab);
		}

		// The flags of every CliFlag option whose feature the baker has and whose value isn't its default
		public static List<string> Flags(LightmapBakeQuality quality, LightmapBakerFeatures features)
		{
			List<string> args = new List<string>();
			foreach(LightmapBakeOption o in All)
			{
				if(o.Kind != LightmapOptionKind.CliFlag || !o.Available(features)) continue;
				double value = quality.Value(o.Id);
				if(value == Convert.ToDouble(o.Default, CultureInfo.InvariantCulture)) continue;
				args.Add(o.Key);
				args.Add(value.ToString(o.Type == LightmapOptionType.Int ? "0" : "0.###", CultureInfo.InvariantCulture));
			}
			return args;
		}
	}

	#region ================== Quality

	internal enum LightmapQualityPreset { Draft, Normal, Final, Custom }

	// The quality values a bake runs (or ran) with
	internal sealed class LightmapBakeQuality
	{
		// Sample distance scale, shadow samples, bounce samples per preset; Final is the baker's default
		private static readonly Dictionary<LightmapQualityPreset, double[]> PRESETS = new Dictionary<LightmapQualityPreset, double[]>
		{
			{ LightmapQualityPreset.Draft, new double[] { 4, 16, 128 } },
			{ LightmapQualityPreset.Normal, new double[] { 2, 64, 512 } },
			{ LightmapQualityPreset.Final, new double[] { 1, 256, 2048 } },
		};

		public const int QUICK_BAKE_MAX_SIZE = 512;

		public float SampleScale = 1.0f;
		public int Samples = 256;
		public int BounceSamples = 2048;
		public int TextureSize = LightmapBakeSettings.DEFAULT_TEXTURE_SIZE;

		public LightmapQualityPreset Preset
		{
			get
			{
				foreach(KeyValuePair<LightmapQualityPreset, double[]> p in PRESETS)
					if(SampleScale == p.Value[0] && Samples == p.Value[1] && BounceSamples == p.Value[2]) return p.Key;
				return LightmapQualityPreset.Custom;
			}
		}

		public static LightmapBakeQuality FromPreset(LightmapQualityPreset preset, int texturesize)
		{
			LightmapBakeQuality q = new LightmapBakeQuality { TextureSize = texturesize };
			double[] values;
			if(PRESETS.TryGetValue(preset, out values))
			{
				q.SampleScale = (float)values[0];
				q.Samples = (int)values[1];
				q.BounceSamples = (int)values[2];
			}
			return q;
		}

		// Quick bake: the Draft values with the texture size capped
		public static LightmapBakeQuality Quick(int texturesize)
		{
			return FromPreset(LightmapQualityPreset.Draft, Math.Min(texturesize, QUICK_BAKE_MAX_SIZE));
		}

		public LightmapBakeQuality Clone()
		{
			return (LightmapBakeQuality)MemberwiseClone();
		}

		public double Value(string id)
		{
			switch(id)
			{
				case "samplescale": return SampleScale;
				case "samples": return Samples;
				case "bouncesamples": return BounceSamples;
				case "texturesize": return TextureSize;
				default: throw new ArgumentException("Not a quality option: " + id);
			}
		}

		// What the baker will actually do: a value it has no flag for runs at Final, a size past 1024 at 1024
		public LightmapBakeQuality Effective(LightmapBakerFeatures features)
		{
			LightmapBakeQuality q = Clone();
			if(!features.Has(LightmapBakeOptions.FEATURE_SAMPLESCALE)) q.SampleScale = 1.0f;
			if(!features.Has(LightmapBakeOptions.FEATURE_SAMPLES))
			{
				q.Samples = 256;
				q.BounceSamples = 2048;
			}
			if(!features.Has(LightmapBakeOptions.FEATURE_SIZE4096)) q.TextureSize = Math.Min(q.TextureSize, 1024);
			return q;
		}

		// A coarser sample distance or fewer rays than the target; the texture size is only packing
		public bool IsBelow(LightmapBakeQuality target)
		{
			return SampleScale > target.SampleScale || Samples < target.Samples || BounceSamples < target.BounceSamples;
		}

		public string Serialize()
		{
			return string.Format(CultureInfo.InvariantCulture, "preset={0};scale={1};samples={2};bounce={3};size={4}",
				Preset.ToString().ToLowerInvariant(), SampleScale, Samples, BounceSamples, TextureSize);
		}

		// Null when the text isn't one Serialize wrote
		public static LightmapBakeQuality Parse(string text)
		{
			if(string.IsNullOrEmpty(text)) return null;
			Dictionary<string, string> fields = new Dictionary<string, string>();
			foreach(string part in text.Split(';'))
			{
				int eq = part.IndexOf('=');
				if(eq <= 0) return null;
				fields[part.Substring(0, eq).Trim()] = part.Substring(eq + 1).Trim();
			}

			LightmapBakeQuality q = new LightmapBakeQuality();
			float scale;
			int samples, bounce, size;
			if(!fields.ContainsKey("scale") || !float.TryParse(fields["scale"], NumberStyles.Float, CultureInfo.InvariantCulture, out scale)) return null;
			if(!fields.ContainsKey("samples") || !int.TryParse(fields["samples"], NumberStyles.Integer, CultureInfo.InvariantCulture, out samples)) return null;
			if(!fields.ContainsKey("bounce") || !int.TryParse(fields["bounce"], NumberStyles.Integer, CultureInfo.InvariantCulture, out bounce)) return null;
			if(!fields.ContainsKey("size") || !int.TryParse(fields["size"], NumberStyles.Integer, CultureInfo.InvariantCulture, out size)) return null;
			if(scale <= 0 || samples <= 0 || bounce <= 0 || size <= 0) return null;
			q.SampleScale = scale;
			q.Samples = samples;
			q.BounceSamples = bounce;
			q.TextureSize = size;
			return q;
		}

		public string Describe()
		{
			string name = Preset.ToString();
			if(Preset == LightmapQualityPreset.Custom)
				name += string.Format(CultureInfo.InvariantCulture, " (scale {0}, {1} shadow, {2} bounce samples)", SampleScale, Samples, BounceSamples);
			return name + ", " + TextureSize + "x" + TextureSize;
		}
	}

	#endregion

	#region ================== Per map settings

	internal enum LightmapAutoBake { Off, OnTest, OnSaveAndTest }

	// The map's own bake settings, kept in its .dbs under lightmapbake.
	internal sealed class LightmapBakeMapSettings
	{
		private const string PREFIX = "lightmapbake.";
		private static readonly string[] AUTOBAKE_NAMES = { "off", "test", "saveandtest" };

		public LightmapBakeQuality Quality; // null: the program-wide values apply
		public LightmapAutoBake AutoBake = LightmapAutoBake.Off;
		public string LastQuality;          // the quality of the LIGHTMAP last saved with the map
		public string LastSha;              // that lump's SHA-256

		public static LightmapBakeMapSettings Read(Configuration cfg)
		{
			LightmapBakeMapSettings s = new LightmapBakeMapSettings();
			int size = cfg.ReadSetting(PREFIX + "texturesize", 0);
			if(size > 0)
			{
				s.Quality = new LightmapBakeQuality
				{
					TextureSize = Array.IndexOf(LightmapBakeSettings.TEXTURE_SIZES, size) < 0 ? LightmapBakeSettings.DEFAULT_TEXTURE_SIZE : size,
					SampleScale = Clamp(cfg.ReadSetting(PREFIX + "samplescale", 1.0f), 0.25f, 16f),
					Samples = Math.Max(1, Math.Min(cfg.ReadSetting(PREFIX + "samples", 256), 4096)),
					BounceSamples = Math.Max(1, Math.Min(cfg.ReadSetting(PREFIX + "bouncesamples", 2048), 65536)),
				};
			}
			int autobake = Array.IndexOf(AUTOBAKE_NAMES, cfg.ReadSetting(PREFIX + "autobake", "off"));
			s.AutoBake = (autobake < 0 ? LightmapAutoBake.Off : (LightmapAutoBake)autobake);
			s.LastQuality = NullIfEmpty(cfg.ReadSetting(PREFIX + "lastquality", ""));
			s.LastSha = NullIfEmpty(cfg.ReadSetting(PREFIX + "lastsha", ""));
			return s;
		}

		public void Write(Configuration cfg)
		{
			cfg.DeleteSetting("lightmapbake");
			if(Quality != null)
			{
				cfg.WriteSetting(PREFIX + "preset", Quality.Preset.ToString().ToLowerInvariant());
				cfg.WriteSetting(PREFIX + "texturesize", Quality.TextureSize);
				cfg.WriteSetting(PREFIX + "samplescale", Quality.SampleScale);
				cfg.WriteSetting(PREFIX + "samples", Quality.Samples);
				cfg.WriteSetting(PREFIX + "bouncesamples", Quality.BounceSamples);
			}
			if(AutoBake != LightmapAutoBake.Off) cfg.WriteSetting(PREFIX + "autobake", AUTOBAKE_NAMES[(int)AutoBake]);
			if(LastQuality != null && LastSha != null)
			{
				cfg.WriteSetting(PREFIX + "lastquality", LastQuality);
				cfg.WriteSetting(PREFIX + "lastsha", LastSha);
			}
		}

		// The loaded lump's quality: known only when the lump is the one last saved with this map
		public LightmapBakeQuality QualityOfLoadedLump(string loadedsha)
		{
			if(loadedsha == null || LastSha == null || !string.Equals(loadedsha, LastSha, StringComparison.OrdinalIgnoreCase)) return null;
			return LightmapBakeQuality.Parse(LastQuality);
		}

		private static float Clamp(float v, float min, float max) { return (v < min ? min : (v > max ? max : v)); }
		private static string NullIfEmpty(string s) { return string.IsNullOrEmpty(s) ? null : s; }
	}

	#endregion

	#region ================== Baker features

	// What a ZDRay build supports beyond upstream, from its --features output
	internal sealed class LightmapBakerFeatures
	{
		public const string HEADER = "zdray-features 1";
		private const int PROBE_TIMEOUT_MS = 3000;

		public static readonly LightmapBakerFeatures None = new LightmapBakerFeatures(new string[0]);

		private static readonly Dictionary<string, LightmapBakerFeatures> cache = new Dictionary<string, LightmapBakerFeatures>();
		private static readonly object cachelock = new object();

		private readonly HashSet<string> tokens;

		private LightmapBakerFeatures(IEnumerable<string> tokens)
		{
			this.tokens = new HashSet<string>(tokens, StringComparer.Ordinal);
		}

		public bool Has(string token) { return tokens.Contains(token); }
		public bool IsEmpty { get { return tokens.Count == 0; } }

		public override string ToString()
		{
			return tokens.Count == 0 ? "none (stock ZDRay)" : string.Join(" ", tokens.OrderBy(t => t, StringComparer.Ordinal));
		}

		// Keys only on the header line: stock ZDRay answers an unknown option with its usage hint and exit 0
		public static LightmapBakerFeatures Parse(string stdout)
		{
			if(string.IsNullOrEmpty(stdout)) return None;
			string[] lines = stdout.Replace("\r", "").Split('\n');
			int header = Array.FindIndex(lines, l => l.Trim() == HEADER);
			if(header < 0) return None;
			return new LightmapBakerFeatures(lines.Skip(header + 1).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.Contains(' ')));
		}

		// Runs path --features, cached by path, size and modification time. Blocks up to 3 s: call it off the UI thread.
		public static LightmapBakerFeatures Probe(string path)
		{
			if(string.IsNullOrEmpty(path) || !File.Exists(path)) return None;
			FileInfo info = new FileInfo(path);
			string key = info.FullName + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
			lock(cachelock)
			{
				LightmapBakerFeatures cached;
				if(cache.TryGetValue(key, out cached)) return cached;
			}

			LightmapBakerFeatures result = None;
			try
			{
				ProcessStartInfo start = new ProcessStartInfo
				{
					FileName = path,
					Arguments = "--features",
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
				};
				using(Process p = Process.Start(start))
				{
					StringBuilder output = new StringBuilder();
					using(System.Threading.ManualResetEvent eof = new System.Threading.ManualResetEvent(false))
					{
						p.OutputDataReceived += (s, e) => { if(e.Data != null) { lock(output) output.Append(e.Data).Append('\n'); } else try { eof.Set(); } catch(ObjectDisposedException) { } };
						p.ErrorDataReceived += (s, e) => { };
						p.BeginOutputReadLine();
						p.BeginErrorReadLine();
						if(!p.WaitForExit(PROBE_TIMEOUT_MS))
						{
							try { p.Kill(); } catch(InvalidOperationException) { }
							General.WriteLogLine("Lightmap baker features: probe timed out, treating as none (stock ZDRay)");
							return None;
						}
						// A grandchild holding stdout open (a wrapper script) only gets this long to let go
						eof.WaitOne(PROBE_TIMEOUT_MS);
						lock(output) result = Parse(output.ToString());
					}
				}
			}
			catch(Exception e)
			{
				General.WriteLogLine("Lightmap baker features: probe failed (" + e.GetType().Name + ": " + e.Message + ")");
				return None;
			}

			General.WriteLogLine("Lightmap baker features: " + result);
			lock(cachelock) cache[key] = result;
			return result;
		}
	}

	#endregion
}
