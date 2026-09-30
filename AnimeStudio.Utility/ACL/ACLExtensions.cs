using System;
using ACLLibs;

namespace AnimeStudio
{
    public static class ACLExtensions
    {
        public static void Process(this ACLClip m_ACLClip, Game game, out float[] values, out float[] times) 
        {
            if (game.Type.IsSRGroup())
            {
                var aclClip = m_ACLClip as MHYACLClip;
                SRACL.DecompressClip(aclClip.m_ClipData, out values, out times);
            }
            else
            {
                switch (m_ACLClip)
                {
                    case GIACLClip giaclClip:
                        if (giaclClip.m_ClipData.Length < 12)
                            throw new System.IO.InvalidDataException("Genshin ACL clip is missing a compressed buffer header.");
                        uint tag = BitConverter.ToUInt32(giaclClip.m_ClipData, 8);
                        if (tag == 0xAC10AC10)
                        {
                            if (giaclClip.m_DatabaseData.Length != 0)
                                throw new System.IO.InvalidDataException("Legacy Genshin ACL clip unexpectedly has database data.");
                            ACL.DecompressGenshinLegacyClip(giaclClip.m_ClipData, out values, out times);
                        }
                        else if (tag == 0xAC11AC11)
                            DBACL.DecompressTracks(giaclClip.m_ClipData, giaclClip.m_DatabaseData, out values, out times, isGenshin: true);
                        else
                            throw new System.IO.InvalidDataException($"Unsupported Genshin ACL buffer tag 0x{tag:X8}.");
                        if (times.Length == 0 || values.LongLength != (long)times.Length * giaclClip.CurveCount)
                            throw new System.IO.InvalidDataException("Genshin ACL sample count does not match the clip's curve count.");
                        break;
                    case ZZZACLClip zzzaclClip:
                        DBACL.DecompressTracksV2(zzzaclClip.m_TransformData, zzzaclClip.m_ScalarData, zzzaclClip.m_databaseData, zzzaclClip.m_DatabaseData, out values, out times);
                        break;
                    case MHYACLClip mhyaclClip:
                        if (game.Type.IsZZZ())
                        {
                            DBACL.DecompressTracks(mhyaclClip.m_ClipData, mhyaclClip.m_databaseData, out values, out times, true);
                        }
                        else
                        {
                            ACL.DecompressClip(mhyaclClip.m_ClipData, out values, out times);
                        }
                        break;
                    default:
                        values = Array.Empty<float>();
                        times = Array.Empty<float>();
                        break;
                }
            }
        }
    }
}
