namespace AnimeStudio
{
    // Preserve component identity and the common renderer material references. The
    // particle simulation modules remain raw until their verified decoder runs.
    public sealed class GenshinParticleSystem : Component
    {
        public GenshinParticleSystem(ObjectReader reader) : base(reader) { }
    }
    public sealed class GenshinParticleSystemRenderer : Renderer
    {
        public PPtr<Mesh>[] m_Meshes;

        public GenshinParticleSystemRenderer(ObjectReader reader) : base(reader)
        {
            // Genshin 2017.4.30f1's 396-byte renderer stores four mesh PPtrs
            // after the vertex-stream list. The first slot is used by Idle's
            // Bubble emitter; reading it makes dependency resolution source-keyed.
            if (reader.version[0] == 2017 && reader.version[1] == 4 &&
                reader.byteSize == 396 && reader.serializedType?.Match("EC270FAF17AE20EA92C009F528E47167") == true)
            {
                reader.Position = reader.byteStart + 332;
                m_Meshes = new PPtr<Mesh>[4];
                for (int i = 0; i < m_Meshes.Length; i++) m_Meshes[i] = new PPtr<Mesh>(reader);
            }
        }
    }
    public sealed class GenshinTrailRenderer : Renderer
    {
        public GenshinTrailRenderer(ObjectReader reader) : base(reader) { }
    }
}
