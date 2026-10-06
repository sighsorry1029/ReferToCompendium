using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Xml;

namespace ReferToCompendium
{
    /// <summary>Versioned local UI preferences; no player/world save data or server settings.</summary>
    public sealed class LayoutStore
    {
        private const int CurrentVersion = 1;
        private const int MaximumBytes = 2 * 1024 * 1024;
        private const int MaximumEntries = 4096;
        private const int MaximumIdLength = 4096;
        private readonly string path;
        private readonly Action<string> warning;
        private readonly HashSet<string> preserved = new HashSet<string>(StringComparer.Ordinal);
        private bool readOnly;

        public LayoutState State { get; private set; }

        public LayoutStore(string path, Action<string>? warning)
        {
            this.path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
            this.warning = warning ?? (_ => { });
            State = new LayoutState();
            Load();
        }

        public bool Save()
        {
            if (readOnly) return false;
            string? temporary = null;
            try
            {
                // Recheck the disk file: another installation may have written a newer format.
                ReadResult primary = Read(path);
                if (primary.Kind == ReadKind.Unsupported || primary.Kind == ReadKind.Unavailable)
                {
                    Block(primary.Message);
                    return false;
                }
                if (primary.Kind == ReadKind.Corrupt && !Preserve(path, primary.Bytes)) return false;

                List<LayoutEntry> entries = State.Snapshot();
                Validate(entries);
                byte[] bytes;
                using (var memory = new MemoryStream())
                {
                    Serializer(typeof(LayoutDocument)).WriteObject(memory, new LayoutDocument { Version = CurrentVersion, Entries = entries });
                    if (memory.Length > MaximumBytes) throw new SerializationException("Layout exceeds the maximum file size.");
                    bytes = memory.ToArray();
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new IOException("Layout path has no parent directory."));
                temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                WriteNew(temporary, bytes);
                if (primary.Kind == ReadKind.Missing) File.Move(temporary, path);
                else File.Replace(temporary, path, primary.Kind == ReadKind.Supported ? path + ".bak" : null, true);
                temporary = null;
                return true;
            }
            catch (Exception exception) when (IsFileOrDataError(exception))
            {
                warning("Could not save compendium layout: " + exception.Message);
                return false;
            }
            finally
            {
                if (temporary != null)
                {
                    try { File.Delete(temporary); }
                    catch (Exception exception) when (IsFileOrDataError(exception)) { warning("Could not remove layout temporary file: " + exception.Message); }
                }
            }
        }

        private void Load()
        {
            ReadResult primary = Read(path);
            if (primary.Kind == ReadKind.Supported) { State = primary.State!; return; }
            if (primary.Kind == ReadKind.Unsupported || primary.Kind == ReadKind.Unavailable)
            {
                Block(primary.Message);
                return;
            }
            if (primary.Kind == ReadKind.Corrupt)
            {
                warning("Compendium layout is invalid; trying its backup. " + primary.Message);
                if (!Preserve(path, primary.Bytes)) readOnly = true;
            }
            ReadResult backup = Read(path + ".bak");
            if (backup.Kind == ReadKind.Supported)
            {
                State = backup.State!;
                warning("Recovered compendium layout from its backup.");
            }
            else if (backup.Kind == ReadKind.Unsupported || backup.Kind == ReadKind.Unavailable) Block(backup.Message);
            else if (backup.Kind == ReadKind.Corrupt)
            {
                warning("Compendium layout backup is invalid; using an empty layout. " + backup.Message);
                if (!Preserve(path + ".bak", backup.Bytes)) readOnly = true;
            }
        }

