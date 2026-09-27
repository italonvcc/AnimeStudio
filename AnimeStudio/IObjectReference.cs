using System;

namespace AnimeStudio
{
    public interface IObjectReference
    {
        long PathID { get; }
        string SerializedFileName { get; }
        Type ObjectType { get; }
        bool IsNull { get; }
        bool TryGetObject(out Object value);
    }
}
