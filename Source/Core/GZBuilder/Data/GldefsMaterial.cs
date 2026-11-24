using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.Rendering;

namespace CodeImp.DoomBuilder.GZBuilder.Data
{
	internal enum MaterialTarget { Any, Texture, Flat, Sprite }

	internal sealed class ResolvedMaterial
	{
		internal GldefsMaterial Definition;
		internal ImageData Brightmap;
		internal ImageData[] Textures;
		internal UserShaderProgram Program;
		internal MaterialModel Model;
		internal ImageData Normal, Specular, Metallic, Roughness, Ao;

		// The layer images the model samples, in texture unit order after the brightmap
		internal ImageData[] Layers
		{
			get
			{
				switch(Model)
				{
					case MaterialModel.Specular: return new[] { Normal, Specular };
					case MaterialModel.Pbr: return new[] { Normal, Metallic, Roughness, Ao };
					default: return new ImageData[0];
				}
			}
		}
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
		internal static ImageData SelectTarget(IList<ImageData> newestfirst, MaterialTarget target, bool overridable)
		{
			foreach(ImageData image in newestfirst)
			{
				if(target == MaterialTarget.Any || (overridable && image.AnimationUseType == 2 && image.TextureNamespace != TextureNamespace.SPRITE)) return image;
				if(target == MaterialTarget.Sprite && image.TextureNamespace == TextureNamespace.SPRITE) return image;
				if(image.TextureNamespace != TextureNamespace.SPRITE && ((target == MaterialTarget.Texture && image.AnimationUseType == 0) || (target == MaterialTarget.Flat && image.AnimationUseType == 1))) return image;
			}
			return newestfirst.Count > 0 ? newestfirst[0] : null;
		}

		// UZDoom's AddAutoMaterials search, minus detail and glow maps; the first brightmap prefix found wins
		private static readonly KeyValuePair<string, string>[] autolayers =
		{
			new KeyValuePair<string, string>("brightmap", "brightmaps/auto/"),
			new KeyValuePair<string, string>("brightmap", "materials/brightmaps/auto/"),
			new KeyValuePair<string, string>("normal", "materials/normalmaps/auto/"),
			new KeyValuePair<string, string>("specular", "materials/specular/auto/"),
			new KeyValuePair<string, string>("metallic", "materials/metallic/auto/"),
			new KeyValuePair<string, string>("roughness", "materials/roughness/auto/"),
			new KeyValuePair<string, string>("ao", "materials/ao/auto/"),
		};

		// Fills the layers a definition left empty from the auto paths; returns the material, new if one was needed
		internal static GldefsMaterial AddAutoLayers(GldefsMaterial material, string shortname, Func<string, string> find)
		{
			foreach(var layer in autolayers)
			{
				bool brightmap = layer.Key == "brightmap";
				if(material != null && (brightmap ? material.Brightmap != null : material.HasLayer(layer.Key))) continue;
				string path = find(layer.Value + shortname);
				if(path == null) continue;
				if(material == null) material = new GldefsMaterial();
				if(brightmap) material.Brightmap = path;
				else material.Layers[layer.Key] = path;
			}
			return material;
		}

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
		// UZDoom's defaults when a material block gives no value
		internal const float DefaultGlossiness = 10.0f, DefaultSpecularLevel = 0.1f;
		internal float? Glossiness, SpecularLevel;
		// The model a shader is written for; null means a material block's shader, typed from the layers
		internal MaterialModel? ShaderModel;
		internal readonly List<string> Defines = new List<string>();
		internal readonly List<KeyValuePair<string, string>> Textures = new List<KeyValuePair<string, string>>();
		// Image layer paths by GLDEFS key: brightmap, normal, specular, metallic, roughness, ao
		internal readonly Dictionary<string, string> Layers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		internal void Merge(GldefsMaterial definition)
		{
			if(definition.Brightmap != null || definition.DisableFullbright)
			{
				if(definition.Brightmap != null) Brightmap = definition.Brightmap;
				DisableFullbright = definition.DisableFullbright;
			}
			foreach(var layer in definition.Layers) Layers[layer.Key] = layer.Value;
			if(definition.Glossiness.HasValue) Glossiness = definition.Glossiness;
			if(definition.SpecularLevel.HasValue) SpecularLevel = definition.SpecularLevel;
			if(definition.Shader == null) return;
			Shader = definition.Shader;
			Speed = definition.Speed;
			DisableAlphaTest = definition.DisableAlphaTest;
			ShaderModel = definition.ShaderModel;
			Defines.Clear();
			Defines.AddRange(definition.Defines);
			Textures.Clear();
			Textures.AddRange(definition.Textures);
		}

		internal bool HasLayer(string name)
		{
			string path;
			return Layers.TryGetValue(name, out path) && !string.IsNullOrEmpty(path);
		}
	}
}
