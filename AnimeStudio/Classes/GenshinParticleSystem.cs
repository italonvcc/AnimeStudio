namespace AnimeStudio
{
    // Preserve component identity and the common renderer material references. The
    // particle simulation modules and renderer-specific mesh fields remain raw.
    public sealed class GenshinParticleSystem : Component
    {
        public GenshinParticleSystem(ObjectReader reader) : base(reader) { }
    }
    public sealed class GenshinParticleSystemRenderer : Renderer
    {
        public GenshinParticleSystemRenderer(ObjectReader reader) : base(reader) { }
    }
    public sealed class GenshinTrailRenderer : Renderer
    {
        public GenshinTrailRenderer(ObjectReader reader) : base(reader) { }
    }
}
