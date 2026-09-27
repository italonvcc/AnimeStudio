namespace AnimeStudio
{
    // GI uses the Texture2D image header with six source faces. Keep a separate
    // type so a generic Texture2D exporter cannot silently export only face zero.
    public sealed class Cubemap : Texture
    {
        public Texture2D ImageLayout { get; }
        public string LayoutError { get; }
        public Cubemap(ObjectReader reader) : base(reader)
        {
            try
            {
                reader.Reset();
                var layout = new Texture2D(reader);
                if (layout.m_ImageCount != 6 || layout.m_Width != layout.m_Height || layout.m_Width is < 1 or > 8192 ||
                    layout.m_MipCount is < 1 or > 15 || layout.image_data.Size is < 1 or > 512 * 1024 * 1024 || layout.image_data.Size % 6 != 0)
                    throw new System.IO.InvalidDataException($"Unsupported cubemap image layout: {layout.m_Width}x{layout.m_Height}, {layout.m_TextureFormat}, {layout.m_MipCount} mips, {layout.m_ImageCount} faces, {layout.image_data.Size} bytes.");
                ImageLayout = layout;
            }
            catch (System.Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or System.ArgumentException or System.OverflowException)
            {
                LayoutError = ex.Message;
            }
        }
    }
}