        private ReadResult Read(string source)
        {
            byte[] bytes;
            try
            {
                using (var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var memory = new MemoryStream())
                {
                    if (stream.Length > MaximumBytes) return new ReadResult(ReadKind.Unavailable, "Layout is larger than the supported read limit: " + source);
                    var buffer = new byte[8192];
                    int count;
                    while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        if (memory.Length + count > MaximumBytes) return new ReadResult(ReadKind.Unavailable, "Layout grew beyond the supported read limit: " + source);
                        memory.Write(buffer, 0, count);
                    }
                    bytes = memory.ToArray();
                }
            }
            catch (FileNotFoundException) { return new ReadResult(ReadKind.Missing); }
            catch (DirectoryNotFoundException) { return new ReadResult(ReadKind.Missing); }
            catch (Exception exception) when (IsFileOrDataError(exception)) { return new ReadResult(ReadKind.Unavailable, exception.Message); }
            try
            {
                // Read only the header first so future entry structures cannot be mistaken for corruption.
                var header = (LayoutHeader)Deserialize(typeof(LayoutHeader), bytes);
                if (header == null) throw new SerializationException("Missing layout object.");
                if (header.Version != CurrentVersion)
                    return new ReadResult(ReadKind.Unsupported, "Unsupported layout format " + header.Version + "; preserving " + source + " without changes.");
                var document = (LayoutDocument)Deserialize(typeof(LayoutDocument), bytes);
                Validate(document.Entries);
                return new ReadResult(ReadKind.Supported) { State = LayoutState.FromEntries(document.Entries) };
            }
            catch (Exception exception) when (IsFileOrDataError(exception))
            {
                return new ReadResult(ReadKind.Corrupt, exception.Message) { Bytes = bytes };
            }
        }

        private bool Preserve(string source, byte[] bytes)
        {
            // Preservation uses the bytes actually inspected, without deleting or renaming the source.
            string signature;
            using (var hash = System.Security.Cryptography.SHA256.Create())
                signature = source + ":" + Convert.ToBase64String(hash.ComputeHash(bytes));
            if (preserved.Contains(signature)) return true;
            try
            {
                string copy = source + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
                WriteNew(copy, bytes);
                preserved.Add(signature);
                warning("Preserved invalid compendium layout as " + copy);
                return true;
            }
            catch (Exception exception) when (IsFileOrDataError(exception))
            {
                Block("Cannot preserve invalid layout; saving is disabled to avoid data loss. " + exception.Message);
                return false;
            }
        }

        private void Block(string message)
        {
            readOnly = true;
            warning(message + " Layout saving is disabled for this session.");
        }

        private static object Deserialize(Type type, byte[] bytes)
        {
            var quotas = new XmlDictionaryReaderQuotas { MaxDepth = 32, MaxStringContentLength = MaximumBytes, MaxArrayLength = MaximumBytes, MaxBytesPerRead = MaximumBytes, MaxNameTableCharCount = 65536 };
            using (XmlDictionaryReader reader = JsonReaderWriterFactory.CreateJsonReader(bytes, quotas))
            {
                object result = Serializer(type).ReadObject(reader) ?? throw new SerializationException("Missing layout object.");
                if (reader.MoveToContent() != XmlNodeType.None) throw new SerializationException("Unexpected data after layout object.");
                return result;
            }
        }

        private static DataContractJsonSerializer Serializer(Type type)
        {
            return new DataContractJsonSerializer(type, new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 32768 });
        }

        private static void Validate(List<LayoutEntry> entries)
        {
            if (entries == null || entries.Count > MaximumEntries) throw new SerializationException("Invalid layout entry count.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (LayoutEntry entry in entries)
                if (entry == null || string.IsNullOrEmpty(entry.Id) || entry.Id.Length > MaximumIdLength || !seen.Add(entry.Id))
                    throw new SerializationException("Layout has an invalid or duplicate entry ID.");
        }

        private static void WriteNew(string destination, byte[] bytes)
        {
            using (var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static bool IsFileOrDataError(Exception exception)
        {
            return exception is IOException || exception is UnauthorizedAccessException || exception is SerializationException || exception is XmlException || exception is ArgumentException || exception is System.Security.SecurityException || exception is NotSupportedException || exception is FormatException;
        }

        private enum ReadKind { Missing, Supported, Unsupported, Corrupt, Unavailable }
        private sealed class ReadResult
        {
            public readonly ReadKind Kind;
            public readonly string Message;
            public byte[] Bytes = Array.Empty<byte>();
            public LayoutState? State;
            public ReadResult(ReadKind kind, string message = "") { Kind = kind; Message = message; }
        }

        [DataContract]
        private sealed class LayoutHeader
        {
            [DataMember(Name = "version", IsRequired = true)] public int Version { get; set; }
        }

        [DataContract]
        private sealed class LayoutDocument
        {
            [DataMember(Name = "version", Order = 0, IsRequired = true)] public int Version;
            [DataMember(Name = "entries", Order = 1, IsRequired = true)] public List<LayoutEntry> Entries = new List<LayoutEntry>();
        }
    }
}
