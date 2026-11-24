// Adapted from UZDoom's func_spec.fp. Copyright GZDoom and UZDoom Maintainers and Contributors. GPL-3.0-or-later.
void SetupMaterial(inout Material material)
{
    SetMaterialProps(material, vTexCoord.st);
    material.Specular = texture(speculartexture, vTexCoord.st).rgb;
    material.Glossiness = uSpecularMaterial.x;
    material.SpecularLevel = uSpecularMaterial.y;
}
