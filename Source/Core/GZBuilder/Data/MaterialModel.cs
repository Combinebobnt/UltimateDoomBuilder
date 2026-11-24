using CodeImp.DoomBuilder.Rendering;

namespace CodeImp.DoomBuilder.GZBuilder.Data
{
	// The light models UZDoom runs a texture's layers through
	internal enum MaterialModel { Default, Specular, Pbr }

	internal static class MaterialModels
	{
		// UZDoom's FMaterial constructor: Specular needs normal and specular layers and wins over PBR,
		// which needs normal, metallic, roughness and AO. A normal layer alone stays Default.
		internal static MaterialModel Select(bool normal, bool specular, bool metallic, bool roughness, bool ao, UserShaderDialect dialect)
		{
			if(dialect != UserShaderDialect.UZDoom || !normal) return MaterialModel.Default;
			if(specular) return MaterialModel.Specular;
			return metallic && roughness && ao ? MaterialModel.Pbr : MaterialModel.Default;
		}

		internal static MaterialModel Select(GldefsMaterial material, UserShaderDialect dialect)
		{
			return Select(material.HasLayer("normal"), material.HasLayer("specular"), material.HasLayer("metallic"),
				material.HasLayer("roughness"), material.HasLayer("ao"), dialect);
		}

		// UZDoom applies a user shader only to a texture whose model matches the shader's type; Zandronum has one model
		internal static bool ShaderApplies(GldefsMaterial material, MaterialModel model, UserShaderDialect dialect)
		{
			return dialect != UserShaderDialect.UZDoom || (material.ShaderModel ?? model) == model;
		}

		// A HardwareShader block's "material" keyword; null for an unknown type
		internal static MaterialModel? FromShaderType(string type)
		{
			switch(type)
			{
				case "normal": case "brightmap": return MaterialModel.Default;
				case "specular": case "specularbrightmap": return MaterialModel.Specular;
				case "pbr": case "pbrbrightmap": return MaterialModel.Pbr;
				default: return null;
			}
		}
	}
}
