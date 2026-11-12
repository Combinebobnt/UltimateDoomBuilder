using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Data;

namespace CodeImp.DoomBuilder.GZBuilder.Data
{
	internal enum MaterialTarget { Any, Texture, Flat, Sprite }

	internal sealed class ResolvedMaterial
	{
		internal GldefsMaterial Definition;
		internal ImageData Brightmap;
	}

	internal sealed class MaterialLumpImage : ImageData
	{
		private readonly DataReader reader;
		private readonly string path;
		internal MaterialLumpImage(DataReader reader, string path)
		{
			this.reader = reader;
			this.path = path;
			SetName(path);
		}
		protected override LocalLoadResult LocalLoadImage()
		{
			using(var stream = reader.LoadFile(path))
			{
				var bitmap = stream == null ? null : ImageDataFormat.TryLoadImage(stream, ImageDataFormat.DOOMPICTURE, General.Map.Data.Palette);
				return new LocalLoadResult(bitmap, bitmap == null ? "Unable to read material image " + path : null, null);
			}
		}
	}

	internal sealed class GldefsMaterial
	{
		internal static bool IsGZDoom(string engine)
		{
			return string.Equals(engine, "gzdoom", StringComparison.OrdinalIgnoreCase) || string.Equals(engine, "uzdoom", StringComparison.OrdinalIgnoreCase);
		}

		internal MaterialTarget Target;
		internal string Name;
		internal bool Overridable;
		internal string Brightmap;
		internal bool DisableFullbright;
		internal string Shader;
		internal float Speed = 1.0f;
		internal bool DisableAlphaTest;
		internal string MaterialType = "normal";
		internal readonly List<string> Defines = new List<string>();
		internal readonly List<KeyValuePair<string, string>> Textures = new List<KeyValuePair<string, string>>();
		internal readonly Dictionary<string, string> Layers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		internal void Merge(GldefsMaterial definition)
		{
			if(definition.Brightmap != null) Brightmap = definition.Brightmap;
			DisableFullbright |= definition.DisableFullbright;
			foreach(var layer in definition.Layers) Layers[layer.Key] = layer.Value;
			if(definition.Shader == null) return;
			Shader = definition.Shader;
			Speed = definition.Speed;
			DisableAlphaTest = definition.DisableAlphaTest;
			MaterialType = definition.MaterialType;
			Defines.Clear();
			Defines.AddRange(definition.Defines);
			Textures.Clear();
			Textures.AddRange(definition.Textures);
		}
	}
}
