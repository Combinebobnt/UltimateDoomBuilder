// Adapted from UZDoom's func_pbr.fp. Copyright GZDoom and UZDoom Maintainers and Contributors. GPL-3.0-or-later.
void SetupMaterial(inout Material material)
{
    SetMaterialProps(material, vTexCoord.st);
    material.Metallic = texture(metallictexture, vTexCoord.st).r;
    material.Roughness = texture(roughnesstexture, vTexCoord.st).r;
    material.AO = texture(aotexture, vTexCoord.st).r;
}
