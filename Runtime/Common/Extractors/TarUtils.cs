using System;
using System.IO;
using System.Text;

namespace PonyuDev.SherpaOnnx.Common.Extractors
{
    /// <summary>
    /// Shared tar parsing utilities used by TarGz and TarBz2 extractors.
    /// </summary>
    internal static class TarUtils
    {
        internal const int BlockSize = 512;

        internal static int ReadExact(Stream s, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int r = s.Read(buffer, offset + total, count - total);
                if (r <= 0)
                    return total;
                total += r;
            }
            return total;
        }

        internal static bool IsAllZeroBlock(byte[] block)
        {
            for (int i = 0; i < block.Length; i++)
            {
                if (block[i] != 0)
                    return false;
            }
            return true;
        }

        internal static string NormalizeEntryPath(string name)
        {
            name = name.Replace('\\', '/');
            while (name.StartsWith("/", StringComparison.Ordinal))
                name = name.Substring(1);

            if (name.Contains(".."))
                throw new InvalidDataException("Tar entry contains invalid path: " + name);

            return name;
        }

        /// <summary>
        /// Reads an entry's data plus its padding to the next 512-byte
        /// boundary. Used for metadata entries (GNU long names, PAX
        /// headers), which are small.
        /// </summary>
        internal static byte[] ReadEntryData(Stream s, long size)
        {
            if (size < 0 || size > MaxMetadataEntrySize)
                throw new InvalidDataException($"Tar metadata entry has invalid size {size}.");

            long padded = (size + BlockSize - 1) / BlockSize * BlockSize;
            var data = new byte[padded];
            int read = ReadExact(s, data, 0, (int)padded);
            if (read < padded)
                throw new EndOfStreamException("Unexpected end of tar stream (metadata entry).");

            if (padded == size)
                return data;

            var exact = new byte[size];
            Buffer.BlockCopy(data, 0, exact, 0, (int)size);
            return exact;
        }

        /// <summary>The name stored in a GNU 'L' entry: NUL-terminated.</summary>
        internal static string ReadGnuLongName(byte[] data)
        {
            int end = 0;
            while (end < data.Length && data[end] != 0)
                end++;
            return Encoding.UTF8.GetString(data, 0, end);
        }

        /// <summary>
        /// The "path" record of a PAX extended header, or null. Records are
        /// "LENGTH KEY=VALUE\n", where LENGTH counts the whole record.
        /// </summary>
        internal static string ReadPaxPath(byte[] data)
        {
            int pos = 0;
            while (pos < data.Length)
            {
                int space = Array.IndexOf(data, (byte)' ', pos);
                if (space < 0)
                    break;

                if (!int.TryParse(Encoding.ASCII.GetString(data, pos, space - pos), out int length) ||
                    length <= 0 || pos + length > data.Length)
                    break;

                int recordEnd = pos + length;
                int equals = Array.IndexOf(data, (byte)'=', space + 1, recordEnd - space - 1);
                if (equals > 0)
                {
                    string key = Encoding.UTF8.GetString(data, space + 1, equals - space - 1);
                    if (key == "path")
                    {
                        int valueEnd = recordEnd;
                        if (valueEnd > equals + 1 && data[valueEnd - 1] == (byte)'\n')
                            valueEnd--;
                        return Encoding.UTF8.GetString(data, equals + 1, valueEnd - equals - 1);
                    }
                }

                pos = recordEnd;
            }

            return null;
        }

        // GNU long names and PAX headers are a few hundred bytes; anything
        // far larger means a corrupt archive rather than a real header.
        private const long MaxMetadataEntrySize = 1024 * 1024;
    }

    /// <summary>
    /// Parsed tar header. Supports POSIX ustar (prefix field), GNU long
    /// names ('L') and PAX extended headers ('x'); the extractors apply a
    /// long name from a preceding metadata entry with <see cref="WithName"/>.
    /// </summary>
    internal readonly struct TarHeader
    {
        public readonly string Name;
        public readonly long Size;
        public readonly byte TypeFlag;

        public bool IsDirectory =>
            TypeFlag == (byte)'5' || Name.EndsWith("/", StringComparison.Ordinal);

        /// <summary>GNU 'L': the data is the full name of the next entry.</summary>
        public bool IsGnuLongName => TypeFlag == (byte)'L';

        /// <summary>PAX 'x': key/value records for the next entry.</summary>
        public bool IsPaxHeader => TypeFlag == (byte)'x';

        /// <summary>
        /// Metadata with nothing to extract: GNU long link target ('K') and
        /// PAX global header ('g'). Their data must still be consumed.
        /// </summary>
        public bool IsIgnoredMetadata => TypeFlag == (byte)'K' || TypeFlag == (byte)'g';

        private TarHeader(string name, long size, byte typeFlag)
        {
            Name = name;
            Size = size;
            TypeFlag = typeFlag;
        }

        public TarHeader WithName(string name) => new TarHeader(name, Size, TypeFlag);

        public static TarHeader Parse(byte[] header)
        {
            string name = ReadNullTerminatedString(header, 0, 100);
            string sizeOctal = ReadNullTerminatedString(header, 124, 12);
            byte typeFlag = header[156];

            // Only POSIX ustar ("ustar\0") has a name prefix at offset 345.
            // GNU tar ("ustar  \0") stores other fields there and uses 'L'
            // entries for long names instead.
            if (IsPosixUstar(header))
            {
                string prefix = ReadNullTerminatedString(header, 345, 155);
                if (!string.IsNullOrEmpty(prefix))
                    name = prefix + "/" + name;
            }

            long size = ParseOctalLong(sizeOctal);

            if (typeFlag == 0)
                typeFlag = (byte)'0';

            return new TarHeader(name, size, typeFlag);
        }

        private static bool IsPosixUstar(byte[] header) =>
            header[257] == (byte)'u' && header[258] == (byte)'s' && header[259] == (byte)'t' &&
            header[260] == (byte)'a' && header[261] == (byte)'r' && header[262] == 0;

        private static string ReadNullTerminatedString(byte[] bytes, int offset, int length)
        {
            int end = offset;
            int max = offset + length;

            while (end < max && bytes[end] != 0)
                end++;

            return Encoding.ASCII.GetString(bytes, offset, end - offset).Trim();
        }

        private static long ParseOctalLong(string octal)
        {
            if (string.IsNullOrEmpty(octal))
                return 0;

            octal = octal.Trim();

            long value = 0;
            for (int i = 0; i < octal.Length; i++)
            {
                char c = octal[i];
                if (c < '0' || c > '7')
                    break;

                value = (value << 3) + (c - '0');
            }
            return value;
        }
    }
}
