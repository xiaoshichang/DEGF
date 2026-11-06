using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DE.Share.Entities
{
    public sealed class EntitySnapshot
    {
        public EntitySnapshot(Guid entityId, string typeName, byte[] data)
        {
            EntityId = entityId;
            TypeName = typeName;
            Data = data;
        }

        public Guid EntityId { get; }
        public string TypeName { get; }
        public byte[] Data { get; }
    }

    public sealed class SpaceEnterSnapshot
    {
        public SpaceEnterSnapshot(int spaceNo, EntitySnapshot space, IReadOnlyList<EntitySnapshot> entities, byte[] ownerData = null)
        {
            SpaceNo = spaceNo;
            Space = space;
            Entities = entities;
            OwnerData = ownerData ?? Array.Empty<byte>();
        }

        public int SpaceNo { get; }
        public EntitySnapshot Space { get; }
        public IReadOnlyList<EntitySnapshot> Entities { get; }
        public byte[] OwnerData { get; }
    }

    public static class SpaceSnapshotCodec
    {
        public const int MaxPayloadBytes = 8 * 1024 * 1024;
        public const int MaxEntityCount = 16384;
        private const int Version = 1;

        public static byte[] Serialize(SpaceEnterSnapshot snapshot)
        {
            if (snapshot == null || snapshot.SpaceNo <= 0 || snapshot.Entities == null || snapshot.Entities.Count > MaxEntityCount)
            {
                throw new ArgumentException("Invalid Space snapshot.", nameof(snapshot));
            }
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(Version);
                writer.Write(snapshot.SpaceNo);
                WriteEntity(writer, snapshot.Space);
                WriteBytes(writer, snapshot.OwnerData);
                writer.Write(snapshot.Entities.Count);
                var ids = new HashSet<Guid> { snapshot.Space.EntityId };
                foreach (var entity in snapshot.Entities)
                {
                    if (entity == null || !ids.Add(entity.EntityId))
                    {
                        throw new ArgumentException("Duplicate or null entity in Space snapshot.");
                    }
                    WriteEntity(writer, entity);
                }
                if (stream.Length > MaxPayloadBytes)
                {
                    throw new ArgumentException("Space snapshot is too large.");
                }
                return stream.ToArray();
            }
        }

        public static SpaceEnterSnapshot Deserialize(byte[] bytes)
        {
            if (bytes == null || bytes.Length > MaxPayloadBytes)
            {
                throw new InvalidOperationException("Invalid Space snapshot size.");
            }
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    if (reader.ReadInt32() != Version)
                    {
                        throw new InvalidOperationException("Unsupported Space snapshot version.");
                    }
                    int spaceNo = reader.ReadInt32();
                    var space = ReadEntity(reader);
                    var ownerData = ReadBytes(reader);
                    int count = reader.ReadInt32();
                    if (spaceNo <= 0 || count < 0 || count > MaxEntityCount)
                    {
                        throw new InvalidOperationException("Invalid Space snapshot header.");
                    }
                    var entities = new List<EntitySnapshot>(count);
                    var ids = new HashSet<Guid> { space.EntityId };
                    for (int i = 0; i < count; i++)
                    {
                        var entity = ReadEntity(reader);
                        if (!ids.Add(entity.EntityId))
                        {
                            throw new InvalidOperationException("Duplicate entity in Space snapshot.");
                        }
                        entities.Add(entity);
                    }
                    if (stream.Position != stream.Length)
                    {
                        throw new InvalidOperationException("Trailing Space snapshot bytes.");
                    }
                    return new SpaceEnterSnapshot(spaceNo, space, entities, ownerData);
                }
            }
            catch (IOException error)
            {
                throw new InvalidOperationException("Truncated Space snapshot.", error);
            }
        }

        public static byte[] SerializeEntity(EntitySnapshot entity)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                WriteEntity(writer, entity);
                return stream.ToArray();
            }
        }

        public static EntitySnapshot DeserializeEntity(byte[] bytes)
        {
            if (bytes == null || bytes.Length > MaxPayloadBytes)
            {
                throw new InvalidOperationException("Invalid entity snapshot size.");
            }
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    var entity = ReadEntity(reader);
                    if (stream.Position != stream.Length)
                    {
                        throw new InvalidOperationException("Trailing entity snapshot bytes.");
                    }
                    return entity;
                }
            }
            catch (IOException error)
            {
                throw new InvalidOperationException("Truncated entity snapshot.", error);
            }
        }

        private static void WriteEntity(BinaryWriter writer, EntitySnapshot entity)
        {
            if (entity == null || entity.EntityId == Guid.Empty || string.IsNullOrWhiteSpace(entity.TypeName) || entity.TypeName.Length > 128)
            {
                throw new ArgumentException("Invalid entity snapshot identity.");
            }
            writer.Write(entity.EntityId.ToByteArray());
            WriteBytes(writer, Encoding.UTF8.GetBytes(entity.TypeName));
            WriteBytes(writer, entity.Data);
        }

        private static EntitySnapshot ReadEntity(BinaryReader reader)
        {
            byte[] idBytes = reader.ReadBytes(16);
            if (idBytes.Length != 16)
            {
                throw new InvalidOperationException("Truncated entity id.");
            }
            var id = new Guid(idBytes);
            var typeBytes = ReadBytes(reader, 512);
            var type = new UTF8Encoding(false, true).GetString(typeBytes);
            if (id == Guid.Empty || string.IsNullOrWhiteSpace(type) || type.Length > 128)
            {
                throw new InvalidOperationException("Invalid entity snapshot identity.");
            }
            return new EntitySnapshot(id, type, ReadBytes(reader));
        }

        private static void WriteBytes(BinaryWriter writer, byte[] bytes)
        {
            if (bytes == null || bytes.Length > MaxPayloadBytes || writer.BaseStream.Length + bytes.Length + 4 > MaxPayloadBytes)
            {
                throw new ArgumentException("Invalid snapshot data size.");
            }
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static byte[] ReadBytes(BinaryReader reader, int limit = MaxPayloadBytes)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > limit || length > reader.BaseStream.Length - reader.BaseStream.Position)
            {
                throw new InvalidOperationException("Invalid snapshot data length.");
            }
            return reader.ReadBytes(length);
        }
    }
}
