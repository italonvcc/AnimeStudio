using System;
using System.Runtime.InteropServices;
using AnimeStudio.PInvoke;

namespace ACLLibs
{
    public struct DecompressedClip
    {
        public IntPtr Values;
        public int ValuesCount;
        public IntPtr Times;
        public int TimesCount;
    }
    public static class ACL
    {
        private const string DLL_NAME = "AnimeStudio.ACL.MHY";
        static ACL()
        {
            // The x64-only MHY decoder is packaged beside the application,
            // like the SR/DB decoders, rather than under an x64 subfolder.
            DllLoader.PreloadDll(DLL_NAME, archSpecific: false);
        }
        public static void DecompressClip(byte[] data, out float[] values, out float[] times)
        {
            var decompressedClip = new DecompressedClip();
            DecompressClip(data, ref decompressedClip);

            values = new float[decompressedClip.ValuesCount];
            Marshal.Copy(decompressedClip.Values, values, 0, decompressedClip.ValuesCount);

            times = new float[decompressedClip.TimesCount];
            Marshal.Copy(decompressedClip.Times, times, 0, decompressedClip.TimesCount);

            Dispose(ref decompressedClip);
        }

        // The older Genshin clips are ACL 1.x uniformly sampled CompressedClip
        // buffers, not the database-backed CompressedTracks buffers. Validate the
        // vendored MHY decoder's header before passing source bytes to native code.
        public static void DecompressGenshinLegacyClip(byte[] data, out float[] values, out float[] times)
        {
            GenshinLegacyAclHeader.Validate(data, out int expectedValues, out int expectedTimes);
            IntPtr allocation = Marshal.AllocHGlobal(checked(data.Length + 15));
            var clip = new DecompressedClip();
            try
            {
                IntPtr aligned = new((allocation.ToInt64() + 15) & ~15L);
                Marshal.Copy(data, 0, aligned, data.Length);
                DecompressAlignedClip(aligned, ref clip);
                if (clip.ValuesCount != expectedValues || clip.TimesCount != expectedTimes ||
                    clip.Values == IntPtr.Zero || clip.Times == IntPtr.Zero)
                    throw new System.IO.InvalidDataException("Genshin legacy ACL output does not match its validated header.");
                values = new float[expectedValues];
                times = new float[expectedTimes];
                Marshal.Copy(clip.Values, values, 0, expectedValues);
                Marshal.Copy(clip.Times, times, 0, expectedTimes);
            }
            finally
            {
                Dispose(ref clip);
                Marshal.FreeHGlobal(allocation);
            }
        }

        #region importfunctions

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void DecompressClip(byte[] data, ref DecompressedClip decompressedClip);

        [DllImport(DLL_NAME, EntryPoint = "DecompressClip", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DecompressAlignedClip(nint data, ref DecompressedClip decompressedClip);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Dispose(ref DecompressedClip decompressedClip);

        #endregion
    }

    // Managed-only validation is separate from ACL's native DLL initializer so
    // source header fixtures can be checked without loading a decoder.
    public static class GenshinLegacyAclHeader
    {
        public static void Validate(byte[] data, out int valueCount, out int sampleCount)
        {
            valueCount = sampleCount = 0;
            // Offsets follow CompressedClip (16 bytes) and ClipHeader in the
            // vendored AnimeStudio.ACL.MHY/acl/core/compressed_clip.h.
            if (data == null || data.Length < 48 || BitConverter.ToUInt32(data, 8) != 0xAC10AC10 ||
                BitConverter.ToUInt16(data, 12) != 3 || data[14] != 0 || data[15] != 0)
                throw new System.IO.InvalidDataException("Unsupported Genshin legacy ACL clip header.");
            uint size = BitConverter.ToUInt32(data, 0);
            ushort bones = BitConverter.ToUInt16(data, 16);
            ushort segments = BitConverter.ToUInt16(data, 18);
            uint samples = BitConverter.ToUInt32(data, 28);
            uint rate = BitConverter.ToUInt32(data, 32);
            long count = (long)bones * 10 * samples;
            if (size < 48 || size > data.Length || bones == 0 || segments == 0 ||
                samples == 0 || rate == 0 || samples > 1_000_000 || count > 20_000_000)
                throw new System.IO.InvalidDataException("Invalid or unbounded Genshin legacy ACL clip dimensions.");
            // The five PtrOffset16 fields are relative to ClipHeader at byte 16.
            // SegmentHeader has five uint32 fields (20 bytes) in the vendored API.
            ushort segmentOffset = BitConverter.ToUInt16(data, 36);
            if (segmentOffset is 0 or ushort.MaxValue ||
                16L + segmentOffset + (long)segments * 20 > size)
                throw new System.IO.InvalidDataException("Genshin legacy ACL segment headers are out of bounds.");
            for (int field = 38; field <= 44; field += 2)
            {
                ushort relative = BitConverter.ToUInt16(data, field);
                if (relative != 0 && relative != ushort.MaxValue && 16L + relative >= size)
                    throw new System.IO.InvalidDataException("Genshin legacy ACL data offset is out of bounds.");
            }
            valueCount = checked((int)count);
            sampleCount = checked((int)samples);
        }
    }

    public static class SRACL
    {
        private const string DLL_NAME = "AnimeStudio.ACL.SR";
        static SRACL()
        {
            // x64 only, so it lives in the application directory rather than in x86/x64.
            DllLoader.PreloadDll(DLL_NAME, archSpecific: false);
        }
        public static void DecompressClip(byte[] data, out float[] values, out float[] times)
        {
            int alignment = 16;
            IntPtr raw = Marshal.AllocHGlobal(data.Length + alignment);
            IntPtr aligned = new IntPtr((raw.ToInt64() + alignment - 1) & ~(alignment - 1));
            var decompressedClip = new DecompressedClip();

            try
            {
                Marshal.Copy(data, 0, aligned, data.Length);
                DecompressClip(aligned, ref decompressedClip);
            } finally
            {
                Marshal.FreeHGlobal(raw);
            }

            values = new float[decompressedClip.ValuesCount];
            Marshal.Copy(decompressedClip.Values, values, 0, decompressedClip.ValuesCount);

            times = new float[decompressedClip.TimesCount];
            Marshal.Copy(decompressedClip.Times, times, 0, decompressedClip.TimesCount);

            Dispose(ref decompressedClip);
        }

        #region importfunctions

        // This one is the acl 1.x uniformly-sampled decoder; its export is named DecompressClip.
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void DecompressClip(nint data, ref DecompressedClip decompressedClip);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Dispose(ref DecompressedClip decompressedClip);

        #endregion
    }

    public static class DBACL
    {
        private const string DLL_NAME = "AnimeStudio.ACL.DB";
        private const string DLL_NAME_ZZZ = "AnimeStudio.ACL.ZZZ";
        private const string DLL_NAME_ZZZ_V2 = "AnimeStudio.ACL.ZZZV2";
        static DBACL()
        {
            // x64 only, so they live in the application directory rather than in x86/x64.
            DllLoader.PreloadDll(DLL_NAME, archSpecific: false);
            DllLoader.PreloadDll(DLL_NAME_ZZZ, archSpecific: false);
        }
        private static IntPtr AlignAndCopyData(byte[] data, out IntPtr base_ptr)
        {
            base_ptr = IntPtr.Zero;
            if (data == null)
            {
                return IntPtr.Zero;
            }
            base_ptr = Marshal.AllocHGlobal(checked(data.Length + 15));
            var dataAligned = new IntPtr(16 * (((long)base_ptr + 15) / 16));
            Marshal.Copy(data, 0, dataAligned, data.Length);
            return dataAligned;
        }

        public static void DecompressTracksV2(byte[] transform_data, byte[] scalar_data, byte[] db, byte[] db_bulk_data, out float[] values, out float[] times)
        {
            var decompressedClip = new DecompressedClip();

            var transform_data_ptr = AlignAndCopyData(transform_data, out var transform_data_base_ptr);
            var scalar_data_ptr = AlignAndCopyData(scalar_data, out var scalar_data_base_ptr);
            var db_ptr = AlignAndCopyData(db, out var db_base_ptr);
            var db_bulk_data_ptr = AlignAndCopyData(db_bulk_data, out var db_bulk_data_base_ptr);

            DecompressTracksZZZV2(transform_data_ptr, scalar_data_ptr, db_ptr, db_bulk_data_ptr, ref decompressedClip);

            Marshal.FreeHGlobal(transform_data_base_ptr);
            Marshal.FreeHGlobal(scalar_data_base_ptr);
            Marshal.FreeHGlobal(db_base_ptr);
            Marshal.FreeHGlobal(db_bulk_data_base_ptr);

            values = new float[decompressedClip.ValuesCount];
            Marshal.Copy(decompressedClip.Values, values, 0, decompressedClip.ValuesCount);

            times = new float[decompressedClip.TimesCount];
            Marshal.Copy(decompressedClip.Times, times, 0, decompressedClip.TimesCount);

            DisposeZZZV2(ref decompressedClip);
        }
        public static void DecompressTracks(byte[] data, byte[] db, out float[] values, out float[] times, bool isZZZ = false, bool isGenshin = false)
        {
            var decompressedClip = new DecompressedClip();
            bool hasDatabase = db != null && db.Length > 0;
            if (isGenshin && !hasDatabase) ValidateStandaloneGenshinTracks(data);
            var bulkOffset = isGenshin && hasDatabase ? GenshinBulkOffset(db) : 0;
            var dataPtr = Marshal.AllocHGlobal(checked(data.Length + 15));
            var dbPtr = IntPtr.Zero;
            try
            {
                var dataAligned = new IntPtr(16 * (((long)dataPtr + 15) / 16));
                Marshal.Copy(data, 0, dataAligned, data.Length);

                var dbAligned = IntPtr.Zero;
                if (hasDatabase)
                {
                    dbPtr = Marshal.AllocHGlobal(checked(db.Length + 15));
                    dbAligned = new IntPtr(16 * (((long)dbPtr + 15) / 16));
                    Marshal.Copy(db, 0, dbAligned, db.Length);
                }

                // Genshin appends the external database tiers after the serialized database header.
                // A null streamer leaves the MHY scalar context uninitialized and decodes garbage.
                var streamer = isGenshin && hasDatabase ? IntPtr.Add(dbAligned, bulkOffset) : IntPtr.Zero;
                if (isZZZ)
                {
                    DecompressTracksZZZ(dataAligned, dbAligned, streamer, ref decompressedClip);
                }
                else
                {
                    DecompressTracks(dataAligned, dbAligned, streamer, ref decompressedClip);
                }

                values = new float[decompressedClip.ValuesCount];
                Marshal.Copy(decompressedClip.Values, values, 0, decompressedClip.ValuesCount);

                times = new float[decompressedClip.TimesCount];
                Marshal.Copy(decompressedClip.Times, times, 0, decompressedClip.TimesCount);
            }
            finally
            {
                if (isZZZ)
                {
                    DisposeZZZ(ref decompressedClip);
                }
                else
                {
                    Dispose(ref decompressedClip);
                }
                Marshal.FreeHGlobal(dataPtr);
                if (dbPtr != IntPtr.Zero) Marshal.FreeHGlobal(dbPtr);
            }
        }

        internal static int GenshinBulkOffset(byte[] database)
        {
            // raw_buffer_header (8 bytes), then database_header; offsets match the vendored ACL DB format.
            if (database == null || database.Length < 64 || BitConverter.ToUInt32(database, 8) != 0xAC11DB01)
                throw new System.IO.InvalidDataException("Missing or invalid Genshin ACL database header.");
            var version = BitConverter.ToUInt16(database, 12);
            if (version != 100 || (BitConverter.ToUInt16(database, 14) & 1) != 0)
                throw new System.IO.InvalidDataException("Unsupported Genshin ACL database version or inline layout.");
            long size = BitConverter.ToUInt32(database, 0);
            long offset = (size + 3) & ~3L;
            long mediumSize = BitConverter.ToUInt32(database, 40);
            long lowSize = BitConverter.ToUInt32(database, 44);
            long total = offset + ((mediumSize + 3) & ~3L) + lowSize;
            if (size < 64 || total > database.Length)
                throw new System.IO.InvalidDataException("Genshin ACL database bulk data is truncated.");
            return checked((int)offset);
        }

        internal static void ValidateStandaloneGenshinTracks(byte[] data)
        {
            // The vendored ACL DB decoder supports a null database only when
            // neither the transform nor scalar tracks refer to one.
            int offset = 0;
            while (true)
            {
                if (data == null || data.Length - offset < 32 || BitConverter.ToUInt32(data, offset + 8) != 0xAC11AC11)
                    throw new System.IO.InvalidDataException("Invalid standalone Genshin ACL tracks.");
                uint size = BitConverter.ToUInt32(data, offset);
                if (size < 32 || size > data.Length - offset)
                    throw new System.IO.InvalidDataException("Truncated standalone Genshin ACL tracks.");
                if ((BitConverter.ToUInt32(data, offset + 28) & (1 << 8)) != 0)
                    throw new System.IO.InvalidDataException("Genshin ACL tracks require a missing database.");
                if (data[offset + 15] != 12) break; // scalar block; qvvf is followed by scalar tracks
                offset = checked((int)(((long)offset + size + 15) & ~15L));
            }
        }

        #region importfunctions

        // Both DLLs are built from the same dllmain.cpp, so they share this signature.
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void DecompressTracks(nint data, nint db, nint streamer, ref DecompressedClip decompressedClip);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Dispose(ref DecompressedClip decompressedClip);

        [DllImport(DLL_NAME_ZZZ, CallingConvention = CallingConvention.Cdecl, EntryPoint = "DecompressTracks")]
        private static extern void DecompressTracksZZZ(nint data, nint db, nint streamer, ref DecompressedClip decompressedClip);

        [DllImport(DLL_NAME_ZZZ, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Dispose")]
        private static extern void DisposeZZZ(ref DecompressedClip decompressedClip);

        [DllImport(DLL_NAME_ZZZ_V2, CallingConvention = CallingConvention.Cdecl, EntryPoint = "DecompressTracksZZZ")]
        private static extern void DecompressTracksZZZV2(nint transform_tracks, nint scalar_tracks, nint database, nint bulk_data, ref DecompressedClip decompressedClip);
        [DllImport(DLL_NAME_ZZZ, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Dispose")]
        private static extern void DisposeZZZV2(ref DecompressedClip decompressedClip);

        #endregion
    }
}
